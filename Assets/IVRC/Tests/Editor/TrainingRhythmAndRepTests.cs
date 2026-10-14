using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class TrainingRhythmAndRepTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance |
            BindingFlags.NonPublic;

        [TestCase(1f, true, 1f, true, true, true)]
        [TestCase(0f, false, 1f, true, true, false)]
        [TestCase(1f, true, 0f, false, false, false)]
        [TestCase(0.34f, true, 1f, true, true, false)]
        [TestCase(0.35f, true, 0.35f, true, true, true)]
        public void RepResultSeparatesMotionFromRhythmClear(
            float upAccuracy,
            bool upHit,
            float downAccuracy,
            bool downHit,
            bool expectedMotion,
            bool expectedRhythm)
        {
            var up = new TrainingTimingFeedback(
                0,
                1,
                BodyRepKind.ElbowFlexion,
                RepTimingPoint.Up,
                upAccuracy,
                upHit);
            var down = new TrainingTimingFeedback(
                0,
                1,
                BodyRepKind.ElbowFlexion,
                RepTimingPoint.Down,
                downAccuracy,
                downHit);
            var result = new TrainingRepTimingResult(
                7,
                0,
                1,
                BodyRepKind.ElbowFlexion,
                TrainingSideMask.Both,
                up,
                down,
                downHit
                    ? TrainingRepFinalizationReason.Completed
                    : TrainingRepFinalizationReason.TimedOut);

            Assert.That(
                TrainingSuccessFeedbackRules
                    .IsMotionCompleted(result),
                Is.EqualTo(expectedMotion));
            Assert.That(
                TrainingSuccessFeedbackRules
                    .IsRhythmClear(result),
                Is.EqualTo(expectedRhythm));
        }

        [Test]
        public void RepResultPublishesOnceAfterUpAndDownAndResetsCleanly()
        {
            var host = new GameObject(
                "TrainingRhythmAndRepTests_RepResult");
            host.SetActive(false);

            try
            {
                var scorer =
                    host.AddComponent<TrainingRhythmScorer>();
                var results =
                    new List<TrainingRepTimingResult>();
                scorer.RepTimingResultReady += results.Add;
                var record =
                    typeof(TrainingRhythmScorer).GetMethod(
                        "RecordRepTimingFeedback",
                        PrivateInstance);
                Assert.That(record, Is.Not.Null);
                var note = new TrainingRhythmNote(
                    0,
                    0,
                    0,
                    BodyRepKind.ElbowFlexion,
                    TrainingSideMask.Both,
                    4,
                    6);
                var up = new TrainingTimingFeedback(
                    0,
                    1,
                    BodyRepKind.ElbowFlexion,
                    RepTimingPoint.Up,
                    0.8f,
                    true);
                var down = new TrainingTimingFeedback(
                    0,
                    1,
                    BodyRepKind.ElbowFlexion,
                    RepTimingPoint.Down,
                    0.7f,
                    true);

                record.Invoke(scorer, new object[] { note, up });
                record.Invoke(scorer, new object[] { note, up });
                Assert.That(results, Is.Empty);

                record.Invoke(scorer, new object[] { note, down });
                record.Invoke(scorer, new object[] { note, down });
                Assert.That(results, Has.Count.EqualTo(1));
                Assert.That(results[0].MotionCompleted, Is.True);
                Assert.That(results[0].RhythmCleared, Is.True);

                scorer.ResetScore();
                record.Invoke(scorer, new object[] { note, down });
                Assert.That(
                    results,
                    Has.Count.EqualTo(1),
                    "A pending UP from the previous session must not leak " +
                    "through ResetScore.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void RepCycleRequiresHeldLowHighLowSequence()
        {
            var detector = new RepCycleDetector();

            Assert.That(
                Update(detector, 20f, 0.04f, 0.04f),
                Is.EqualTo(RepCycleUpdateResult.None));
            Assert.That(
                detector.Phase,
                Is.EqualTo(RepPhase.WaitingForStart));
            Assert.That(
                Update(detector, 20f, 0.04f, 0.08f),
                Is.EqualTo(RepCycleUpdateResult.None));
            Assert.That(
                detector.Phase,
                Is.EqualTo(RepPhase.Ready));

            Assert.That(
                Update(detector, 110f, 0.04f, 0.12f),
                Is.EqualTo(RepCycleUpdateResult.None));
            Assert.That(
                Update(detector, 110f, 0.04f, 0.16f),
                Is.EqualTo(RepCycleUpdateResult.UpReached));
            Assert.That(
                detector.Count,
                Is.Zero);

            Assert.That(
                Update(detector, 20f, 0.04f, 0.20f),
                Is.EqualTo(RepCycleUpdateResult.None));
            Assert.That(
                Update(detector, 20f, 0.04f, 0.24f),
                Is.EqualTo(RepCycleUpdateResult.DownReached));
            Assert.That(detector.Count, Is.EqualTo(1));
            Assert.That(
                detector.Phase,
                Is.EqualTo(RepPhase.Ready));
        }

        [Test]
        public void ThresholdNoiseResetsHoldAndSuspendPreservesCount()
        {
            var detector = new RepCycleDetector();
            Update(detector, 20f, 0.04f, 0.04f);
            Update(detector, 20f, 0.04f, 0.08f);

            Update(detector, 110f, 0.04f, 0.12f);
            Update(detector, 60f, 0.04f, 0.16f);
            Assert.That(
                Update(detector, 110f, 0.04f, 0.20f),
                Is.EqualTo(RepCycleUpdateResult.None));
            Assert.That(
                Update(detector, 110f, 0.04f, 0.24f),
                Is.EqualTo(RepCycleUpdateResult.UpReached));

            Update(detector, 20f, 0.04f, 0.28f);
            Assert.That(
                Update(detector, 20f, 0.04f, 0.32f),
                Is.EqualTo(RepCycleUpdateResult.DownReached));
            Assert.That(detector.Count, Is.EqualTo(1));

            detector.Suspend();
            Assert.That(
                detector.Phase,
                Is.EqualTo(RepPhase.WaitingForStart));
            Assert.That(detector.Count, Is.EqualTo(1));

            detector.ResetCount();
            Assert.That(detector.Count, Is.Zero);
        }

        [Test]
        public void RhythmClockAccuracyUsesNearestBeat()
        {
            var host = new GameObject(
                "TrainingRhythmAndRepTests_Clock");
            host.SetActive(false);
            try
            {
                var clock = host.AddComponent<RhythmClock>();
                clock.SetTempo(120f);
                SetField(clock, "_startDelaySeconds", 0f);
                clock.StartClock();
                var origin = (double)GetField(
                    clock,
                    "_clockOriginDspTime");

                Assert.That(
                    clock.GetDistanceToNearestBeatSeconds(origin),
                    Is.EqualTo(0d).Within(0.000001d));
                Assert.That(
                    clock.GetNormalizedTimingAccuracy(
                        origin,
                        0.05f,
                        0.25f),
                    Is.EqualTo(1f).Within(0.0001f));
                Assert.That(
                    clock.GetNormalizedTimingAccuracy(
                        origin + 0.25d,
                        0.05f,
                        0.25f),
                    Is.EqualTo(0f).Within(0.0001f));
                Assert.That(
                    clock.GetNormalizedTimingAccuracy(
                        origin + 0.5d,
                        0.05f,
                        0.25f),
                    Is.EqualTo(1f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void UpAndDownTogetherAwardOneConfiguredRepGrowth()
        {
            const float growthPerRep = 0.04f;
            var perfectMultiplier =
                TrainingRhythmMath.CalculateMultiplier(
                    1f,
                    0.25f,
                    1f);

            var upGrowth =
                TrainingRhythmMath.CalculateTimingPointGrowth(
                    growthPerRep,
                    perfectMultiplier);
            var downGrowth =
                TrainingRhythmMath.CalculateTimingPointGrowth(
                    growthPerRep,
                    perfectMultiplier);

            Assert.That(
                upGrowth,
                Is.EqualTo(growthPerRep * 0.5f)
                    .Within(0.000001f));
            Assert.That(
                upGrowth + downGrowth,
                Is.EqualTo(growthPerRep)
                    .Within(0.000001f));
            Assert.That(
                TrainingRhythmMath.GetTimingLabel(1f),
                Is.EqualTo("PERFECT"));
            Assert.That(
                TrainingRhythmMath.GetTimingLabel(0f),
                Is.EqualTo("MISS"));
        }

        [Test]
        public void UnplayedChartPointCountsAsMissWithoutGrowth()
        {
            var host = new GameObject(
                "TrainingRhythmAndRepTests_Miss");
            host.SetActive(false);
            try
            {
                var scorer =
                    host.AddComponent<TrainingRhythmScorer>();
                SetField(scorer, "_growthPerRep", 0.04f);
                scorer.ResetScore();
                var method = typeof(TrainingRhythmScorer)
                    .GetMethod(
                        "ApplyTimingPoint",
                        PrivateInstance);
                Assert.That(method, Is.Not.Null);

                method.Invoke(
                    scorer,
                    new object[]
                    {
                        BodyRepKind.ElbowFlexion,
                        BodySide.Left,
                        RepTimingPoint.Up,
                        1,
                        0f,
                        false
                    });

                Assert.That(
                    scorer.ScoredTimingPointCount,
                    Is.EqualTo(1));
                Assert.That(
                    scorer.AverageAccuracy,
                    Is.Zero);
                Assert.That(
                    scorer.GrowthNormalized,
                    Is.Zero);
                Assert.That(
                    scorer.LastMultiplier,
                    Is.Zero);
                Assert.That(
                    scorer.LastTimingLabel,
                    Is.EqualTo("MISS"));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void PerfectChartGrowthNormalizesToRequiredSides()
        {
            var host = new GameObject(
                "TrainingRhythmAndRepTests_GrowthNormalization");
            host.SetActive(false);
            var pattern =
                ScriptableObject.CreateInstance<
                    TrainingRhythmPattern>();
            try
            {
                var chart =
                    host.AddComponent<TrainingRhythmChart>();
                var scorer =
                    host.AddComponent<TrainingRhythmScorer>();
                SetField(chart, "_pattern", pattern);
                SetField(scorer, "_rhythmChart", chart);
                SetField(scorer, "_targetChartGrowth", 1f);
                var method = typeof(TrainingRhythmScorer)
                    .GetMethod(
                        "GetGrowthPerSideRep",
                        PrivateInstance);
                Assert.That(method, Is.Not.Null);

                var growthPerSideRep =
                    (float)method.Invoke(scorer, null);

                Assert.That(
                    pattern.TotalRequiredSideRepetitions,
                    Is.EqualTo(64));
                Assert.That(
                    growthPerSideRep,
                    Is.EqualTo(1f / 64f)
                        .Within(0.000001f));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(pattern);
            }
        }

        [Test]
        public void BilateralFeedbackPublishesOnceUsingWeakerSide()
        {
            var host = new GameObject(
                "TrainingRhythmAndRepTests_Feedback");
            host.SetActive(false);
            var pattern =
                ScriptableObject.CreateInstance<
                    TrainingRhythmPattern>();

            try
            {
                var clock = host.AddComponent<RhythmClock>();
                var counter =
                    host.AddComponent<BodyRepCounter>();
                var flow =
                    host.AddComponent<GameFlowController>();
                var chart =
                    host.AddComponent<TrainingRhythmChart>();
                var scorer =
                    host.AddComponent<TrainingRhythmScorer>();
                SetField(clock, "_startDelaySeconds", 0f);
                SetField(chart, "_pattern", pattern);
                SetField(chart, "_rhythmClock", clock);
                SetField(chart, "_gameFlow", flow);
                SetField(chart, "_repCounter", counter);
                SetField(scorer, "_rhythmChart", chart);
                SetProperty(
                    flow,
                    nameof(GameFlowController.CurrentPhase),
                    ExperiencePhase.Training);
                clock.StartClock();
                InvokePrivate(chart, "Update");
                scorer.ResetScore();

                var feedbacks =
                    new List<TrainingTimingFeedback>();
                var repResults =
                    new List<TrainingRepTimingResult>();
                scorer.TimingFeedbackReady +=
                    feedbacks.Add;
                scorer.RepTimingResultReady +=
                    repResults.Add;
                var applyChartPoint =
                    typeof(TrainingRhythmScorer).GetMethod(
                        "ApplyChartTimingPoint",
                        PrivateInstance);
                Assert.That(
                    applyChartPoint,
                    Is.Not.Null);
                var note = chart.ScheduledNotes[0];
                var target = chart.GetTargetDspTime(
                    note,
                    RepTimingPoint.Up);

                applyChartPoint.Invoke(
                    scorer,
                    new object[]
                    {
                        new TrainingRhythmTimingMatch(
                            note.Index,
                            1,
                            note.Kind,
                            BodySide.Left,
                            RepTimingPoint.Up,
                            target,
                            target + 0.2d,
                            true),
                        0.2f,
                        true
                    });
                Assert.That(feedbacks, Is.Empty);

                applyChartPoint.Invoke(
                    scorer,
                    new object[]
                    {
                        new TrainingRhythmTimingMatch(
                            note.Index,
                            1,
                            note.Kind,
                            BodySide.Right,
                            RepTimingPoint.Up,
                            target,
                            target + 0.02d,
                            true),
                        0.02f,
                        true
                    });
                Assert.That(feedbacks, Has.Count.EqualTo(1));
                Assert.That(
                    feedbacks[0].TimingLabel,
                    Is.EqualTo("OK"));
                Assert.That(
                    feedbacks[0].TimingPoint,
                    Is.EqualTo(RepTimingPoint.Up));
                Assert.That(repResults, Is.Empty);

                applyChartPoint.Invoke(
                    scorer,
                    new object[]
                    {
                        new TrainingRhythmTimingMatch(
                            note.Index,
                            1,
                            note.Kind,
                            BodySide.Left,
                            RepTimingPoint.Down,
                            target,
                            target,
                            true),
                        0f,
                        true
                    });
                Assert.That(repResults, Is.Empty);
                applyChartPoint.Invoke(
                    scorer,
                    new object[]
                    {
                        new TrainingRhythmTimingMatch(
                            note.Index,
                            1,
                            note.Kind,
                            BodySide.Right,
                            RepTimingPoint.Down,
                            target,
                            target,
                            false),
                        0f,
                        false
                    });

                Assert.That(feedbacks, Has.Count.EqualTo(2));
                Assert.That(
                    feedbacks[1].TimingLabel,
                    Is.EqualTo("MISS"));
                Assert.That(repResults, Has.Count.EqualTo(1));
                Assert.That(
                    repResults[0].MotionCompleted,
                    Is.False);
                Assert.That(
                    repResults[0].RhythmCleared,
                    Is.False);
                Assert.That(
                    repResults[0].FinalizationReason,
                    Is.EqualTo(
                        TrainingRepFinalizationReason.TimedOut));
                Assert.That(
                    scorer.ScoredTimingPointCount,
                    Is.EqualTo(4));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(pattern);
            }
        }

        [Test]
        public void RepCycleEventsReachScorerOnceForUpAndDown()
        {
            const float growthPerRep = 0.04f;
            var host = new GameObject(
                "TrainingRhythmAndRepTests_Integration");
            host.SetActive(false);

            try
            {
                var clock = host.AddComponent<RhythmClock>();
                var counter =
                    host.AddComponent<BodyRepCounter>();
                var flow =
                    host.AddComponent<GameFlowController>();
                var scorer =
                    host.AddComponent<TrainingRhythmScorer>();

                SetField(clock, "_startDelaySeconds", 0f);
                SetField(
                    counter,
                    "_thresholdHoldSeconds",
                    0f);
                SetField(
                    counter,
                    "_minimumRepIntervalSeconds",
                    0f);
                SetField(scorer, "_rhythmClock", clock);
                SetField(scorer, "_repCounter", counter);
                SetField(scorer, "_gameFlow", flow);
                SetField(
                    scorer,
                    "_perfectWindowSeconds",
                    10f);
                SetField(
                    scorer,
                    "_maximumWindowSeconds",
                    11f);
                SetField(
                    scorer,
                    "_growthPerRep",
                    growthPerRep);
                SetField(
                    scorer,
                    "_minimumMultiplier",
                    1f);
                SetField(
                    scorer,
                    "_perfectMultiplier",
                    1f);
                SetProperty(
                    flow,
                    nameof(GameFlowController.CurrentPhase),
                    ExperiencePhase.Training);

                scorer.ResetScore();
                InvokePrivate(scorer, "Subscribe");
                clock.StartClock();

                var detector = new RepCycleDetector();
                var thresholds =
                    new RepAngleThresholds(30f, 100f);
                ProcessChannel(
                    counter,
                    detector,
                    20f,
                    thresholds);
                ProcessChannel(
                    counter,
                    detector,
                    110f,
                    thresholds);
                ProcessChannel(
                    counter,
                    detector,
                    20f,
                    thresholds);

                Assert.That(detector.Count, Is.EqualTo(1));
                Assert.That(
                    scorer.ScoredTimingPointCount,
                    Is.EqualTo(2));
                Assert.That(
                    scorer.GrowthNormalized,
                    Is.EqualTo(growthPerRep)
                        .Within(0.000001f));
                Assert.That(
                    scorer.LastTimingPointLabel,
                    Is.EqualTo("DOWN"));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void DefaultPatternBuildsThirtyTwoOrderedLongNotes()
        {
            var pattern =
                ScriptableObject.CreateInstance<
                    TrainingRhythmPattern>();
            try
            {
                var notes = pattern.BuildSchedule();

                Assert.That(notes, Has.Count.EqualTo(32));
                Assert.That(
                    pattern.TotalRepetitions,
                    Is.EqualTo(32));
                Assert.That(
                    pattern.TotalRequiredSideRepetitions,
                    Is.EqualTo(64));
                Assert.That(pattern.TotalBeats, Is.EqualTo(148));
                Assert.That(notes[0].UpBeat, Is.EqualTo(4));
                Assert.That(notes[0].DownBeat, Is.EqualTo(6));
                Assert.That(
                    notes[0].Kind,
                    Is.EqualTo(BodyRepKind.ElbowFlexion));
                Assert.That(notes[8].UpBeat, Is.EqualTo(40));
                Assert.That(
                    notes[8].Kind,
                    Is.EqualTo(BodyRepKind.ArmRaise));
                Assert.That(notes[31].UpBeat, Is.EqualTo(140));
                Assert.That(notes[31].DownBeat, Is.EqualTo(142));

                for (var index = 0;
                     index < notes.Count;
                     index++)
                {
                    Assert.That(
                        notes[index].Index,
                        Is.EqualTo(index));
                    Assert.That(
                        notes[index].DownBeat,
                        Is.GreaterThan(notes[index].UpBeat));
                    if (index > 0)
                    {
                        Assert.That(
                            notes[index].UpBeat,
                            Is.GreaterThan(
                                notes[index - 1].UpBeat));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(pattern);
            }
        }

        [Test]
        public void ChartMatchesOnlyExpectedSideExerciseAndPoint()
        {
            var host = new GameObject(
                "TrainingRhythmAndRepTests_Chart");
            host.SetActive(false);
            var pattern =
                ScriptableObject.CreateInstance<
                    TrainingRhythmPattern>();

            try
            {
                var clock = host.AddComponent<RhythmClock>();
                var counter =
                    host.AddComponent<BodyRepCounter>();
                var flow =
                    host.AddComponent<GameFlowController>();
                var chart =
                    host.AddComponent<TrainingRhythmChart>();

                SetField(clock, "_startDelaySeconds", 0f);
                SetField(chart, "_pattern", pattern);
                SetField(chart, "_rhythmClock", clock);
                SetField(chart, "_gameFlow", flow);
                SetField(chart, "_repCounter", counter);
                SetProperty(
                    flow,
                    nameof(GameFlowController.CurrentPhase),
                    ExperiencePhase.Training);
                clock.StartClock();
                InvokePrivate(chart, "Update");

                var first = chart.ScheduledNotes[0];
                var upDspTime = chart.GetTargetDspTime(
                    first,
                    RepTimingPoint.Up);
                Assert.That(
                    chart.TryMatchTimingPoint(
                        BodyRepKind.ElbowFlexion,
                        BodySide.Left,
                        RepTimingPoint.Down,
                        upDspTime,
                        0.35f,
                        out _),
                    Is.False,
                    "DOWN cannot consume a note before its " +
                    "UP point is resolved or expired.");
                Assert.That(
                    chart.TryMatchTimingPoint(
                        BodyRepKind.ElbowFlexion,
                        BodySide.Left,
                        RepTimingPoint.Up,
                        upDspTime,
                        0.35f,
                        out var leftUp),
                    Is.True);
                Assert.That(leftUp.NoteIndex, Is.Zero);
                Assert.That(
                    chart.TryMatchTimingPoint(
                        BodyRepKind.ElbowFlexion,
                        BodySide.Left,
                        RepTimingPoint.Up,
                        upDspTime,
                        0.35f,
                        out _),
                    Is.False,
                    "A side cannot score the same chart point twice.");
                Assert.That(
                    chart.TryMatchTimingPoint(
                        BodyRepKind.ArmRaise,
                        BodySide.Right,
                        RepTimingPoint.Up,
                        upDspTime,
                        0.35f,
                        out _),
                    Is.False);
                Assert.That(
                    chart.TryMatchTimingPoint(
                        BodyRepKind.ElbowFlexion,
                        BodySide.Right,
                        RepTimingPoint.Up,
                        upDspTime,
                        0.35f,
                        out _),
                    Is.True);
                Assert.That(
                    chart.GetTimingVisualState(
                        0,
                        RepTimingPoint.Up),
                    Is.EqualTo(
                        TrainingTimingVisualState.Hit));

                var downDspTime = chart.GetTargetDspTime(
                    first,
                    RepTimingPoint.Down);
                Assert.That(
                    chart.TryTakeExpiredTimingPoint(
                        downDspTime + 0.36d,
                        0.35f,
                        out var leftDownMiss),
                    Is.True);
                Assert.That(
                    leftDownMiss.TimingPoint,
                    Is.EqualTo(RepTimingPoint.Down));
                Assert.That(
                    leftDownMiss.Side,
                    Is.EqualTo(BodySide.Left));
                Assert.That(
                    leftDownMiss.WasHit,
                    Is.False);
                Assert.That(
                    chart.TryTakeExpiredTimingPoint(
                        downDspTime + 0.36d,
                        0.35f,
                        out var rightDownMiss),
                    Is.True);
                Assert.That(
                    rightDownMiss.Side,
                    Is.EqualTo(BodySide.Right));
                Assert.That(
                    chart.GetTimingVisualState(
                        0,
                        RepTimingPoint.Down),
                    Is.EqualTo(
                        TrainingTimingVisualState.Miss));
                Assert.That(
                    chart.ResolvedNoteCount,
                    Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(pattern);
            }
        }

        [Test]
        public void ExerciseFilterSuspendsUnselectedRepChannels()
        {
            var host = new GameObject(
                "TrainingRhythmAndRepTests_Filter");
            host.SetActive(false);

            try
            {
                var counter =
                    host.AddComponent<BodyRepCounter>();
                var detector = new RepCycleDetector();
                var thresholds =
                    new RepAngleThresholds(30f, 100f);
                SetField(
                    counter,
                    "_thresholdHoldSeconds",
                    0f);
                SetField(
                    counter,
                    "_minimumRepIntervalSeconds",
                    0f);

                counter.SetActiveTrainingKind(
                    BodyRepKind.ArmRaise);
                ProcessChannel(
                    counter,
                    detector,
                    20f,
                    thresholds);
                ProcessChannel(
                    counter,
                    detector,
                    110f,
                    thresholds);
                ProcessChannel(
                    counter,
                    detector,
                    20f,
                    thresholds);
                Assert.That(detector.Count, Is.Zero);

                counter.ClearActiveTrainingKindFilter();
                ProcessChannel(
                    counter,
                    detector,
                    20f,
                    thresholds);
                ProcessChannel(
                    counter,
                    detector,
                    110f,
                    thresholds);
                ProcessChannel(
                    counter,
                    detector,
                    20f,
                    thresholds);
                Assert.That(detector.Count, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void BlockPreviewDoesNotCountButReleasesBeforeScoredUp()
        {
            var host = new GameObject(
                "TrainingRhythmAndRepTests_PreviewGate");
            host.SetActive(false);
            var pattern =
                ScriptableObject.CreateInstance<
                    TrainingRhythmPattern>();

            try
            {
                var clock = host.AddComponent<RhythmClock>();
                var counter =
                    host.AddComponent<BodyRepCounter>();
                var flow =
                    host.AddComponent<GameFlowController>();
                var chart =
                    host.AddComponent<TrainingRhythmChart>();
                SetField(clock, "_startDelaySeconds", 0f);
                SetField(chart, "_pattern", pattern);
                SetField(chart, "_rhythmClock", clock);
                SetField(chart, "_gameFlow", flow);
                SetField(chart, "_repCounter", counter);
                SetField(
                    chart,
                    "_blockPreviewReleaseLeadBeats",
                    1f);
                SetProperty(
                    flow,
                    nameof(GameFlowController.CurrentPhase),
                    ExperiencePhase.Training);
                SetField(
                    counter,
                    "_thresholdHoldSeconds",
                    0f);
                SetField(
                    counter,
                    "_minimumRepIntervalSeconds",
                    0f);

                clock.StartClock();
                SetClockBeatPosition(clock, 2.5d);
                InvokePrivate(chart, "Update");

                Assert.That(
                    chart.ScheduledNotes[0].UpBeat,
                    Is.EqualTo(4));
                Assert.That(
                    counter.TrainingKindFilterEnabled,
                    Is.True);
                Assert.That(
                    counter.ActiveTrainingKind,
                    Is.Null,
                    "The DOWN -> UP -> DOWN demonstration must stay " +
                    "outside rep counting.");

                var detector = new RepCycleDetector();
                var thresholds =
                    new RepAngleThresholds(30f, 100f);
                ProcessChannel(
                    counter,
                    detector,
                    20f,
                    thresholds);
                ProcessChannel(
                    counter,
                    detector,
                    110f,
                    thresholds);
                ProcessChannel(
                    counter,
                    detector,
                    20f,
                    thresholds);
                Assert.That(detector.Count, Is.Zero);

                SetClockBeatPosition(clock, 3.05d);
                InvokePrivate(chart, "Update");
                Assert.That(
                    counter.ActiveTrainingKind,
                    Is.EqualTo(BodyRepKind.ElbowFlexion),
                    "Detection must resume one beat before the scored UP " +
                    "so the movement into the target is observable.");

                ProcessChannel(
                    counter,
                    detector,
                    20f,
                    thresholds);
                ProcessChannel(
                    counter,
                    detector,
                    110f,
                    thresholds);
                ProcessChannel(
                    counter,
                    detector,
                    20f,
                    thresholds);
                Assert.That(detector.Count, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(pattern);
            }
        }

        [Test]
        public void RhythmClockResumeKeepsTheSameSession()
        {
            var host = new GameObject(
                "TrainingRhythmAndRepTests_Resume");
            host.SetActive(false);

            try
            {
                var clock = host.AddComponent<RhythmClock>();
                SetField(clock, "_startDelaySeconds", 0f);
                clock.StartClock();
                var sessionId = clock.ClockSessionId;
                var originalBeatTime =
                    clock.GetBeatDspTime(8);
                clock.StopClock();
                var pausedDspTime =
                    AudioSettings.dspTime - 1d;
                SetField(
                    clock,
                    "_pausedAtDspTime",
                    pausedDspTime);

                Assert.That(
                    clock.GetBeatPosition(
                        AudioSettings.dspTime + 10d),
                    Is.EqualTo(
                        clock.GetBeatPosition(pausedDspTime))
                        .Within(0.0001d),
                    "Paused visuals must sample the frozen DSP time.");

                clock.ResumeClock();

                Assert.That(clock.IsRunning, Is.True);
                Assert.That(
                    clock.ClockSessionId,
                    Is.EqualTo(sessionId));
                Assert.That(
                    clock.GetBeatDspTime(8),
                    Is.GreaterThan(
                        originalBeatTime + 0.9d));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ChartKeepsResolvedPointsAcrossTrackingPause()
        {
            var host = new GameObject(
                "TrainingRhythmAndRepTests_ChartPause");
            host.SetActive(false);
            var pattern =
                ScriptableObject.CreateInstance<
                    TrainingRhythmPattern>();

            try
            {
                var clock = host.AddComponent<RhythmClock>();
                var counter =
                    host.AddComponent<BodyRepCounter>();
                var flow =
                    host.AddComponent<GameFlowController>();
                var chart =
                    host.AddComponent<TrainingRhythmChart>();
                SetField(clock, "_startDelaySeconds", 0f);
                SetField(chart, "_pattern", pattern);
                SetField(chart, "_rhythmClock", clock);
                SetField(chart, "_gameFlow", flow);
                SetField(chart, "_repCounter", counter);
                SetProperty(
                    flow,
                    nameof(GameFlowController.CurrentPhase),
                    ExperiencePhase.Training);

                clock.StartClock();
                InvokePrivate(chart, "Update");
                var first = chart.ScheduledNotes[0];
                var originalTarget = chart.GetTargetDspTime(
                    first,
                    RepTimingPoint.Up);
                Assert.That(
                    chart.TryMatchTimingPoint(
                        first.Kind,
                        BodySide.Left,
                        RepTimingPoint.Up,
                        originalTarget,
                        0.35f,
                        out _),
                    Is.True);

                clock.StopClock();
                SetField(
                    clock,
                    "_pausedAtDspTime",
                    AudioSettings.dspTime - 1d);
                InvokePrivate(chart, "Update");
                clock.ResumeClock();
                InvokePrivate(chart, "Update");

                Assert.That(
                    chart.GetTargetDspTime(
                        first,
                        RepTimingPoint.Up),
                    Is.GreaterThan(
                        originalTarget + 0.9d));
                Assert.That(
                    chart.GetTimingVisualState(
                        first.Index,
                        RepTimingPoint.Up),
                    Is.EqualTo(
                        TrainingTimingVisualState.Pending),
                    "The other required side is still pending.");
                Assert.That(
                    chart.TryMatchTimingPoint(
                        first.Kind,
                        BodySide.Left,
                        RepTimingPoint.Up,
                        chart.GetTargetDspTime(
                            first,
                            RepTimingPoint.Up),
                        0.35f,
                        out _),
                    Is.False,
                    "A resolved side must stay resolved " +
                    "instead of restarting the chart.");
                Assert.That(
                    chart.ScheduledNotes,
                    Has.Count.EqualTo(32));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(pattern);
            }
        }

        [Test]
        public void ActiveExerciseWaitsForUnresolvedDownAtAnyTempo()
        {
            var host = new GameObject(
                "TrainingRhythmAndRepTests_ExerciseRelease");
            host.SetActive(false);
            var pattern =
                ScriptableObject.CreateInstance<
                    TrainingRhythmPattern>();

            try
            {
                var clock = host.AddComponent<RhythmClock>();
                var counter =
                    host.AddComponent<BodyRepCounter>();
                var flow =
                    host.AddComponent<GameFlowController>();
                var chart =
                    host.AddComponent<TrainingRhythmChart>();
                clock.SetTempo(240f);
                SetField(clock, "_startDelaySeconds", 0f);
                SetField(chart, "_pattern", pattern);
                SetField(chart, "_rhythmClock", clock);
                SetField(chart, "_gameFlow", flow);
                SetField(chart, "_repCounter", counter);
                SetProperty(
                    flow,
                    nameof(GameFlowController.CurrentPhase),
                    ExperiencePhase.Training);

                clock.StartClock();
                InvokePrivate(chart, "Update");
                var sides = new[]
                {
                    BodySide.Left,
                    BodySide.Right
                };
                var points = new[]
                {
                    RepTimingPoint.Up,
                    RepTimingPoint.Down
                };

                for (var noteIndex = 0;
                     noteIndex < 7;
                     noteIndex++)
                {
                    var note = chart.ScheduledNotes[noteIndex];
                    foreach (var point in points)
                    {
                        foreach (var side in sides)
                        {
                            Assert.That(
                                chart.TryMatchTimingPoint(
                                    note.Kind,
                                    side,
                                    point,
                                    chart.GetTargetDspTime(
                                        note,
                                        point),
                                    0.35f,
                                    out _),
                                Is.True);
                        }
                    }
                }

                var unresolved =
                    chart.ScheduledNotes[7];
                var elapsedBeats =
                    unresolved.DownBeat + 0.6d;
                SetField(
                    clock,
                    "_clockOriginDspTime",
                    AudioSettings.dspTime -
                    elapsedBeats *
                    clock.SecondsPerBeat);
                InvokePrivate(chart, "Update");

                Assert.That(
                    counter.ActiveTrainingKind,
                    Is.EqualTo(unresolved.Kind),
                    "A BPM change must not release the current " +
                    "exercise before its DOWN points resolve.");

                foreach (var point in points)
                {
                    foreach (var side in sides)
                    {
                        Assert.That(
                            chart.TryMatchTimingPoint(
                                unresolved.Kind,
                                side,
                                point,
                                chart.GetTargetDspTime(
                                    unresolved,
                                    point),
                                0.35f,
                                out _),
                            Is.True);
                    }
                }

                InvokePrivate(chart, "Update");
                Assert.That(
                    counter.ActiveTrainingKind,
                    Is.Null,
                    "After the previous DOWN resolves, the next block's " +
                    "unscored practice must not count as ArmRaise.");

                var nextBlockNote = chart.ScheduledNotes[8];
                SetClockBeatPosition(
                    clock,
                    nextBlockNote.UpBeat - 0.95d);
                InvokePrivate(chart, "Update");
                Assert.That(
                    counter.ActiveTrainingKind,
                    Is.EqualTo(BodyRepKind.ArmRaise));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(pattern);
            }
        }

        [TestCase(ExperiencePhase.Setup, false)]
        [TestCase(ExperiencePhase.Ready, false)]
        [TestCase(ExperiencePhase.Countdown, false)]
        [TestCase(ExperiencePhase.Tutorial, true)]
        [TestCase(ExperiencePhase.Training, true)]
        [TestCase(ExperiencePhase.Transformation, false)]
        [TestCase(ExperiencePhase.StageTransition, false)]
        [TestCase(ExperiencePhase.StageEntrance, false)]
        [TestCase(ExperiencePhase.Posing, false)]
        [TestCase(ExperiencePhase.Results, false)]
        public void BodyRepsAreOnlyEligibleWhereMovementIsAskedFor(
            ExperiencePhase phase,
            bool expected)
        {
            Assert.That(
                BodyRepCounter.AllowsCounting(phase),
                Is.EqualTo(expected));
        }

        [Test]
        public void SetupCanBecomeReadyWithoutCountingReps()
        {
            var host = new GameObject(
                "TrainingRhythmAndRepTests_Readiness");
            host.SetActive(false);

            try
            {
                var metrics =
                    host.AddComponent<BodyPoseMetrics>();
                var counter =
                    host.AddComponent<BodyRepCounter>();
                var flow =
                    host.AddComponent<GameFlowController>();

                SetProperty(
                    metrics,
                    nameof(BodyPoseMetrics.HasPose),
                    true);
                SetProperty(
                    flow,
                    nameof(GameFlowController.CurrentPhase),
                    ExperiencePhase.Setup);
                SetField(counter, "_metrics", metrics);
                SetField(counter, "_gameFlow", flow);
                SetField(
                    counter,
                    "_resumeDelaySeconds",
                    0f);
                SetField(
                    counter,
                    "_countOnlyDuringTraining",
                    true);

                InvokePrivate(counter, "LateUpdate");

                Assert.That(counter.IsReady, Is.True);
                Assert.That(
                    counter.WaitingReason,
                    Is.Empty);
                Assert.That(
                    counter.LeftElbowCount,
                    Is.Zero);
                Assert.That(
                    counter.LeftElbowPhase,
                    Is.EqualTo(
                        RepPhase.WaitingForStart));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static RepCycleUpdateResult Update(
            RepCycleDetector detector,
            float angle,
            float deltaTime,
            float currentTime)
        {
            return detector.Update(
                angle,
                30f,
                100f,
                deltaTime,
                currentTime,
                0.08f,
                0.35f);
        }

        private static void SetField(
            object target,
            string fieldName,
            object value)
        {
            var field = target.GetType().GetField(
                fieldName,
                PrivateInstance);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private static void SetProperty(
            object target,
            string propertyName,
            object value)
        {
            var property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null);
            property.SetValue(target, value);
        }

        private static void SetClockBeatPosition(
            RhythmClock clock,
            double beatPosition)
        {
            SetField(
                clock,
                "_clockOriginDspTime",
                AudioSettings.dspTime -
                beatPosition * clock.SecondsPerBeat);
        }

        private static void InvokePrivate(
            object target,
            string methodName)
        {
            var method = target.GetType().GetMethod(
                methodName,
                PrivateInstance);
            Assert.That(method, Is.Not.Null);
            method.Invoke(target, null);
        }

        private static void ProcessChannel(
            BodyRepCounter counter,
            RepCycleDetector detector,
            float angle,
            RepAngleThresholds thresholds)
        {
            var method = typeof(BodyRepCounter).GetMethod(
                "ProcessChannel",
                PrivateInstance);
            Assert.That(method, Is.Not.Null);
            method.Invoke(
                counter,
                new object[]
                {
                    detector,
                    angle,
                    thresholds,
                    BodyRepKind.ElbowFlexion,
                    BodySide.Left
                });
        }

        private static object GetField(
            object target,
            string fieldName)
        {
            var field = target.GetType().GetField(
                fieldName,
                PrivateInstance);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(target);
        }
    }
}
