using System;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Minimal direct-UDP controller for the ESP32 pneumatic hardware.
    ///
    /// Assumptions:
    /// - each packet selects one upper-arm or shoulder valve/pump state
    /// - a completed curl drives only the upper-arm circuit
    /// - a completed side raise drives only the shoulder circuit
    ///
    /// Gameplay timing is kept close to the previous implementation:
    /// a rhythm-clear repetition starts one bounded valve-close pulse while
    /// a MISS starts a bounded valve-open pulse on the same circuit,
    /// duplicate repetition events do not extend either pulse,
    /// and every non-Training / unhealthy state closes both valves and stops
    /// both pumps.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10200)]
    public sealed class PneumaticCuffController : MonoBehaviour
    {
        private enum PneumaticCommand
        {
            ArmValveOpen,
            ArmValveClosed,
            ArmPumpOn,
            ArmPumpOff,
            ShoulderValveOpen,
            ShoulderValveClosed,
            ShoulderPumpOn,
            ShoulderPumpOff
        }

        private readonly struct PneumaticCommandSet :
            IEquatable<PneumaticCommandSet>
        {
            public PneumaticCommandSet(
                PneumaticCommand armValve,
                PneumaticCommand armPump,
                PneumaticCommand shoulderValve,
                PneumaticCommand shoulderPump)
            {
                ArmValve = armValve;
                ArmPump = armPump;
                ShoulderValve = shoulderValve;
                ShoulderPump = shoulderPump;
            }

            public PneumaticCommand ArmValve { get; }
            public PneumaticCommand ArmPump { get; }
            public PneumaticCommand ShoulderValve { get; }
            public PneumaticCommand ShoulderPump { get; }

            public bool Equals(PneumaticCommandSet other)
            {
                return ArmValve == other.ArmValve &&
                       ArmPump == other.ArmPump &&
                       ShoulderValve == other.ShoulderValve &&
                       ShoulderPump == other.ShoulderPump;
            }
        }

        private static readonly byte[] ArmValveOpenPacket =
            { (byte)'A', (byte)'V', (byte)'T' };
        private static readonly byte[] ArmValveClosedPacket =
            { (byte)'A', (byte)'V', (byte)'F' };
        private static readonly byte[] ArmPumpOnPacket =
            { (byte)'A', (byte)'P', (byte)'T' };
        private static readonly byte[] ArmPumpOffPacket =
            { (byte)'A', (byte)'P', (byte)'F' };
        private static readonly byte[] ShoulderValveOpenPacket =
            { (byte)'S', (byte)'V', (byte)'T' };
        private static readonly byte[] ShoulderValveClosedPacket =
            { (byte)'S', (byte)'V', (byte)'F' };
        private static readonly byte[] ShoulderPumpOnPacket =
            { (byte)'S', (byte)'P', (byte)'T' };
        private static readonly byte[] ShoulderPumpOffPacket =
            { (byte)'S', (byte)'P', (byte)'F' };

        private static readonly PneumaticCommandSet AllOffCommands =
            new PneumaticCommandSet(
                PneumaticCommand.ArmValveClosed,
                PneumaticCommand.ArmPumpOff,
                PneumaticCommand.ShoulderValveClosed,
                PneumaticCommand.ShoulderPumpOff);

        [Header("Gameplay")]
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private TrainingRhythmScorer _rhythmScorer;
        [SerializeField] private BodyRepCounter _repCounter;

        [Tooltip(
            "How long a finished repetition keeps its result state: " +
            "valve closed for a rhythm clear, or valve open for a MISS.")]
        [SerializeField, Min(0f)]
        private float _repBuildPulseSeconds;

        [Header("UDP")]
        [Tooltip(
            "Enable only when the Quest/PC is connected to the ESP32 network.")]
        [SerializeField] private bool _transmitEnabled;

        [SerializeField] private string _host = "192.168.4.1";
        [SerializeField] private int _port = 5000;

        [Tooltip(
            "Periodic resend interval. 0.1 s keeps the same basic cadence " +
            "as the previous implementation.")]
        [SerializeField, Min(0.02f)]
        private float _sendIntervalSeconds = 0.1f;

        private UdpClient _udpClient;
        private IPEndPoint _endPoint;

        private bool _buildPulseActive;
        private bool _releasePulseActive;
        private float _buildPulseUntil;
        private BodyRepKind _buildPulseKind;
        private int _lastPulseClockSessionId = int.MinValue;
        private int _lastPulseNoteIndex = int.MinValue;

        private float _nextPeriodicSendAt;
        private PneumaticCommandSet _lastSuccessfullySentCommands;
        private bool _hasSentCommandSet;

        public bool IsBuildPulseActive =>
            _buildPulseActive &&
            Time.unscaledTime < _buildPulseUntil;

        public bool IsReleasePulseActive =>
            _releasePulseActive &&
            Time.unscaledTime < _buildPulseUntil;

        public string OutputStatusLabel =>
            !_transmitEnabled
                ? "UDP / DISABLED"
                : _udpClient != null
                    ? "UDP / READY"
                    : "UDP / ERROR";

        public string DebugStatusLabel =>
            TryGetSelectedExerciseKind(out var kind)
                ? kind == BodyRepKind.ElbowFlexion
                    ? IsReleasePulseActive
                        ? "CURL / AVT + APT (MISS)"
                        : "CURL / AVF + APT"
                    : IsReleasePulseActive
                        ? "RAISE / SVT + SPT (MISS)"
                        : "RAISE / SVF + SPT"
                : "ALL OFF";

        private void Awake()
        {
            ResolveReferences();
            ValidateSettings();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ValidateSettings();
            Subscribe();

            ClearBuildPulse();
            _lastPulseClockSessionId = int.MinValue;
            _lastPulseNoteIndex = int.MinValue;
            _lastSuccessfullySentCommands = AllOffCommands;
            _hasSentCommandSet = false;
            _nextPeriodicSendAt = 0f;

            OpenUdp();
            SendAllOffImmediately();
        }

        private void LateUpdate()
        {
            var now = Time.unscaledTime;

            var canBuild = CanGameplayBuild(now);
            var selectedKind = default(BodyRepKind);
            var hasSelectedExercise =
                canBuild &&
                TryGetSelectedExerciseKind(out selectedKind);
            if (!hasSelectedExercise)
            {
                ClearBuildPulse();
            }
            else if ((_buildPulseActive || _releasePulseActive) &&
                     selectedKind != _buildPulseKind)
            {
                // A pulse never crosses an exercise switch into the other
                // circuit. The next completed repetition starts a new one.
                ClearBuildPulse();
            }
            else if ((_buildPulseActive || _releasePulseActive) &&
                     now >= _buildPulseUntil)
            {
                ClearBuildPulse();
            }

            var desiredCommands = hasSelectedExercise
                ? GetExerciseCommands(selectedKind)
                : AllOffCommands;

            // Preserve the old response timing: send immediately on a state
            // change, otherwise repeat the complete four-channel state.
            if (!_hasSentCommandSet ||
                !desiredCommands.Equals(
                    _lastSuccessfullySentCommands) ||
                now >= _nextPeriodicSendAt)
            {
                SendCommandSet(desiredCommands);
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            ClearBuildPulse();
            SendAllOffImmediately();
            CloseUdp();
        }

        private void OnDestroy()
        {
            SendAllOffImmediately();
            CloseUdp();
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused)
            {
                return;
            }

            ClearBuildPulse();
            SendAllOffImmediately();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused)
            {
                return;
            }

            ClearBuildPulse();
            SendAllOffImmediately();
        }

        private void OnApplicationQuit()
        {
            ClearBuildPulse();
            SendAllOffImmediately();
            CloseUdp();
        }

        private void OnValidate()
        {
            ResolveReferences();
            ValidateSettings();
        }

        private void HandlePhaseChanged(ExperiencePhase phase)
        {
            ClearBuildPulse();

            if (phase == ExperiencePhase.Setup ||
                phase == ExperiencePhase.Training)
            {
                _lastPulseClockSessionId = int.MinValue;
                _lastPulseNoteIndex = int.MinValue;
            }

            SendAllOffImmediately();
        }

        private void HandleRepTimingResultReady(
            TrainingRepTimingResult result)
        {
            // Same repetition result must not create a second pulse.
            if (result.ClockSessionId == _lastPulseClockSessionId &&
                result.NoteIndex == _lastPulseNoteIndex)
            {
                return;
            }

            _lastPulseClockSessionId = result.ClockSessionId;
            _lastPulseNoteIndex = result.NoteIndex;

            var now = Time.unscaledTime;

            if (_repBuildPulseSeconds <= 0f ||
                !CanGameplayBuild(now) ||
                !TryGetSelectedExerciseKind(out var selectedKind) ||
                selectedKind != result.Kind)
            {
                ClearBuildPulse();
                return;
            }

            if (TrainingSuccessFeedbackRules.IsRhythmClear(result))
            {
                // Preserve the existing behavior for repeated successful
                // results: one close pulse is never extended by another
                // close result while it is still active.
                if (IsBuildPulseActive)
                {
                    return;
                }

                StartValveResultPulse(result.Kind, true, now);
                return;
            }

            // A displayed MISS, including a timing miss after the motion was
            // completed, immediately cancels a previous close pulse and
            // keeps the selected circuit open for the configured duration.
            StartValveResultPulse(result.Kind, false, now);
        }

        private void StartValveResultPulse(
            BodyRepKind kind,
            bool closeValve,
            float now)
        {
            _buildPulseUntil = now + _repBuildPulseSeconds;
            _buildPulseKind = kind;
            var isValidPulse =
                !float.IsNaN(_buildPulseUntil) &&
                !float.IsInfinity(_buildPulseUntil) &&
                _buildPulseUntil > now;
            _buildPulseActive = isValidPulse && closeValve;
            _releasePulseActive = isValidPulse && !closeValve;

            if (!isValidPulse)
            {
                ClearBuildPulse();
            }
        }

        private bool CanGameplayBuild(float now)
        {
            if (_gameFlow == null ||
                !_gameFlow.isActiveAndEnabled ||
                _gameFlow.CurrentPhase != ExperiencePhase.Training ||
                _gameFlow.IsPausedForTracking)
            {
                return false;
            }

            if (_rhythmScorer == null ||
                !_rhythmScorer.isActiveAndEnabled)
            {
                return false;
            }

            if (_repCounter == null ||
                !_repCounter.isActiveAndEnabled ||
                !_repCounter.IsReady)
            {
                return false;
            }

            return !float.IsNaN(now) &&
                   !float.IsInfinity(now);
        }

        private bool TryGetSelectedExerciseKind(
            out BodyRepKind kind)
        {
            kind = default;
            if (_repCounter == null ||
                !_repCounter.ActiveTrainingKind.HasValue)
            {
                return false;
            }

            kind = _repCounter.ActiveTrainingKind.Value;
            return kind == BodyRepKind.ElbowFlexion ||
                   kind == BodyRepKind.ArmRaise;
        }

        private PneumaticCommandSet GetExerciseCommands(
            BodyRepKind kind)
        {
            if (kind == BodyRepKind.ElbowFlexion)
            {
                return new PneumaticCommandSet(
                    IsReleasePulseActive
                        ? PneumaticCommand.ArmValveOpen
                        : PneumaticCommand.ArmValveClosed,
                    PneumaticCommand.ArmPumpOn,
                    PneumaticCommand.ShoulderValveClosed,
                    PneumaticCommand.ShoulderPumpOff);
            }

            if (kind == BodyRepKind.ArmRaise)
            {
                return new PneumaticCommandSet(
                    PneumaticCommand.ArmValveClosed,
                    PneumaticCommand.ArmPumpOff,
                    IsReleasePulseActive
                        ? PneumaticCommand.ShoulderValveOpen
                        : PneumaticCommand.ShoulderValveClosed,
                    PneumaticCommand.ShoulderPumpOn);
            }

            return AllOffCommands;
        }

        private void SendAllOffImmediately()
        {
            if (!_transmitEnabled || _udpClient == null)
            {
                return;
            }

            SendCommandSet(AllOffCommands);
        }

        private bool SendCommandSet(
            PneumaticCommandSet commands)
        {
            if (!_transmitEnabled || _udpClient == null || _endPoint == null)
            {
                return false;
            }

            try
            {
                if (_hasSentCommandSet)
                {
                    SendCircuitShutdownBeforeSwitch(
                        _lastSuccessfullySentCommands,
                        commands);
                }

                SendCommand(commands.ArmValve);
                SendCommand(commands.ArmPump);
                SendCommand(commands.ShoulderValve);
                SendCommand(commands.ShoulderPump);

                _lastSuccessfullySentCommands = commands;
                _hasSentCommandSet = true;
                _nextPeriodicSendAt =
                    Time.unscaledTime + _sendIntervalSeconds;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Cuff] UDP send failed: " + exception.Message,
                    this);

                _hasSentCommandSet = false;
                return false;
            }
        }

        private void SendCircuitShutdownBeforeSwitch(
            PneumaticCommandSet previous,
            PneumaticCommandSet next)
        {
            if (previous.ArmPump == PneumaticCommand.ArmPumpOn &&
                next.ArmPump == PneumaticCommand.ArmPumpOff)
            {
                SendCommand(PneumaticCommand.ArmValveClosed);
                SendCommand(PneumaticCommand.ArmPumpOff);
            }

            if (previous.ShoulderPump ==
                    PneumaticCommand.ShoulderPumpOn &&
                next.ShoulderPump ==
                    PneumaticCommand.ShoulderPumpOff)
            {
                SendCommand(PneumaticCommand.ShoulderValveClosed);
                SendCommand(PneumaticCommand.ShoulderPumpOff);
            }
        }

        private void SendCommand(PneumaticCommand command)
        {
            var packet = GetPacket(command);
            _udpClient.Send(packet, packet.Length, _endPoint);
        }

        private static byte[] GetPacket(PneumaticCommand command)
        {
            switch (command)
            {
                case PneumaticCommand.ArmValveOpen:
                    return ArmValveOpenPacket;
                case PneumaticCommand.ArmValveClosed:
                    return ArmValveClosedPacket;
                case PneumaticCommand.ArmPumpOn:
                    return ArmPumpOnPacket;
                case PneumaticCommand.ArmPumpOff:
                    return ArmPumpOffPacket;
                case PneumaticCommand.ShoulderValveOpen:
                    return ShoulderValveOpenPacket;
                case PneumaticCommand.ShoulderValveClosed:
                    return ShoulderValveClosedPacket;
                case PneumaticCommand.ShoulderPumpOn:
                    return ShoulderPumpOnPacket;
                default:
                    return ShoulderPumpOffPacket;
            }
        }

        private void OpenUdp()
        {
            CloseUdp();

            if (!_transmitEnabled)
            {
                return;
            }

            try
            {
                if (!IPAddress.TryParse(_host, out var address))
                {
                    throw new ArgumentException(
                        "ESP32 host must be a numeric IPv4 address.");
                }

                if (_port <= 0 || _port > 65535)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(_port),
                        "UDP port must be 1-65535.");
                }

                _endPoint = new IPEndPoint(address, _port);
                _udpClient = new UdpClient();
            }
            catch (Exception exception)
            {
                _udpClient = null;
                _endPoint = null;
                Debug.LogWarning(
                    "[Cuff] Could not open UDP socket: " +
                    exception.Message,
                    this);
            }
        }

        private void CloseUdp()
        {
            if (_udpClient != null)
            {
                try
                {
                    _udpClient.Close();
                }
                catch (Exception)
                {
                    // Best effort on shutdown.
                }
            }

            _udpClient = null;
            _endPoint = null;
            _hasSentCommandSet = false;
        }

        private void Subscribe()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= HandlePhaseChanged;
                _gameFlow.PhaseChanged += HandlePhaseChanged;
            }

            if (_rhythmScorer != null)
            {
                _rhythmScorer.RepTimingResultReady -=
                    HandleRepTimingResultReady;
                _rhythmScorer.RepTimingResultReady +=
                    HandleRepTimingResultReady;
            }
        }

        private void Unsubscribe()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= HandlePhaseChanged;
            }

            if (_rhythmScorer != null)
            {
                _rhythmScorer.RepTimingResultReady -=
                    HandleRepTimingResultReady;
            }
        }

        private void ClearBuildPulse()
        {
            _buildPulseActive = false;
            _releasePulseActive = false;
            _buildPulseUntil = 0f;
        }

        private void ResolveReferences()
        {
            _gameFlow ??=
                FindFirstObjectByType<GameFlowController>();
            _rhythmScorer ??=
                FindFirstObjectByType<TrainingRhythmScorer>();
            _repCounter ??=
                FindFirstObjectByType<BodyRepCounter>();
        }

        private void ValidateSettings()
        {
            _repBuildPulseSeconds =
                float.IsNaN(_repBuildPulseSeconds) ||
                float.IsInfinity(_repBuildPulseSeconds)
                    ? 0f
                    : Mathf.Max(0f, _repBuildPulseSeconds);

            _sendIntervalSeconds =
                float.IsNaN(_sendIntervalSeconds) ||
                float.IsInfinity(_sendIntervalSeconds)
                    ? 0.1f
                    : Mathf.Max(0.02f, _sendIntervalSeconds);
        }
    }
}
