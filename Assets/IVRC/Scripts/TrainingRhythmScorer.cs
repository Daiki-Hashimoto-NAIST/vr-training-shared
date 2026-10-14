using System;
using System.Collections.Generic;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Deterministic scoring rules for one UP or DOWN timing point.
    /// One complete rep has two timing points, so each awards half of the
    /// configured per-rep growth.
    /// </summary>
    public static class TrainingRhythmMath
    {
        public static float CalculateMultiplier(
            float accuracy,
            float minimumMultiplier,
            float perfectMultiplier)
        {
            return Mathf.Lerp(
                Mathf.Clamp01(minimumMultiplier),
                Mathf.Max(
                    1f,
                    minimumMultiplier,
                    perfectMultiplier),
                Mathf.Clamp01(accuracy));
        }

        public static float CalculateTimingPointGrowth(
            float growthPerRep,
            float multiplier)
        {
            return Mathf.Max(0f, growthPerRep) *
                   0.5f *
                   Mathf.Max(0f, multiplier);
        }

        public static float CalculateNormalizedAccuracy(
            float absoluteOffsetSeconds,
            float perfectWindowSeconds,
            float maximumWindowSeconds)
        {
            perfectWindowSeconds =
                Mathf.Max(0f, perfectWindowSeconds);
            maximumWindowSeconds = Mathf.Max(
                perfectWindowSeconds + 0.001f,
                maximumWindowSeconds);
            return 1f - Mathf.InverseLerp(
                perfectWindowSeconds,
                maximumWindowSeconds,
                Mathf.Max(0f, absoluteOffsetSeconds));
        }

        public static TrainingTimingGrade GetTimingGrade(
            float accuracy)
        {
            if (accuracy >= 0.9f)
            {
                return TrainingTimingGrade.Perfect;
            }

            if (accuracy >= 0.65f)
            {
                return TrainingTimingGrade.Good;
            }

            if (accuracy >= 0.35f)
            {
                return TrainingTimingGrade.Ok;
            }

            return TrainingTimingGrade.Miss;
        }

        public static string GetTimingLabel(float accuracy)
        {
            return GetTimingGrade(accuracy) switch
            {
                TrainingTimingGrade.Perfect => "PERFECT",
                TrainingTimingGrade.Good => "GOOD",
                TrainingTimingGrade.Ok => "OK",
                _ => "MISS"
            };
        }
    }

    /// <summary>
    /// The four verdicts the player is shown, as a value rather than a string,
    /// so sound and colour can be keyed to the same judgement without either
    /// re-deriving the thresholds.
    /// </summary>
    public enum TrainingTimingGrade
    {
        Miss,
        Ok,
        Good,
        Perfect
    }

    public readonly struct TrainingTimingFeedback
    {
        public TrainingTimingFeedback(
            int noteIndex,
            int repetitionNumber,
            BodyRepKind kind,
            RepTimingPoint timingPoint,
            float accuracy,
            bool allRequiredSidesHit)
        {
            NoteIndex = noteIndex;
            RepetitionNumber = repetitionNumber;
            Kind = kind;
            TimingPoint = timingPoint;
            Accuracy = Mathf.Clamp01(accuracy);
            AllRequiredSidesHit = allRequiredSidesHit;
            IsValid = true;
        }

        public int NoteIndex { get; }
        public int RepetitionNumber { get; }
        public BodyRepKind Kind { get; }
        public RepTimingPoint TimingPoint { get; }
        public float Accuracy { get; }
        public bool AllRequiredSidesHit { get; }
        public bool IsValid { get; }
        public string TimingPointLabel =>
            TimingPoint == RepTimingPoint.Up ? "UP" : "DOWN";
        public string TimingLabel =>
            IsValid
                ? TrainingRhythmMath.GetTimingLabel(Accuracy)
                : "--";
    }

    public enum TrainingRepFinalizationReason
    {
        Completed,
        TimedOut
    }

    /// <summary>
    /// One immutable chart-note result published only after both UP and DOWN
    /// have been resolved for every required side. Motion completion and
    /// rhythm success remain separate facts so a completed exercise is never
    /// presented as if it did not happen merely because its timing was late.
    /// </summary>
    public readonly struct TrainingRepTimingResult
    {
        public TrainingRepTimingResult(
            int clockSessionId,
            int noteIndex,
            int repetitionNumber,
            BodyRepKind kind,
            TrainingSideMask requiredSides,
            TrainingTimingFeedback up,
            TrainingTimingFeedback down,
            TrainingRepFinalizationReason finalizationReason)
        {
            ClockSessionId = clockSessionId;
            NoteIndex = noteIndex;
            RepetitionNumber = repetitionNumber;
            Kind = kind;
            RequiredSides = requiredSides & TrainingSideMask.Both;
            Up = up;
            Down = down;
            FinalizationReason = finalizationReason;
            IsValid =
                up.IsValid &&
                down.IsValid &&
                up.NoteIndex == noteIndex &&
                down.NoteIndex == noteIndex &&
                up.RepetitionNumber == repetitionNumber &&
                down.RepetitionNumber == repetitionNumber &&
                up.Kind == kind &&
                down.Kind == kind &&
                up.TimingPoint == RepTimingPoint.Up &&
                down.TimingPoint == RepTimingPoint.Down;
        }

        public int ClockSessionId { get; }
        public int NoteIndex { get; }
        public int RepetitionNumber { get; }
        public BodyRepKind Kind { get; }
        public TrainingSideMask RequiredSides { get; }
        public TrainingTimingFeedback Up { get; }
        public TrainingTimingFeedback Down { get; }
        public TrainingRepFinalizationReason FinalizationReason { get; }
        public bool IsValid { get; }
        public bool MotionCompleted =>
            IsValid &&
            Down.AllRequiredSidesHit;
        public bool RhythmCleared =>
            MotionCompleted &&
            Up.AllRequiredSidesHit &&
            Up.Accuracy >=
                TrainingSuccessFeedbackRules.MinimumSuccessAccuracy &&
            Down.Accuracy >=
                TrainingSuccessFeedbackRules.MinimumSuccessAccuracy;
        public float CombinedAccuracy =>
            RhythmCleared
                ? Mathf.Min(Up.Accuracy, Down.Accuracy)
                : 0f;
    }

    /// <summary>
    /// Player-facing rules for the final UP + DOWN result of one repetition.
    /// </summary>
    public static class TrainingSuccessFeedbackRules
    {
        public const float MinimumSuccessAccuracy = 0.35f;

        public static bool IsMotionCompleted(
            TrainingRepTimingResult result)
        {
            return result.IsValid && result.MotionCompleted;
        }

        public static bool IsRhythmClear(
            TrainingRepTimingResult result)
        {
            return result.IsValid && result.RhythmCleared;
        }
    }

    /// <summary>
    /// Scores expected chart points when a chart is present, or falls back to
    /// the nearest rhythm beat for isolated detector tests and simple scenes.
    /// </summary>
    [DefaultExecutionOrder(10150)]
    public sealed class TrainingRhythmScorer : MonoBehaviour
    {
        private sealed class TimingFeedbackAccumulator
        {
            public TrainingSideMask ResolvedSides;
            public float MinimumAccuracy = 1f;
            public bool AllRequiredSidesHit = true;
        }

        private sealed class RepTimingFeedbackAccumulator
        {
            public TrainingTimingFeedback Up;
            public TrainingTimingFeedback Down;
            public bool HasUp;
            public bool HasDown;
        }

        [Header("Data")]
        [SerializeField] private RhythmClock _rhythmClock;
        [SerializeField] private BodyRepCounter _repCounter;
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private TrainingRhythmChart _rhythmChart;

        [Header("Timing Windows")]
        [SerializeField, Min(0f)] private float _perfectWindowSeconds = 0.08f;
        [SerializeField, Min(0.01f)] private float _maximumWindowSeconds = 0.35f;

        [Header("Growth")]
        [Tooltip(
            "Growth per side-specific rep when no chart is assigned.")]
        [SerializeField, Min(0f)] private float _growthPerRep = 0.04f;
        [Tooltip(
            "Total growth awarded by a fully perfect chart.")]
        [SerializeField, Range(0f, 1f)]
        private float _targetChartGrowth = 1f;
        [SerializeField, Range(0f, 1f)] private float _minimumMultiplier = 0.25f;
        [SerializeField, Min(1f)] private float _perfectMultiplier = 1f;

        [Tooltip(
            "Logs every scored timing point. Off by default: these fire " +
            "alongside the rep counter's own, several per frame at the moment " +
            "a repetition completes, and each one captures a stack trace.")]
        [SerializeField] private bool _logScoreDetails;

        public event Action ScoreChanged;
        public float PerfectWindowSeconds => _perfectWindowSeconds;
        public float MaximumWindowSeconds => _maximumWindowSeconds;
        public event Action<TrainingTimingFeedback>
            TimingFeedbackReady;
        public event Action<TrainingRepTimingResult>
            RepTimingResultReady;

        public float GrowthNormalized { get; private set; }
        public float LastAccuracy { get; private set; }
        public float LastMultiplier { get; private set; } = 1f;
        public float LastTimingOffsetSeconds { get; private set; }
        public string LastTimingLabel { get; private set; } = "--";
        public string LastTimingPointLabel { get; private set; } = "--";
        public TrainingTimingFeedback LastTimingFeedback
        {
            get;
            private set;
        }
        public TrainingRepTimingResult LastRepTimingResult
        {
            get;
            private set;
        }
        public int ScoredTimingPointCount { get; private set; }
        public float AverageAccuracy =>
            ScoredTimingPointCount <= 0
                ? 0f
                : Mathf.Clamp01(
                    _accumulatedAccuracy /
                    ScoredTimingPointCount);

        private float _accumulatedAccuracy;
        private readonly Dictionary<int, TimingFeedbackAccumulator>
            _timingFeedbackAccumulators =
                new Dictionary<int, TimingFeedbackAccumulator>();
        private readonly Dictionary<long, RepTimingFeedbackAccumulator>
            _repTimingFeedbackAccumulators =
                new Dictionary<long, RepTimingFeedbackAccumulator>();
        private readonly HashSet<long> _publishedRepTimingResults =
            new HashSet<long>();

        // Per-muscle-group growth, indexed by MuscleGrowthChannel. Accumulated
        // with the same increment as GrowthNormalized so an exercise only
        // grows the group it trains.
        private readonly float[] _channelGrowth =
            new float[
                System.Enum.GetValues(typeof(MuscleGrowthChannel)).Length];

        /// <summary>
        /// Growth for one muscle group. Overall returns the whole-body value.
        /// </summary>
        public float GetChannelGrowthNormalized(
            MuscleGrowthChannel channel)
        {
            if (channel == MuscleGrowthChannel.Overall)
            {
                return GrowthNormalized;
            }

            // The chest is worked by both arm exercises, so it advances with
            // their combined progress on that side.
            if (channel == MuscleGrowthChannel.ChestLeft)
            {
                return AverageChannels(
                    MuscleGrowthChannel.ElbowFlexionLeft,
                    MuscleGrowthChannel.ArmRaiseLeft);
            }

            if (channel == MuscleGrowthChannel.ChestRight)
            {
                return AverageChannels(
                    MuscleGrowthChannel.ElbowFlexionRight,
                    MuscleGrowthChannel.ArmRaiseRight);
            }

            return ReadChannel(channel);
        }

        /// <summary>
        /// Captures every live growth channel together for the phase boundary.
        /// The returned value is detached from this scorer's mutable state.
        /// </summary>
        public MuscleGrowthSnapshot CaptureGrowthSnapshot()
        {
            return new MuscleGrowthSnapshot(
                new MuscleGrowthChannels(
                    GrowthNormalized,
                    GetChannelGrowthNormalized(
                        MuscleGrowthChannel.ElbowFlexionLeft),
                    GetChannelGrowthNormalized(
                        MuscleGrowthChannel.ElbowFlexionRight),
                    GetChannelGrowthNormalized(
                        MuscleGrowthChannel.ArmRaiseLeft),
                    GetChannelGrowthNormalized(
                        MuscleGrowthChannel.ArmRaiseRight),
                    GetChannelGrowthNormalized(
                        MuscleGrowthChannel.ChestLeft),
                    GetChannelGrowthNormalized(
                        MuscleGrowthChannel.ChestRight)));
        }

        private float AverageChannels(
            MuscleGrowthChannel first,
            MuscleGrowthChannel second)
        {
            return Mathf.Clamp01(
                (ReadChannel(first) + ReadChannel(second)) * 0.5f);
        }

        private float ReadChannel(MuscleGrowthChannel channel)
        {
            var index = (int)channel;
            return index >= 0 && index < _channelGrowth.Length
                ? _channelGrowth[index]
                : 0f;
        }

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
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void LateUpdate()
        {
            if (_rhythmChart == null ||
                _gameFlow == null ||
                _gameFlow.CurrentPhase !=
                    ExperiencePhase.Training ||
                _rhythmClock == null ||
                !_rhythmClock.IsRunning ||
                (_repCounter != null &&
                 !_repCounter.IsReady))
            {
                return;
            }

            var currentDspTime = AudioSettings.dspTime;
            var expiredPointCount = 0;
            while (expiredPointCount < 256 &&
                   _rhythmChart.TryTakeExpiredTimingPoint(
                       currentDspTime,
                       _maximumWindowSeconds,
                       out var miss))
            {
                ApplyChartTimingPoint(
                    miss,
                    0f,
                    false);
                expiredPointCount++;
            }
        }

        private void OnValidate()
        {
            ResolveReferences();
            ValidateSettings();
        }

        [ContextMenu("Reset Rhythm Score")]
        public void ResetScore()
        {
            GrowthNormalized = 0f;
            LastAccuracy = 0f;
            LastMultiplier = 1f;
            LastTimingOffsetSeconds = 0f;
            LastTimingLabel = "--";
            LastTimingPointLabel = "--";
            LastTimingFeedback = default;
            LastRepTimingResult = default;
            ScoredTimingPointCount = 0;
            _accumulatedAccuracy = 0f;
            _timingFeedbackAccumulators.Clear();
            _repTimingFeedbackAccumulators.Clear();
            _publishedRepTimingResults.Clear();
            System.Array.Clear(
                _channelGrowth,
                0,
                _channelGrowth.Length);
            ScoreChanged?.Invoke();
        }

        private void HandleTimingPointReached(
            BodyRepKind kind,
            BodySide side,
            RepTimingPoint timingPoint,
            int count)
        {
            if (_gameFlow == null ||
                _gameFlow.CurrentPhase != ExperiencePhase.Training ||
                _rhythmClock == null ||
                !_rhythmClock.IsRunning)
            {
                return;
            }

            var eventDspTime = AudioSettings.dspTime;
            if (_rhythmChart != null)
            {
                if (!_rhythmChart.TryMatchTimingPoint(
                        kind,
                        side,
                        timingPoint,
                        eventDspTime,
                        _maximumWindowSeconds,
                        out var match))
                {
                    return;
                }

                ApplyChartTimingPoint(
                    match,
                    (float)Math.Abs(
                        match.TimingOffsetSeconds),
                    true);
                return;
            }

            ApplyTimingPoint(
                kind,
                side,
                timingPoint,
                count,
                (float)_rhythmClock
                    .GetDistanceToNearestBeatSeconds(
                        eventDspTime),
                true);
        }

        private void ApplyTimingPoint(
            BodyRepKind kind,
            BodySide side,
            RepTimingPoint timingPoint,
            int repetitionNumber,
            float absoluteOffsetSeconds,
            bool wasHit)
        {
            LastTimingOffsetSeconds = wasHit
                ? Mathf.Max(0f, absoluteOffsetSeconds)
                : _maximumWindowSeconds;
            LastAccuracy = wasHit
                ? TrainingRhythmMath
                    .CalculateNormalizedAccuracy(
                        LastTimingOffsetSeconds,
                        _perfectWindowSeconds,
                        _maximumWindowSeconds)
                : 0f;
            LastMultiplier =
                wasHit
                    ? TrainingRhythmMath.CalculateMultiplier(
                        LastAccuracy,
                        _minimumMultiplier,
                        _perfectMultiplier)
                    : 0f;
            LastTimingLabel =
                TrainingRhythmMath.GetTimingLabel(LastAccuracy);
            LastTimingPointLabel =
                timingPoint == RepTimingPoint.Up ? "UP" : "DOWN";
            _accumulatedAccuracy += LastAccuracy;

            if (wasHit)
            {
                var pointGrowth =
                    TrainingRhythmMath.CalculateTimingPointGrowth(
                        GetGrowthPerSideRep(),
                        LastMultiplier);
                GrowthNormalized = Mathf.Clamp01(
                    GrowthNormalized + pointGrowth);

                // Credit the muscle group this exercise trains. Normalized by
                // that group's own rep count, so it reaches full growth when
                // its blocks are done rather than when the whole chart is.
                var channel = MuscleGrowthChannels.FromRep(kind, side);
                var channelIndex = (int)channel;
                if (channelIndex >= 0 &&
                    channelIndex < _channelGrowth.Length)
                {
                    _channelGrowth[channelIndex] = Mathf.Clamp01(
                        _channelGrowth[channelIndex] +
                        TrainingRhythmMath.CalculateTimingPointGrowth(
                            GetGrowthPerChannelRep(channel),
                            LastMultiplier));
                }
            }

            ScoredTimingPointCount++;
            ScoreChanged?.Invoke();

            if (_logScoreDetails)
            {
                Debug.Log(
                    $"[RhythmScore] {kind} {side} " +
                    $"note {repetitionNumber} " +
                    $"{LastTimingPointLabel}: " +
                    $"{LastTimingLabel}, offset={LastTimingOffsetSeconds * 1000f:F0} ms, " +
                    $"accuracy={LastAccuracy:P0}, multiplier=x{LastMultiplier:F2}, " +
                    $"growth={GrowthNormalized:P0}.",
                    this);
            }
        }

        private void ApplyChartTimingPoint(
            TrainingRhythmTimingMatch match,
            float absoluteOffsetSeconds,
            bool wasHit)
        {
            ApplyTimingPoint(
                match.Kind,
                match.Side,
                match.TimingPoint,
                match.RepetitionNumber,
                absoluteOffsetSeconds,
                wasHit);
            RecordChartTimingFeedback(
                match,
                LastAccuracy,
                wasHit);
        }

        private void RecordChartTimingFeedback(
            TrainingRhythmTimingMatch match,
            float accuracy,
            bool wasHit)
        {
            if (_rhythmChart == null ||
                match.NoteIndex < 0 ||
                match.NoteIndex >=
                _rhythmChart.ScheduledNotes.Count)
            {
                return;
            }

            var note =
                _rhythmChart.ScheduledNotes[match.NoteIndex];
            var requiredSides =
                note.RequiredSides & TrainingSideMask.Both;
            var resolvedSide =
                match.Side == BodySide.Left
                    ? TrainingSideMask.Left
                    : TrainingSideMask.Right;
            if ((requiredSides & resolvedSide) == 0)
            {
                return;
            }

            var timingIndex =
                match.TimingPoint == RepTimingPoint.Up
                    ? 0
                    : 1;
            var key = match.NoteIndex * 2 + timingIndex;
            if (!_timingFeedbackAccumulators.TryGetValue(
                    key,
                    out var accumulator))
            {
                accumulator =
                    new TimingFeedbackAccumulator();
                _timingFeedbackAccumulators.Add(
                    key,
                    accumulator);
            }

            if ((accumulator.ResolvedSides & resolvedSide) != 0)
            {
                return;
            }

            accumulator.ResolvedSides |= resolvedSide;
            accumulator.MinimumAccuracy = Mathf.Min(
                accumulator.MinimumAccuracy,
                Mathf.Clamp01(accuracy));
            accumulator.AllRequiredSidesHit &= wasHit;
            if ((accumulator.ResolvedSides & requiredSides) !=
                requiredSides)
            {
                return;
            }

            var combinedAccuracy =
                accumulator.AllRequiredSidesHit
                    ? accumulator.MinimumAccuracy
                    : 0f;
            LastTimingFeedback =
                new TrainingTimingFeedback(
                    match.NoteIndex,
                    match.RepetitionNumber,
                    match.Kind,
                    match.TimingPoint,
                    combinedAccuracy,
                    accumulator.AllRequiredSidesHit);
            _timingFeedbackAccumulators.Remove(key);
            TimingFeedbackReady?.Invoke(
                LastTimingFeedback);
            RecordRepTimingFeedback(
                note,
                LastTimingFeedback);
        }

        private void RecordRepTimingFeedback(
            TrainingRhythmNote note,
            TrainingTimingFeedback feedback)
        {
            var clockSessionId = _rhythmClock == null
                ? -1
                : _rhythmClock.ClockSessionId;
            var key = MakeRepResultKey(
                clockSessionId,
                feedback.NoteIndex);
            if (_publishedRepTimingResults.Contains(key))
            {
                return;
            }

            if (!_repTimingFeedbackAccumulators.TryGetValue(
                    key,
                    out var accumulator))
            {
                accumulator = new RepTimingFeedbackAccumulator();
                _repTimingFeedbackAccumulators.Add(
                    key,
                    accumulator);
            }

            if (feedback.TimingPoint == RepTimingPoint.Up)
            {
                if (accumulator.HasUp)
                {
                    return;
                }

                accumulator.Up = feedback;
                accumulator.HasUp = true;
            }
            else
            {
                if (accumulator.HasDown)
                {
                    return;
                }

                accumulator.Down = feedback;
                accumulator.HasDown = true;
            }

            if (!accumulator.HasUp || !accumulator.HasDown)
            {
                return;
            }

            var finalizationReason =
                accumulator.Down.AllRequiredSidesHit
                    ? TrainingRepFinalizationReason.Completed
                    : TrainingRepFinalizationReason.TimedOut;
            LastRepTimingResult =
                new TrainingRepTimingResult(
                    clockSessionId,
                    feedback.NoteIndex,
                    feedback.RepetitionNumber,
                    feedback.Kind,
                    note.RequiredSides,
                    accumulator.Up,
                    accumulator.Down,
                    finalizationReason);
            _repTimingFeedbackAccumulators.Remove(key);
            _publishedRepTimingResults.Add(key);
            RepTimingResultReady?.Invoke(
                LastRepTimingResult);
        }

        private static long MakeRepResultKey(
            int clockSessionId,
            int noteIndex)
        {
            return ((long)clockSessionId << 32) |
                   (uint)noteIndex;
        }

        private void HandlePhaseChanged(ExperiencePhase phase)
        {
            if (phase == ExperiencePhase.Setup ||
                phase == ExperiencePhase.Training)
            {
                ResetScore();
            }
        }

        private void Subscribe()
        {
            if (_repCounter != null)
            {
                _repCounter.TimingPointReached -=
                    HandleTimingPointReached;
                _repCounter.TimingPointReached +=
                    HandleTimingPointReached;
            }

            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= HandlePhaseChanged;
                _gameFlow.PhaseChanged += HandlePhaseChanged;
            }
        }

        private void Unsubscribe()
        {
            if (_repCounter != null)
            {
                _repCounter.TimingPointReached -=
                    HandleTimingPointReached;
            }

            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= HandlePhaseChanged;
            }
        }

        private void ResolveReferences()
        {
            _rhythmClock ??= FindFirstObjectByType<RhythmClock>();
            _repCounter ??= FindFirstObjectByType<BodyRepCounter>();
            _gameFlow ??= FindFirstObjectByType<GameFlowController>();
            _rhythmChart ??=
                FindFirstObjectByType<TrainingRhythmChart>();
        }

        private void ValidateSettings()
        {
            _perfectWindowSeconds = Mathf.Max(0f, _perfectWindowSeconds);
            _maximumWindowSeconds = Mathf.Max(
                _perfectWindowSeconds + 0.01f,
                _maximumWindowSeconds);
            _growthPerRep = Mathf.Max(0f, _growthPerRep);
            _targetChartGrowth =
                Mathf.Clamp01(_targetChartGrowth);
            _minimumMultiplier = Mathf.Clamp01(_minimumMultiplier);
            _perfectMultiplier = Mathf.Max(
                1f,
                _minimumMultiplier,
                _perfectMultiplier);
        }

        /// <summary>
        /// Growth per rep for one muscle group, normalized so the group is
        /// fully grown once every rep that trains it has been scored.
        /// </summary>
        private float GetGrowthPerChannelRep(
            MuscleGrowthChannel channel)
        {
            var pattern = _rhythmChart == null
                ? null
                : _rhythmChart.Pattern;
            if (pattern == null)
            {
                return _growthPerRep;
            }

            var channelRepetitions =
                pattern.GetRequiredSideRepetitions(channel);
            return channelRepetitions > 0
                ? _targetChartGrowth / channelRepetitions
                : 0f;
        }

        private float GetGrowthPerSideRep()
        {
            var requiredSideRepetitions =
                _rhythmChart == null
                    ? 0
                    : _rhythmChart
                        .TotalRequiredSideRepetitions;
            return requiredSideRepetitions > 0
                ? _targetChartGrowth /
                  requiredSideRepetitions
                : _growthPerRep;
        }
    }
}
