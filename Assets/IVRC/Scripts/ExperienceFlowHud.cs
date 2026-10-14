using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IVRC
{
    /// <summary>
    /// Shows the vertical-slice phase, timer, beat, and training result.
    /// </summary>
    public sealed class ExperienceFlowHud : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private GameFlowController _gameFlow;
        [Tooltip(
            "Optional. Supplies the introduction's own text block, which " +
            "keeps the remaining repetitions on their own line instead of " +
            "buried in the message.")]
        [SerializeField] private TutorialController _tutorial;
        [SerializeField] private RhythmClock _rhythmClock;
        [SerializeField] private BodyRepCounter _repCounter;
        [SerializeField] private TrainingRhythmScorer _rhythmScorer;
        [SerializeField] private ThreePointPosingScorer _posingScorer;
        [SerializeField] private ExperienceResultController
            _resultController;
        [SerializeField] private MuscleGrowthController
            _muscleGrowth;
        [SerializeField] private PneumaticCuffController
            _pneumaticCuff;
        [SerializeField] private AvatarRegistry _avatarRegistry;
        [SerializeField] private BodyPoseMetrics _bodyPoseMetrics;
        [SerializeField] private AvatarCalibrationController
            _alignmentCalibration;
        [SerializeField] private HeightCalibrationController
            _heightCalibration;

        [Header("View")]
        [SerializeField] private TMP_Text _text;
        [SerializeField] private Image _growthFill;
        [SerializeField] private TMP_Text _cuffStatusText;
        [SerializeField] private GameObject _firstPoseGuideRoot;
        [SerializeField] private TMP_Text _firstPoseGuideText;
        [SerializeField] private PoseGhostGuideController
            _poseGhostGuide;
        [SerializeField, Min(1f)] private float _refreshRate = 10f;
        [SerializeField, Min(0.1f)]
        private float _poseCompletionMessageSeconds = 0.9f;
        [SerializeField, Min(0.1f)]
        private float _setupAcknowledgementSeconds = 0.9f;
        [SerializeField, Min(0f)]
        private float _poseAdaptiveHintDelaySeconds = 3f;
        [SerializeField, Min(0.5f)]
        private float _poseAdaptiveHintDurationSeconds = 2.5f;
        [SerializeField, Range(0f, 100f)]
        [Tooltip(
            "Fallback only. When a pose profile is active, its actual " +
            "hold threshold is used so recovery guidance cannot disappear " +
            "before the pose is holdable.")]
        private float _poseAdaptiveHintMatchThreshold = 75f;
        [SerializeField, Min(0f)]
        private float _resultRevealDurationSeconds = 0.6f;
        [SerializeField, Range(0.05f, 1f)]
        private float _resultRankRevealAtNormalized = 0.75f;

        [Header("Debug")]
        [SerializeField]
        private bool _showTechnicalDetails;

        private float _nextRefreshTime;
        private bool _hasSeenValidTracking;
        private PoseCompletionResult _lastPoseCompletion;
        private float _poseCompletionMessageUntil;
        private bool _hasPoseCompletionMessage;
        private bool _setupCompletionStateInitialized;
        private bool _previousAlignmentComplete;
        private bool _previousHeightComplete;
        private SetupReadyAcknowledgement _setupAcknowledgement;
        private float _setupAcknowledgementUntil;
        private readonly PoseAdaptiveHintState
            _poseAdaptiveHintState =
                new PoseAdaptiveHintState();
        private float _poseAdaptiveHintHideTime;
        private readonly PlayerFacingResultsRevealSession
            _resultsRevealSession =
                new PlayerFacingResultsRevealSession();

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            SubscribePoseCompletion();
            SubscribeResultsReveal();
            SyncResultsRevealPhase();
            _nextRefreshTime = 0f;
            RefreshNow();
        }

        private void OnDisable()
        {
            UnsubscribePoseCompletion();
            UnsubscribeResultsReveal();
            _resultsRevealSession.LeaveResults();
            _hasPoseCompletionMessage = false;
            ResetPoseAdaptiveHint();
            ResetSetupAcknowledgement();
            SetFirstPoseGuideVisible(false);
        }

        private void Update()
        {
            UpdatePoseAdaptiveHint();
            if (IsResultsRevealAnimating() ||
                _poseAdaptiveHintState.IsActive ||
                Time.unscaledTime >= _nextRefreshTime)
            {
                RefreshNow();
            }
        }

        [ContextMenu("Refresh Flow HUD")]
        public void RefreshNow()
        {
            _nextRefreshTime =
                Time.unscaledTime + 1f / Mathf.Max(1f, _refreshRate);
            RefreshCuffStatus();

            if (_text == null)
            {
                SetFirstPoseGuideVisible(false);
                return;
            }

            if (_gameFlow == null)
            {
                _hasPoseCompletionMessage = false;
                SetFirstPoseGuideVisible(false);
                if (_growthFill != null)
                {
                    _growthFill.fillAmount = 0f;
                }

                _text.text =
                    "<b>EXPERIENCE FLOW</b>\n" +
                    "<color=#FF7070>GAME FLOW NOT ASSIGNED</color>";
                return;
            }

            if (_gameFlow.CurrentPhase != ExperiencePhase.Posing)
            {
                _hasPoseCompletionMessage = false;
                ResetPoseAdaptiveHint();
                SetFirstPoseGuideVisible(false);
            }

            if (_gameFlow.CurrentPhase != ExperiencePhase.Setup &&
                _gameFlow.CurrentPhase != ExperiencePhase.Ready)
            {
                ResetSetupAcknowledgement();
            }

            SyncResultsRevealPhase();

            RefreshPlayerLayout();

            var phaseColor =
                _gameFlow.CurrentPhase == ExperiencePhase.Results
                    ? "#70FFB0"
                    : "#55D8FF";
            var muscleGrowth = GetAppliedMuscleGrowth();

            if (_gameFlow.CurrentPhase == ExperiencePhase.Posing)
            {
                RefreshPosingHud(muscleGrowth);
                return;
            }

            if (_gameFlow.CurrentPhase == ExperiencePhase.Results)
            {
                RefreshResultsHud();
                return;
            }

            if (_growthFill != null)
            {
                _growthFill.fillAmount =
                    ShouldShowMuscleOutput()
                        ? muscleGrowth
                        : 0f;
            }

            if (!_showTechnicalDetails)
            {
                RefreshPlayerFacingHud(
                    phaseColor,
                    muscleGrowth);
                return;
            }

            var timeText =
                _gameFlow.CurrentPhaseDuration > Mathf.Epsilon
                    ? $"{_gameFlow.PhaseTimeRemaining:0.0} s"
                    : "--";
            var beatText = FormatBeat();
            var elbowReps = GetElbowReps();
            var armReps = GetArmReps();
            var timingText = FormatTiming();
            var growthLine =
                ShouldShowMuscleOutput()
                    ? FormatMuscle(muscleGrowth)
                    : $"GROWTH       {muscleGrowth:P0}";
            _text.text =
                "<b>EXPERIENCE FLOW</b>\n" +
                FormatAvatarLine() +
                $"PHASE   <color={phaseColor}>{_gameFlow.PhaseDisplayName}</color>\n" +
                $"<color=#FFD36A>{_gameFlow.InstructionText}</color>\n\n" +
                $"TIME    {timeText}\n" +
                $"RHYTHM  {beatText}\n" +
                $"TIMING  {timingText}\n\n" +
                $"ELBOW REPS   {elbowReps:00}\n" +
                $"ARM REPS     {armReps:00}\n" +
                $"{growthLine}";
        }

        private void RefreshPlayerFacingHud(
            string phaseColor,
            float muscleGrowth)
        {
            switch (_gameFlow.CurrentPhase)
            {
                case ExperiencePhase.Setup:
                case ExperiencePhase.Ready:
                    RefreshSetupReadyHud();
                    break;
                case ExperiencePhase.Tutorial:
                    // TutorialController draws its own panel.
                    _text.text = string.Empty;
                    break;
                case ExperiencePhase.Transformation:
                    var transformation =
                        PlayerFacingFlowHudText
                            .GetTransformationMessage(
                                _gameFlow
                                    .NormalizedPhaseProgress);
                    _text.text =
                        $"<size=95%><color=#55D8FF>{transformation.Eyebrow}</color></size>\n" +
                        $"<size=170%><b>{transformation.Title}</b></size>\n" +
                        $"<color=#FFD36A>{transformation.Instruction}</color>\n" +
                        $"<size=70%><color=#8CA0AE>{transformation.Detail}</color></size>";
                    break;
                case ExperiencePhase.StageTransition:
                    var transition =
                        PlayerFacingFlowHudText.GetPhaseMessage(
                            _gameFlow.CurrentPhase,
                            _gameFlow.TransitionDestination);
                    _text.text =
                        $"<size=90%><color=#55D8FF>{transition.Eyebrow}</color></size>\n" +
                        $"<size=145%><b><color={phaseColor}>{transition.Title}</color></b></size>\n\n" +
                        $"<color=#FFD36A>{transition.Instruction}</color>\n" +
                        $"<size=70%><color=#8CA0AE>{transition.Detail}</color></size>";
                    break;
                case ExperiencePhase.StageEntrance:
                    var entrance =
                        PlayerFacingFlowHudText.GetPhaseMessage(
                            _gameFlow.CurrentPhase,
                            _gameFlow.TransitionDestination);
                    _text.text =
                        $"<size=90%><color=#55D8FF>{entrance.Eyebrow}</color></size>\n" +
                        $"<size=155%><b>{entrance.Title}</b></size>\n\n" +
                        $"<color=#FFD36A>{entrance.Instruction}</color>\n" +
                        $"<size=70%><color=#8CA0AE>{entrance.Detail}</color></size>";
                    break;
                default:
                    _text.text =
                        $"<b><color={phaseColor}>" +
                        $"{_gameFlow.PhaseDisplayName}</color></b>\n\n" +
                        "<color=#FFD36A>次の場面を準備しています</color>";
                    break;
            }
        }

        private void RefreshSetupReadyHud()
        {
            var trackingValid =
                _bodyPoseMetrics != null &&
                _bodyPoseMetrics.IsTrackingValid;
            var alignmentComplete =
                _alignmentCalibration == null ||
                _alignmentCalibration.HasCalibrated;
            var heightComplete =
                _heightCalibration == null ||
                _heightCalibration.LastCalibrationSucceeded;
            var model = SetupReadyUiModelBuilder.Build(
                new SetupReadyUiState(
                    _gameFlow.CurrentPhase == ExperiencePhase.Ready
                        ? SetupReadyPhase.Ready
                        : SetupReadyPhase.Setup,
                    trackingValid,
                    alignmentComplete,
                    heightComplete,
                    _alignmentCalibration != null &&
                    _alignmentCalibration.IsCalibrating,
                    _heightCalibration != null &&
                    _heightCalibration.IsCalibrating,
                    AvatarSwapInput.AllowsSwapping(
                        _gameFlow.CurrentPhase),
                    _hasSeenValidTracking,
                    _repCounter == null || _repCounter.IsReady,
                    _repCounter == null
                        ? string.Empty
                        : _repCounter.WaitingReason,
                    _alignmentCalibration != null &&
                    _alignmentCalibration.HasAttemptedCalibration &&
                    !_alignmentCalibration.HasCalibrated &&
                    !_alignmentCalibration.IsCalibrating,
                    _heightCalibration != null &&
                    _heightCalibration.HasAttemptedCalibration &&
                    !_heightCalibration.LastCalibrationSucceeded &&
                    !_heightCalibration.IsCalibrating));
            _hasSeenValidTracking |= trackingValid;
            UpdateSetupAcknowledgement(
                alignmentComplete,
                heightComplete);
            var acknowledgement =
                GetVisibleSetupAcknowledgement();

            var completeCount = 0;
            foreach (var item in model.Checklist)
            {
                if (item.IsComplete)
                {
                    completeCount++;
                }
            }

            var instructionColor =
                acknowledgement.IsVisible
                ? "#70FFB0"
                : (model.IsTrackingRecovery ||
                   model.HasCalibrationFailure)
                    ? "#FFD36A"
                    : model.IsReady
                        ? "#70FFB0"
                        : "#55D8FF";
            var instruction = acknowledgement.IsVisible
                ? acknowledgement.Message
                : model.IsTrackingRecovery
                    ? model.RecoveryInstruction
                    : model.MainInstruction;
            _text.text =
                $"<size=75%><color=#8CA0AE>" +
                $"準備 {completeCount}/3</color></size>\n" +
                $"<size=155%><b>{model.Title}</b></size>\n" +
                "<size=88%>" +
                $"{FormatSetupChecklistItem(model.Tracking)}\n" +
                $"{FormatSetupChecklistItem(model.Alignment)}\n" +
                $"{FormatSetupChecklistItem(model.Height)}</size>\n\n" +
                $"<size=115%><b><color={instructionColor}>" +
                $"{instruction}</color></b></size>\n\n" +
                FormatSetupPrimaryAction(model) +
                FormatAvatarLine(false);
        }

        private void UpdateSetupAcknowledgement(
            bool alignmentComplete,
            bool heightComplete)
        {
            if (!_setupCompletionStateInitialized)
            {
                _setupCompletionStateInitialized = true;
                _previousAlignmentComplete = alignmentComplete;
                _previousHeightComplete = heightComplete;
                return;
            }

            var acknowledgement =
                SetupReadyAcknowledgementBuilder.Resolve(
                    _previousAlignmentComplete,
                    alignmentComplete,
                    _previousHeightComplete,
                    heightComplete);
            _previousAlignmentComplete = alignmentComplete;
            _previousHeightComplete = heightComplete;
            if (!acknowledgement.IsVisible)
            {
                return;
            }

            _setupAcknowledgement = acknowledgement;
            _setupAcknowledgementUntil =
                Time.unscaledTime + _setupAcknowledgementSeconds;
        }

        private SetupReadyAcknowledgement
            GetVisibleSetupAcknowledgement()
        {
            if (!_setupAcknowledgement.IsVisible ||
                Time.unscaledTime >= _setupAcknowledgementUntil)
            {
                _setupAcknowledgement =
                    SetupReadyAcknowledgement.Hidden;
                _setupAcknowledgementUntil = 0f;
            }

            return _setupAcknowledgement;
        }

        private void ResetSetupAcknowledgement()
        {
            _setupCompletionStateInitialized = false;
            _previousAlignmentComplete = false;
            _previousHeightComplete = false;
            _setupAcknowledgement =
                SetupReadyAcknowledgement.Hidden;
            _setupAcknowledgementUntil = 0f;
        }

        private void RefreshPlayerLayout()
        {
            if (_text != null)
            {
                _text.textWrappingMode = TextWrappingModes.Normal;
            }

            if (_growthFill == null)
            {
                return;
            }

            var growthMeter = _growthFill.transform.parent;
            var showGrowth =
                _showTechnicalDetails ||
                PlayerFacingFlowHudText.ShouldShowProgressMeter(
                    _gameFlow.CurrentPhase);
            if (growthMeter != null &&
                growthMeter.gameObject.activeSelf != showGrowth)
            {
                growthMeter.gameObject.SetActive(showGrowth);
            }

            if (_text == null ||
                _text.rectTransform.anchorMin != Vector2.zero ||
                _text.rectTransform.anchorMax != Vector2.one)
            {
                return;
            }

            _text.rectTransform.offsetMin = new Vector2(
                32f,
                showGrowth ? 85f : 32f);
            _text.rectTransform.offsetMax = new Vector2(-32f, -32f);
        }

        private static string FormatSetupChecklistItem(
            SetupReadyChecklistItem item)
        {
            var color = item.State switch
            {
                SetupReadyStepState.Complete => "#70FFB0",
                SetupReadyStepState.Active => "#FFD36A",
                _ => "#657683"
            };
            return $"<color={color}>{item.DisplayText}</color>";
        }

        private static string FormatSetupPrimaryAction(
            SetupReadyUiModel model)
        {
            if (model.PrimaryHint.IsVisible &&
                model.PrimaryHint.HasInput)
            {
                return
                    "<size=105%><b><color=#FFD36A>" +
                    $"{model.PrimaryHint.HandLabel}の" +
                    $"{model.PrimaryHint.ButtonPositionLabel} " +
                    $"[{model.PrimaryHint.ButtonLabel}]" +
                    "</color>を押す</b></size>\n";
            }

            if (!model.Tracking.IsComplete)
            {
                return
                    "<size=70%><color=#8CA0AE>" +
                    "頭と両手を認識すると、自動で次へ進みます" +
                    "</color></size>\n";
            }

            if (model.IsCalibrationInProgress)
            {
                return
                    "<size=70%><color=#8CA0AE>" +
                    "計測が終わるまで、そのままお待ちください" +
                    "</color></size>\n";
            }

            if (model.IsWaitingForMotionReadiness)
            {
                return
                    "<size=70%><color=#8CA0AE>" +
                    "準備が整うとスタートボタンを案内します" +
                    "</color></size>\n";
            }

            return string.Empty;
        }

        private string FormatAvatarLine(bool showSwapHint = false)
        {
            var avatarName =
                _avatarRegistry == null ||
                _avatarRegistry.Active == null
                    ? "--"
                    : _avatarRegistry.Active.DisplayName;
            var swapHint =
                showSwapHint &&
                _gameFlow != null &&
                AvatarSwapInput.AllowsSwapping(
                    _gameFlow.CurrentPhase)
                    ? "\n<size=70%><color=#8CA0AE>" +
                      "任意：左手の上側 [Y] でアバター変更" +
                      "</color></size>"
                    : string.Empty;
            return
                $"アバター  <color=#70FFB0>{avatarName}</color>" +
                $"{swapHint}\n";
        }

        private void RefreshPosingHud(float muscleGrowth)
        {
            if (TryRefreshPoseCompletionMessage())
            {
                SetFirstPoseGuideVisible(false);
                return;
            }

            if (!_showTechnicalDetails)
            {
                RefreshPlayerFacingPosingHud();
                return;
            }

            SetFirstPoseGuideVisible(false);

            if (_posingScorer == null)
            {
                if (_growthFill != null)
                {
                    _growthFill.fillAmount = 0f;
                }

                _text.text =
                    "<b>POSE CHALLENGE</b>\n" +
                    "<color=#FF7070>POSE SCORER NOT ASSIGNED</color>\n\n" +
                    FormatMuscle(muscleGrowth);
                return;
            }

            if (_growthFill != null)
            {
                _growthFill.fillAmount =
                    _posingScorer.HoldProgressNormalized;
            }

            var targetName = _posingScorer.TargetDisplayName;
            var targetInstruction =
                _posingScorer.TargetInstruction;
            if (_posingScorer.State ==
                    PosingRuntimeState.TrackingLost ||
                _posingScorer.State ==
                    PosingRuntimeState.Unavailable)
            {
                _text.text =
                    "<b>POSE CHALLENGE</b>\n" +
                    $"<color=#55D8FF>{targetName}</color>\n" +
                    $"<color=#FFD36A>{targetInstruction}</color>\n\n" +
                    "<color=#FF7070>" +
                    $"{_posingScorer.StatusText}</color>\n" +
                    "SCORING PAUSED\n\n" +
                    FormatMuscle(muscleGrowth);
                return;
            }

            _text.text =
                "<b>POSE CHALLENGE</b>\n" +
                $"<color=#55D8FF>{targetName}</color>\n" +
                $"<color=#FFD36A>{targetInstruction}</color>\n" +
                "<size=120%><b><color=#FFFFFF>" +
                "お手本に合わせたポーズをとってください" +
                "</color></b></size>\n" +
                FormatMuscle(muscleGrowth);
        }

        private void RefreshPlayerFacingPosingHud()
        {
            var view =
                PlayerFacingPoseCopyFormatter.FormatPosing(
                    _posingScorer);
            RefreshPoseGuide(view);
            if (_growthFill != null)
            {
                _growthFill.fillAmount = _posingScorer == null
                    ? 0f
                    : _posingScorer.HoldProgressNormalized;
            }

            if (view.IsTrackingLost ||
                view.State ==
                    PlayerFacingPoseDisplayState.Settling ||
                view.State ==
                PlayerFacingPoseDisplayState.Unavailable)
            {
                _text.text =
                    $"<size=75%><color=#8CA0AE>{view.PoseProgress}</color></size>\n" +
                    $"<b>{view.PoseName}</b>\n" +
                    $"<color=#FFD36A>{view.Instruction}</color>\n\n" +
                    "<size=80%>あなた（大）　｜　お手本（小）</size>\n\n" +
                    $"<size=130%><b><color=#FFD36A>{view.Status}</color></b></size>\n" +
                    $"<color=#FFFFFF>{view.Recovery}</color>\n" +
                    "<size=62%><color=#8CA0AE>" +
                    $"{view.Safety}" +
                    "</color></size>";
                return;
            }

            _text.text =
                $"<size=75%><color=#8CA0AE>{view.PoseProgress}</color></size>\n" +
                $"<b><color=#55D8FF>{view.PoseName}</color></b>\n" +
                $"<color=#FFD36A>{view.Instruction}</color>\n" +
                "<size=80%>あなた（大）　｜　お手本（小）</size>\n" +
                "<size=118%><b><color=#FFFFFF>" +
                "お手本に合わせたポーズをとってください" +
                "</color></b></size>\n" +
                $"<size=62%><color=#8CA0AE>{view.Safety}</color></size>";
        }

        private void RefreshFirstPoseGuide(
            in PlayerFacingPosingView view)
        {
            var shouldShow =
                view.ShowFirstPoseGuide &&
                !view.IsTrackingLost &&
                view.State !=
                    PlayerFacingPoseDisplayState.Unavailable &&
                !string.IsNullOrWhiteSpace(view.FirstPoseGuide) &&
                _firstPoseGuideRoot != null &&
                _firstPoseGuideText != null;
            if (!shouldShow)
            {
                SetFirstPoseGuideVisible(false);
                return;
            }

            var guideLines = view.FirstPoseGuide.Split('\n');
            _firstPoseGuideText.text = guideLines.Length >= 3
                ? $"<size=82%><b>{guideLines[0]}</b></size>\n" +
                  $"<color=#FFFFFF>{guideLines[1]}</color>\n" +
                  $"<color=#70FFB0><b>{guideLines[2]}</b></color>"
                : view.FirstPoseGuide;
            SetFirstPoseGuideVisible(true);
        }

        private void RefreshPoseGuide(
            in PlayerFacingPosingView view)
        {
            if (_poseAdaptiveHintState.IsActive &&
                !view.IsTrackingLost &&
                view.State ==
                    PlayerFacingPoseDisplayState.Matching)
            {
                var guideLines =
                    PlayerFacingPoseCopyFormatter
                        .AdaptivePoseGuide.Split('\n');
                if (_firstPoseGuideText != null)
                {
                    _firstPoseGuideText.text =
                        $"<size=82%><b><color=#9BE8FF>" +
                        $"{guideLines[0]}</color></b></size>\n" +
                        $"<color=#FFFFFF>{guideLines[1]}</color>\n" +
                        $"<color=#70FFB0><b>{guideLines[2]}</b></color>";
                }

                SetFirstPoseGuideVisible(
                    _firstPoseGuideRoot != null &&
                    _firstPoseGuideText != null);
                return;
            }

            RefreshFirstPoseGuide(view);
        }

        private void UpdatePoseAdaptiveHint()
        {
            if (_gameFlow == null ||
                _gameFlow.CurrentPhase != ExperiencePhase.Posing ||
                _posingScorer == null ||
                _showTechnicalDetails ||
                _hasPoseCompletionMessage)
            {
                ApplyPoseAdaptiveHintAction(
                    _poseAdaptiveHintState.Suspend());
                return;
            }

            if (_poseAdaptiveHintState.IsActive &&
                Time.unscaledTime >= _poseAdaptiveHintHideTime)
            {
                ApplyPoseAdaptiveHintAction(
                    _poseAdaptiveHintState.Expire());
                return;
            }

            var action = _poseAdaptiveHintState.Observe(
                _posingScorer.CurrentPoseIndex,
                _posingScorer.State,
                _posingScorer.IsTrackingValid,
                _posingScorer.CurrentResult.SpatialScore,
                ResolvePoseAdaptiveHintMatchThreshold(),
                _poseAdaptiveHintDelaySeconds,
                Time.unscaledDeltaTime);
            ApplyPoseAdaptiveHintAction(action);
        }

        private float ResolvePoseAdaptiveHintMatchThreshold()
        {
            return ResolvePoseAdaptiveHintMatchThreshold(
                _posingScorer == null
                    ? null
                    : _posingScorer.CurrentProfile,
                _poseAdaptiveHintMatchThreshold);
        }

        public static float ResolvePoseAdaptiveHintMatchThreshold(
            PosingScoreProfile profile,
            float fallbackThreshold)
        {
            return profile == null
                ? Mathf.Clamp(fallbackThreshold, 0f, 100f)
                : Mathf.Clamp(
                    profile.ToSettings().Hold.MinimumSpatialScore,
                    0f,
                    100f);
        }

        private void ApplyPoseAdaptiveHintAction(
            PoseAdaptiveHintAction action)
        {
            switch (action)
            {
                case PoseAdaptiveHintAction.Show:
                    _poseAdaptiveHintHideTime =
                        Time.unscaledTime +
                        _poseAdaptiveHintDurationSeconds;
                    _poseGhostGuide?.SetAdaptiveHintActive(true);
                    _nextRefreshTime = 0f;
                    break;
                case PoseAdaptiveHintAction.Hide:
                    _poseAdaptiveHintHideTime = 0f;
                    _poseGhostGuide?.SetAdaptiveHintActive(false);
                    _nextRefreshTime = 0f;
                    break;
            }
        }

        private void ResetPoseAdaptiveHint()
        {
            _poseAdaptiveHintState.ResetSession();
            _poseAdaptiveHintHideTime = 0f;
            _poseGhostGuide?.SetAdaptiveHintActive(false);
        }

        private void SetFirstPoseGuideVisible(bool visible)
        {
            if (_firstPoseGuideRoot != null &&
                _firstPoseGuideRoot.activeSelf != visible)
            {
                _firstPoseGuideRoot.SetActive(visible);
            }
        }

        private bool TryRefreshPoseCompletionMessage()
        {
            if (!_hasPoseCompletionMessage)
            {
                return false;
            }

            if (Time.unscaledTime >= _poseCompletionMessageUntil)
            {
                _hasPoseCompletionMessage = false;
                return false;
            }

            if (!_lastPoseCompletion.Succeeded)
            {
                return false;
            }

            var view =
                PlayerFacingPoseCopyFormatter.FormatPoseCompletion(
                    _lastPoseCompletion);
            if (_growthFill != null)
            {
                _growthFill.fillAmount = view.IsSuccess ? 1f : 0f;
            }

            _text.text =
                $"<size=75%><color=#8CA0AE>{view.PoseProgress}</color></size>\n" +
                $"<b><color=#55D8FF>{view.PoseName}</color></b>\n\n" +
                "<size=165%><b><color=#70FFB0>成功！</color></b></size>\n\n" +
                "<size=62%><color=#8CA0AE>腕を楽にして、その場でお待ちください</color></size>";
            return true;
        }

        private void HandlePoseCompleted(PoseCompletionResult result)
        {
            if (_gameFlow == null ||
                _gameFlow.CurrentPhase != ExperiencePhase.Posing)
            {
                return;
            }

            _lastPoseCompletion = result;
            ApplyPoseAdaptiveHintAction(
                _poseAdaptiveHintState.Suspend());
            if (!result.Succeeded)
            {
                _hasPoseCompletionMessage = false;
                return;
            }

            _poseCompletionMessageUntil =
                Time.unscaledTime +
                _poseCompletionMessageSeconds;
            _hasPoseCompletionMessage = true;
            _nextRefreshTime = 0f;
            RefreshNow();
        }

        private void SubscribePoseCompletion()
        {
            if (_posingScorer == null)
            {
                return;
            }

            _posingScorer.PoseCompleted -= HandlePoseCompleted;
            _posingScorer.PoseCompleted += HandlePoseCompleted;
        }

        private void UnsubscribePoseCompletion()
        {
            if (_posingScorer != null)
            {
                _posingScorer.PoseCompleted -= HandlePoseCompleted;
            }
        }

        private void RefreshResultsHud()
        {
            if (!_showTechnicalDetails)
            {
                RefreshPlayerFacingResultsHud();
                return;
            }

            if (_resultController == null ||
                !_resultController.HasResult)
            {
                if (_growthFill != null)
                {
                    _growthFill.fillAmount = 0f;
                }

                _text.text =
                    "<b>RESULTS</b>\n" +
                    "<color=#FF7070>RESULT DATA UNAVAILABLE</color>\n\n" +
                    "<color=#FFD36A>PRESS A TO RETRY</color>";
                return;
            }

            var result = _resultController.FinalResult;
            var rankColor = result.Rank switch
            {
                ExperienceRank.S => "#70FFB0",
                ExperienceRank.A => "#55D8FF",
                ExperienceRank.B => "#FFD36A",
                ExperienceRank.C => "#FF9A62",
                _ => "#FF7070"
            };

            if (_growthFill != null)
            {
                _growthFill.fillAmount =
                    result.FinalScore / 100f;
            }

            var completedReps =
                _gameFlow.CompletedElbowReps +
                _gameFlow.CompletedArmRaiseReps;
            _text.text =
                "<b>RESULTS</b>\n" +
                $"<size=160%><color={rankColor}>" +
                $"RANK {result.Rank}</color></size>\n" +
                $"FINAL       {result.FinalScore:0} / 100\n\n" +
                "<size=100%>" +
                $"SUCCESSFUL REPS  {completedReps:00}\n" +
                $"ACCURACY         " +
                $"{_gameFlow.CompletedTimingAccuracy:P0}\n" +
                $"POSE SUCCESSES   " +
                $"{_gameFlow.CompletedPoseSuccessCount} / " +
                $"{_gameFlow.CompletedPoseCount}\n" +
                "</size>" +
                "\n" +
                "<color=#FFD36A>PRESS A TO RETRY</color>";
        }

        private void RefreshPlayerFacingResultsHud()
        {
            var finalView =
                PlayerFacingPoseCopyFormatter.FormatResults(
                    _resultController);
            var finalScore = _resultController != null &&
                             _resultController.HasResult
                ? _resultController.FinalResult.FinalScore
                : 0f;
            var view = finalView;
            var displayedScore = finalScore;
            var showRank = true;
            if (_resultsRevealSession.HasStarted &&
                finalView.HasResult)
            {
                var frame = GetResultsRevealFrame(finalScore);
                view = PlayerFacingResultsRevealFormatter.Apply(
                    finalView,
                    frame);
                displayedScore = frame.DisplayedFinalScore;
                showRank = frame.ShowRank;
            }

            if (_growthFill != null)
            {
                _growthFill.fillAmount =
                    Mathf.Clamp01(displayedScore / 100f);
            }

            var rankColor = _resultController == null ||
                            !_resultController.HasResult ||
                            !showRank
                ? PlayerFacingResultsStyle.MissingColor
                : PlayerFacingResultsStyle.GetRankColor(
                    _resultController.FinalResult.Rank);
            _text.text =
                PlayerFacingResultsRichTextFormatter.Format(
                    view,
                    rankColor);
        }

        private PlayerFacingResultsRevealFrame
            GetResultsRevealFrame(float finalScore)
        {
            var elapsed = Mathf.Max(
                0f,
                Time.unscaledTime -
                _resultsRevealSession.StartedAt);
            return PlayerFacingResultsRevealMath.Evaluate(
                elapsed,
                _resultRevealDurationSeconds,
                _resultRankRevealAtNormalized,
                finalScore);
        }

        private bool IsResultsRevealAnimating()
        {
            if (_showTechnicalDetails ||
                _gameFlow == null ||
                _gameFlow.CurrentPhase != ExperiencePhase.Results ||
                !_resultsRevealSession.HasStarted ||
                _resultController == null ||
                !_resultController.HasResult)
            {
                return false;
            }

            return !GetResultsRevealFrame(
                _resultController.FinalResult.FinalScore)
                .IsComplete;
        }

        private void SyncResultsRevealPhase()
        {
            if (_gameFlow == null ||
                _gameFlow.CurrentPhase != ExperiencePhase.Results)
            {
                _resultsRevealSession.LeaveResults();
                return;
            }

            _resultsRevealSession.EnterResults();
            _resultsRevealSession.TryStart(
                _resultController != null &&
                _resultController.HasResult,
                Time.unscaledTime);
        }

        private void HandleResultsPhaseChanged(
            ExperiencePhase phase)
        {
            if (phase == ExperiencePhase.Results)
            {
                _resultsRevealSession.EnterResults();
                _resultsRevealSession.TryStart(
                    _resultController != null &&
                    _resultController.HasResult,
                    Time.unscaledTime);
            }
            else
            {
                _resultsRevealSession.LeaveResults();
            }

            _nextRefreshTime = 0f;
            RefreshNow();
        }

        private void HandleResultCompleted(
            ExperienceScoreResult result)
        {
            if (_gameFlow == null ||
                _gameFlow.CurrentPhase != ExperiencePhase.Results)
            {
                return;
            }

            _resultsRevealSession.EnterResults();
            _resultsRevealSession.TryStart(
                true,
                Time.unscaledTime);
            _nextRefreshTime = 0f;
            RefreshNow();
        }

        private void SubscribeResultsReveal()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -=
                    HandleResultsPhaseChanged;
                _gameFlow.PhaseChanged +=
                    HandleResultsPhaseChanged;
            }

            if (_resultController != null)
            {
                _resultController.ResultCompleted -=
                    HandleResultCompleted;
                _resultController.ResultCompleted +=
                    HandleResultCompleted;
            }
        }

        private void UnsubscribeResultsReveal()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -=
                    HandleResultsPhaseChanged;
            }

            if (_resultController != null)
            {
                _resultController.ResultCompleted -=
                    HandleResultCompleted;
            }
        }

        private float GetAppliedMuscleGrowth()
        {
            if (_muscleGrowth != null)
            {
                return MuscleGrowthMath.SanitizeNormalized(
                    _muscleGrowth.AppliedGrowthNormalized);
            }

            return MuscleGrowthMath.SanitizeNormalized(
                _rhythmScorer == null
                    ? 0f
                    : _rhythmScorer.GrowthNormalized);
        }

        private bool ShouldShowMuscleOutput()
        {
            return
                _gameFlow.CurrentPhase ==
                    ExperiencePhase.Transformation ||
                _gameFlow.CurrentPhase ==
                    ExperiencePhase.StageTransition ||
                _gameFlow.CurrentPhase ==
                    ExperiencePhase.StageEntrance ||
                _gameFlow.CurrentPhase ==
                    ExperiencePhase.Posing ||
                _gameFlow.CurrentPhase ==
                    ExperiencePhase.Results;
        }

        private string FormatMuscle(float growth)
        {
            if (_muscleGrowth == null)
            {
                return
                    "MUSCLE      --  " +
                    "<color=#FF7070>NOT WIRED</color>";
            }

            var color = _muscleGrowth.HasRealOutput
                ? "#70FFB0"
                : _muscleGrowth.HasAvailableOutput
                    ? "#8CA0AE"
                    : "#FF7070";
            return
                $"MUSCLE      " +
                $"{MuscleGrowthMath.SanitizeNormalized(growth):P0}  " +
                $"<color={color}>" +
                $"{_muscleGrowth.OutputStatusLabel}</color>";
        }

        private void RefreshCuffStatus()
        {
            if (_cuffStatusText == null)
            {
                return;
            }

            // 新しい簡略構成では Emergency Latch がないため、
            // カフ状態は技術情報表示が有効なときだけ表示する。
            var showStatus = _showTechnicalDetails;

            if (_cuffStatusText.gameObject.activeSelf != showStatus)
            {
                _cuffStatusText.gameObject.SetActive(showStatus);
            }

            if (!showStatus)
            {
                return;
            }

            if (_pneumaticCuff == null)
            {
                _cuffStatusText.text =
                    "CUFF  <color=#FF7070>" +
                    "NOT WIRED</color>";
                return;
            }

            var color =
                _pneumaticCuff.IsBuildPulseActive
                    ? "#70FFB0"
                    : "#8CA0AE";

            _cuffStatusText.text =
                "CUFF  " +
                $"{_pneumaticCuff.OutputStatusLabel}  " +
                $"<color={color}>" +
                $"{_pneumaticCuff.DebugStatusLabel}" +
                "</color>";
        }

        private string FormatBeat()
        {
            if (_rhythmClock == null)
            {
                return "N/A";
            }

            var beatNumber =
                _rhythmClock.LastBeatInBar < 0
                    ? "--"
                    : (_rhythmClock.LastBeatInBar + 1).ToString();
            var isPulse =
                _rhythmClock.IsRunning &&
                _rhythmClock.LastBeatIndex >= 0 &&
                AudioSettings.dspTime - _rhythmClock.LastBeatDspTime < 0.14d;
            var pulse = isPulse
                ? "<color=#70FFB0>*</color>"
                : "o";
            return
                $"{pulse} {_rhythmClock.BeatsPerMinute:0} BPM  " +
                $"{beatNumber}/{_rhythmClock.BeatsPerBar}";
        }

        private string FormatTiming()
        {
            if (_rhythmScorer == null ||
                _rhythmScorer.ScoredTimingPointCount <= 0)
            {
                return "--";
            }

            var color = _rhythmScorer.LastAccuracy >= 0.65f
                ? "#70FFB0"
                : _rhythmScorer.LastAccuracy >= 0.35f
                    ? "#FFD36A"
                    : "#FF7070";
            return
                $"{_rhythmScorer.LastTimingPointLabel}  " +
                $"<color={color}>{_rhythmScorer.LastTimingLabel}</color>  " +
                $"{_rhythmScorer.LastAccuracy:P0}  " +
                $"x{_rhythmScorer.LastMultiplier:0.00}";
        }

        private int GetElbowReps()
        {
            if (_gameFlow.CurrentPhase == ExperiencePhase.Transformation ||
                _gameFlow.CurrentPhase == ExperiencePhase.StageTransition ||
                _gameFlow.CurrentPhase == ExperiencePhase.StageEntrance ||
                _gameFlow.CurrentPhase == ExperiencePhase.Posing ||
                _gameFlow.CurrentPhase == ExperiencePhase.Results)
            {
                return _gameFlow.CompletedElbowReps;
            }

            return _repCounter == null
                ? 0
                : _repCounter.LeftElbowCount + _repCounter.RightElbowCount;
        }

        private int GetArmReps()
        {
            if (_gameFlow.CurrentPhase == ExperiencePhase.Transformation ||
                _gameFlow.CurrentPhase == ExperiencePhase.StageTransition ||
                _gameFlow.CurrentPhase == ExperiencePhase.StageEntrance ||
                _gameFlow.CurrentPhase == ExperiencePhase.Posing ||
                _gameFlow.CurrentPhase == ExperiencePhase.Results)
            {
                return _gameFlow.CompletedArmRaiseReps;
            }

            return _repCounter == null
                ? 0
                : _repCounter.LeftArmRaiseCount + _repCounter.RightArmRaiseCount;
        }

        private void ResolveReferences()
        {
            _gameFlow ??= FindFirstObjectByType<GameFlowController>();
            _rhythmClock ??= FindFirstObjectByType<RhythmClock>();
            _repCounter ??= FindFirstObjectByType<BodyRepCounter>();
            _rhythmScorer ??=
                FindFirstObjectByType<TrainingRhythmScorer>();
            _posingScorer ??=
                FindFirstObjectByType<ThreePointPosingScorer>();
            _resultController ??=
                FindFirstObjectByType<ExperienceResultController>();
            _muscleGrowth ??=
                FindFirstObjectByType<MuscleGrowthController>();
            _pneumaticCuff ??=
                FindFirstObjectByType<
                    PneumaticCuffController>();
            _avatarRegistry ??=
                FindFirstObjectByType<AvatarRegistry>();
            _bodyPoseMetrics ??=
                FindFirstObjectByType<BodyPoseMetrics>();
            _alignmentCalibration ??=
                FindFirstObjectByType<
                    AvatarCalibrationController>();
            _heightCalibration ??=
                FindFirstObjectByType<
                    HeightCalibrationController>();
            _poseGhostGuide ??=
                FindFirstObjectByType<PoseGhostGuideController>(
                    FindObjectsInactive.Include);
            if (_text == null)
            {
                _text = GetComponentInChildren<TMP_Text>(true);
            }

            if (_cuffStatusText == null)
            {
                var cuffStatus =
                    transform.Find("CuffStatusText");
                if (cuffStatus != null)
                {
                    _cuffStatusText =
                        cuffStatus.GetComponent<TMP_Text>();
                }
            }
        }

        private void OnValidate()
        {
            _refreshRate = Mathf.Max(1f, _refreshRate);
            _poseCompletionMessageSeconds = Mathf.Max(
                0.1f,
                _poseCompletionMessageSeconds);
            _setupAcknowledgementSeconds = Mathf.Max(
                0.1f,
                _setupAcknowledgementSeconds);
            _poseAdaptiveHintDelaySeconds = Mathf.Max(
                0f,
                _poseAdaptiveHintDelaySeconds);
            _poseAdaptiveHintDurationSeconds = Mathf.Max(
                0.5f,
                _poseAdaptiveHintDurationSeconds);
            _poseAdaptiveHintMatchThreshold = Mathf.Clamp(
                _poseAdaptiveHintMatchThreshold,
                0f,
                100f);
            _resultRevealDurationSeconds = Mathf.Max(
                0f,
                _resultRevealDurationSeconds);
            _resultRankRevealAtNormalized = Mathf.Clamp(
                _resultRankRevealAtNormalized,
                0.05f,
                1f);
            ResolveReferences();
        }

        public void SetTechnicalDetailsVisible(bool visible)
        {
            _showTechnicalDetails = visible;
            _nextRefreshTime = 0f;
            RefreshNow();
        }
    }

    /// <summary>
    /// Short, testable copy for non-interactive transition phases. Each
    /// message states whether the participant should move and what comes next.
    /// </summary>
    public readonly struct PlayerFacingFlowMessage
    {
        public PlayerFacingFlowMessage(
            string eyebrow,
            string title,
            string instruction,
            string detail)
        {
            Eyebrow = eyebrow ?? string.Empty;
            Title = title ?? string.Empty;
            Instruction = instruction ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public string Eyebrow { get; }
        public string Title { get; }
        public string Instruction { get; }
        public string Detail { get; }
    }

    public static class PlayerFacingFlowHudText
    {
        public const string ExperienceOverview =
            "鍛える → 変身 → 3ポーズ";

        public const string SetupSafetyInstruction =
            "体験中は歩かない・腕を広げても周囲に触れないことを確認\n" +
            "気分が悪い・痛みがある時は腕を下ろし、スタッフに伝える";

        public static bool ShouldShowProgressMeter(
            ExperiencePhase phase)
        {
            return
                phase == ExperiencePhase.Transformation ||
                phase == ExperiencePhase.Posing ||
                phase == ExperiencePhase.Results;
        }

        public static PlayerFacingFlowMessage GetPhaseMessage(
            ExperiencePhase phase,
            StageTransitionDestination destination)
        {
            if (phase == ExperiencePhase.Transformation)
            {
                return GetTransformationMessage(0f);
            }

            if (phase == ExperiencePhase.StageEntrance)
            {
                return new PlayerFacingFlowMessage(
                    "ステージ入場",
                    "変身後のあなた",
                    "歩かず、その場でステージを見てください",
                    "このあと お手本に合わせる3ポーズ");
            }

            if (phase == ExperiencePhase.StageTransition)
            {
                if (destination == StageTransitionDestination.Training)
                {
                    return new PlayerFacingFlowMessage(
                        "リトライ準備",
                        "鏡の前へ戻っています",
                        "歩かず、その場でお待ちください",
                        "戻ると最初の準備から始まります");
                }

                return new PlayerFacingFlowMessage(
                    "ステージへ移動",
                    "入場準備中",
                    "歩かず、その場で正面を見てください",
                    "映像の中だけで移動します");
            }

            return new PlayerFacingFlowMessage(
                string.Empty,
                "準備中",
                "その場でお待ちください",
                string.Empty);
        }

        public static PlayerFacingFlowMessage GetTransformationMessage(
            float normalizedProgress)
        {
            switch (TransformationPresentationMath.GetStage(
                        normalizedProgress))
            {
                case TransformationPresentationStage.Lock:
                    return new PlayerFacingFlowMessage(
                        "トレーニング完了",
                        "成果を確定",
                        "その場で正面の鏡を見てください",
                        "体を楽にして、そのまま");
                case TransformationPresentationStage.Reveal:
                    return new PlayerFacingFlowMessage(
                        "変身中",
                        "鍛えた体をチェック",
                        "鏡の上半身を見てください",
                        "あなたの運動結果を反映中");
                default:
                    return new PlayerFacingFlowMessage(
                        "変身完了",
                        "ステージへ行こう",
                        "その場で鏡を見てください",
                        "次はステージで3ポーズ");
            }
        }
    }
}
