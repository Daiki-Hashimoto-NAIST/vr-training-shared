using UnityEngine;

namespace IVRC
{
    public static class BeatHapticsSafety
    {
        public const float MinimumPulseDurationSeconds = 0.01f;
        public const float MaximumPulseDurationSeconds = 0.20f;

        public static float ClampPulseDuration(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
            {
                return MinimumPulseDurationSeconds;
            }

            return Mathf.Clamp(
                seconds,
                MinimumPulseDurationSeconds,
                MaximumPulseDurationSeconds);
        }
    }

    /// <summary>
    /// Converts RhythmClock beats into short Touch controller pulses.
    /// The first beat of each bar is intentionally stronger.
    /// </summary>
    public sealed class BeatHaptics : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private RhythmClock _rhythmClock;
        [SerializeField] private TrainingRhythmScorer _rhythmScorer;

        [Header("Pulse")]
        [SerializeField, Range(0f, 1f)] private float _regularAmplitude = 0.35f;
        [SerializeField, Range(0f, 1f)] private float _firstBeatAmplitude = 0.65f;
        [SerializeField, Range(0f, 1f)] private float _frequency = 1f;
        [SerializeField, Range(
            BeatHapticsSafety.MinimumPulseDurationSeconds,
            BeatHapticsSafety.MaximumPulseDurationSeconds)]
        private float _durationSeconds = 0.08f;

        [Header("Rep Completion")]
        [SerializeField, Range(0f, 1f)]
        private float _completionAmplitude = 0.45f;
        [SerializeField, Range(
            BeatHapticsSafety.MinimumPulseDurationSeconds,
            BeatHapticsSafety.MaximumPulseDurationSeconds)]
        private float _completionDurationSeconds = 0.09f;

        [Header("Rhythm Clear")]
        [SerializeField, Range(0f, 1f)]
        private float _successMinimumAmplitude = 0.55f;
        [SerializeField, Range(0f, 1f)]
        private float _successMaximumAmplitude = 0.9f;
        [SerializeField, Range(0f, 1f)]
        private float _successFrequency = 1f;
        [SerializeField, Range(
            BeatHapticsSafety.MinimumPulseDurationSeconds,
            BeatHapticsSafety.MaximumPulseDurationSeconds)]
        private float _successDurationSeconds = 0.14f;

        private double _stopAtDspTime;
        private bool _isPulsing;
        private bool _leftIsPulsing;
        private bool _rightIsPulsing;
        private int _activePulsePriority;

        private void Awake()
        {
            ResolveReferences();
            ValidateSettings();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (_rhythmClock != null)
            {
                _rhythmClock.BeatOccurred -= HandleBeat;
                _rhythmClock.BeatOccurred += HandleBeat;
            }

            if (_rhythmScorer != null)
            {
                _rhythmScorer.RepTimingResultReady -=
                    HandleRepTimingResult;
                _rhythmScorer.RepTimingResultReady +=
                    HandleRepTimingResult;
            }
        }

        private void Update()
        {
            if (_isPulsing && AudioSettings.dspTime >= _stopAtDspTime)
            {
                StopPulse();
            }
        }

        private void OnDisable()
        {
            if (_rhythmClock != null)
            {
                _rhythmClock.BeatOccurred -= HandleBeat;
            }

            if (_rhythmScorer != null)
            {
                _rhythmScorer.RepTimingResultReady -=
                    HandleRepTimingResult;
            }

            StopPulse();
        }

        private void OnValidate()
        {
            ResolveReferences();
            ValidateSettings();
        }

        private void HandleBeat(RhythmBeat beat)
        {
            var amplitude =
                beat.BeatInBar == 0
                    ? _firstBeatAmplitude
                    : _regularAmplitude;

            StartPulse(
                _frequency,
                amplitude,
                _durationSeconds,
                0);
        }

        private void HandleRepTimingResult(
            TrainingRepTimingResult result)
        {
            if (!TrainingSuccessFeedbackRules
                    .IsMotionCompleted(result))
            {
                return;
            }

            if (!TrainingSuccessFeedbackRules
                    .IsRhythmClear(result))
            {
                StartPulse(
                    _successFrequency,
                    _completionAmplitude,
                    _completionDurationSeconds,
                    1);
                return;
            }

            var amplitude = Mathf.Lerp(
                _successMinimumAmplitude,
                _successMaximumAmplitude,
                result.CombinedAccuracy);
            StartPulse(
                _successFrequency,
                amplitude,
                _successDurationSeconds,
                2);
        }

        private void StartPulse(
            float frequency,
            float amplitude,
            float durationSeconds,
            int priority)
        {
            if (_isPulsing && priority < _activePulsePriority)
            {
                return;
            }

            var leftIsConnected = OVRInput.IsControllerConnected(
                OVRInput.Controller.LTouch);
            var rightIsConnected = OVRInput.IsControllerConnected(
                OVRInput.Controller.RTouch);
            if (!leftIsConnected && !rightIsConnected)
            {
                return;
            }

            if (leftIsConnected)
            {
                OVRInput.SetControllerVibration(
                    frequency,
                    amplitude,
                    OVRInput.Controller.LTouch);
                _leftIsPulsing = true;
            }

            if (rightIsConnected)
            {
                OVRInput.SetControllerVibration(
                    frequency,
                    amplitude,
                    OVRInput.Controller.RTouch);
                _rightIsPulsing = true;
            }

            _stopAtDspTime =
                AudioSettings.dspTime +
                BeatHapticsSafety.ClampPulseDuration(
                    durationSeconds);
            _isPulsing = true;
            _activePulsePriority = priority;
        }

        private void StopPulse()
        {
            if (!_isPulsing)
            {
                return;
            }

            if (_leftIsPulsing &&
                OVRInput.IsControllerConnected(OVRInput.Controller.LTouch))
            {
                OVRInput.SetControllerVibration(
                    0f,
                    0f,
                    OVRInput.Controller.LTouch);
            }

            if (_rightIsPulsing &&
                OVRInput.IsControllerConnected(OVRInput.Controller.RTouch))
            {
                OVRInput.SetControllerVibration(
                    0f,
                    0f,
                    OVRInput.Controller.RTouch);
            }

            _leftIsPulsing = false;
            _rightIsPulsing = false;
            _isPulsing = false;
            _activePulsePriority = 0;
        }

        private void ResolveReferences()
        {
            _rhythmClock ??= FindFirstObjectByType<RhythmClock>();
            _rhythmScorer ??=
                FindFirstObjectByType<TrainingRhythmScorer>();
        }

        private void ValidateSettings()
        {
            _regularAmplitude = Mathf.Clamp01(_regularAmplitude);
            _firstBeatAmplitude = Mathf.Clamp01(_firstBeatAmplitude);
            _frequency = Mathf.Clamp01(_frequency);
            _durationSeconds =
                BeatHapticsSafety.ClampPulseDuration(
                    _durationSeconds);
            _completionAmplitude = Mathf.Clamp01(
                _completionAmplitude);
            _completionDurationSeconds =
                BeatHapticsSafety.ClampPulseDuration(
                    _completionDurationSeconds);
            _successMinimumAmplitude = Mathf.Clamp01(
                _successMinimumAmplitude);
            _successMaximumAmplitude = Mathf.Clamp(
                _successMaximumAmplitude,
                _successMinimumAmplitude,
                1f);
            _successFrequency = Mathf.Clamp01(
                _successFrequency);
            _successDurationSeconds =
                BeatHapticsSafety.ClampPulseDuration(
                    _successDurationSeconds);
        }
    }
}
