using System;
using System.Collections.Generic;
using UnityEngine;

namespace IVRC
{
    public enum TrainingTimingVisualState
    {
        Pending,
        Hit,
        Miss
    }

    public readonly struct TrainingRhythmTimingMatch
    {
        public TrainingRhythmTimingMatch(
            int noteIndex,
            int repetitionNumber,
            BodyRepKind kind,
            BodySide side,
            RepTimingPoint timingPoint,
            double targetDspTime,
            double eventDspTime,
            bool wasHit)
        {
            NoteIndex = noteIndex;
            RepetitionNumber = repetitionNumber;
            Kind = kind;
            Side = side;
            TimingPoint = timingPoint;
            TargetDspTime = targetDspTime;
            EventDspTime = eventDspTime;
            WasHit = wasHit;
        }

        public int NoteIndex { get; }
        public int RepetitionNumber { get; }
        public BodyRepKind Kind { get; }
        public BodySide Side { get; }
        public RepTimingPoint TimingPoint { get; }
        public double TargetDspTime { get; }
        public double EventDspTime { get; }
        public bool WasHit { get; }
        public double TimingOffsetSeconds =>
            EventDspTime - TargetDspTime;
    }

    /// <summary>
    /// Owns chart progression and exact expected timing points. It does not
    /// detect body motion or award score; those remain reusable systems.
    /// </summary>
    [DefaultExecutionOrder(10050)]
    public sealed class TrainingRhythmChart : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private TrainingRhythmPattern _pattern;
        [SerializeField] private RhythmClock _rhythmClock;
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private BodyRepCounter _repCounter;

        [Header("Flow")]
        [SerializeField, Min(0f)]
        private float _completionGraceSeconds = 0.75f;
        [Tooltip(
            "At the start of each exercise block, keep rep detection " +
            "paused during the unscored line-art preview. Re-enable it this " +
            "many beats before the first scored UP so the real movement can " +
            "still be detected on the way to the beat.")]
        [SerializeField, Min(0f)]
        private float _blockPreviewReleaseLeadBeats = 1f;

        private sealed class RuntimeNote
        {
            private readonly bool[,] _resolved = new bool[2, 2];
            private readonly bool[,] _hit = new bool[2, 2];

            public RuntimeNote(TrainingRhythmNote note)
            {
                Note = note;
            }

            public TrainingRhythmNote Note { get; }

            public bool IsResolved(
                BodySide side,
                RepTimingPoint timingPoint)
            {
                return _resolved[
                    GetSideIndex(side),
                    GetTimingIndex(timingPoint)];
            }

            public bool WasHit(
                BodySide side,
                RepTimingPoint timingPoint)
            {
                return _hit[
                    GetSideIndex(side),
                    GetTimingIndex(timingPoint)];
            }

            public void Resolve(
                BodySide side,
                RepTimingPoint timingPoint,
                bool hit)
            {
                var sideIndex = GetSideIndex(side);
                var timingIndex = GetTimingIndex(timingPoint);
                _resolved[sideIndex, timingIndex] = true;
                _hit[sideIndex, timingIndex] = hit;
            }

            public TrainingTimingVisualState GetVisualState(
                RepTimingPoint timingPoint)
            {
                var anyMiss = false;
                foreach (var side in RequiredSides(Note))
                {
                    if (!IsResolved(side, timingPoint))
                    {
                        return TrainingTimingVisualState.Pending;
                    }

                    anyMiss |= !WasHit(side, timingPoint);
                }

                return anyMiss
                    ? TrainingTimingVisualState.Miss
                    : TrainingTimingVisualState.Hit;
            }

            public bool IsComplete =>
                GetVisualState(RepTimingPoint.Down) !=
                TrainingTimingVisualState.Pending;

            public bool IsSuccessful =>
                GetVisualState(RepTimingPoint.Up) ==
                    TrainingTimingVisualState.Hit &&
                GetVisualState(RepTimingPoint.Down) ==
                    TrainingTimingVisualState.Hit;

            private static int GetSideIndex(BodySide side)
            {
                return side == BodySide.Left ? 0 : 1;
            }

            private static int GetTimingIndex(
                RepTimingPoint timingPoint)
            {
                return timingPoint == RepTimingPoint.Up ? 0 : 1;
            }
        }

        private readonly List<TrainingRhythmNote> _notes =
            new List<TrainingRhythmNote>();
        private readonly List<RuntimeNote> _runtimeNotes =
            new List<RuntimeNote>();

        private int _activeClockSessionId = -1;

        public event Action SessionStarted;
        public event Action ChartChanged;

        public TrainingRhythmPattern Pattern => _pattern;
        public IReadOnlyList<TrainingRhythmNote> ScheduledNotes =>
            _notes;
        public bool IsRunning { get; private set; }
        public bool HasSession => _activeClockSessionId >= 0;
        public float CurrentBeat =>
            _rhythmClock == null ||
            !_rhythmClock.HasStarted
                ? 0f
                : (float)_rhythmClock.GetBeatPosition(
                    AudioSettings.dspTime);
        public float RecommendedDurationSeconds =>
            _pattern == null || _rhythmClock == null
                ? 0f
                : _pattern.TotalBeats *
                  _rhythmClock.SecondsPerBeat +
                  Mathf.Max(0f, _completionGraceSeconds);
        public int TotalRepetitions =>
            _notes.Count > 0
                ? _notes.Count
                : _pattern == null
                    ? 0
                    : _pattern.TotalRepetitions;
        public int TotalRequiredSideRepetitions =>
            _pattern == null
                ? 0
                : _pattern.TotalRequiredSideRepetitions;
        public int ResolvedNoteCount
        {
            get
            {
                var completed = 0;
                foreach (var note in _runtimeNotes)
                {
                    if (note.IsComplete)
                    {
                        completed++;
                    }
                }

                return completed;
            }
        }

        public int SuccessfulNoteCount
        {
            get
            {
                var successful = 0;
                foreach (var note in _runtimeNotes)
                {
                    if (note.IsSuccessful)
                    {
                        successful++;
                    }
                }

                return successful;
            }
        }

        public bool IsCompleted =>
            _runtimeNotes.Count > 0 &&
            ResolvedNoteCount >= _runtimeNotes.Count;

        private void Awake()
        {
            ResolveReferences();
            ValidateSettings();
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            ResetSession();
        }

        private void OnDisable()
        {
            Unsubscribe();
            IsRunning = false;
            _repCounter?.ClearActiveTrainingKindFilter();
        }

        private void Update()
        {
            if (!CanRun())
            {
                IsRunning = false;
                if (_gameFlow != null &&
                    _gameFlow.CurrentPhase ==
                    ExperiencePhase.Training)
                {
                    _repCounter?.SetActiveTrainingKind(null);
                }

                return;
            }

            EnsureSession();
            IsRunning = true;
            ApplyExerciseFilter();
        }

        public bool TryMatchTimingPoint(
            BodyRepKind kind,
            BodySide side,
            RepTimingPoint timingPoint,
            double eventDspTime,
            float maximumWindowSeconds,
            out TrainingRhythmTimingMatch match)
        {
            match = default;
            if (!CanRun())
            {
                return false;
            }

            EnsureSession();
            maximumWindowSeconds =
                Mathf.Max(0.001f, maximumWindowSeconds);
            RuntimeNote selected = null;
            var selectedTargetDspTime = 0d;

            for (var index = 0;
                 index < _runtimeNotes.Count;
                 index++)
            {
                var runtimeNote = _runtimeNotes[index];
                var note = runtimeNote.Note;
                if (note.Kind != kind ||
                    !note.Requires(side) ||
                    runtimeNote.IsResolved(side, timingPoint))
                {
                    continue;
                }

                var targetDspTime =
                    GetTargetDspTime(note, timingPoint);
                if (timingPoint == RepTimingPoint.Down &&
                    !runtimeNote.IsResolved(
                        side,
                        RepTimingPoint.Up))
                {
                    var upDeadline =
                        GetTargetDspTime(
                            note,
                            RepTimingPoint.Up) +
                        maximumWindowSeconds;
                    if (eventDspTime <= upDeadline)
                    {
                        return false;
                    }
                }

                if (eventDspTime <
                    targetDspTime -
                    maximumWindowSeconds)
                {
                    // This is the earliest unresolved matching point.
                    // A later note must not consume the input first.
                    return false;
                }

                if (eventDspTime >
                    targetDspTime +
                    maximumWindowSeconds)
                {
                    continue;
                }

                selected = runtimeNote;
                selectedTargetDspTime = targetDspTime;
                break;
            }

            if (selected == null)
            {
                return false;
            }

            selected.Resolve(side, timingPoint, true);
            match = new TrainingRhythmTimingMatch(
                selected.Note.Index,
                selected.Note.Index + 1,
                selected.Note.Kind,
                side,
                timingPoint,
                selectedTargetDspTime,
                eventDspTime,
                true);
            ChartChanged?.Invoke();
            return true;
        }

        public bool TryTakeExpiredTimingPoint(
            double currentDspTime,
            float maximumWindowSeconds,
            out TrainingRhythmTimingMatch miss)
        {
            miss = default;
            if (!CanRun())
            {
                return false;
            }

            EnsureSession();
            maximumWindowSeconds =
                Mathf.Max(0.001f, maximumWindowSeconds);

            foreach (var runtimeNote in _runtimeNotes)
            {
                foreach (var timingPoint in TimingPoints)
                {
                    var targetDspTime = GetTargetDspTime(
                        runtimeNote.Note,
                        timingPoint);
                    if (currentDspTime <=
                        targetDspTime +
                        maximumWindowSeconds)
                    {
                        continue;
                    }

                    foreach (var side in RequiredSides(
                                 runtimeNote.Note))
                    {
                        if (runtimeNote.IsResolved(
                                side,
                                timingPoint))
                        {
                            continue;
                        }

                        runtimeNote.Resolve(
                            side,
                            timingPoint,
                            false);
                        miss = new TrainingRhythmTimingMatch(
                            runtimeNote.Note.Index,
                            runtimeNote.Note.Index + 1,
                            runtimeNote.Note.Kind,
                            side,
                            timingPoint,
                            targetDspTime,
                            targetDspTime,
                            false);
                        ChartChanged?.Invoke();
                        return true;
                    }
                }
            }

            return false;
        }

        public TrainingTimingVisualState GetTimingVisualState(
            int noteIndex,
            RepTimingPoint timingPoint)
        {
            return noteIndex < 0 ||
                   noteIndex >= _runtimeNotes.Count
                ? TrainingTimingVisualState.Pending
                : _runtimeNotes[noteIndex]
                    .GetVisualState(timingPoint);
        }

        public double GetTargetDspTime(
            TrainingRhythmNote note,
            RepTimingPoint timingPoint)
        {
            return _rhythmClock == null
                ? double.NaN
                : _rhythmClock.GetBeatDspTime(
                    note.GetBeat(timingPoint));
        }

        public bool TryGetActiveKind(out BodyRepKind kind)
        {
            kind = default;
            var index = GetActiveNoteIndex();
            if (index < 0)
            {
                return false;
            }

            kind = _notes[index].Kind;
            return true;
        }

        public bool TryGetNextDifferentKind(
            out BodyRepKind kind)
        {
            return TryGetNextDifferentKind(
                out kind,
                out _);
        }

        public bool TryGetNextDifferentKind(
            out BodyRepKind kind,
            out float beatsUntil)
        {
            kind = default;
            beatsUntil = 0f;
            var currentIndex = GetActiveNoteIndex();
            if (currentIndex < 0)
            {
                return false;
            }

            var currentKind = _notes[currentIndex].Kind;
            for (var index = currentIndex + 1;
                 index < _notes.Count;
                 index++)
            {
                if (_notes[index].Kind == currentKind)
                {
                    continue;
                }

                kind = _notes[index].Kind;
                beatsUntil = Mathf.Max(
                    0f,
                    _notes[index].UpBeat -
                    CurrentBeat);
                return true;
            }

            return false;
        }

        [ContextMenu("Reset Training Chart")]
        public void ResetSession()
        {
            IsRunning = false;
            _activeClockSessionId = -1;
            _notes.Clear();
            _runtimeNotes.Clear();
            _repCounter?.SetActiveTrainingKind(null);
            ChartChanged?.Invoke();
        }

        private void EnsureSession()
        {
            if (_rhythmClock == null ||
                !_rhythmClock.HasStarted)
            {
                return;
            }

            if (_activeClockSessionId ==
                _rhythmClock.ClockSessionId)
            {
                return;
            }

            _notes.Clear();
            _runtimeNotes.Clear();
            if (_pattern != null)
            {
                _notes.AddRange(_pattern.BuildSchedule());
            }

            foreach (var note in _notes)
            {
                _runtimeNotes.Add(new RuntimeNote(note));
            }

            _activeClockSessionId =
                _rhythmClock.ClockSessionId;
            SessionStarted?.Invoke();
            ChartChanged?.Invoke();
        }

        private void ApplyExerciseFilter()
        {
            var index = GetActiveNoteIndex();
            if (index < 0)
            {
                _repCounter?.SetActiveTrainingKind(null);
                return;
            }

            var note = _notes[index];
            var isUnscoredBlockPreview =
                note.RepetitionInBlock == 0 &&
                CurrentBeat <
                    note.UpBeat -
                    Mathf.Max(
                        0f,
                        _blockPreviewReleaseLeadBeats);
            if (isUnscoredBlockPreview)
            {
                _repCounter?.SetActiveTrainingKind(null);
                return;
            }

            _repCounter?.SetActiveTrainingKind(note.Kind);
        }

        private int GetActiveNoteIndex()
        {
            for (var index = 0;
                 index < _runtimeNotes.Count;
                 index++)
            {
                if (!_runtimeNotes[index].IsComplete)
                {
                    return index;
                }
            }

            return -1;
        }

        private bool CanRun()
        {
            return
                _pattern != null &&
                _gameFlow != null &&
                _gameFlow.CurrentPhase ==
                    ExperiencePhase.Training &&
                _rhythmClock != null &&
                _rhythmClock.IsRunning;
        }

        private void HandlePhaseChanged(ExperiencePhase phase)
        {
            if (phase == ExperiencePhase.Training)
            {
                ResetSession();
                return;
            }

            IsRunning = false;
            _repCounter?.ClearActiveTrainingKindFilter();
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
            _rhythmClock ??= FindFirstObjectByType<RhythmClock>();
            _gameFlow ??=
                FindFirstObjectByType<GameFlowController>();
            _repCounter ??=
                FindFirstObjectByType<BodyRepCounter>();
        }

        private void ValidateSettings()
        {
            _blockPreviewReleaseLeadBeats = Mathf.Max(
                0f,
                _blockPreviewReleaseLeadBeats);
            _completionGraceSeconds = Mathf.Max(
                0f,
                _completionGraceSeconds);
        }

        private void OnValidate()
        {
            ResolveReferences();
            ValidateSettings();
        }

        private static readonly RepTimingPoint[] TimingPoints =
        {
            RepTimingPoint.Up,
            RepTimingPoint.Down
        };

        private static IEnumerable<BodySide> RequiredSides(
            TrainingRhythmNote note)
        {
            if (note.Requires(BodySide.Left))
            {
                yield return BodySide.Left;
            }

            if (note.Requires(BodySide.Right))
            {
                yield return BodySide.Right;
            }
        }
    }
}
