using System.Collections.Generic;
using NUnit.Framework;

namespace IVRC.Tests
{
    public sealed class PosingRoutineTests
    {
        private static List<PosingRoutineEntry> Entries(
            params (float score, bool cleared)[] items)
        {
            var list = new List<PosingRoutineEntry>(items.Length);
            foreach (var item in items)
            {
                list.Add(
                    new PosingRoutineEntry(
                        item.score,
                        item.cleared));
            }

            return list;
        }

        [Test]
        public void EmptyRoutineScoresZero()
        {
            Assert.That(
                PosingRoutineCalculator.AverageScore(null),
                Is.Zero);
            Assert.That(
                PosingRoutineCalculator.AverageScore(
                    Entries()),
                Is.Zero);
        }

        [Test]
        public void EveryPoseCountsEqually()
        {
            Assert.That(
                PosingRoutineCalculator.AverageScore(
                    Entries(
                        (90f, true),
                        (60f, true),
                        (30f, false))),
                Is.EqualTo(60f).Within(0.001f));
        }

        [Test]
        public void ScoresAreClampedIntoRange()
        {
            var entries = Entries(
                (150f, true),
                (-40f, false));
            Assert.That(
                entries[0].Score,
                Is.EqualTo(100f).Within(0.001f));
            Assert.That(
                entries[1].Score,
                Is.Zero);
            Assert.That(
                PosingRoutineCalculator.AverageScore(entries),
                Is.EqualTo(50f).Within(0.001f));
        }

        [Test]
        public void BrokenScoreDoesNotPoisonTheAverage()
        {
            var entries = Entries(
                (float.NaN, true),
                (80f, true));
            Assert.That(
                PosingRoutineCalculator.AverageScore(entries),
                Is.EqualTo(40f).Within(0.001f));
        }

        [Test]
        public void ClearedCountIgnoresScore()
        {
            Assert.That(
                PosingRoutineCalculator.CountCleared(
                    Entries(
                        (10f, true),
                        (99f, false),
                        (55f, true))),
                Is.EqualTo(2));
        }

        [Test]
        public void RoutineSucceedsOnlyWhenEveryPoseWasHeld()
        {
            var all = Entries(
                (70f, true),
                (70f, true),
                (70f, true));
            var one = Entries(
                (70f, true),
                (70f, false),
                (70f, true));

            Assert.That(
                PosingRoutineCalculator.AllCleared(all, 3),
                Is.True);
            Assert.That(
                PosingRoutineCalculator.AllCleared(one, 3),
                Is.False);
        }

        [Test]
        public void AnUnfinishedRoutineIsNotASuccess()
        {
            // Two poses held but the third never attempted must not read as
            // a cleared routine.
            Assert.That(
                PosingRoutineCalculator.AllCleared(
                    Entries((80f, true), (80f, true)),
                    3),
                Is.False);
            Assert.That(
                PosingRoutineCalculator.AllCleared(
                    Entries(),
                    0),
                Is.False);
        }

        [Test]
        public void PhaseLengthCoversEveryCountdownAndWindow()
        {
            // Three poses, each a 3 s countdown plus a 10 s window.
            Assert.That(
                PosingRoutineCalculator.TotalSeconds(
                    new List<float> { 3f, 3f, 3f },
                    10f),
                Is.EqualTo(39f).Within(0.001f));
        }

        [Test]
        public void PhaseLengthIsZeroWithoutPoses()
        {
            Assert.That(
                PosingRoutineCalculator.TotalSeconds(
                    null,
                    10f),
                Is.Zero);
            Assert.That(
                PosingRoutineCalculator.TotalSeconds(
                    new List<float>(),
                    10f),
                Is.Zero);
        }

        [Test]
        public void PhaseLengthSanitizesBrokenValues()
        {
            Assert.That(
                PosingRoutineCalculator.TotalSeconds(
                    new List<float> { float.NaN, -5f },
                    10f),
                Is.EqualTo(20f).Within(0.001f));
            Assert.That(
                PosingRoutineCalculator.TotalSeconds(
                    new List<float> { 3f },
                    float.NaN),
                Is.EqualTo(3f).Within(0.001f));
        }

        [Test]
        public void RoutineAdvancesThenCompletes()
        {
            Assert.That(
                PosingRoutineCalculator.NextIndex(0, 3),
                Is.EqualTo(1));
            Assert.That(
                PosingRoutineCalculator.IsRoutineComplete(1, 3),
                Is.False);
            Assert.That(
                PosingRoutineCalculator.NextIndex(2, 3),
                Is.EqualTo(3));
            Assert.That(
                PosingRoutineCalculator.IsRoutineComplete(3, 3),
                Is.True);
        }

        [Test]
        public void IndexCannotRunPastTheEnd()
        {
            Assert.That(
                PosingRoutineCalculator.NextIndex(9, 3),
                Is.EqualTo(3));
            Assert.That(
                PosingRoutineCalculator.IsRoutineComplete(0, 0),
                Is.True,
                "A routine with no poses is already over.");
        }

        [Test]
        public void PoseCompletionResultKeepsTheResolvedPoseSnapshot()
        {
            var result = new PoseCompletionResult(
                1,
                3,
                "  SINGLE BICEPS  ",
                84.6f,
                true,
                2);

            Assert.That(result.PoseIndex, Is.EqualTo(1));
            Assert.That(result.PoseNumber, Is.EqualTo(2));
            Assert.That(result.PoseCount, Is.EqualTo(3));
            Assert.That(result.PoseDisplayName,
                Is.EqualTo("SINGLE BICEPS"));
            Assert.That(result.Score,
                Is.EqualTo(84.6f).Within(0.001f));
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.ClearedPoseCount, Is.EqualTo(2));
            Assert.That(result.IsFinalPose, Is.False);
        }

        [Test]
        public void PoseCompletionResultSanitizesBrokenInputs()
        {
            var result = new PoseCompletionResult(
                99,
                3,
                " ",
                float.NaN,
                false,
                99);

            Assert.That(result.PoseIndex, Is.EqualTo(2));
            Assert.That(result.PoseNumber, Is.EqualTo(3));
            Assert.That(result.PoseDisplayName, Is.EqualTo("POSE"));
            Assert.That(result.Score, Is.Zero);
            Assert.That(result.ClearedPoseCount, Is.EqualTo(3));
            Assert.That(result.IsFinalPose, Is.True);
        }
    }
}
