using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace IVRC
{
    public enum PosingRuntimeState
    {
        Inactive,
        Countdown,
        Matching,
        Holding,
        Completed,
        TrackingLost,
        Settling,
        Unavailable
    }

    public enum PoseTrackingReadiness
    {
        Ready,
        TrackingLost,
        Settling
    }

    /// <summary>
    /// Keeps a newly reacquired HMD/controller sample out of pose scoring
    /// until it has remained valid briefly. The frame that completes the
    /// delay is still presentation-only, so its full delta time can never be
    /// mistaken for hold progress.
    /// </summary>
    public sealed class PoseTrackingRecoveryState
    {
        public PoseTrackingReadiness Readiness { get; private set; } =
            PoseTrackingReadiness.Ready;
        public float StableSeconds { get; private set; }

        private bool _requiresSettling;
        private bool _releaseOnNextValidSample;

        public PoseTrackingReadiness Observe(
            bool trackingValid,
            float deltaTime,
            float requiredStableSeconds)
        {
            if (!trackingValid)
            {
                _requiresSettling = true;
                _releaseOnNextValidSample = false;
                StableSeconds = 0f;
                Readiness = PoseTrackingReadiness.TrackingLost;
                return Readiness;
            }

            if (_releaseOnNextValidSample)
            {
                _requiresSettling = false;
                _releaseOnNextValidSample = false;
                StableSeconds = 0f;
                Readiness = PoseTrackingReadiness.Ready;
                return Readiness;
            }

            if (!_requiresSettling)
            {
                StableSeconds = 0f;
                Readiness = PoseTrackingReadiness.Ready;
                return Readiness;
            }

            var safeDeltaTime =
                float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime)
                    ? 0f
                    : Mathf.Max(0f, deltaTime);
            var safeRequiredSeconds =
                PoseTrackingRecoverySafety.ClampDuration(
                    requiredStableSeconds);
            StableSeconds = Mathf.Min(
                safeRequiredSeconds,
                StableSeconds + safeDeltaTime);
            if (StableSeconds + 0.0001f >= safeRequiredSeconds)
            {
                _releaseOnNextValidSample = true;
            }

            Readiness = PoseTrackingReadiness.Settling;
            return Readiness;
        }

        public void Reset()
        {
            _requiresSettling = false;
            _releaseOnNextValidSample = false;
            StableSeconds = 0f;
            Readiness = PoseTrackingReadiness.Ready;
        }
    }

    public static class PoseTrackingRecoverySafety
    {
        public const float MinimumDurationSeconds = 0.10f;
        public const float MaximumDurationSeconds = 0.75f;
        public const float DefaultDurationSeconds = 0.35f;

        public static float ClampDuration(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
            {
                return DefaultDurationSeconds;
            }

            return Mathf.Clamp(
                seconds,
                MinimumDurationSeconds,
                MaximumDurationSeconds);
        }
    }

    /// <summary>
    /// Samples the HMD and both Touch controllers, then feeds one deterministic
    /// three-point pose sample per LateUpdate into PosingScoreCalculator.
    /// The retargeted avatar is deliberately not used as a scoring source.
    /// </summary>
    [DefaultExecutionOrder(10300)]
    public sealed class ThreePointPosingScorer : MonoBehaviour
    {
        [Header("Flow")]
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private AvatarRegistry _avatarRegistry;

        [Tooltip(
            "The poses asked for, in order. The routine advances when a " +
            "pose is held or when its window runs out.")]
        [SerializeField] private PosingScoreProfile[] _profiles =
            Array.Empty<PosingScoreProfile>();
        [Tooltip(
            "Legacy single-pose field. Folded into Profiles on validate so " +
            "scenes authored before the routine keep working.")]
        [SerializeField] private PosingScoreProfile _profile;
        [Tooltip(
            "Scoring window for ONE pose, on top of that pose's countdown.")]
        [SerializeField, Min(0.1f)]
        private float _poseWindowSeconds = 10f;

        [Header("Tracking")]
        [SerializeField] private OVRCameraRig _cameraRig;
        [SerializeField] private Transform _stageFrame;
        [SerializeField] private HeightCalibrationController
            _heightCalibration;
        [SerializeField, Min(0f)]
        private float _calibratedArmLengthMeters;
        [SerializeField]
        private bool _requireRuntimeTrackingValidity = true;
        [SerializeField, Range(
            PoseTrackingRecoverySafety.MinimumDurationSeconds,
            PoseTrackingRecoverySafety.MaximumDurationSeconds)]
        [Tooltip(
            "Valid tracking must remain stable for this long after a loss " +
            "before pose scoring, holding, and phase time resume.")]
        private float _trackingRecoverySeconds =
            PoseTrackingRecoverySafety.DefaultDurationSeconds;

        [Header("Editor Pose Override")]
        [SerializeField] private bool _usePoseOverrides;
        [SerializeField] private bool _overrideTrackingValid = true;
        [SerializeField] private Transform _headOverride;
        [SerializeField] private Transform _leftHandOverride;
        [SerializeField] private Transform _rightHandOverride;

        private readonly List<PosingRoutineEntry> _entries =
            new List<PosingRoutineEntry>(4);
        private PoseHoldState _holdState;
        private bool _isScoring;
        private bool _poseSucceeded;
        private float _poseWindowRemaining;
        private float _fallbackStageYawDegrees;
        private readonly PoseTrackingRecoveryState
            _trackingRecoveryState =
                new PoseTrackingRecoveryState();

        public event Action<PoseScoreResult> ScoreChanged;
        public event Action<PosingScoreProfile> PoseChanged;
        public event Action<PoseCompletionResult> PoseCompleted;

        public PosingRuntimeState State { get; private set; } =
            PosingRuntimeState.Inactive;
        public PoseScoreResult CurrentResult { get; private set; }
        public PoseScoreResult BestResult { get; private set; }
        public PoseScoreResult FinalResult { get; private set; }
        public bool IsTrackingValid { get; private set; }
        public bool IsReadyForScoring =>
            IsTrackingValid &&
            _trackingRecoveryState.Readiness ==
                PoseTrackingReadiness.Ready;
        public bool IsSettlingAfterTrackingRecovery =>
            _trackingRecoveryState.Readiness ==
                PoseTrackingReadiness.Settling;
        public bool HasCompleted { get; private set; }
        public bool HasSucceeded { get; private set; }
        public float CountdownRemaining { get; private set; }
        public string WaitingReason { get; private set; } = "INACTIVE";

        private PosingScoreProfile[] ActiveProfiles
        {
            get
            {
                var avatarProfiles =
                    _avatarRegistry == null ||
                    _avatarRegistry.Active == null
                        ? null
                        : _avatarRegistry.Active.PosingProfiles;
                return avatarProfiles != null &&
                       avatarProfiles.Length > 0
                    ? avatarProfiles
                    : _profiles;
            }
        }

        public int PoseCount =>
            ActiveProfiles == null ? 0 : ActiveProfiles.Length;

        public int CurrentPoseIndex { get; private set; }

        /// <summary>
        /// The pose being asked for right now. Every consumer reads the
        /// target from here so the guide cannot drift out of step with what
        /// is actually being scored.
        /// </summary>
        public PosingScoreProfile CurrentProfile =>
            ActiveProfiles != null &&
            CurrentPoseIndex >= 0 &&
            CurrentPoseIndex < ActiveProfiles.Length
                ? ActiveProfiles[CurrentPoseIndex]
                : null;

        public IReadOnlyList<PosingRoutineEntry> PoseResults =>
            _entries;

        public bool HasCompletedRoutine { get; private set; }

        public int ClearedPoseCount =>
            PosingRoutineCalculator.CountCleared(_entries);

        public float RoutineScore =>
            PosingRoutineCalculator.AverageScore(_entries);

        public float PoseWindowRemaining => _poseWindowRemaining;

        public float TotalRoutineSeconds
        {
            get
            {
                var countdowns = new List<float>(PoseCount);
                for (var index = 0; index < PoseCount; index++)
                {
                    var profile = ActiveProfiles[index];
                    countdowns.Add(
                        profile == null
                            ? 0f
                            : profile.CountdownSeconds);
                }

                return PosingRoutineCalculator.TotalSeconds(
                    countdowns,
                    _poseWindowSeconds);
            }
        }

        /// <summary>
        /// Where the player is in the routine, for the stage HUD. Empty for
        /// a single pose, where a counter would only be noise.
        /// </summary>
        public string RoutineProgressText =>
            PoseCount <= 1
                ? string.Empty
                : $"POSE {Mathf.Min(CurrentPoseIndex + 1, PoseCount)}" +
                  $" / {PoseCount}";

        public string TargetDisplayName =>
            CurrentProfile == null
                ? "POSE"
                : CurrentProfile.DisplayName;

        public string TargetInstruction =>
            CurrentProfile == null
                ? "POSE PROFILE NOT ASSIGNED"
                : CurrentProfile.Instruction;

        public float RequiredHoldSeconds =>
            CurrentProfile == null
                ? 0f
                : CurrentProfile.ToSettings().Hold.RequiredSeconds;

        public float CountdownDuration =>
            CurrentProfile == null
                ? 0f
                : CurrentProfile.CountdownSeconds;

        public float HoldRemainingSeconds =>
            Mathf.Max(
                0f,
                RequiredHoldSeconds - CurrentResult.HeldSeconds);

        public float HoldProgressNormalized =>
            Mathf.Clamp01(CurrentResult.HoldScore / 100f);

        public float CurrentScoreNormalized =>
            Mathf.Clamp01(CurrentResult.TotalScore / 100f);

        /// <summary>
        /// The routine average, which is what the final result grades. The
        /// last pose alone would under-report a strong routine.
        /// </summary>
        public float FinalScore => RoutineScore;

        public float CurrentPoseScore => FinalResult.TotalScore;

        public string StatusText
        {
            get
            {
                var progress = RoutineProgressText;
                return string.IsNullOrEmpty(progress)
                    ? StateText
                    : $"{progress}  {StateText}";
            }
        }

        private string StateText => State switch
        {
            PosingRuntimeState.Countdown => "GET READY",
            PosingRuntimeState.Matching => "MATCH THE TARGET",
            PosingRuntimeState.Holding => "POSE LOCKED",
            PosingRuntimeState.Completed =>
                HasCompletedRoutine
                    ? "ROUTINE COMPLETE"
                    : "POSE CLEARED",
            PosingRuntimeState.TrackingLost => WaitingReason,
            PosingRuntimeState.Settling => "TRACKING SETTLING",
            PosingRuntimeState.Unavailable => WaitingReason,
            _ => "WAITING"
        };

        private void Awake()
        {
            ResolveReferences();
            ValidateSettings();
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            ResetScore();
            if (_gameFlow != null &&
                _gameFlow.CurrentPhase == ExperiencePhase.Posing)
            {
                BeginRound();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            _isScoring = false;
            _trackingRecoveryState.Reset();
        }

        private void OnValidate()
        {
            ResolveReferences();
            ValidateSettings();
        }

        private void LateUpdate()
        {
            var hasSample = TryBuildSample(
                out var sample,
                out var waitingReason);
            ProcessSample(
                hasSample,
                sample,
                waitingReason,
                Time.unscaledDeltaTime);
        }

        private void ProcessSample(
            bool hasSample,
            ThreePointPoseSample sample,
            string waitingReason,
            float deltaTime)
        {
            deltaTime =
                float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime)
                    ? 0f
                    : Mathf.Max(0f, deltaTime);
            IsTrackingValid = hasSample && sample.IsFullyTracked;
            WaitingReason = IsTrackingValid
                ? string.Empty
                : waitingReason ?? string.Empty;

            if (!_isScoring ||
                _gameFlow == null ||
                _gameFlow.CurrentPhase != ExperiencePhase.Posing)
            {
                return;
            }

            if (CurrentProfile == null)
            {
                State = PosingRuntimeState.Unavailable;
                WaitingReason = "POSE PROFILE MISSING";
                return;
            }

            if (!IsTrackingValid)
            {
                _trackingRecoveryState.Observe(
                    false,
                    0f,
                    _trackingRecoverySeconds);
                State = PosingRuntimeState.TrackingLost;
                EvaluateSample(sample, deltaTime);
                return;
            }

            if (!CanScoreSample(sample))
            {
                EvaluateSample(sample, deltaTime);
                IsTrackingValid = false;
                _trackingRecoveryState.Observe(
                    false,
                    0f,
                    _trackingRecoverySeconds);
                State = PosingRuntimeState.TrackingLost;
                WaitingReason = "INVALID POSE SAMPLE";
                return;
            }

            if (_trackingRecoveryState.Observe(
                    true,
                    deltaTime,
                    _trackingRecoverySeconds) !=
                PoseTrackingReadiness.Ready)
            {
                State = PosingRuntimeState.Settling;
                WaitingReason = "TRACKING SETTLING";
                return;
            }

            WaitingReason = string.Empty;

            if (CountdownRemaining > Mathf.Epsilon)
            {
                CountdownRemaining = Mathf.Max(
                    0f,
                    CountdownRemaining - deltaTime);
                State = PosingRuntimeState.Countdown;
                return;
            }

            // The window is only spent once the pose is actually scorable,
            // so a tracking dropout cannot eat somebody's attempt.
            _poseWindowRemaining = Mathf.Max(
                0f,
                _poseWindowRemaining - deltaTime);

            EvaluateSample(sample, deltaTime);
            if (!CurrentResult.IsValid)
            {
                State = PosingRuntimeState.TrackingLost;
                WaitingReason = "INVALID POSE SAMPLE";
                return;
            }

            State = CurrentResult.IsHolding
                ? PosingRuntimeState.Holding
                : PosingRuntimeState.Matching;

            if (CurrentResult.TotalScore > BestResult.TotalScore ||
                (Mathf.Approximately(
                     CurrentResult.TotalScore,
                     BestResult.TotalScore) &&
                 CurrentResult.HeldSeconds >
                 BestResult.HeldSeconds))
            {
                BestResult = CurrentResult;
            }

            var held =
                CurrentResult.IsHolding &&
                CurrentResult.HoldScore >= 100f - 0.001f;
            if (!held && _poseWindowRemaining > Mathf.Epsilon)
            {
                return;
            }

            if (held && CurrentResult.TotalScore >
                BestResult.TotalScore)
            {
                BestResult = CurrentResult;
            }

            CompleteCurrentPose(held);
        }

        /// <summary>
        /// Records the pose that just ended and moves to the next one, or
        /// closes the routine when that was the last.
        /// </summary>
        private void CompleteCurrentPose(bool succeeded)
        {
            var completedPoseIndex = CurrentPoseIndex;
            var name = CurrentProfile == null
                ? "POSE"
                : CurrentProfile.DisplayName;
            FinalResult = BestResult;
            _poseSucceeded = succeeded;
            _entries.Add(
                new PosingRoutineEntry(
                    FinalResult.TotalScore,
                    succeeded));
            Debug.Log(
                succeeded
                    ? $"[PoseScore] {name} cleared at " +
                      $"{FinalResult.TotalScore:F0}/100."
                    : $"[PoseScore] {name} timed out at " +
                      $"{FinalResult.TotalScore:F0}/100.",
                this);

            PoseCompleted?.Invoke(
                new PoseCompletionResult(
                    completedPoseIndex,
                    PoseCount,
                    name,
                    FinalResult.TotalScore,
                    succeeded,
                    ClearedPoseCount));

            CurrentPoseIndex =
                PosingRoutineCalculator.NextIndex(
                    CurrentPoseIndex,
                    PoseCount);
            if (PosingRoutineCalculator.IsRoutineComplete(
                    CurrentPoseIndex,
                    PoseCount))
            {
                CloseRoutine();
                return;
            }

            BeginCurrentPose();
        }

        private void CloseRoutine()
        {
            HasCompleted = true;
            HasCompletedRoutine = true;
            HasSucceeded =
                PosingRoutineCalculator.AllCleared(
                    _entries,
                    PoseCount);
            State = PosingRuntimeState.Completed;
            _isScoring = false;
            ScoreChanged?.Invoke(CurrentResult);
            Debug.Log(
                $"[PoseScore] Routine finished: " +
                $"{ClearedPoseCount}/{PoseCount} cleared, " +
                $"average {RoutineScore:F0}/100.",
                this);
        }

        /// <summary>
        /// Arms the pose at the current index. Per-pose scoring state is
        /// cleared so one pose cannot inherit another's hold or best score.
        /// </summary>
        private void BeginCurrentPose()
        {
            CurrentResult = default;
            BestResult = default;
            FinalResult = default;
            _holdState = default;
            _poseSucceeded = false;
            _poseWindowRemaining = _poseWindowSeconds;
            CountdownRemaining = CurrentProfile == null
                ? 0f
                : CurrentProfile.CountdownSeconds;
            State = CountdownRemaining > Mathf.Epsilon
                ? PosingRuntimeState.Countdown
                : PosingRuntimeState.Matching;
            WaitingReason = string.Empty;
            PoseChanged?.Invoke(CurrentProfile);
            ScoreChanged?.Invoke(CurrentResult);
        }

        /// <summary>
        /// The stage yaw the targets are expressed in. Exposed so a desktop
        /// driver can build a sample in the same frame the scorer reads it.
        /// </summary>
        public float StageYawDegrees => GetStageYawDegrees();

        /// <summary>
        /// The body scale the last evaluation used, or 0 before the first
        /// one. A driver needs it to place hands at a normalized target.
        /// </summary>
        public float LastBodyScaleMeters =>
            CurrentResult.BodyScaleMeters;

        /// <summary>
        /// Feeds the scorer from three transforms instead of the headset.
        /// Desktop simulation only: it bypasses the runtime tracking checks,
        /// so nothing may call this on the headset path.
        /// </summary>
        public void UsePoseOverrides(
            Transform head,
            Transform leftHand,
            Transform rightHand,
            bool trackingValid = true)
        {
            _headOverride = head;
            _leftHandOverride = leftHand;
            _rightHandOverride = rightHand;
            _overrideTrackingValid = trackingValid;
            _usePoseOverrides =
                head != null &&
                leftHand != null &&
                rightHand != null;
        }

        [ContextMenu("Begin Pose Round")]
        public void BeginRound()
        {
            ResolveReferences();
            ValidateSettings();

            _entries.Clear();
            CurrentPoseIndex = 0;
            HasCompleted = false;
            HasCompletedRoutine = false;
            HasSucceeded = false;
            CaptureFallbackStageYaw();

            if (CurrentProfile == null)
            {
                CurrentResult = default;
                BestResult = default;
                FinalResult = default;
                _holdState = default;
                CountdownRemaining = 0f;
                _isScoring = false;
                State = PosingRuntimeState.Unavailable;
                WaitingReason = "POSE PROFILE MISSING";
                return;
            }

            _isScoring = true;
            _trackingRecoveryState.Reset();
            BeginCurrentPose();
        }

        [ContextMenu("Finalize Pose Round")]
        public void FinishRound()
        {
            if (HasCompleted && !_isScoring)
            {
                return;
            }

            // Record whatever the unfinished poses managed, so a routine cut
            // short by the phase timer still reports honestly rather than
            // silently scoring the remainder as zero attempts.
            while (!PosingRoutineCalculator.IsRoutineComplete(
                       CurrentPoseIndex,
                       PoseCount))
            {
                FinalResult = BestResult;
                _entries.Add(
                    new PosingRoutineEntry(
                        FinalResult.TotalScore,
                        _poseSucceeded));
                CurrentPoseIndex =
                    PosingRoutineCalculator.NextIndex(
                        CurrentPoseIndex,
                        PoseCount);
                BestResult = default;
                _poseSucceeded = false;
            }

            CloseRoutine();
        }

        [ContextMenu("Reset Pose Score")]
        public void ResetScore()
        {
            _entries.Clear();
            CurrentPoseIndex = 0;
            CurrentResult = default;
            BestResult = default;
            FinalResult = default;
            _holdState = default;
            IsTrackingValid = false;
            HasCompleted = false;
            HasCompletedRoutine = false;
            HasSucceeded = false;
            _poseSucceeded = false;
            _poseWindowRemaining = 0f;
            CountdownRemaining = 0f;
            _trackingRecoveryState.Reset();
            WaitingReason = "INACTIVE";
            State = PosingRuntimeState.Inactive;
            _isScoring = false;
            PoseChanged?.Invoke(CurrentProfile);
            ScoreChanged?.Invoke(CurrentResult);
        }

        /// <summary>
        /// Records whatever is being tracked right now as a reference pose,
        /// through the same transform scoring reads targets through, so a pose
        /// captured here scores as correct when it is repeated.
        ///
        /// Exists because the shipped targets are typed-in round numbers. A
        /// participant holding a front double biceps properly was being marked
        /// against hand positions nobody had ever stood in, and the tracking
        /// cannot see the elbows that actually define the pose — it infers
        /// them from these same three points — so the only way to make the
        /// target honest is to measure it off a body.
        /// </summary>
        public bool TryCaptureTarget(
            out PosingScoreTarget target,
            out string reason)
        {
            target = default;
            if (!TryBuildSample(out var sample, out reason))
            {
                return false;
            }

            if (!sample.IsFullyTracked)
            {
                reason = "TRACKING INCOMPLETE";
                return false;
            }

            if (!PosingScoreCalculator.TryResolveCaptureScale(
                    CurrentProfile,
                    GetBodyScales(),
                    out var bodyScale))
            {
                reason = "BODY SCALE NOT RESOLVED";
                return false;
            }

            target = PosingScoreCalculator.BuildTarget(
                sample,
                GetStageYawDegrees(),
                bodyScale);
            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// Where the solver put the elbows while the pose was held, in the
        /// form the guide reads them.
        ///
        /// Separate from the target because it is not scored: three-point
        /// tracking infers elbows from the head and hands, so scoring them
        /// would weigh the same three points twice. The guide still has to
        /// show them, and showing the ones a real body produced is what stops
        /// it asking for a bend nobody made.
        /// </summary>
        public bool TryCaptureElbowHints(
            out Vector3 leftHint,
            out Vector3 rightHint,
            out string reason)
        {
            leftHint = Vector3.zero;
            rightHint = Vector3.zero;

            ResolveReferences();
            var metrics = FindFirstObjectByType<BodyPoseMetrics>(
                FindObjectsInactive.Include);
            if (metrics == null)
            {
                reason = "BODY POSE METRICS MISSING";
                return false;
            }

            var leftShoulder = metrics.LeftShoulder;
            var leftElbow = metrics.LeftElbow;
            var rightShoulder = metrics.RightShoulder;
            var rightElbow = metrics.RightElbow;
            if (leftShoulder == null ||
                leftElbow == null ||
                rightShoulder == null ||
                rightElbow == null)
            {
                reason = "ARM BONES NOT RESOLVED";
                return false;
            }

            if (!PosingScoreCalculator.TryResolveCaptureScale(
                    CurrentProfile,
                    GetBodyScales(),
                    out var bodyScale))
            {
                reason = "BODY SCALE NOT RESOLVED";
                return false;
            }

            var stageRotation = Quaternion.Euler(
                0f,
                GetStageYawDegrees(),
                0f);
            leftHint = PoseGhostGuideController.BuildElbowHint(
                leftShoulder.position,
                leftElbow.position,
                stageRotation,
                bodyScale);
            rightHint = PoseGhostGuideController.BuildElbowHint(
                rightShoulder.position,
                rightElbow.position,
                stageRotation,
                bodyScale);
            reason = string.Empty;
            return true;
        }

        private void EvaluateSample(
            ThreePointPoseSample sample,
            float deltaTime)
        {
            var settings = CurrentProfile.ToSettings();
            var result = PosingScoreCalculator.Evaluate(
                sample,
                GetStageYawDegrees(),
                GetBodyScales(),
                settings,
                _holdState,
                deltaTime,
                out _holdState);
            CurrentResult = result;
            ScoreChanged?.Invoke(result);
        }

        private bool CanScoreSample(
            ThreePointPoseSample sample)
        {
            var settings = CurrentProfile.ToSettings();
            var preview = PosingScoreCalculator.Evaluate(
                sample,
                GetStageYawDegrees(),
                GetBodyScales(),
                settings,
                _holdState,
                0f,
                out _);
            return preview.IsValid;
        }

        private bool TryBuildSample(
            out ThreePointPoseSample sample,
            out string waitingReason)
        {
            ResolveReferences();
            if (_usePoseOverrides)
            {
                return TryBuildOverrideSample(
                    out sample,
                    out waitingReason);
            }

            if (_cameraRig == null ||
                _cameraRig.trackingSpace == null ||
                _cameraRig.centerEyeAnchor == null)
            {
                sample = default;
                waitingReason = "CAMERA RIG MISSING";
                return false;
            }

            var headTracked =
                !_requireRuntimeTrackingValidity ||
                IsHeadTrackingValid();
            var leftTracked =
                !_requireRuntimeTrackingValidity ||
                IsTouchControllerTrackingValid(
                    OVRInput.Controller.LTouch);
            var rightTracked =
                !_requireRuntimeTrackingValidity ||
                IsTouchControllerTrackingValid(
                    OVRInput.Controller.RTouch);

            var trackingSpace = _cameraRig.trackingSpace;
            var leftLocalPosition =
                OVRInput.GetLocalControllerPosition(
                    OVRInput.Controller.LTouch);
            var rightLocalPosition =
                OVRInput.GetLocalControllerPosition(
                    OVRInput.Controller.RTouch);
            var leftLocalRotation =
                OVRInput.GetLocalControllerRotation(
                    OVRInput.Controller.LTouch);
            var rightLocalRotation =
                OVRInput.GetLocalControllerRotation(
                    OVRInput.Controller.RTouch);

            sample = new ThreePointPoseSample(
                new Pose(
                    _cameraRig.centerEyeAnchor.position,
                    _cameraRig.centerEyeAnchor.rotation),
                new Pose(
                    trackingSpace.TransformPoint(leftLocalPosition),
                    trackingSpace.rotation * leftLocalRotation),
                new Pose(
                    trackingSpace.TransformPoint(rightLocalPosition),
                    trackingSpace.rotation * rightLocalRotation),
                headTracked,
                leftTracked,
                rightTracked);

            if (!headTracked)
            {
                waitingReason = "HMD TRACKING LOST";
                return false;
            }

            if (!leftTracked)
            {
                waitingReason = "LEFT CONTROLLER LOST";
                return false;
            }

            if (!rightTracked)
            {
                waitingReason = "RIGHT CONTROLLER LOST";
                return false;
            }

            waitingReason = string.Empty;
            return true;
        }

        private bool TryBuildOverrideSample(
            out ThreePointPoseSample sample,
            out string waitingReason)
        {
            if (_headOverride == null ||
                _leftHandOverride == null ||
                _rightHandOverride == null)
            {
                sample = default;
                waitingReason = "POSE OVERRIDES MISSING";
                return false;
            }

            sample = new ThreePointPoseSample(
                new Pose(
                    _headOverride.position,
                    _headOverride.rotation),
                new Pose(
                    _leftHandOverride.position,
                    _leftHandOverride.rotation),
                new Pose(
                    _rightHandOverride.position,
                    _rightHandOverride.rotation),
                _overrideTrackingValid,
                _overrideTrackingValid,
                _overrideTrackingValid);
            waitingReason = _overrideTrackingValid
                ? string.Empty
                : "OVERRIDE TRACKING LOST";
            return _overrideTrackingValid;
        }

        private static bool IsTouchControllerTrackingValid(
            OVRInput.Controller controller)
        {
            return
                OVRInput.IsControllerConnected(controller) &&
                OVRInput.GetControllerPositionValid(controller) &&
                OVRInput.GetControllerOrientationValid(controller);
        }

        private static bool IsHeadTrackingValid()
        {
            var device =
                InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!device.isValid)
            {
                return false;
            }

            if (device.TryGetFeatureValue(
                    CommonUsages.isTracked,
                    out var isTracked) &&
                !isTracked)
            {
                return false;
            }

            if (device.TryGetFeatureValue(
                    CommonUsages.trackingState,
                    out var trackingState))
            {
                const InputTrackingState required =
                    InputTrackingState.Position |
                    InputTrackingState.Rotation;
                return (trackingState & required) == required;
            }

            return true;
        }

        private PosingBodyScales GetBodyScales()
        {
            var heightMeters = _heightCalibration == null
                ? EstimateHeightFromTrackingSpace()
                : _heightCalibration.HeightMeters;
            return new PosingBodyScales(
                heightMeters,
                _calibratedArmLengthMeters);
        }

        private float EstimateHeightFromTrackingSpace()
        {
            if (_cameraRig == null ||
                _cameraRig.trackingSpace == null ||
                _cameraRig.centerEyeAnchor == null)
            {
                return 0f;
            }

            var eyeHeight = _cameraRig.trackingSpace
                .InverseTransformPoint(
                    _cameraRig.centerEyeAnchor.position)
                .y;
            return Mathf.Max(0f, eyeHeight + 0.11f);
        }

        private float GetStageYawDegrees()
        {
            return _stageFrame == null
                ? _fallbackStageYawDegrees
                : _stageFrame.eulerAngles.y;
        }

        private void CaptureFallbackStageYaw()
        {
            var source = _cameraRig == null
                ? null
                : _cameraRig.centerEyeAnchor;
            if (source == null)
            {
                _fallbackStageYawDegrees = 0f;
                return;
            }

            var forward = Vector3.ProjectOnPlane(
                source.forward,
                Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                _fallbackStageYawDegrees =
                    source.eulerAngles.y;
                return;
            }

            _fallbackStageYawDegrees =
                Quaternion.LookRotation(
                    forward.normalized,
                    Vector3.up)
                .eulerAngles.y;
        }

        private void HandlePhaseChanged(ExperiencePhase phase)
        {
            if (phase == ExperiencePhase.Setup)
            {
                ResetScore();
            }
            else if (phase == ExperiencePhase.Posing)
            {
                BeginRound();
            }
            else if (phase == ExperiencePhase.Results)
            {
                FinishRound();
            }
        }

        private void Subscribe()
        {
            if (_gameFlow == null)
            {
                return;
            }

            _gameFlow.PhaseChanged -= HandlePhaseChanged;
            _gameFlow.PhaseChanged += HandlePhaseChanged;
        }

        private void Unsubscribe()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= HandlePhaseChanged;
            }
        }

        private void ResolveReferences()
        {
            _gameFlow ??=
                FindFirstObjectByType<GameFlowController>();
            _avatarRegistry ??=
                FindFirstObjectByType<AvatarRegistry>();
            _cameraRig ??=
                FindFirstObjectByType<OVRCameraRig>();
            _heightCalibration ??=
                FindFirstObjectByType<HeightCalibrationController>();
            if (_stageFrame == null)
            {
                var stageTransition =
                    FindFirstObjectByType<StageTransitionController>();
                if (stageTransition != null)
                {
                    _stageFrame =
                        stageTransition.StageArrivalPoint;
                }
            }
        }

        private void ValidateSettings()
        {
            _calibratedArmLengthMeters = Mathf.Max(
                0f,
                _calibratedArmLengthMeters);
            _poseWindowSeconds = Mathf.Max(
                0.1f,
                _poseWindowSeconds);
            _trackingRecoverySeconds =
                PoseTrackingRecoverySafety.ClampDuration(
                    _trackingRecoverySeconds);
            _profiles ??= Array.Empty<PosingScoreProfile>();
            if (_profiles.Length <= 0 && _profile != null)
            {
                _profiles = new[] { _profile };
            }
        }
    }
}
