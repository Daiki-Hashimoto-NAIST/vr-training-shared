using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IVRC
{
    public enum TrainingRhythmLanePreparationState
    {
        None,
        Countdown,
        LeadIn,
        Transition
    }

    public enum TrainingAdaptiveHintAction
    {
        None,
        Show,
        Hide
    }

    /// <summary>
    /// Tracks whether a participant has missed enough consecutive timing
    /// points to need one short reminder. The chart and score remain the
    /// source of truth; this state only controls presentation.
    /// </summary>
    public sealed class TrainingAdaptiveHintState
    {
        public int ActiveBlockIndex { get; private set; } = -1;
        public int ShownBlockIndex { get; private set; } = -1;
        public int ConsecutiveMisses { get; private set; }
        public bool IsActive { get; private set; }

        public TrainingAdaptiveHintAction ObserveTimingPoint(
            int blockIndex,
            bool allRequiredSidesHit,
            int missThreshold)
        {
            if (blockIndex < 0)
            {
                return TrainingAdaptiveHintAction.None;
            }

            var wasActive = IsActive;
            if (ActiveBlockIndex != blockIndex)
            {
                ActiveBlockIndex = blockIndex;
                ConsecutiveMisses = 0;
                IsActive = false;
            }

            if (allRequiredSidesHit)
            {
                ConsecutiveMisses = 0;
                if (!IsActive && !wasActive)
                {
                    return TrainingAdaptiveHintAction.None;
                }

                IsActive = false;
                return TrainingAdaptiveHintAction.Hide;
            }

            if (ShownBlockIndex == blockIndex)
            {
                return wasActive && !IsActive
                    ? TrainingAdaptiveHintAction.Hide
                    : TrainingAdaptiveHintAction.None;
            }

            ConsecutiveMisses++;
            if (ConsecutiveMisses < Mathf.Max(1, missThreshold))
            {
                return wasActive && !IsActive
                    ? TrainingAdaptiveHintAction.Hide
                    : TrainingAdaptiveHintAction.None;
            }

            ShownBlockIndex = blockIndex;
            IsActive = true;
            return TrainingAdaptiveHintAction.Show;
        }

        public TrainingAdaptiveHintAction Suspend()
        {
            ConsecutiveMisses = 0;
            if (!IsActive)
            {
                return TrainingAdaptiveHintAction.None;
            }

            IsActive = false;
            return TrainingAdaptiveHintAction.Hide;
        }

        public TrainingAdaptiveHintAction Expire()
        {
            if (!IsActive)
            {
                return TrainingAdaptiveHintAction.None;
            }

            IsActive = false;
            return TrainingAdaptiveHintAction.Hide;
        }

        public void ResetSession()
        {
            ActiveBlockIndex = -1;
            ShownBlockIndex = -1;
            ConsecutiveMisses = 0;
            IsActive = false;
        }
    }

    public static class TrainingRhythmLaneHudText
    {
        public const string SafetyInstruction =
            "痛み、しびれは係に報告";

        /// <summary>
        /// The right-hand column's heading. During a preparation window it
        /// is empty, because the exercise being announced is already across
        /// the middle of the view at twice the size — see
        /// <see cref="GetAnnouncement"/>. Repeating it here put the same
        /// two words in two places at once.
        /// </summary>
        public static string GetPreparationHeading(
            TrainingRhythmLanePreparationState state)
        {
            return string.Empty;
        }

        /// <summary>
        /// What the announcement panel says while a movement is being
        /// introduced. Named rather than rehearsed: the participant has
        /// four beats, and reading which exercise is coming is a better
        /// use of them than being told they are practising.
        /// </summary>
        public static string GetAnnouncement(
            TrainingRhythmLanePreparationState state,
            BodyRepKind kind)
        {
            // One shape for every announcement the set makes:
            // まずは…！ / つぎは…！ / そろそろ時間だ！ — a lead-in word,
            // the thing, and the exclamation. LeadIn is the first block of
            // the set and Transition is any later one, so the wording
            // follows from the state without counting anything.
            return state switch
            {
                TrainingRhythmLanePreparationState.LeadIn =>
                    "まずは" + GetExerciseName(kind) + "！",
                TrainingRhythmLanePreparationState.Transition =>
                    "つぎは" + GetExerciseName(kind) + "！",
                _ => string.Empty
            };
        }

        /// <summary>
        /// Shown once the last repetition is done. The whistle has already
        /// sounded; this says where to go.
        /// </summary>
        public const string CompletedAnnouncement =
            "そろそろ時間だ！" + "\n" +
            "ステージへ行こう！";

        public static string GetPreparationInstruction(
            TrainingRhythmLanePreparationState state)
        {
            return string.Empty;
        }

        public static string WithSafetyFooter(string instruction)
        {
            return (instruction ?? string.Empty) +
                   "\n<size=65%>" +
                   SafetyInstruction +
                   "</size>";
        }

        public static string GetActiveTimingInstruction(
            bool ringVisualsEnabled)
        {
            return ringVisualsEnabled
                ? "リングが重なる瞬間に  上げる / 下ろす"
                : "線画が重なる瞬間に  上げる / 下ろす";
        }

        public static string GetTrackingRecoveryInstruction()
        {
            return WithSafetyFooter(
                "正面を向き、両手を見える位置へ戻す\n" +
                "戻るまで判定は止まります");
        }

        public static string GetAdaptiveHintHeading(
            BodyRepKind kind)
        {
            return "リズムと線画に合わせて！";
        }

        public static string GetExerciseName(BodyRepKind kind)
        {
            return kind == BodyRepKind.ElbowFlexion
                ? "アームカール"
                : "サイドレイズ";
        }

        public static string GetTimingPointLabel(
            RepTimingPoint timingPoint)
        {
            return timingPoint == RepTimingPoint.Up
                ? "上げる"
                : "下ろす";
        }

        public static RepTimingPoint GetPendingTimingPoint(
            TrainingTimingVisualState upState)
        {
            return upState == TrainingTimingVisualState.Pending
                ? RepTimingPoint.Up
                : RepTimingPoint.Down;
        }

        public static string GetRepResultText(
            TrainingRepTimingResult result)
        {
            if (!result.IsValid)
            {
                return string.Empty;
            }

            if (TrainingSuccessFeedbackRules
                    .IsRhythmClear(result))
            {
                return $"{result.RepetitionNumber:00} 回目 成功\n" +
                       (result.Kind == BodyRepKind.ElbowFlexion
                           ? "上腕が成長！"
                           : "肩が成長！");
            }

            return TrainingSuccessFeedbackRules
                    .IsMotionCompleted(result)
                ? "リズムに合わせて！"
                : string.Empty;
        }

        /// <summary>
        /// Classifies the gap before a pending chart note using the chart's
        /// explicit note and block metadata. A same-block gap remains active
        /// guidance; only a block boundary is labelled as a transition.
        /// </summary>
        public static TrainingRhythmLanePreparationState
            GetPreparationState(
                IReadOnlyList<TrainingRhythmNote> notes,
                int pendingNoteIndex,
                float currentBeat)
        {
            if (notes == null ||
                pendingNoteIndex < 0 ||
                pendingNoteIndex >= notes.Count)
            {
                return TrainingRhythmLanePreparationState.None;
            }

            var note = notes[pendingNoteIndex];
            if (currentBeat >= note.UpBeat)
            {
                return TrainingRhythmLanePreparationState.None;
            }

            if (pendingNoteIndex == 0)
            {
                return TrainingRhythmLanePreparationState.LeadIn;
            }

            return notes[pendingNoteIndex - 1].BlockIndex !=
                   note.BlockIndex
                ? TrainingRhythmLanePreparationState.Transition
                : TrainingRhythmLanePreparationState.None;
        }
    }

    /// <summary>
    /// Presents each UP and DOWN timing point as a translucent ring that
    /// contracts onto one central target. The mirror avatar remains visible
    /// through the outline-only graphics.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrainingRhythmLaneHud : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private TrainingRhythmChart _chart;
        [SerializeField] private RhythmClock _rhythmClock;
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private TrainingRhythmScorer _rhythmScorer;
        [SerializeField] private RhythmPoseGuideController _poseGuide;

        [Header("Pulse Rings")]
        [SerializeField] private RectTransform _noteArea;
        [SerializeField] private Image _judgementRing;
        [SerializeField] private Sprite _roundedSprite;
        [SerializeField, Min(40f)]
        private float _targetDiameter = 112f;
        [SerializeField, Min(60f)]
        private float _maximumDiameter = 220f;
        [SerializeField, Min(0.25f)]
        private float _approachBeats = 2f;
        [SerializeField, Min(0f)]
        private float _lingerBeats = 0.35f;
        [SerializeField, Min(0f)]
        private float _nextExercisePreviewBeats = 8f;
        [SerializeField]
        private bool _ringVisualsEnabled = true;

        [Header("Text")]
        [SerializeField] private TMP_Text _movementText;
        [SerializeField] private TMP_Text _nextMovementText;
        [SerializeField] private TMP_Text _progressText;
        [SerializeField] private TMP_Text _feedbackText;
        [SerializeField] private TMP_Text _countdownText;
        [SerializeField, Min(0.1f)]
        private float _feedbackDurationSeconds = 0.65f;

        [Header("Adaptive Guidance")]
        [Tooltip(
            "Show one short line-art reminder after this many consecutive " +
            "unresolved timing points in the same exercise block.")]
        [SerializeField, Min(1)]
        private int _adaptiveHintMissThreshold = 2;
        [SerializeField, Min(0.5f)]
        private float _adaptiveHintDurationSeconds = 2.5f;

        [Header("Rep Result")]
        [SerializeField, Min(0.1f)]
        private float _successFeedbackDurationSeconds = 0.9f;
        [SerializeField, Min(0.05f)]
        private float _successPulseDurationSeconds = 0.3f;
        [SerializeField, Range(1f, 1.5f)]
        private float _successPulseScale = 1.18f;

        [Header("Colors")]
        [SerializeField] private Color _armCurlColor =
            new Color(1f, 0.55f, 0.2f, 0.84f);
        [SerializeField] private Color _sideRaiseColor =
            new Color(0.2f, 0.82f, 1f, 0.84f);
        [SerializeField] private Color _hitColor =
            new Color(0.35f, 1f, 0.62f, 0.95f);
        [SerializeField] private Color _completionColor =
            new Color(0.85f, 0.92f, 1f, 0.95f);
        [SerializeField] private Color _missColor =
            new Color(1f, 0.68f, 0.28f, 0.9f);

        private sealed class TimingPulseView
        {
            public int NoteIndex;
            public RepTimingPoint TimingPoint;
            public GameObject Root;
            public RectTransform Rect;
            public Image Glow;
            public Image Ring;
            public RectTransform LabelRect;
            public TMP_Text Label;
        }

        private readonly List<TimingPulseView> _pulseViews =
            new List<TimingPulseView>();
        private readonly TrainingAdaptiveHintState _adaptiveHintState =
            new TrainingAdaptiveHintState();
        private float _feedbackHideTime;
        private float _adaptiveHintHideTime;
        private float _successPulseStartTime;
        private float _successPulseEndTime;

        public bool RingVisualsEnabled =>
            _ringVisualsEnabled;

        private void Awake()
        {
            ResolveReferences();
            ValidateSettings();
            ConfigureTargetRing();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ValidateSettings();
            ConfigureTargetRing();
            Subscribe();
            RebuildPulseViews();
            RefreshText();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ResetAdaptiveHint();
            ResetSuccessPulse();
        }

        private void Update()
        {
            RefreshAdaptiveHint();
            RefreshText();
            RenderPulses();
            RefreshFeedback();
        }

        private void HandleSessionStarted()
        {
            _feedbackHideTime = 0f;
            ResetAdaptiveHint();
            ResetSuccessPulse();
            if (_feedbackText != null)
            {
                _feedbackText.text = string.Empty;
            }

            ConfigureTargetRing();
            RebuildPulseViews();
        }

        private void HandleChartChanged()
        {
            if (_chart != null &&
                _pulseViews.Count !=
                _chart.ScheduledNotes.Count * 2)
            {
                RebuildPulseViews();
            }
        }

        private void HandleTimingFeedback(
            TrainingTimingFeedback feedback)
        {
            if (!feedback.IsValid)
            {
                return;
            }

            ObserveAdaptiveHint(feedback);
            if (_feedbackText == null)
            {
                return;
            }

            _feedbackText.text =
                $"{TrainingRhythmLaneHudText.GetTimingPointLabel(feedback.TimingPoint)}  " +
                feedback.TimingLabel;
            var color = GetFeedbackColor(
                feedback.TimingLabel);
            _feedbackText.color = color;
            if (_judgementRing != null)
            {
                _judgementRing.color = color;
            }

            _feedbackHideTime =
                Time.unscaledTime + _feedbackDurationSeconds;
        }

        private void HandleRepTimingResult(
            TrainingRepTimingResult result)
        {
            if (!result.IsValid || _feedbackText == null)
            {
                return;
            }

            var motionCompleted =
                TrainingSuccessFeedbackRules
                    .IsMotionCompleted(result);
            var rhythmCleared =
                TrainingSuccessFeedbackRules
                    .IsRhythmClear(result);
            _feedbackText.text =
                TrainingRhythmLaneHudText.GetRepResultText(
                    result);
            var color = rhythmCleared
                ? _hitColor
                : motionCompleted
                    ? _completionColor
                    : _missColor;
            _feedbackText.color = color;
            if (_judgementRing != null)
            {
                _judgementRing.color = color;
            }

            _feedbackHideTime =
                Time.unscaledTime +
                (motionCompleted
                    ? _successFeedbackDurationSeconds
                    : _feedbackDurationSeconds);
            if (!rhythmCleared)
            {
                return;
            }

            _successPulseStartTime = Time.unscaledTime;
            _successPulseEndTime =
                _successPulseStartTime +
                _successPulseDurationSeconds;
        }

        private bool TryGetPendingNote(
            out TrainingRhythmNote pendingNote,
            out int pendingNoteIndex,
            out RepTimingPoint pendingTimingPoint)
        {
            pendingNote = default;
            pendingNoteIndex = -1;
            pendingTimingPoint = RepTimingPoint.Up;
            if (_chart == null)
            {
                return false;
            }

            var notes = _chart.ScheduledNotes;
            for (var index = 0;
                 index < notes.Count;
                 index++)
            {
                var note = notes[index];
                if (_chart.GetTimingVisualState(
                        note.Index,
                        RepTimingPoint.Down) !=
                    TrainingTimingVisualState.Pending)
                {
                    continue;
                }

                pendingNote = note;
                pendingNoteIndex = index;
                pendingTimingPoint =
                    TrainingRhythmLaneHudText
                        .GetPendingTimingPoint(
                            _chart.GetTimingVisualState(
                                note.Index,
                                RepTimingPoint.Up));
                return true;
            }

            return false;
        }

        private void RefreshText()
        {
            if (_gameFlow == null)
            {
                SetText(_movementText, "RHYTHM SYSTEM");
                SetText(_nextMovementText, "NOT CONNECTED");
                SetText(_progressText, "-- / --");
                SetText(_countdownText, string.Empty);
                return;
            }

            if (_gameFlow.CurrentPhase ==
                ExperiencePhase.Countdown)
            {
                SetText(_movementText, string.Empty);
                SetText(_nextMovementText, string.Empty);
                SetText(_progressText, string.Empty);
                // No number here. The count that matters is 3-2-1-GO on the
                // announcement panel, timed to the music, and a second
                // countdown running before it was counting the participant
                // in twice.
                SetText(_countdownText, string.Empty);
                return;
            }

            SetText(_countdownText, string.Empty);
            if (_gameFlow.CurrentPhase !=
                ExperiencePhase.Training)
            {
                return;
            }

            if (_gameFlow.IsPausedForTracking)
            {
                SetText(_movementText, "トラッキング停止");
                SetText(
                    _nextMovementText,
                    TrainingRhythmLaneHudText
                        .GetTrackingRecoveryInstruction());
                return;
            }

            if (_chart == null ||
                _rhythmClock == null ||
                !_rhythmClock.HasStarted ||
                !_chart.HasSession)
            {
                SetText(
                    _movementText,
                    TrainingRhythmLaneHudText.GetPreparationHeading(
                        TrainingRhythmLanePreparationState.LeadIn));
                SetText(
                    _nextMovementText,
                    TrainingRhythmLaneHudText
                        .GetPreparationInstruction(
                            TrainingRhythmLanePreparationState.LeadIn));
                SetText(
                    _progressText,
                    _chart == null
                        ? "-- / --"
                        : $"回数  00 / " +
                          $"{_chart.TotalRepetitions:00}");
                return;
            }

            if (!TryGetPendingNote(
                    out var pendingNote,
                    out var pendingNoteIndex,
                    out var pendingTimingPoint))
            {
                // The whistle has just gone. Where to go next is on the
                // announcement panel in the middle of the view; this column
                // only reports that the set is over.
                SetText(_movementText, "トレーニング完了");
                SetText(_nextMovementText, string.Empty);
            }
            else
            {
                if (_adaptiveHintState.IsActive)
                {
                    SetText(
                        _movementText,
                        TrainingRhythmLaneHudText
                            .GetAdaptiveHintHeading(
                                pendingNote.Kind));
                    SetText(_nextMovementText, string.Empty);
                    SetText(
                        _progressText,
                        $"回数  {_chart.ResolvedNoteCount:00} / " +
                        $"{_chart.TotalRepetitions:00}");
                    return;
                }

                var preparationState =
                    TrainingRhythmLaneHudText.GetPreparationState(
                        _chart.ScheduledNotes,
                        pendingNoteIndex,
                        _chart.CurrentBeat);
                if (preparationState !=
                    TrainingRhythmLanePreparationState.None)
                {
                    SetText(_movementText, string.Empty);
                    SetText(_nextMovementText, string.Empty);
                    SetText(
                        _progressText,
                        $"回数  {_chart.ResolvedNoteCount:00} / " +
                        $"{_chart.TotalRepetitions:00}");
                    return;
                }

                // The ring and ghost already show the requested direction.
                // Keep this area empty during active training rather than
                // repeating the exercise name and 上げる／下ろす text.
                SetText(_movementText, string.Empty);
                if (_chart.TryGetNextDifferentKind(
                        out var nextKind,
                        out var beatsUntilNext) &&
                    beatsUntilNext <= _nextExercisePreviewBeats)
                {
                    SetText(
                        _nextMovementText,
                        "次は " +
                        TrainingRhythmLaneHudText.GetExerciseName(
                            nextKind));
                }
                else
                {
                    SetText(
                        _nextMovementText,
                        TrainingRhythmLaneHudText.WithSafetyFooter(
                            TrainingRhythmLaneHudText
                                .GetActiveTimingInstruction(
                                    _ringVisualsEnabled)));
                }
            }

            SetText(
                _progressText,
                $"回数  {_chart.ResolvedNoteCount:00} / " +
                $"{_chart.TotalRepetitions:00}");
        }

        private void RenderPulses()
        {
            if (!_ringVisualsEnabled)
            {
                return;
            }

            if (_chart == null ||
                _noteArea == null ||
                _gameFlow == null ||
                _gameFlow.CurrentPhase !=
                    ExperiencePhase.Training ||
                _gameFlow.IsPausedForTracking ||
                !_chart.HasSession ||
                _pulseViews.Count !=
                    _chart.ScheduledNotes.Count * 2)
            {
                SetAllPulsesActive(false);
                return;
            }

            var currentBeat = _chart.CurrentBeat;
            TimingPulseView activeView = null;
            foreach (var view in _pulseViews)
            {
                var note =
                    _chart.ScheduledNotes[view.NoteIndex];
                if (_chart.GetTimingVisualState(
                        note.Index,
                        view.TimingPoint) ==
                    TrainingTimingVisualState.Pending)
                {
                    activeView = view;
                    break;
                }
            }

            foreach (var view in _pulseViews)
            {
                if (view != activeView)
                {
                    view.Root.SetActive(false);
                    continue;
                }

                var note =
                    _chart.ScheduledNotes[view.NoteIndex];
                var targetBeat =
                    note.GetBeat(view.TimingPoint);
                var beatsRemaining =
                    targetBeat - currentBeat;
                var visible =
                    beatsRemaining <= _approachBeats &&
                    beatsRemaining >= -_lingerBeats;
                view.Root.SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                var approachProgress =
                    beatsRemaining <= 0f
                        ? 1f
                        : 1f - Mathf.Clamp01(
                            beatsRemaining /
                            _approachBeats);
                var diameter = Mathf.Lerp(
                    _maximumDiameter,
                    _targetDiameter,
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        approachProgress));
                var alpha = Mathf.Lerp(
                    0.22f,
                    0.88f,
                    approachProgress);
                if (beatsRemaining < 0f &&
                    _lingerBeats > Mathf.Epsilon)
                {
                    var lingerProgress = Mathf.Clamp01(
                        -beatsRemaining /
                        _lingerBeats);
                    diameter = Mathf.Lerp(
                        _targetDiameter,
                        _targetDiameter * 1.12f,
                        lingerProgress);
                    alpha *= 1f - lingerProgress;
                }

                view.Rect.sizeDelta =
                    Vector2.one * diameter;
                view.LabelRect.anchoredPosition =
                    new Vector2(
                        0f,
                        diameter * 0.5f - 28f);
                var state =
                    _chart.GetTimingVisualState(
                        note.Index,
                        view.TimingPoint);
                var color = GetPulseColor(
                    note.Kind,
                    state);
                color.a *= alpha;
                view.Ring.color = color;
                var glowColor = color;
                glowColor.a *= 0.24f;
                view.Glow.color = glowColor;
                var labelColor = color;
                labelColor.a = Mathf.Min(
                    1f,
                    alpha + 0.1f);
                view.Label.color = labelColor;
            }
        }

        private void RebuildPulseViews()
        {
            ClearPulseViews();
            if (_chart == null ||
                _noteArea == null)
            {
                return;
            }

            foreach (var note in _chart.ScheduledNotes)
            {
                _pulseViews.Add(
                    CreatePulseView(
                        note,
                        RepTimingPoint.Up));
                _pulseViews.Add(
                    CreatePulseView(
                        note,
                        RepTimingPoint.Down));
            }
        }

        private TimingPulseView CreatePulseView(
            TrainingRhythmNote note,
            RepTimingPoint timingPoint)
        {
            var timingToken =
                timingPoint == RepTimingPoint.Up
                    ? "UP"
                    : "DOWN";
            var timingLabel =
                TrainingRhythmLaneHudText.GetTimingPointLabel(
                    timingPoint);
            var root = new GameObject(
                $"Pulse_{note.Index:00}_{timingToken}",
                typeof(RectTransform));
            root.transform.SetParent(_noteArea, false);

            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta =
                Vector2.one * _maximumDiameter;

            var glow = CreateRingImage(
                root.transform,
                "Glow",
                new Vector2(20f, 20f));
            var ring = CreateRingImage(
                root.transform,
                "Ring",
                Vector2.zero);
            var label = CreatePulseLabel(
                root.transform,
                timingLabel,
                out var labelRect);

            root.SetActive(false);
            return new TimingPulseView
            {
                NoteIndex = note.Index,
                TimingPoint = timingPoint,
                Root = root,
                Rect = rect,
                Glow = glow,
                Ring = ring,
                LabelRect = labelRect,
                Label = label
            };
        }

        private Image CreateRingImage(
            Transform parent,
            string objectName,
            Vector2 sizeExpansion)
        {
            var ringObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            ringObject.transform.SetParent(parent, false);

            var rect =
                ringObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = sizeExpansion;

            var image = ringObject.GetComponent<Image>();
            image.raycastTarget = false;
            image.sprite = _roundedSprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            return image;
        }

        private TMP_Text CreatePulseLabel(
            Transform parent,
            string label,
            out RectTransform labelRect)
        {
            var labelObject = new GameObject(
                "Action",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(parent, false);

            labelRect =
                labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin =
                new Vector2(0.5f, 0.5f);
            labelRect.anchorMax =
                new Vector2(0.5f, 0.5f);
            labelRect.pivot =
                new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta =
                new Vector2(120f, 34f);

            var text =
                labelObject.GetComponent<TextMeshProUGUI>();
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.enableAutoSizing = true;
            text.fontSizeMin = 14f;
            text.fontSizeMax = 24f;
            text.fontStyle = FontStyles.Bold;
            if (_movementText != null)
            {
                text.font = _movementText.font;
            }

            return text;
        }

        private void ClearPulseViews()
        {
            foreach (var view in _pulseViews)
            {
                if (view.Root == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(view.Root);
                }
                else
                {
                    DestroyImmediate(view.Root);
                }
            }

            _pulseViews.Clear();
        }

        private void SetAllPulsesActive(bool active)
        {
            foreach (var view in _pulseViews)
            {
                if (view.Root != null)
                {
                    view.Root.SetActive(active);
                }
            }
        }

        private void RefreshFeedback()
        {
            RefreshSuccessPulse();
            if (_feedbackText == null ||
                Time.unscaledTime < _feedbackHideTime)
            {
                return;
            }

            _feedbackText.text = string.Empty;
            if (_judgementRing != null)
            {
                _judgementRing.color =
                    GetCurrentExerciseColor(0.32f);
            }
        }

        private void ObserveAdaptiveHint(
            TrainingTimingFeedback feedback)
        {
            if (_chart == null ||
                _gameFlow == null ||
                _gameFlow.CurrentPhase !=
                    ExperiencePhase.Training ||
                _gameFlow.IsPausedForTracking ||
                feedback.NoteIndex < 0 ||
                feedback.NoteIndex >=
                    _chart.ScheduledNotes.Count)
            {
                return;
            }

            var blockIndex = _chart
                .ScheduledNotes[feedback.NoteIndex]
                .BlockIndex;
            var action = _adaptiveHintState.ObserveTimingPoint(
                blockIndex,
                feedback.AllRequiredSidesHit,
                _adaptiveHintMissThreshold);
            ApplyAdaptiveHintAction(action);
        }

        private void RefreshAdaptiveHint()
        {
            if (_gameFlow == null ||
                _gameFlow.CurrentPhase !=
                    ExperiencePhase.Training ||
                _gameFlow.IsPausedForTracking)
            {
                ApplyAdaptiveHintAction(
                    _adaptiveHintState.Suspend());
                return;
            }

            if (_adaptiveHintState.IsActive &&
                Time.unscaledTime >= _adaptiveHintHideTime)
            {
                ApplyAdaptiveHintAction(
                    _adaptiveHintState.Expire());
            }
        }

        private void ApplyAdaptiveHintAction(
            TrainingAdaptiveHintAction action)
        {
            switch (action)
            {
                case TrainingAdaptiveHintAction.Show:
                    _adaptiveHintHideTime =
                        Time.unscaledTime +
                        _adaptiveHintDurationSeconds;
                    _poseGuide?.SetAdaptiveHintActive(true);
                    break;
                case TrainingAdaptiveHintAction.Hide:
                    _adaptiveHintHideTime = 0f;
                    _poseGuide?.SetAdaptiveHintActive(false);
                    break;
            }
        }

        private void ResetAdaptiveHint()
        {
            _adaptiveHintState.ResetSession();
            _adaptiveHintHideTime = 0f;
            _poseGuide?.SetAdaptiveHintActive(false);
        }

        private void RefreshSuccessPulse()
        {
            if (_judgementRing == null)
            {
                return;
            }

            if (_successPulseEndTime <= 0f ||
                Time.unscaledTime >= _successPulseEndTime)
            {
                _judgementRing.rectTransform.localScale =
                    Vector3.one;
                if (_feedbackText != null)
                {
                    _feedbackText.rectTransform.localScale =
                        Vector3.one;
                }
                _successPulseEndTime = 0f;
                return;
            }

            var duration = Mathf.Max(
                0.05f,
                _successPulseEndTime - _successPulseStartTime);
            var progress = Mathf.Clamp01(
                (Time.unscaledTime - _successPulseStartTime) /
                duration);
            var pulse = Mathf.Sin(progress * Mathf.PI);
            var scale = Mathf.Lerp(
                1f,
                _successPulseScale,
                pulse);
            _judgementRing.rectTransform.localScale =
                Vector3.one * scale;
            if (_feedbackText != null)
            {
                _feedbackText.rectTransform.localScale =
                    Vector3.one * scale;
            }
        }

        private void ResetSuccessPulse()
        {
            _successPulseStartTime = 0f;
            _successPulseEndTime = 0f;
            if (_judgementRing != null)
            {
                _judgementRing.rectTransform.localScale =
                    Vector3.one;
            }

            if (_feedbackText != null)
            {
                _feedbackText.rectTransform.localScale =
                    Vector3.one;
            }
        }

        private Color GetPulseColor(
            BodyRepKind kind,
            TrainingTimingVisualState state)
        {
            return state switch
            {
                TrainingTimingVisualState.Hit => _hitColor,
                TrainingTimingVisualState.Miss => _missColor,
                _ => kind == BodyRepKind.ElbowFlexion
                    ? _armCurlColor
                    : _sideRaiseColor
            };
        }

        private Color GetCurrentExerciseColor(float alpha)
        {
            var color =
                _chart != null &&
                _chart.TryGetActiveKind(out var kind) &&
                kind == BodyRepKind.ArmRaise
                    ? _sideRaiseColor
                    : _armCurlColor;
            color.a = alpha;
            return color;
        }

        private Color GetFeedbackColor(string label)
        {
            return label switch
            {
                "PERFECT" => _hitColor,
                "GOOD" => new Color(
                    0.45f,
                    0.9f,
                    1f,
                    0.95f),
                "OK" => new Color(
                    1f,
                    0.8f,
                    0.3f,
                    0.95f),
                _ => _missColor
            };
        }

        private void ConfigureTargetRing()
        {
            if (_judgementRing == null)
            {
                return;
            }

            _judgementRing.gameObject.SetActive(
                _ringVisualsEnabled);
            _judgementRing.sprite = _roundedSprite;
            _judgementRing.type = Image.Type.Simple;
            _judgementRing.preserveAspect = true;
            _judgementRing.raycastTarget = false;
            _judgementRing.rectTransform.sizeDelta =
                Vector2.one * _targetDiameter;
            _judgementRing.color =
                GetCurrentExerciseColor(0.32f);
        }

        /// <summary>
        /// Controls only the ring graphics. Rhythm timing, scoring, movement
        /// text, progress, and feedback remain active.
        /// </summary>
        public void SetRingVisualsEnabled(bool visible)
        {
            _ringVisualsEnabled = visible;
            if (!visible)
            {
                SetAllPulsesActive(false);
            }

            ConfigureTargetRing();
        }

        private void Subscribe()
        {
            if (_chart != null)
            {
                _chart.SessionStarted -= HandleSessionStarted;
                _chart.SessionStarted += HandleSessionStarted;
                _chart.ChartChanged -= HandleChartChanged;
                _chart.ChartChanged += HandleChartChanged;
            }

            if (_rhythmScorer != null)
            {
                _rhythmScorer.TimingFeedbackReady -=
                    HandleTimingFeedback;
                _rhythmScorer.TimingFeedbackReady +=
                    HandleTimingFeedback;
                _rhythmScorer.RepTimingResultReady -=
                    HandleRepTimingResult;
                _rhythmScorer.RepTimingResultReady +=
                    HandleRepTimingResult;
            }
        }

        private void Unsubscribe()
        {
            if (_chart != null)
            {
                _chart.SessionStarted -= HandleSessionStarted;
                _chart.ChartChanged -= HandleChartChanged;
            }

            if (_rhythmScorer != null)
            {
                _rhythmScorer.TimingFeedbackReady -=
                    HandleTimingFeedback;
                _rhythmScorer.RepTimingResultReady -=
                    HandleRepTimingResult;
            }
        }

        private void ResolveReferences()
        {
            _chart ??=
                FindFirstObjectByType<TrainingRhythmChart>();
            _rhythmClock ??=
                FindFirstObjectByType<RhythmClock>();
            _gameFlow ??=
                FindFirstObjectByType<GameFlowController>();
            _rhythmScorer ??=
                FindFirstObjectByType<TrainingRhythmScorer>();
            _poseGuide ??=
                FindFirstObjectByType<RhythmPoseGuideController>(
                    FindObjectsInactive.Include);
        }

        private void ValidateSettings()
        {
            _targetDiameter = Mathf.Max(
                40f,
                _targetDiameter);
            _maximumDiameter = Mathf.Max(
                _targetDiameter + 20f,
                _maximumDiameter);
            _approachBeats = Mathf.Max(
                0.25f,
                _approachBeats);
            _lingerBeats = Mathf.Max(
                0f,
                _lingerBeats);
            _nextExercisePreviewBeats = Mathf.Max(
                0f,
                _nextExercisePreviewBeats);
            _feedbackDurationSeconds =
                Mathf.Max(0.1f, _feedbackDurationSeconds);
            _adaptiveHintMissThreshold = Mathf.Max(
                1,
                _adaptiveHintMissThreshold);
            _adaptiveHintDurationSeconds = Mathf.Max(
                0.5f,
                _adaptiveHintDurationSeconds);
            _successFeedbackDurationSeconds = Mathf.Max(
                0.1f,
                _successFeedbackDurationSeconds);
            _successPulseDurationSeconds = Mathf.Max(
                0.05f,
                _successPulseDurationSeconds);
            _successPulseScale = Mathf.Clamp(
                _successPulseScale,
                1f,
                1.5f);
        }

        private void OnValidate()
        {
            ResolveReferences();
            ValidateSettings();
            ConfigureTargetRing();
        }

        private static void SetText(
            TMP_Text target,
            string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }
    }
}
