using TMPro;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Pure helpers for the announcement panel's countdown.
    /// </summary>
    public static class TrainingAnnounceMath
    {
        /// <summary>How many beats of countdown the panel shows.</summary>
        public const int CountdownBeats = 4;

        /// <summary>
        /// What the panel says once the set is over. The whistle sounds as
        /// Transformation begins, so the first line lands with it, and the
        /// instruction to move follows a beat later rather than arriving
        /// before the sound that ends the set.
        /// </summary>
        public static string GetCompletionHeadline(
            float secondsSinceCompleted,
            float holdSeconds)
        {
            if (float.IsNaN(secondsSinceCompleted) ||
                float.IsInfinity(secondsSinceCompleted) ||
                float.IsNaN(holdSeconds) ||
                float.IsInfinity(holdSeconds))
            {
                return CompletedHeadline;
            }

            return secondsSinceCompleted < Mathf.Max(0f, holdSeconds)
                ? CompletedHeadline
                : LeaveHeadline;
        }

        public const string CompletedHeadline = "終了！";
        public const string LeaveHeadline =
            "そろそろ時間だ！ ステージへ行こう！";

        /// <summary>
        /// One label per beat over the four beats before a block starts:
        /// three, two, one, go. Empty outside that window, so the panel is
        /// silent rather than showing a stale number.
        /// </summary>
        public static string GetCountdownLabel(float beatsUntilUp)
        {
            if (float.IsNaN(beatsUntilUp) ||
                float.IsInfinity(beatsUntilUp) ||
                beatsUntilUp <= 0f ||
                beatsUntilUp > CountdownBeats)
            {
                return string.Empty;
            }

            if (beatsUntilUp > 3f)
            {
                return "3";
            }

            if (beatsUntilUp > 2f)
            {
                return "2";
            }

            return beatsUntilUp > 1f ? "1" : "GO";
        }
    }

    /// <summary>
    /// The wide panel low and central in front of the participant.
    ///
    /// It names the exercise about to start and counts the four beats into
    /// it, and once the set is over it says where to go next.
    ///
    /// It is deliberately not part of the rhythm lane. The lane lives under
    /// Phase_Training and stops updating the moment the phase changes, and
    /// the flow switches that whole subtree off on the way to the stage — so
    /// a message about leaving could only ever have shown for one frame from
    /// there. This lives outside it and reads the phase itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrainingAnnounceHud : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private TrainingRhythmChart _chart;

        [Header("Panel")]
        [SerializeField] private GameObject _root;
        [Tooltip("The exercise name, or the instruction to leave.")]
        [SerializeField] private TMP_Text _headlineText;
        [Tooltip(
            "Sits above the headline. Counts the beats into the block so " +
            "the first scored repetition is not a surprise.")]
        [SerializeField] private TMP_Text _countdownText;

        [Header("Completion")]
        [Tooltip(
            "How long 終了！ holds with the whistle before the panel turns " +
            "into the instruction to leave.")]
        [SerializeField, Min(0f)]
        private float _completedHoldSeconds = 1.2f;

        private float _completedSeconds;

        private void OnDisable()
        {
            Hide();
        }

        private void Update()
        {
            if (_gameFlow == null)
            {
                Hide();
                _completedSeconds = 0f;
                return;
            }

            // The whistle sounds on the repetition that ends the set, and
            // 終了！ lands with it. The instruction to move waits out the
            // hold, so the two do not arrive on top of each other. Both
            // states run the same clock, because the set ends before the
            // phase does.
            if (_gameFlow.CurrentPhase ==
                ExperiencePhase.Transformation)
            {
                ShowCompletion();
                return;
            }

            if (_gameFlow.CurrentPhase != ExperiencePhase.Training ||
                _chart == null ||
                !_chart.HasSession)
            {
                Hide();
                _completedSeconds = 0f;
                return;
            }

            if (!TryGetPendingNoteIndex(out var index))
            {
                ShowCompletion();
                return;
            }

            _completedSeconds = 0f;

            var note = _chart.ScheduledNotes[index];
            var state = TrainingRhythmLaneHudText.GetPreparationState(
                _chart.ScheduledNotes,
                index,
                _chart.CurrentBeat);
            var headline = TrainingRhythmLaneHudText.GetAnnouncement(
                state,
                note.Kind);
            if (string.IsNullOrEmpty(headline))
            {
                Hide();
                return;
            }

            Show(
                headline,
                TrainingAnnounceMath.GetCountdownLabel(
                    note.UpBeat - _chart.CurrentBeat));
        }

        private void ShowCompletion()
        {
            _completedSeconds += Time.deltaTime;
            Show(
                TrainingAnnounceMath.GetCompletionHeadline(
                    _completedSeconds,
                    _completedHoldSeconds),
                string.Empty);
        }

        private bool TryGetPendingNoteIndex(out int pendingIndex)
        {
            pendingIndex = -1;
            var notes = _chart.ScheduledNotes;
            for (var index = 0; index < notes.Count; index++)
            {
                if (_chart.GetTimingVisualState(
                        notes[index].Index,
                        RepTimingPoint.Down) !=
                    TrainingTimingVisualState.Pending)
                {
                    continue;
                }

                pendingIndex = index;
                return true;
            }

            return false;
        }

        private void Show(string headline, string countdown)
        {
            SetRootActive(true);
            if (_headlineText != null)
            {
                _headlineText.text = headline ?? string.Empty;
            }

            if (_countdownText != null)
            {
                _countdownText.text = countdown ?? string.Empty;
            }
        }

        private void Hide()
        {
            SetRootActive(false);
            if (_headlineText != null)
            {
                _headlineText.text = string.Empty;
            }

            if (_countdownText != null)
            {
                _countdownText.text = string.Empty;
            }
        }

        private void SetRootActive(bool active)
        {
            if (_root != null &&
                _root.activeSelf != active)
            {
                _root.SetActive(active);
            }
        }
    }
}
