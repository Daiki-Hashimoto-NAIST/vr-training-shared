using System.Collections.Generic;
using TMPro;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

namespace IVRC
{
    /// <summary>
    /// Runs the introduction between calibration and training: a few lines of
    /// situation, then one practice segment per movement.
    ///
    /// The progression itself lives in <see cref="TutorialSequence"/>, which
    /// has no Unity dependencies. This component only supplies input, time
    /// and the rep feed, and composes the text the HUD shows.
    /// </summary>
    [DefaultExecutionOrder(10200)]
    [DisallowMultipleComponent]
    public sealed class TutorialController : MonoBehaviour
    {
        [Header("Systems")]
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private BodyRepCounter _repCounter;
        [SerializeField] private TrainingRhythmScorer _rhythmScorer;
        [SerializeField] private TrainingRhythmChart _rhythmChart;
        [SerializeField] private TutorialMusicController _music;

        [Header("Steps")]
        [SerializeField] private List<TutorialStep> _steps =
            new List<TutorialStep>();

        [Header("Input")]
        [Tooltip(
            "Confirm button. A is used for Start outside the tutorial, but " +
            "that input does not advance the game flow during this phase.")]
        [SerializeField] private OVRInput.RawButton _confirmButton =
            OVRInput.RawButton.A;

        [Header("Panel")]
        [Tooltip(
            "The introduction speaks in the participant's own voice, so it " +
            "gets a wide, shallow panel of its own rather than the tall " +
            "status board off to the side.")]
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private TMP_Text _messageLabel;

        [Header("Confirm Sound")]
        [Tooltip(
            "Played when the participant presses to advance a line. Its own " +
            "slot rather than the calibration sounds: those confirm that a " +
            "measurement was taken, this confirms a button was heard.")]
        [SerializeField] private AudioSource _confirmSource;
        [SerializeField] private AudioClip _confirmClip;
        [SerializeField, Range(0f, 1f)]
        private float _confirmVolume = 0.6f;

        [Header("Presentation")]
        [SerializeField] private string _accentColor = "#70FFB0";
        [SerializeField] private string _mutedColor = "#8CA0AE";
        [SerializeField] private string _confirmGlyph =
            "Aボタン（右手上側）";

        private TutorialSequence _sequence;
        private readonly TutorialPracticeJudge _practiceJudge = new();
        private int _practiceStep = -1;
        private string _practiceFeedback = string.Empty;
        private float _feedbackUntil;
        public string PracticeFeedback => CurrentStepIsPractice &&
            Time.unscaledTime < _feedbackUntil ? _practiceFeedback : string.Empty;

        public bool HasSteps => _steps != null && _steps.Count > 0;
        public bool IsRunning { get; private set; }
        public bool IsComplete =>
            _sequence == null || _sequence.IsComplete;
        public int CurrentIndex =>
            _sequence == null ? 0 : _sequence.CurrentIndex;
        public int StepCount =>
            _sequence == null ? 0 : _sequence.StepCount;

        /// <summary>
        /// True while the current step asks for a movement rather than a line
        /// to read, which is when a guide pose is worth showing.
        /// </summary>
        public bool CurrentStepIsPractice =>
            !IsComplete &&
            EnsureSequence().CurrentStep.Kind ==
                TutorialStepKind.Practice;

        /// <summary>The movement the current step is teaching.</summary>
        public BodyRepKind CurrentExercise =>
            IsComplete
                ? default
                : EnsureSequence().CurrentStep.Exercise;

        /// <summary>
        /// Longest the introduction can run, which is the figure the five
        /// minute budget has to be checked against.
        /// </summary>
        public float WorstCaseSeconds =>
            EnsureSequence().WorstCaseSeconds();

        private void Awake()
        {
            ResolveReferences();
            EnsureSequence();
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ResetPractice();
        }

        private void LateUpdate()
        {
            if (TryPreparePractice(out var beat)) _practiceJudge.Tick(beat);
            RefreshPanel();
        }

        private void Update()
        {
            if (!IsRunning || _sequence == null)
            {
                return;
            }

            if (WasConfirmPressed())
            {
                // Sounds on the press, not on the advance: a practice step
                // ignores confirm, and silence there reads as a dead button
                // rather than as a step that cannot be skipped.
                PlayConfirmSound();
                _sequence.Confirm();
            }

            _sequence.Tick(Time.unscaledDeltaTime);
            if (_sequence.IsComplete)
            {
                IsRunning = false;
            }

            RefreshPanel();
        }

        private void RefreshPanel()
        {
            var visible = IsRunning && !IsComplete;
            if (_panelRoot != null &&
                _panelRoot.activeSelf != visible)
            {
                _panelRoot.SetActive(visible);
            }

            if (_messageLabel != null)
            {
                _messageLabel.text = visible
                    ? BuildHudBlock()
                    : string.Empty;
            }
        }

        public void BeginTutorial()
        {
            ResolveReferences();
            ResetPractice();
            EnsureSequence().Restart();
            IsRunning = !_sequence.IsComplete;
            RefreshPanel();
        }

        public void EndTutorial()
        {
            IsRunning = false;
            ResetPractice();
            RefreshPanel();
        }

        /// <summary>
        /// Staff shortcut for a visitor who cannot or will not continue.
        /// </summary>
        public void SkipAll()
        {
            EnsureSequence().SkipAll();
            IsRunning = false;
            ResetPractice();
            RefreshPanel();
        }

        /// <summary>
        /// The line being read, or the movement being asked for.
        /// </summary>
        public string MessageText
        {
            get
            {
                var sequence = EnsureSequence();
                if (sequence.IsComplete)
                {
                    return string.Empty;
                }

                return sequence.CurrentStep.Message ?? string.Empty;
            }
        }

        /// <summary>
        /// The remaining repetitions, kept out of the message so the two do
        /// not read as one wall of text. Empty during a line.
        /// </summary>
        public string ProgressText
        {
            get
            {
                var sequence = EnsureSequence();
                if (sequence.IsComplete)
                {
                    return string.Empty;
                }

                var step = sequence.CurrentStep;
                if (step.Kind != TutorialStepKind.Practice)
                {
                    return string.Empty;
                }

                var remaining = Mathf.Max(
                    0,
                    Mathf.Max(1, step.RequiredReps) -
                    sequence.RepsInStep);
                return remaining <= 0
                    ? "OK!"
                    : $"あと {remaining}回";
            }
        }

        /// <summary>
        /// The button hint, shown only where pressing it does something.
        /// </summary>
        public string HintText
        {
            get
            {
                var sequence = EnsureSequence();
                if (sequence.IsComplete ||
                    sequence.CurrentStep.Kind !=
                    TutorialStepKind.Message)
                {
                    return string.Empty;
                }

                return $"{_confirmGlyph} 次へ";
            }
        }

        /// <summary>
        /// One block for the existing flow HUD. The remaining repetitions
        /// are given their own line, larger and in the accent colour, so the
        /// participant can tell at a glance what still has to happen.
        /// </summary>
        public string BuildHudBlock()
        {
            var sequence = EnsureSequence();
            if (sequence.IsComplete)
            {
                return string.Empty;
            }

            var block = $"<size=115%><b>{MessageText}</b></size>";

            var progress = ProgressText;
            if (!string.IsNullOrEmpty(progress))
            {
                block +=
                    $"\n\n<size=150%><b><color={_accentColor}>" +
                    $"{progress}</color></b></size>";
            }

            var hint = HintText;
            var feedback = PracticeFeedback;
            if (!string.IsNullOrEmpty(feedback))
            {
                block += $"\n<size=100%><color=#FFD580>{feedback}</color></size>";
            }
            if (!string.IsNullOrEmpty(hint))
            {
                block +=
                    $"\n<align=right><size=85%><color={_mutedColor}>" +
                    $"{hint}</color></size></align>";
            }

            return block;
        }

        private void PlayConfirmSound()
        {
            if (_confirmSource == null || _confirmClip == null)
            {
                return;
            }

            _confirmSource.PlayOneShot(
                _confirmClip,
                Mathf.Clamp01(_confirmVolume));
        }

        private bool WasConfirmPressed()
        {
            if (OVRInput.GetDown(_confirmButton))
            {
                return true;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var keyboard = Keyboard.current;
            if (keyboard != null &&
                keyboard.aKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
            return false;
        }

        private TutorialSequence EnsureSequence()
        {
            return _sequence ??= new TutorialSequence(_steps);
        }

        private void HandleTimingPointReached(
            BodyRepKind kind,
            BodySide side,
            RepTimingPoint timingPoint,
            int count)
        {
            if (!TryPreparePractice(out var beat)) return;
            _practiceJudge.Report(kind, side, timingPoint, beat);
        }

        private bool TryPreparePractice(out float beat)
        {
            beat = 0f;
            if (!IsRunning || !CurrentStepIsPractice ||
                _repCounter == null || !_repCounter.IsReady ||
                _music == null || !_music.TryGetBeatPosition(out beat) ||
                _rhythmScorer == null || _rhythmChart == null ||
                _rhythmChart.Pattern == null)
            {
                ResetPractice();
                return false;
            }
            if (_practiceStep == CurrentIndex && _practiceJudge.IsRunning) return true;
            ResetPractice();
            foreach (var block in _rhythmChart.Pattern.Blocks)
            {
                if (block == null || block.Kind != CurrentExercise) continue;
                _practiceStep = CurrentIndex;
                _practiceJudge.Begin(block, beat, _music.SecondsPerBeat,
                    _rhythmScorer.PerfectWindowSeconds, _rhythmScorer.MaximumWindowSeconds);
                return _practiceJudge.IsRunning;
            }
            return false;
        }

        private void ResetPractice()
        {
            _practiceJudge.Reset();
            _practiceStep = -1;
            _practiceFeedback = string.Empty;
            _feedbackUntil = 0f;
        }

        private void HandlePracticeSuccess(BodyRepKind kind)
        {
            if (!IsRunning || _practiceStep != CurrentIndex) return;
            var advanced = _sequence.ReportSuccessfulRep(kind);
            if (advanced) ResetPractice();
            else
            {
                _practiceFeedback = string.Empty;
                _feedbackUntil = 0f;
            }
        }

        private void HandlePracticeFeedback(string message)
        {
            if (!IsRunning || _practiceStep != CurrentIndex) return;
            // Keep an explicit timing diagnosis readable when a missing arm
            // expires in the same frame.
            if (message == TutorialPracticeJudge.PoseMessage &&
                !string.IsNullOrEmpty(PracticeFeedback)) return;
            _practiceFeedback = message;
            _feedbackUntil = Time.unscaledTime + 1.5f;
        }

        private void Subscribe()
        {
            _practiceJudge.RepetitionSucceeded -= HandlePracticeSuccess;
            _practiceJudge.RepetitionSucceeded += HandlePracticeSuccess;
            _practiceJudge.FeedbackReady -= HandlePracticeFeedback;
            _practiceJudge.FeedbackReady += HandlePracticeFeedback;
            if (_repCounter == null)
            {
                return;
            }

            _repCounter.TimingPointReached -= HandleTimingPointReached;
            _repCounter.TimingPointReached += HandleTimingPointReached;
        }

        private void Unsubscribe()
        {
            _practiceJudge.RepetitionSucceeded -= HandlePracticeSuccess;
            _practiceJudge.FeedbackReady -= HandlePracticeFeedback;
            if (_repCounter != null)
            {
                _repCounter.TimingPointReached -= HandleTimingPointReached;
            }
        }

        private void ResolveReferences()
        {
            _rhythmScorer ??= FindFirstObjectByType<TrainingRhythmScorer>(FindObjectsInactive.Include);
            _rhythmChart ??= FindFirstObjectByType<TrainingRhythmChart>(FindObjectsInactive.Include);
            _music ??= FindFirstObjectByType<TutorialMusicController>(FindObjectsInactive.Include);
            _gameFlow ??=
                FindFirstObjectByType<GameFlowController>();
            _repCounter ??=
                FindFirstObjectByType<BodyRepCounter>(
                    FindObjectsInactive.Include);
        }
    }
}
