using System;
using UnityEngine;

namespace IVRC
{
    public enum BodyRepKind
    {
        ElbowFlexion,
        ArmRaise
    }

    public enum BodySide
    {
        Left,
        Right
    }

    public enum RepTimingPoint
    {
        Up,
        Down
    }

    public enum RepPhase
    {
        WaitingForStart,
        Ready,
        TargetReached
    }

    public enum RepCycleUpdateResult
    {
        None,
        UpReached,
        DownReached
    }

    /// <summary>
    /// Pure low-high-low cycle detector shared by every rep channel.
    /// It deliberately has no Unity lifecycle dependency so threshold and
    /// noise-rejection behavior can be regression-tested deterministically.
    /// </summary>
    public sealed class RepCycleDetector
    {
        private float _thresholdDuration;
        private float _lastRepTime = float.NegativeInfinity;

        public int Count { get; private set; }
        public RepPhase Phase { get; private set; } =
            RepPhase.WaitingForStart;

        public RepCycleUpdateResult Update(
            float angle,
            float startAngle,
            float targetAngle,
            float deltaTime,
            float currentTime,
            float thresholdHoldSeconds,
            float minimumRepIntervalSeconds)
        {
            switch (Phase)
            {
                case RepPhase.WaitingForStart:
                    if (HoldCondition(
                            angle <= startAngle,
                            deltaTime,
                            thresholdHoldSeconds))
                    {
                        Phase = RepPhase.Ready;
                        _thresholdDuration = 0f;
                    }
                    break;

                case RepPhase.Ready:
                    if (HoldCondition(
                            angle >= targetAngle,
                            deltaTime,
                            thresholdHoldSeconds))
                    {
                        Phase = RepPhase.TargetReached;
                        _thresholdDuration = 0f;
                        return RepCycleUpdateResult.UpReached;
                    }
                    break;

                case RepPhase.TargetReached:
                    if (HoldCondition(
                            angle <= startAngle,
                            deltaTime,
                            thresholdHoldSeconds) &&
                        currentTime - _lastRepTime >=
                        minimumRepIntervalSeconds)
                    {
                        Count++;
                        _lastRepTime = currentTime;
                        Phase = RepPhase.Ready;
                        _thresholdDuration = 0f;
                        return RepCycleUpdateResult.DownReached;
                    }
                    break;
            }

            return RepCycleUpdateResult.None;
        }

        public void Suspend()
        {
            Phase = RepPhase.WaitingForStart;
            _thresholdDuration = 0f;
        }

        public void ResetCount()
        {
            Count = 0;
            _lastRepTime = float.NegativeInfinity;
            Suspend();
        }

        private bool HoldCondition(
            bool condition,
            float deltaTime,
            float requiredDuration)
        {
            if (!condition)
            {
                _thresholdDuration = 0f;
                return false;
            }

            _thresholdDuration += Mathf.Max(0f, deltaTime);
            return _thresholdDuration >= Mathf.Max(
                0f,
                requiredDuration);
        }
    }

    /// <summary>
    /// Defines the low and high angle gates for one rep channel.
    /// Keeping these values independent makes per-person and per-side
    /// calibration possible without replacing the counting logic.
    /// </summary>
    [Serializable]
    public sealed class RepAngleThresholds
    {
        private const float MinimumThresholdGap = 5f;

        [SerializeField, Range(0f, 180f)] private float _startAngle = 30f;
        [SerializeField, Range(0f, 180f)] private float _targetAngle = 90f;

        public float StartAngle => _startAngle;
        public float TargetAngle => _targetAngle;

        public RepAngleThresholds(float startAngle, float targetAngle)
        {
            Set(startAngle, targetAngle);
        }

        public void Set(float startAngle, float targetAngle)
        {
            _startAngle = Mathf.Clamp(startAngle, 0f, 180f - MinimumThresholdGap);
            _targetAngle = Mathf.Clamp(
                targetAngle,
                _startAngle + MinimumThresholdGap,
                180f);
        }

        public void Validate()
        {
            Set(_startAngle, _targetAngle);
        }
    }

    /// <summary>
    /// Counts complete low-high-low cycles for elbow flexion and arm elevation.
    /// Counts are independent for each side and survive brief tracking loss.
    /// </summary>
    [DefaultExecutionOrder(10100)]
    public sealed class BodyRepCounter : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private BodyPoseMetrics _metrics;
        [SerializeField] private AvatarCalibrationController _alignmentCalibration;
        [SerializeField] private HeightCalibrationController _heightCalibration;

        [Header("Flow")]
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private bool _countOnlyDuringTraining = true;

        [Header("Arm Curl Thresholds")]
        [SerializeField] private RepAngleThresholds _leftElbow =
            new RepAngleThresholds(35f, 105f);
        [SerializeField] private RepAngleThresholds _rightElbow =
            new RepAngleThresholds(35f, 105f);

        [Header("Side Raise Thresholds")]
        [SerializeField] private RepAngleThresholds _leftArmRaise =
            new RepAngleThresholds(25f, 75f);
        [SerializeField] private RepAngleThresholds _rightArmRaise =
            new RepAngleThresholds(25f, 75f);

        [Header("Noise Rejection")]
        [SerializeField, Min(0f)] private float _thresholdHoldSeconds = 0.08f;
        [SerializeField, Min(0f)] private float _resumeDelaySeconds = 0.35f;
        [SerializeField, Min(0f)] private float _minimumRepIntervalSeconds = 0.35f;
        [SerializeField] private bool _requireCalibrationReady = true;

        private readonly RepCycleDetector _leftElbowChannel =
            new RepCycleDetector();
        private readonly RepCycleDetector _rightElbowChannel =
            new RepCycleDetector();
        private readonly RepCycleDetector _leftArmChannel =
            new RepCycleDetector();
        private readonly RepCycleDetector _rightArmChannel =
            new RepCycleDetector();

        private float _readyDuration;
        private bool _trainingKindFilterEnabled;
        private BodyRepKind? _activeTrainingKind;

        [Tooltip(
            "Logs every timing point. Off by default: a repetition fires " +
            "several of these in one frame, each capturing a stack trace and " +
            "each appended to the Editor console, which lands as a hitch at " +
            "exactly the moment the movement completes.")]
        [SerializeField] private bool _logRepDetails;

        public event Action<BodyRepKind, BodySide, int> RepCounted;
        public event Action<
            BodyRepKind,
            BodySide,
            RepTimingPoint,
            int> TimingPointReached;

        public int LeftElbowCount => _leftElbowChannel.Count;
        public int RightElbowCount => _rightElbowChannel.Count;
        public int LeftArmRaiseCount => _leftArmChannel.Count;
        public int RightArmRaiseCount => _rightArmChannel.Count;

        public RepPhase LeftElbowPhase => _leftElbowChannel.Phase;
        public RepPhase RightElbowPhase => _rightElbowChannel.Phase;
        public RepPhase LeftArmRaisePhase => _leftArmChannel.Phase;
        public RepPhase RightArmRaisePhase => _rightArmChannel.Phase;
        public bool TrainingKindFilterEnabled =>
            _trainingKindFilterEnabled;
        public BodyRepKind? ActiveTrainingKind =>
            _trainingKindFilterEnabled
                ? _activeTrainingKind
                : null;

        public bool IsReady { get; private set; }
        public string WaitingReason { get; private set; } = "INITIALIZING";

        private void Reset()
        {
            ResolveReferences();
            ValidateSettings();
        }

        private void Awake()
        {
            ResolveReferences();
            ValidateSettings();
        }

        private void OnEnable()
        {
            SuspendCounting("INITIALIZING");
        }

        private void OnValidate()
        {
            ResolveReferences();
            ValidateSettings();
        }

        private void LateUpdate()
        {
            if (!CanSample(out var waitingReason))
            {
                SuspendCounting(waitingReason);
                return;
            }

            _readyDuration += Time.unscaledDeltaTime;
            if (_readyDuration < _resumeDelaySeconds)
            {
                IsReady = false;
                WaitingReason = "SETTLING";
                SuspendChannels();
                return;
            }

            IsReady = true;
            WaitingReason = string.Empty;

            if (_countOnlyDuringTraining &&
                _gameFlow != null &&
                !AllowsCounting(_gameFlow.CurrentPhase))
            {
                SuspendChannels();
                return;
            }

            ProcessChannel(
                _leftElbowChannel,
                _metrics.LeftElbowFlexion,
                _leftElbow,
                BodyRepKind.ElbowFlexion,
                BodySide.Left);
            ProcessChannel(
                _rightElbowChannel,
                _metrics.RightElbowFlexion,
                _rightElbow,
                BodyRepKind.ElbowFlexion,
                BodySide.Right);
            ProcessChannel(
                _leftArmChannel,
                _metrics.LeftArmRaise,
                _leftArmRaise,
                BodyRepKind.ArmRaise,
                BodySide.Left);
            ProcessChannel(
                _rightArmChannel,
                _metrics.RightArmRaise,
                _rightArmRaise,
                BodyRepKind.ArmRaise,
                BodySide.Right);
        }

        [ContextMenu("Reset Rep Counts")]
        public void ResetCounts()
        {
            _leftElbowChannel.ResetCount();
            _rightElbowChannel.ResetCount();
            _leftArmChannel.ResetCount();
            _rightArmChannel.ResetCount();
            SuspendCounting("RESET");
        }

        public void SetElbowThresholds(
            float leftStart,
            float leftTarget,
            float rightStart,
            float rightTarget)
        {
            EnsureThresholdObjects();
            _leftElbow.Set(leftStart, leftTarget);
            _rightElbow.Set(rightStart, rightTarget);
            SuspendCounting("THRESHOLDS UPDATED");
        }

        public void SetArmRaiseThresholds(
            float leftStart,
            float leftTarget,
            float rightStart,
            float rightTarget)
        {
            EnsureThresholdObjects();
            _leftArmRaise.Set(leftStart, leftTarget);
            _rightArmRaise.Set(rightStart, rightTarget);
            SuspendCounting("THRESHOLDS UPDATED");
        }

        /// <summary>
        /// Limits Training sampling to one chart-directed exercise.
        /// Passing null keeps the filter enabled while pausing every channel.
        /// </summary>
        public void SetActiveTrainingKind(BodyRepKind? kind)
        {
            if (_trainingKindFilterEnabled &&
                _activeTrainingKind == kind)
            {
                return;
            }

            _trainingKindFilterEnabled = true;
            _activeTrainingKind = kind;
            SuspendChannels();
        }

        public void ClearActiveTrainingKindFilter()
        {
            if (!_trainingKindFilterEnabled)
            {
                return;
            }

            _trainingKindFilterEnabled = false;
            _activeTrainingKind = null;
            SuspendChannels();
        }

        private void ProcessChannel(
            RepCycleDetector channel,
            float angle,
            RepAngleThresholds thresholds,
            BodyRepKind kind,
            BodySide side)
        {
            if (_trainingKindFilterEnabled &&
                _activeTrainingKind != kind)
            {
                channel.Suspend();
                return;
            }

            var result = channel.Update(
                angle,
                thresholds.StartAngle,
                thresholds.TargetAngle,
                Time.unscaledDeltaTime,
                Time.unscaledTime,
                _thresholdHoldSeconds,
                _minimumRepIntervalSeconds);
            if (result == RepCycleUpdateResult.None)
            {
                return;
            }

            if (result == RepCycleUpdateResult.UpReached)
            {
                TimingPointReached?.Invoke(
                    kind,
                    side,
                    RepTimingPoint.Up,
                    channel.Count + 1);
                if (_logRepDetails)
                {
                    Debug.Log(
                        $"[BodyRepCounter] {kind} {side} UP timing point. " +
                        $"Angle={angle:F0} deg.",
                        this);
                }

                return;
            }

            RepCounted?.Invoke(kind, side, channel.Count);
            TimingPointReached?.Invoke(
                kind,
                side,
                RepTimingPoint.Down,
                channel.Count);
            if (_logRepDetails)
            {
                Debug.Log(
                    $"[BodyRepCounter] {kind} {side} DOWN timing point, " +
                    $"rep {channel.Count}. " +
                    $"Angle={angle:F0} deg.",
                    this);
            }
        }

        private bool CanSample(out string waitingReason)
        {
            if (_metrics == null)
            {
                waitingReason = "NO METRICS";
                return false;
            }

            if ((_alignmentCalibration != null && _alignmentCalibration.IsCalibrating) ||
                (_heightCalibration != null && _heightCalibration.IsCalibrating))
            {
                waitingReason = "CALIBRATING";
                return false;
            }

            if (!_metrics.IsTrackingValid)
            {
                waitingReason = "TRACKING";
                return false;
            }

            if (_requireCalibrationReady &&
                _alignmentCalibration != null &&
                !_alignmentCalibration.HasCalibrated)
            {
                waitingReason = "PRESS B";
                return false;
            }

            if (_requireCalibrationReady &&
                _heightCalibration != null &&
                !_heightCalibration.LastCalibrationSucceeded)
            {
                waitingReason = "PRESS X";
                return false;
            }

            waitingReason = string.Empty;
            return true;
        }

        /// <summary>
        /// Counting is confined to the phases that ask for the movement, so
        /// idle motion elsewhere cannot inflate the result. The introduction
        /// belongs here too: its practice segments are gated on recognised
        /// repetitions, and without this they were discarded and the
        /// remaining count never moved.
        /// </summary>
        public static bool AllowsCounting(ExperiencePhase phase)
        {
            return
                phase == ExperiencePhase.Training ||
                phase == ExperiencePhase.Tutorial;
        }

        private void SuspendCounting(string reason)
        {
            IsReady = false;
            WaitingReason = reason;
            _readyDuration = 0f;
            SuspendChannels();
        }

        private void SuspendChannels()
        {
            _leftElbowChannel.Suspend();
            _rightElbowChannel.Suspend();
            _leftArmChannel.Suspend();
            _rightArmChannel.Suspend();
        }

        private void ResolveReferences()
        {
            if (_metrics == null)
            {
                _metrics = FindFirstObjectByType<BodyPoseMetrics>();
            }

            if (_alignmentCalibration == null)
            {
                _alignmentCalibration =
                    FindFirstObjectByType<AvatarCalibrationController>();
            }

            if (_heightCalibration == null)
            {
                _heightCalibration =
                    FindFirstObjectByType<HeightCalibrationController>();
            }

            if (_gameFlow == null)
            {
                _gameFlow =
                    FindFirstObjectByType<GameFlowController>();
            }
        }

        private void ValidateSettings()
        {
            EnsureThresholdObjects();
            _leftElbow.Validate();
            _rightElbow.Validate();
            _leftArmRaise.Validate();
            _rightArmRaise.Validate();
            _thresholdHoldSeconds = Mathf.Max(0f, _thresholdHoldSeconds);
            _resumeDelaySeconds = Mathf.Max(0f, _resumeDelaySeconds);
            _minimumRepIntervalSeconds = Mathf.Max(
                0f,
                _minimumRepIntervalSeconds);
        }

        private void EnsureThresholdObjects()
        {
            _leftElbow ??= new RepAngleThresholds(35f, 105f);
            _rightElbow ??= new RepAngleThresholds(35f, 105f);
            _leftArmRaise ??= new RepAngleThresholds(25f, 75f);
            _rightArmRaise ??= new RepAngleThresholds(25f, 75f);
        }

    }
}
