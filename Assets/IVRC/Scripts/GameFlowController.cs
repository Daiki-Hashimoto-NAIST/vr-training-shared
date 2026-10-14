using System;
using UnityEngine;

namespace IVRC
{
    public enum ExperiencePhase
    {
        Setup,
        Ready,
        Tutorial,
        Countdown,
        Training,
        Transformation,
        StageTransition,
        StageEntrance,
        Posing,
        Results
    }

    /// <summary>
    /// Owns the first end-to-end prototype flow. Deep scoring, muscle output,
    /// posing, and hardware integrations can subscribe without owning phase timing.
    /// </summary>
    public sealed class GameFlowController : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private OVRInput.RawButton _startButton = OVRInput.RawButton.A;

        [Header("Systems")]
        [SerializeField] private RhythmClock _rhythmClock;
        [SerializeField] private TrainingRhythmScorer _rhythmScorer;
        [SerializeField] private TrainingRhythmChart _trainingRhythmChart;
        [SerializeField] private BodyPoseMetrics _metrics;
        [SerializeField] private BodyRepCounter _repCounter;
        [SerializeField] private AvatarCalibrationController _alignmentCalibration;
        [SerializeField] private HeightCalibrationController _heightCalibration;
        [SerializeField] private StageTransitionController _stageTransition;
        [SerializeField] private StageEntranceCinematicController
            _stageEntrance;
        [SerializeField] private ThreePointPosingScorer _posingScorer;
        [Tooltip(
            "Optional. Without one the flow goes straight from Ready to " +
            "the countdown, exactly as before.")]
        [SerializeField] private TutorialController _tutorial;
        [SerializeField] private AvatarSwapInput _avatarSwapInput;

#if UNITY_EDITOR
        private IStageTransitionDriver
            _stageTransitionDriverOverride = null;
#endif

        [Header("Phase Roots")]
        [SerializeField] private GameObject _trainingRoot;
        [SerializeField] private GameObject _posingRoot;
        [SerializeField] private GameObject _stageEntranceRoot;
        [SerializeField] private bool _hideTrainingDuringPosing;

        [Header("Durations")]
        [SerializeField, Min(0.1f)] private float _countdownSeconds = 3f;
        [SerializeField, Min(0.1f)] private float _trainingSeconds = 20f;
        [SerializeField, Min(0.1f)] private float _transformationSeconds = 3f;
        [SerializeField, Min(0.1f)]
        private float _stageTransitionFallbackSeconds = 1.2f;
        [SerializeField, Min(0.1f)]
        private float _stageEntranceFallbackSeconds = 6.5f;
        [Tooltip(
            "Fallback only. With a posing routine assigned the phase length " +
            "comes from the routine itself.")]
        [SerializeField, Min(0.1f)] private float _posingSeconds = 10f;
        [Tooltip(
            "Slack added to the routine length. The routine closes itself " +
            "pose by pose; this timer only has to outlast it.")]
        [SerializeField, Min(0f)]
        private float _posingTimerMarginSeconds = 2f;

        [Header("Readiness")]
        [SerializeField] private bool _requireReadiness = true;

        public event Action<ExperiencePhase> PhaseChanged;

        public ExperiencePhase CurrentPhase { get; private set; }
        public float PhaseTimeRemaining { get; private set; }
        public float CurrentPhaseDuration { get; private set; }
        public bool IsPausedForTracking { get; private set; }
        public StageTransitionDestination TransitionDestination =>
            _transitionDestination;
        public int CompletedElbowReps { get; private set; }
        public int CompletedArmRaiseReps { get; private set; }
        public float CompletedTimingAccuracy { get; private set; }
        public int CompletedTimingPointCount { get; private set; }
        public MuscleGrowthSnapshot CompletedMuscleGrowth
        {
            get;
            private set;
        }
        public float CompletedGrowthNormalized =>
            CompletedMuscleGrowth.Overall;
        public bool CompletedPoseSucceeded { get; private set; }
        public int CompletedPoseSuccessCount { get; private set; }
        public int CompletedPoseCount { get; private set; }

        private StageTransitionDestination _transitionDestination;
        private ExperiencePhase _phaseAfterTransition;
        private float _durationAfterTransition;
        private bool _transitionStartedWithController;
        private bool _isSafeTransitionRecovery;
        private float _transitionWatchdogRemaining;
        private bool _trainingTimeoutPending;
        private bool _posingTimeoutPending;

        private IStageTransitionDriver StageTransitionDriver
        {
            get
            {
#if UNITY_EDITOR
                if (_stageTransitionDriverOverride != null)
                {
                    return _stageTransitionDriverOverride;
                }
#endif
                // Check Unity's destroyed-object null semantics before
                // exposing the component through a plain C# interface.
                return _stageTransition == null
                    ? null
                    : _stageTransition;
            }
        }

        public bool CanStartExperience =>
            CurrentPhase == ExperiencePhase.Ready && IsReadinessSatisfied();

        public int CountdownNumber =>
            CurrentPhase == ExperiencePhase.Countdown
                ? Mathf.Max(1, Mathf.CeilToInt(PhaseTimeRemaining))
                : 0;

        public float NormalizedPhaseProgress =>
            CurrentPhaseDuration <= Mathf.Epsilon
                ? 0f
                : 1f - Mathf.Clamp01(
                    PhaseTimeRemaining / CurrentPhaseDuration);

        public string PhaseDisplayName => CurrentPhase switch
        {
            ExperiencePhase.Setup => "SETUP",
            ExperiencePhase.Ready => "READY",
            ExperiencePhase.Tutorial => "TUTORIAL",
            ExperiencePhase.Countdown => "COUNTDOWN",
            ExperiencePhase.Training => "TRAINING",
            ExperiencePhase.Transformation => "MUSCLE UP",
            ExperiencePhase.StageTransition =>
                _transitionDestination == StageTransitionDestination.Training
                    ? "RETURNING"
                    : "TO THE STAGE",
            ExperiencePhase.StageEntrance => "STAGE ENTRANCE",
            ExperiencePhase.Posing => "POSING",
            ExperiencePhase.Results => "RESULTS",
            _ => CurrentPhase.ToString().ToUpperInvariant()
        };

        public string InstructionText
        {
            get
            {
                if (CurrentPhase == ExperiencePhase.Setup)
                {
                    return GetSetupInstruction();
                }

                return CurrentPhase switch
                {
                    ExperiencePhase.Ready => "PRESS A TO START",
                    ExperiencePhase.Tutorial =>
                        _tutorial == null
                            ? string.Empty
                            : _tutorial.MessageText,
                    ExperiencePhase.Countdown => CountdownNumber.ToString(),
                    ExperiencePhase.Training when IsPausedForTracking =>
                        "TRACKING PAUSED",
                    ExperiencePhase.Training => "MOVE TO THE BEAT",
                    ExperiencePhase.Transformation => "MUSCLE UP!",
                    ExperiencePhase.StageTransition =>
                        _transitionDestination ==
                        StageTransitionDestination.Training
                            ? "BACK TO MIRROR"
                            : "MOVING TO STAGE",
                    ExperiencePhase.StageEntrance =>
                        "ENTERING THE STAGE",
                    ExperiencePhase.Posing when IsPausedForTracking =>
                        _posingScorer == null ||
                        string.IsNullOrWhiteSpace(
                            _posingScorer.WaitingReason)
                            ? "TRACKING PAUSED"
                            : _posingScorer.WaitingReason,
                    ExperiencePhase.Posing =>
                        _posingScorer == null
                            ? "HOLD YOUR POSE"
                            : _posingScorer.StatusText,
                    ExperiencePhase.Results => "PRESS A TO RETRY",
                    _ => string.Empty
                };
            }
        }

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

        private void Start()
        {
            ResetExperience();
        }

        private void Update()
        {
            Advance(
                Time.unscaledDeltaTime,
                OVRInput.GetDown(_startButton));
        }

        private void Advance(
            float unscaledDeltaTime,
            bool startPressed)
        {
            var deltaTime = SanitizeDeltaTime(
                unscaledDeltaTime);
            RefreshSetupReadyState();

            if (startPressed)
            {
                var phaseBeforeInput = CurrentPhase;
                StartOrRestartExperience();
                if (CurrentPhase != phaseBeforeInput)
                {
                    return;
                }
            }

            switch (CurrentPhase)
            {
                case ExperiencePhase.Setup:
                case ExperiencePhase.Ready:
                    break;

                case ExperiencePhase.Tutorial:
                    UpdateTutorial();
                    break;

                case ExperiencePhase.Countdown:
                    if (!IsReadinessSatisfied())
                    {
                        EnterPhase(ExperiencePhase.Setup, 0f);
                        break;
                    }

                    if (TickPhaseTimer(deltaTime))
                    {
                        EnterPhase(
                            ExperiencePhase.Training,
                            GetTrainingPhaseDuration());
                    }
                    break;

                case ExperiencePhase.Training:
                    UpdateTraining(deltaTime);
                    break;

                case ExperiencePhase.Transformation:
                    if (TickPhaseTimer(deltaTime))
                    {
                        BeginStageTransition(
                            StageTransitionDestination.Judges,
                            ExperiencePhase.StageEntrance,
                            GetStageEntrancePhaseDuration());
                    }
                    break;

                case ExperiencePhase.StageTransition:
                    UpdateStageTransition(deltaTime);
                    break;

                case ExperiencePhase.StageEntrance:
                    if (TickPhaseTimer(deltaTime))
                    {
                        BeginStageTransition(
                            StageTransitionDestination.Stage,
                            ExperiencePhase.Posing,
                            GetPosingPhaseDuration());
                    }
                    break;

                case ExperiencePhase.Posing:
                    UpdatePosing(deltaTime);
                    break;
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Ends the current timed phase immediately, letting the normal
        /// transition run. Iterating on the stage should not cost a full
        /// training round every time, but jumping straight to a phase would
        /// skip the snapshots the later phases read, so this only shortens
        /// the clock rather than bypassing the state machine.
        /// </summary>
        [ContextMenu("Debug: End Current Phase")]
        public void DebugEndCurrentPhase()
        {
            if (CurrentPhaseDuration <= 0f)
            {
                return;
            }

            PhaseTimeRemaining = 0f;
            IsPausedForTracking = false;
        }

        /// <summary>
        /// Restarts from any phase while preserving completed calibration.
        /// Unlike the normal A-button path, this is intentionally allowed in
        /// the middle of Training, entrance, or Posing for rapid iteration.
        /// </summary>
        [ContextMenu("Debug: Restart Experience")]
        public void DebugRestartExperience()
        {
            ResetExperienceState(true);
            RefreshSetupReadyState();
            StartOrRestartExperience();
        }
#endif

        [ContextMenu("Start or Restart Experience")]
        public void StartOrRestartExperience()
        {
            if (CanStartExperience)
            {
                BeginAfterReady();
            }
            else if (CurrentPhase == ExperiencePhase.Results)
            {
                BeginStageTransition(
                    StageTransitionDestination.Training,
                    ExperiencePhase.Setup,
                    0f);
            }
        }

        [ContextMenu("Reset Experience")]
        public void ResetExperience()
        {
            ResetExperienceState(true);
        }

        private void ResetExperienceState(
            bool restoreTrainingPose)
        {
            CompletedElbowReps = 0;
            CompletedArmRaiseReps = 0;
            CompletedTimingAccuracy = 0f;
            CompletedTimingPointCount = 0;
            CompletedMuscleGrowth =
                MuscleGrowthSnapshot.Zero;
            CompletedPoseSucceeded = false;
            CompletedPoseSuccessCount = 0;
            CompletedPoseCount = 0;
            _repCounter?.ResetCounts();
            _rhythmClock?.ResetClock();
            if (restoreTrainingPose)
            {
                StageTransitionDriver
                    ?.RestoreTrainingPose();
            }

            _transitionStartedWithController = false;
            _isSafeTransitionRecovery = false;
            _transitionWatchdogRemaining = 0f;
            _trainingTimeoutPending = false;
            _posingTimeoutPending = false;
            EnterPhase(ExperiencePhase.Setup, 0f);
        }

        /// <summary>
        /// Runs the introduction, then hands over to the countdown. Message
        /// steps may advance on time, while practice steps close only after
        /// their required successful repetitions.
        /// </summary>
        private void UpdateTutorial()
        {
            if (_tutorial == null || _tutorial.IsComplete)
            {
                EnterPhase(
                    ExperiencePhase.Countdown,
                    _countdownSeconds);
                return;
            }

            TickPhaseTimer(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Ready leads into the introduction when one is configured. With no
        /// tutorial in the scene the flow is unchanged, so a half-wired
        /// scene cannot strand anyone in a phase with no way out.
        /// </summary>
        private void BeginAfterReady()
        {
            if (_tutorial == null || !_tutorial.HasSteps)
            {
                EnterPhase(
                    ExperiencePhase.Countdown,
                    _countdownSeconds);
                return;
            }

            _tutorial.BeginTutorial();
            EnterPhase(
                ExperiencePhase.Tutorial,
                _tutorial.WorstCaseSeconds);
        }

        private void UpdateTraining(float deltaTime)
        {
            var trainingReady =
                !_requireReadiness ||
                _repCounter == null ||
                _repCounter.IsReady;
            if (!trainingReady)
            {
                IsPausedForTracking = true;
                _rhythmClock?.StopClock();
                return;
            }

            if (IsPausedForTracking || (_rhythmClock != null && !_rhythmClock.IsRunning))
            {
                _rhythmClock?.ResumeClock();
            }

            IsPausedForTracking = false;
            if (!TickPhaseTimer(deltaTime))
            {
                _trainingTimeoutPending = false;
                return;
            }

            // BodyRepCounter and TrainingRhythmScorer process in LateUpdate.
            // Keep one final frame at the time boundary before snapshotting.
            if (!_trainingTimeoutPending)
            {
                _trainingTimeoutPending = true;
                return;
            }

            CaptureTrainingResults();
            EnterPhase(
                ExperiencePhase.Transformation,
                _transformationSeconds);
        }

        private void UpdatePosing(float deltaTime)
        {
            // The routine ends itself once the last pose is recorded,
            // whether that pose was held or ran out of window.
            if (_posingScorer != null &&
                _posingScorer.HasCompletedRoutine)
            {
                CapturePosingResults();
                EnterPhase(ExperiencePhase.Results, 0f);
                return;
            }

            var posingReady =
                !_requireReadiness ||
                (_posingScorer != null
                    ? _posingScorer.IsReadyForScoring
                    : _metrics != null &&
                      _metrics.IsTrackingValid);
            if (!posingReady)
            {
                IsPausedForTracking = true;
                return;
            }

            IsPausedForTracking = false;
            if (!TickPhaseTimer(deltaTime))
            {
                _posingTimeoutPending = false;
                return;
            }

            // Scoring runs in LateUpdate after the tracked poses settle.
            // Keep one final frame so a hold completed exactly at the time
            // boundary is not discarded by Update order.
            if (!_posingTimeoutPending)
            {
                _posingTimeoutPending = true;
                return;
            }

            _posingScorer?.FinishRound();
            CapturePosingResults();
            EnterPhase(ExperiencePhase.Results, 0f);
        }

        private void UpdateStageTransition(float deltaTime)
        {
            TickPhaseTimer(deltaTime);
            _transitionWatchdogRemaining = Mathf.Max(
                0f,
                _transitionWatchdogRemaining - deltaTime);

            var stageTransition = StageTransitionDriver;
            if (!_transitionStartedWithController ||
                stageTransition == null)
            {
                Debug.LogError(
                    "[GameFlow] Stage transition controller was lost. " +
                    "Returning to setup.",
                    this);
                ResetExperienceState(false);
                return;
            }

            if (stageTransition.HasRelocated)
            {
                ApplyPhaseVisibility();
            }
            if (!stageTransition.HasCompleted)
            {
                if (!stageTransition.IsTransitioning)
                {
                    HandleStageTransitionFailure(
                        "Stage transition was interrupted.");
                    return;
                }

                if (_transitionWatchdogRemaining <= Mathf.Epsilon)
                {
                    if (_isSafeTransitionRecovery)
                    {
                        HandleStageTransitionFailure(
                            "Safe stage-transition recovery timed out.");
                        return;
                    }

                    BeginSafeTransitionRecovery(
                        "Stage transition timed out.");
                    return;
                }

                return;
            }

            if (!stageTransition.LastTransitionSucceeded)
            {
                HandleStageTransitionFailure(
                    "Stage transition failed.");
                return;
            }

            if (PhaseTimeRemaining > Mathf.Epsilon)
            {
                return;
            }

            if (_transitionDestination ==
                StageTransitionDestination.Training)
            {
                ResetExperienceState(false);
                return;
            }

            _isSafeTransitionRecovery = false;
            EnterPhase(_phaseAfterTransition, _durationAfterTransition);
        }

        private void BeginStageTransition(
            StageTransitionDestination destination,
            ExperiencePhase phaseAfterTransition,
            float durationAfterTransition)
        {
            _transitionDestination = destination;
            _phaseAfterTransition = phaseAfterTransition;
            _durationAfterTransition = Mathf.Max(
                0f,
                durationAfterTransition);
            _transitionStartedWithController = false;
            _isSafeTransitionRecovery = false;

            var stageTransition = StageTransitionDriver;
            if (stageTransition == null)
            {
                Debug.LogError(
                    "[GameFlow] StageTransitionController is required.",
                    this);
                if (destination == StageTransitionDestination.Training)
                {
                    EnterPhase(ExperiencePhase.Results, 0f);
                }
                else
                {
                    ResetExperienceState(false);
                }

                return;
            }

            if (!stageTransition.BeginTransition(destination))
            {
                if (stageTransition.IsTransitioning)
                {
                    BeginSafeTransitionRecovery(
                        "A competing stage transition was detected.");
                    return;
                }

                HandleStageTransitionFailure(
                    "Stage transition could not be started.");
                return;
            }

            _transitionStartedWithController = true;
            _transitionWatchdogRemaining =
                GetStageTransitionWatchdogDuration();
            EnterPhase(
                ExperiencePhase.StageTransition,
                GetStageTransitionDuration());
        }

        private void BeginSafeTransitionRecovery(string reason)
        {
            if (_isSafeTransitionRecovery)
            {
                HandleStageTransitionFailure(
                    $"{reason} Safe recovery was already in progress.");
                return;
            }

            Debug.LogError(
                $"[GameFlow] {reason} Recovering through a black fade.",
                this);
            var stageTransition = StageTransitionDriver;
            if (stageTransition == null ||
                !stageTransition.BeginSafeRecoveryToTraining())
            {
                HandleStageTransitionFailure(
                    $"{reason} Safe recovery could not be started.");
                return;
            }

            _transitionDestination =
                StageTransitionDestination.Training;
            _phaseAfterTransition = ExperiencePhase.Setup;
            _durationAfterTransition = 0f;
            _transitionStartedWithController = true;
            _isSafeTransitionRecovery = true;
            var recoveryDuration = GetStageTransitionDuration();
            _transitionWatchdogRemaining =
                GetStageTransitionWatchdogDuration();
            if (CurrentPhase != ExperiencePhase.StageTransition)
            {
                EnterPhase(
                    ExperiencePhase.StageTransition,
                    recoveryDuration);
            }
            else
            {
                PhaseTimeRemaining = recoveryDuration;
                ApplyPhaseVisibility();
            }
        }

        private void HandleStageTransitionFailure(string message)
        {
            Debug.LogError($"[GameFlow] {message}", this);
            _transitionStartedWithController = false;
            _isSafeTransitionRecovery = false;
            _transitionWatchdogRemaining = 0f;
            var stageTransition = StageTransitionDriver;
            stageTransition?.CancelAtCurrentLocation();
            if (stageTransition != null &&
                stageTransition.IsAtStage)
            {
                EnterPhase(ExperiencePhase.Results, 0f);
                return;
            }

            ResetExperienceState(false);
        }

        private void RefreshSetupReadyState()
        {
            if (CurrentPhase == ExperiencePhase.Setup &&
                IsReadinessSatisfied())
            {
                EnterPhase(ExperiencePhase.Ready, 0f);
            }
            else if (CurrentPhase == ExperiencePhase.Ready &&
                     !IsReadinessSatisfied())
            {
                EnterPhase(ExperiencePhase.Setup, 0f);
            }
        }

        private bool TickPhaseTimer(float deltaTime)
        {
            PhaseTimeRemaining = Mathf.Max(
                0f,
                PhaseTimeRemaining - deltaTime);
            return PhaseTimeRemaining <= Mathf.Epsilon;
        }

        private void EnterPhase(ExperiencePhase phase, float duration)
        {
            CurrentPhase = phase;
            CurrentPhaseDuration = Mathf.Max(0f, duration);
            PhaseTimeRemaining = CurrentPhaseDuration;
            IsPausedForTracking = false;

            if (phase == ExperiencePhase.Training)
            {
                _trainingTimeoutPending = false;
                _repCounter?.ResetCounts();
                _rhythmClock?.ResetClock();
            }
            else if (phase != ExperiencePhase.Countdown)
            {
                _rhythmClock?.StopClock();
            }

            if (phase == ExperiencePhase.Posing)
            {
                _posingTimeoutPending = false;
            }

            ApplyPhaseVisibility();
            ApplyInputAvailability();

            Debug.Log($"[GameFlow] Entered {phase}.", this);
            PhaseChanged?.Invoke(phase);
        }

        private void CaptureTrainingResults()
        {
            if (_repCounter == null)
            {
                CompletedElbowReps = 0;
                CompletedArmRaiseReps = 0;
            }
            else
            {
                CompletedElbowReps =
                    _repCounter.LeftElbowCount +
                    _repCounter.RightElbowCount;
                CompletedArmRaiseReps =
                    _repCounter.LeftArmRaiseCount +
                    _repCounter.RightArmRaiseCount;
            }

            CompletedTimingAccuracy =
                _rhythmScorer == null
                    ? 0f
                    : _rhythmScorer.AverageAccuracy;
            CompletedTimingPointCount =
                _rhythmScorer == null
                    ? 0
                    : _rhythmScorer.ScoredTimingPointCount;
            CompletedMuscleGrowth =
                _rhythmScorer == null
                    ? MuscleGrowthSnapshot.Zero
                    : _rhythmScorer
                        .CaptureGrowthSnapshot();
        }

        private void CapturePosingResults()
        {
            if (_posingScorer == null)
            {
                CompletedPoseSucceeded = false;
                CompletedPoseSuccessCount = 0;
                CompletedPoseCount = 0;
                return;
            }

            CompletedPoseSucceeded =
                _posingScorer.HasSucceeded;
            CompletedPoseSuccessCount =
                _posingScorer.ClearedPoseCount;
            CompletedPoseCount = _posingScorer.PoseCount;
        }

        private bool IsReadinessSatisfied()
        {
            if (!_requireReadiness)
            {
                return true;
            }

            return
                _metrics != null &&
                _metrics.IsTrackingValid &&
                (_alignmentCalibration == null ||
                 _alignmentCalibration.HasCalibrated) &&
                (_heightCalibration == null ||
                 _heightCalibration.LastCalibrationSucceeded) &&
                (_repCounter == null || _repCounter.IsReady);
        }

        private string GetSetupInstruction()
        {
            if (!_requireReadiness)
            {
                return "PREPARING";
            }

            if (_metrics == null || !_metrics.IsTrackingValid)
            {
                return "WAIT FOR BODY TRACKING";
            }

            if (_alignmentCalibration != null &&
                !_alignmentCalibration.HasCalibrated)
            {
                return "PRESS B TO ALIGN";
            }

            if (_heightCalibration != null &&
                !_heightCalibration.LastCalibrationSucceeded)
            {
                return "STAND + PRESS X";
            }

            if (_repCounter != null && !_repCounter.IsReady)
            {
                return _repCounter.WaitingReason;
            }

            return "PREPARING";
        }

        private void ApplyPhaseVisibility()
        {
            if (CurrentPhase == ExperiencePhase.StageTransition)
            {
                var stageTransition =
                    StageTransitionDriver;
                var userIsAtStage =
                    stageTransition != null &&
                    stageTransition.IsAtStage;

                if (_posingRoot != null)
                {
                    _posingRoot.SetActive(userIsAtStage);
                }

                if (_trainingRoot != null)
                {
                    _trainingRoot.SetActive(!userIsAtStage);
                }

                if (_stageEntranceRoot != null)
                {
                    var isOutgoingEntranceTransition =
                        _transitionDestination ==
                        StageTransitionDestination.Stage &&
                        _phaseAfterTransition ==
                        ExperiencePhase.Posing;
                    _stageEntranceRoot.SetActive(
                        isOutgoingEntranceTransition);
                }

                return;
            }

            var posingVisible =
                CurrentPhase == ExperiencePhase.StageEntrance ||
                CurrentPhase == ExperiencePhase.Posing ||
                CurrentPhase == ExperiencePhase.Results;

            if (_posingRoot != null)
            {
                _posingRoot.SetActive(posingVisible);
            }

            if (_trainingRoot != null)
            {
                _trainingRoot.SetActive(
                    !_hideTrainingDuringPosing ||
                    !posingVisible);
            }

            if (_stageEntranceRoot != null)
            {
                _stageEntranceRoot.SetActive(
                    CurrentPhase ==
                    ExperiencePhase.StageEntrance);
            }
        }

        private void ApplyInputAvailability()
        {
            var calibrationInputEnabled =
                CurrentPhase == ExperiencePhase.Setup ||
                CurrentPhase == ExperiencePhase.Ready;
            _alignmentCalibration?.SetInputEnabled(
                calibrationInputEnabled);
            _heightCalibration?.SetInputEnabled(
                calibrationInputEnabled);
            _avatarSwapInput?.SetInputEnabled(
                calibrationInputEnabled);
        }

        private float GetStageTransitionDuration()
        {
            var stageTransition = StageTransitionDriver;
            return stageTransition == null
                ? Mathf.Max(0.1f, _stageTransitionFallbackSeconds)
                : Mathf.Max(0.1f, stageTransition.TotalDuration);
        }

        private float GetStageTransitionWatchdogDuration()
        {
            // The coroutine has several frame boundaries in addition to the
            // configured fade durations. Leave enough grace for transient
            // low frame rates without allowing a permanent black screen.
            return GetStageTransitionDuration() + 2f;
        }

        private float GetPosingPhaseDuration()
        {
            var routineSeconds = _posingScorer == null
                ? 0f
                : _posingScorer.TotalRoutineSeconds;
            if (routineSeconds <= Mathf.Epsilon)
            {
                var countdownSeconds = _posingScorer == null
                    ? 0f
                    : _posingScorer.CountdownDuration;
                return _posingSeconds + countdownSeconds;
            }

            return routineSeconds +
                Mathf.Max(0f, _posingTimerMarginSeconds);
        }

        private float GetStageEntrancePhaseDuration()
        {
            return _stageEntrance == null
                ? Mathf.Max(
                    0.1f,
                    _stageEntranceFallbackSeconds)
                : Mathf.Max(
                    0.1f,
                    _stageEntrance.TotalDuration);
        }

        private float GetTrainingPhaseDuration()
        {
            var chartDuration =
                _trainingRhythmChart == null
                    ? 0f
                    : _trainingRhythmChart
                        .RecommendedDurationSeconds;
            return chartDuration > Mathf.Epsilon
                ? chartDuration
                : _trainingSeconds;
        }

        private static float SanitizeDeltaTime(
            float deltaTime)
        {
            if (float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime) ||
                deltaTime <= 0f)
            {
                return 0f;
            }

            return deltaTime;
        }

        private void ResolveReferences()
        {
            _rhythmClock ??= FindFirstObjectByType<RhythmClock>();
            _rhythmScorer ??=
                FindFirstObjectByType<TrainingRhythmScorer>();
            _trainingRhythmChart ??=
                FindFirstObjectByType<TrainingRhythmChart>();
            _metrics ??= FindFirstObjectByType<BodyPoseMetrics>();
            _repCounter ??= FindFirstObjectByType<BodyRepCounter>();
            _alignmentCalibration ??=
                FindFirstObjectByType<AvatarCalibrationController>();
            _heightCalibration ??=
                FindFirstObjectByType<HeightCalibrationController>();
            _avatarSwapInput ??=
                FindFirstObjectByType<AvatarSwapInput>();
            _stageTransition ??=
                FindFirstObjectByType<StageTransitionController>();
            _stageEntrance ??=
                FindFirstObjectByType<
                    StageEntranceCinematicController>(
                    FindObjectsInactive.Include);
            _posingScorer ??=
                FindFirstObjectByType<ThreePointPosingScorer>();
            _tutorial ??=
                FindFirstObjectByType<TutorialController>(
                    FindObjectsInactive.Include);
        }

        private void ValidateSettings()
        {
            _countdownSeconds = Mathf.Max(0.1f, _countdownSeconds);
            _trainingSeconds = Mathf.Max(0.1f, _trainingSeconds);
            _transformationSeconds = Mathf.Max(
                0.1f,
                _transformationSeconds);
            _stageTransitionFallbackSeconds = Mathf.Max(
                0.1f,
                _stageTransitionFallbackSeconds);
            _stageEntranceFallbackSeconds = Mathf.Max(
                0.1f,
                _stageEntranceFallbackSeconds);
            _posingSeconds = Mathf.Max(0.1f, _posingSeconds);
        }
    }
}
