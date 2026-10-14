using System;
using System.Collections.Generic;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Owns the one normalized muscle-growth signal used by avatar visuals.
    /// Training follows the live gauge; all later phases use GameFlow's frozen
    /// snapshot so the stage and Results cannot change retroactively.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MuscleGrowthController : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private TrainingRhythmScorer _rhythmScorer;

        [Header("Outputs")]
        [Tooltip(
            "Components implementing IMuscleGrowthOutput. " +
            "Use BlendShapeMuscleGrowthOutput when the final FBX arrives.")]
        [SerializeField] private MonoBehaviour[] _outputBehaviours =
            Array.Empty<MonoBehaviour>();

        [Header("Response")]
        [SerializeField, Min(0.01f)]
        private float _responseSeconds = 0.12f;

        [Header("Pulse")]
        [Tooltip(
            "Per-repetition response. Growth still rises continuously; this " +
            "only gives each rep a moment the participant can notice.")]
        [SerializeField]
        private MusclePulseSettings _pulseSettings =
            MusclePulseSettings.Default;

        private readonly MusclePulseState _pulses =
            new MusclePulseState();

        private OutputEntry[] _outputs =
            Array.Empty<OutputEntry>();

        // Per-muscle-group values, smoothed with the same response as the
        // overall signal so per-part visuals never pop.
        private static readonly MuscleGrowthChannel[] PartChannels =
        {
            MuscleGrowthChannel.ElbowFlexionLeft,
            MuscleGrowthChannel.ElbowFlexionRight,
            MuscleGrowthChannel.ArmRaiseLeft,
            MuscleGrowthChannel.ArmRaiseRight,
            MuscleGrowthChannel.ChestLeft,
            MuscleGrowthChannel.ChestRight
        };

        private readonly float[] _targetChannelGrowth =
            new float[Enum.GetValues(
                typeof(MuscleGrowthChannel)).Length];
        private readonly float[] _appliedChannelGrowth =
            new float[Enum.GetValues(
                typeof(MuscleGrowthChannel)).Length];

        private float _lastDispatchedGrowth = float.NaN;
        private int _lastAvailableOutputCount = -1;
        private int _lastAvailableOutputSignature;

        public event Action<float> GrowthApplied;

        public float TargetGrowthNormalized { get; private set; }
        public float AppliedGrowthNormalized { get; private set; }

        /// <summary>
        /// Per-repetition pulse, for visuals that light the body. Read-only:
        /// the pulse is driven from scored reps, never set from outside.
        /// </summary>
        public MusclePulseState Pulses => _pulses;

        public bool HasAvailableOutput =>
            GetAvailableOutputCount() > 0;

        public bool HasRealOutput
        {
            get
            {
                foreach (var entry in _outputs)
                {
                    if (IsEntryAvailable(entry) &&
                        !entry.Output.IsMock)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public string OutputStatusLabel
        {
            get
            {
                foreach (var entry in _outputs)
                {
                    if (IsEntryAvailable(entry) &&
                        !entry.Output.IsMock)
                    {
                        return string.IsNullOrWhiteSpace(
                            entry.Output.OutputLabel)
                            ? "REAL"
                            : entry.Output.OutputLabel;
                    }
                }

                foreach (var entry in _outputs)
                {
                    if (IsEntryAvailable(entry) &&
                        entry.Output.IsMock)
                    {
                        return string.IsNullOrWhiteSpace(
                            entry.Output.OutputLabel)
                            ? "MOCK"
                            : entry.Output.OutputLabel;
                    }
                }

                return "NOT WIRED";
            }
        }

        private void Awake()
        {
            ResolveReferences();
            ValidateSettings();
            CacheOutputs();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ValidateSettings();
            CacheOutputs();
            Subscribe();
            BootstrapFromCurrentPhase();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ResetOutputs();
        }

        private void OnValidate()
        {
            ResolveReferences();
            ValidateSettings();
        }

        private void Update()
        {
            RefreshTarget();
            var wasPulsing = _pulses.IsActive;
            _pulses.Advance(
                Time.unscaledDeltaTime,
                _pulseSettings);
            var step =
                Time.unscaledDeltaTime /
                Mathf.Max(0.01f, _responseSeconds);
            AppliedGrowthNormalized = Mathf.MoveTowards(
                AppliedGrowthNormalized,
                TargetGrowthNormalized,
                step);

            var channelsChanged = false;
            foreach (var channel in PartChannels)
            {
                var index = (int)channel;
                var next = Mathf.MoveTowards(
                    _appliedChannelGrowth[index],
                    _targetChannelGrowth[index],
                    step);
                if (!Mathf.Approximately(
                        next,
                        _appliedChannelGrowth[index]))
                {
                    channelsChanged = true;
                }

                _appliedChannelGrowth[index] = next;
            }

            var availableOutputCount =
                GetAvailableOutputState(
                    out var availableOutputSignature);
            var outputSetChanged =
                availableOutputCount !=
                    _lastAvailableOutputCount ||
                availableOutputSignature !=
                    _lastAvailableOutputSignature;
            if (outputSetChanged ||
                channelsChanged ||
                // A decaying pulse changes the dispatched values without
                // moving the earned growth, so it needs its own reason to
                // republish, and one final frame to publish the silence.
                wasPulsing ||
                float.IsNaN(_lastDispatchedGrowth) ||
                !Mathf.Approximately(
                    AppliedGrowthNormalized,
                    _lastDispatchedGrowth))
            {
                ApplyToOutputs(true);
            }
        }

        [ContextMenu("Refresh Muscle Outputs")]
        public void RefreshOutputs()
        {
            ResetOutputEntries();
            CacheOutputs();
            BootstrapFromCurrentPhase();
        }

        private void HandleScoreChanged()
        {
            RefreshTarget();
        }

        private void HandlePhaseChanged(ExperiencePhase phase)
        {
            if (phase == ExperiencePhase.Setup ||
                phase == ExperiencePhase.Ready ||
                phase == ExperiencePhase.Countdown ||
                phase == ExperiencePhase.Training)
            {
                // TrainingRhythmScorer resets through the same event and its
                // subscriber order is not guaranteed. Start from a known zero.
                _pulses.Clear();
                SetGrowthImmediate(0f);
                return;
            }

            // Leaving Training does not silence the last rep. Its pulse is
            // allowed to ring out into Transformation, which is where the
            // participant finally gets to look at the body.
            RefreshTarget();
        }

        /// <summary>
        /// One charted repetition finished. Both sides the note asked for are
        /// credited, so a two-armed exercise lights both arms.
        ///
        /// A rep that was never completed produces nothing at all: there is no
        /// movement to acknowledge. A rep that was completed but mistimed
        /// still pulses, faintly and in its own colour, because the movement
        /// did happen and only the rhythm was off.
        /// </summary>
        private void HandleRepTimingResult(
            TrainingRepTimingResult result)
        {
            if (!TrainingSuccessFeedbackRules
                    .IsMotionCompleted(result))
            {
                return;
            }

            // The weaker of the two timing points, so a rep is judged by its
            // worse half rather than flattered by its better one.
            var accuracy = Mathf.Min(
                result.Up.Accuracy,
                result.Down.Accuracy);
            if ((result.RequiredSides &
                 TrainingSideMask.Left) != 0)
            {
                ReportRep(
                    result.Kind,
                    BodySide.Left,
                    accuracy);
            }

            if ((result.RequiredSides &
                 TrainingSideMask.Right) != 0)
            {
                ReportRep(
                    result.Kind,
                    BodySide.Right,
                    accuracy);
            }
        }

        private void ReportRep(
            BodyRepKind kind,
            BodySide side,
            float accuracy)
        {
            _pulses.ReportRep(
                MuscleGrowthChannels.FromRep(kind, side),
                accuracy,
                _pulseSettings);
        }

        private void BootstrapFromCurrentPhase()
        {
            var phase = _gameFlow == null
                ? ExperiencePhase.Setup
                : _gameFlow.CurrentPhase;
            var target = MuscleGrowthMath.SelectTarget(
                phase,
                _rhythmScorer == null
                    ? 0f
                    : _rhythmScorer.GrowthNormalized,
                _gameFlow == null
                    ? 0f
                    : _gameFlow.CompletedGrowthNormalized);
            SetGrowthImmediate(target);
        }

        private void RefreshTarget()
        {
            var phase = _gameFlow == null
                ? ExperiencePhase.Setup
                : _gameFlow.CurrentPhase;
            TargetGrowthNormalized =
                MuscleGrowthMath.SelectTarget(
                    phase,
                    _rhythmScorer == null
                        ? 0f
                        : _rhythmScorer.GrowthNormalized,
                    _gameFlow == null
                        ? 0f
                        : _gameFlow.CompletedGrowthNormalized);
            RefreshChannelTargets(phase);
        }

        /// <summary>
        /// Per-part targets follow the scorer only during Training. Every
        /// later phase reads GameFlow's immutable boundary snapshot.
        /// </summary>
        private void RefreshChannelTargets(ExperiencePhase phase)
        {
            foreach (var channel in PartChannels)
            {
                var index = (int)channel;
                var liveGrowth = _rhythmScorer == null
                    ? 0f
                    : _rhythmScorer
                        .GetChannelGrowthNormalized(channel);
                var completedGrowth = _gameFlow == null
                    ? 0f
                    : _gameFlow.CompletedMuscleGrowth.Get(
                        channel);
                _targetChannelGrowth[index] =
                    MuscleGrowthMath.SelectTarget(
                        phase,
                        liveGrowth,
                        completedGrowth);
            }
        }

        /// <summary>Smoothed growth for one muscle group.</summary>
        public float GetAppliedChannelGrowth(
            MuscleGrowthChannel channel)
        {
            if (channel == MuscleGrowthChannel.Overall)
            {
                return AppliedGrowthNormalized;
            }

            var index = (int)channel;
            return index >= 0 &&
                   index < _appliedChannelGrowth.Length
                ? _appliedChannelGrowth[index]
                : 0f;
        }

        /// <summary>
        /// Smoothed growth for one muscle group plus whatever a recent
        /// milestone is still adding. Can exceed 1, because a milestone is
        /// allowed to drive the shape past fully trained;
        /// <see cref="GetAppliedChannelGrowth"/> remains the earned value that
        /// scoring is based on.
        /// </summary>
        public float GetPresentedChannelGrowth(
            MuscleGrowthChannel channel)
        {
            return GetAppliedChannelGrowth(channel) +
                   _pulses.GetOvershoot(channel);
        }

        /// <summary>
        /// Earned growth only. The milestone overshoot travels beside this
        /// rather than inside it, so the snapshot every other system reads
        /// stays normalized and stays the value that was scored.
        /// </summary>
        private MuscleGrowthChannels BuildChannelSnapshot()
        {
            return new MuscleGrowthChannels(
                MuscleGrowthMath.SanitizeNormalized(
                    AppliedGrowthNormalized),
                GetAppliedChannelGrowth(
                    MuscleGrowthChannel.ElbowFlexionLeft),
                GetAppliedChannelGrowth(
                    MuscleGrowthChannel.ElbowFlexionRight),
                GetAppliedChannelGrowth(
                    MuscleGrowthChannel.ArmRaiseLeft),
                GetAppliedChannelGrowth(
                    MuscleGrowthChannel.ArmRaiseRight),
                GetAppliedChannelGrowth(
                    MuscleGrowthChannel.ChestLeft),
                GetAppliedChannelGrowth(
                    MuscleGrowthChannel.ChestRight));
        }

        private void SetGrowthImmediate(float value)
        {
            value = MuscleGrowthMath.SanitizeNormalized(value);
            TargetGrowthNormalized = value;
            AppliedGrowthNormalized = value;
            RefreshChannelTargets(
                _gameFlow == null
                    ? ExperiencePhase.Setup
                    : _gameFlow.CurrentPhase);
            Array.Copy(
                _targetChannelGrowth,
                _appliedChannelGrowth,
                _targetChannelGrowth.Length);
            ApplyToOutputs(true);
        }

        private void ApplyToOutputs(bool force)
        {
            var growth =
                MuscleGrowthMath.SanitizeNormalized(
                    AppliedGrowthNormalized);
            if (!force &&
                Mathf.Approximately(
                    growth,
                    _lastDispatchedGrowth))
            {
                return;
            }

            var channels = BuildChannelSnapshot();
            var availableCount = 0;
            var availableSignature = 17;
            foreach (var entry in _outputs)
            {
                if (!IsEntryAvailable(entry))
                {
                    continue;
                }

                entry.Output.ApplyGrowth(growth);

                // Outputs that understand muscle groups additionally receive
                // the per-part snapshot.
                if (entry.Output is IMuscleGrowthChannelOutput
                    channelOutput)
                {
                    channelOutput.ApplyChannelGrowth(channels);
                }

                // The body lights as a whole, so the brightest group speaks
                // for all of them. Which group it was still reads from the
                // shape, which is per-part.
                if (entry.Output is IMusclePulseOutput pulseOutput)
                {
                    pulseOutput.ApplyPulse(
                        _pulses.StrongestGlow,
                        TrainingRhythmMath.GetTimingGrade(
                            _pulses.StrongestGlowAccuracy),
                        _pulses.CaptureOvershoot());
                }

                availableCount++;
                availableSignature = unchecked(
                    availableSignature * 31 +
                    entry.Behaviour.GetInstanceID());
            }

            _lastAvailableOutputCount = availableCount;
            _lastAvailableOutputSignature =
                availableSignature;
            _lastDispatchedGrowth = growth;
            GrowthApplied?.Invoke(growth);
        }

        private void ResetOutputs()
        {
            TargetGrowthNormalized = 0f;
            AppliedGrowthNormalized = 0f;
            _pulses.Clear();
            ResetOutputEntries();

            _lastDispatchedGrowth = 0f;
            _lastAvailableOutputCount =
                GetAvailableOutputState(
                    out _lastAvailableOutputSignature);
            GrowthApplied?.Invoke(0f);
        }

        private void ResetOutputEntries()
        {
            foreach (var entry in _outputs)
            {
                if (entry.Behaviour == null ||
                    entry.Output == null)
                {
                    continue;
                }

                entry.Output.ResetGrowth();
            }
        }

        private void CacheOutputs()
        {
            _outputBehaviours ??=
                Array.Empty<MonoBehaviour>();
            var outputs = new List<OutputEntry>(
                _outputBehaviours.Length);
            var seen = new HashSet<MonoBehaviour>();
            foreach (var behaviour in _outputBehaviours)
            {
                if (behaviour == null ||
                    !seen.Add(behaviour) ||
                    behaviour is not IMuscleGrowthOutput output)
                {
                    continue;
                }

                outputs.Add(
                    new OutputEntry(behaviour, output));
            }

            _outputs = outputs.ToArray();
            _lastAvailableOutputCount = -1;
            _lastAvailableOutputSignature = 0;
            _lastDispatchedGrowth = float.NaN;
        }

        private int GetAvailableOutputCount()
        {
            return GetAvailableOutputState(out _);
        }

        private int GetAvailableOutputState(
            out int signature)
        {
            var count = 0;
            signature = 17;
            foreach (var entry in _outputs)
            {
                if (IsEntryAvailable(entry))
                {
                    count++;
                    signature = unchecked(
                        signature * 31 +
                        entry.Behaviour.GetInstanceID());
                }
            }

            return count;
        }

        private static bool IsEntryAvailable(
            OutputEntry entry)
        {
            return entry.Behaviour != null &&
                   entry.Output != null &&
                   entry.Output.IsAvailable;
        }

        private void Subscribe()
        {
            if (_rhythmScorer != null)
            {
                _rhythmScorer.ScoreChanged -=
                    HandleScoreChanged;
                _rhythmScorer.ScoreChanged +=
                    HandleScoreChanged;
                _rhythmScorer.RepTimingResultReady -=
                    HandleRepTimingResult;
                _rhythmScorer.RepTimingResultReady +=
                    HandleRepTimingResult;
            }

            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -=
                    HandlePhaseChanged;
                _gameFlow.PhaseChanged +=
                    HandlePhaseChanged;
            }
        }

        private void Unsubscribe()
        {
            if (_rhythmScorer != null)
            {
                _rhythmScorer.ScoreChanged -=
                    HandleScoreChanged;
                _rhythmScorer.RepTimingResultReady -=
                    HandleRepTimingResult;
            }

            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -=
                    HandlePhaseChanged;
            }
        }

        private void ResolveReferences()
        {
            _gameFlow ??=
                FindFirstObjectByType<GameFlowController>();
            _rhythmScorer ??=
                FindFirstObjectByType<TrainingRhythmScorer>();
        }

        private void ValidateSettings()
        {
            _responseSeconds =
                float.IsNaN(_responseSeconds) ||
                float.IsInfinity(_responseSeconds)
                    ? 0.12f
                    : Mathf.Max(
                        0.01f,
                        _responseSeconds);

            // A zero half-life cannot be authored: the Inspector forbids it.
            // Reaching here means this component predates the pulse settings
            // and Unity deserialized them as an empty struct, which would
            // leave the feature silently switched off.
            if (_pulseSettings.GlowHalfLifeSeconds <= 0f)
            {
                _pulseSettings = MusclePulseSettings.Default;
            }

            _pulseSettings = _pulseSettings.Sanitized();
        }

        private readonly struct OutputEntry
        {
            public OutputEntry(
                MonoBehaviour behaviour,
                IMuscleGrowthOutput output)
            {
                Behaviour = behaviour;
                Output = output;
            }

            public MonoBehaviour Behaviour { get; }
            public IMuscleGrowthOutput Output { get; }
        }
    }
}
