using NUnit.Framework;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class StageEntranceWalkSlideTests
    {
        private const float WalkSeconds = 5.2f;
        private const float StillSeconds = 0.9f;

        [Test]
        public void ThePathIsFinishedBeforeTheFeetPlant()
        {
            // The stop clip covers its ground in the first 2.1 s of three
            // and holds still for the last 0.9. If the path is still
            // running then, the avatar slides to its mark with its feet
            // planted, which is what it did.
            var atPlant = StageEntranceCinematicMath.EvaluateWalkProgress(
                WalkSeconds - StillSeconds,
                WalkSeconds,
                StillSeconds);

            Assert.That(
                atPlant,
                Is.EqualTo(1f).Within(0.0001f),
                "Arrived by the time the animation stops walking.");
        }

        [Test]
        public void NothingMovesDuringTheStill()
        {
            var previous = StageEntranceCinematicMath
                .EvaluateWalkProgress(
                    WalkSeconds - StillSeconds,
                    WalkSeconds,
                    StillSeconds);

            for (var t = WalkSeconds - StillSeconds;
                 t <= WalkSeconds + 1f;
                 t += 0.05f)
            {
                var progress = StageEntranceCinematicMath
                    .EvaluateWalkProgress(t, WalkSeconds, StillSeconds);
                Assert.That(
                    progress,
                    Is.EqualTo(previous).Within(0.0001f),
                    "The path must not creep while the feet are planted.");
                previous = progress;
            }
        }

        [Test]
        public void TheWalkStillStartsWhereItDid()
        {
            Assert.That(
                StageEntranceCinematicMath.EvaluateWalkProgress(
                    0f, WalkSeconds, StillSeconds),
                Is.EqualTo(0f));
        }

        [Test]
        public void WithoutAStopClipTheOldTimingIsUnchanged()
        {
            for (var t = 0f; t <= WalkSeconds; t += 0.25f)
            {
                Assert.That(
                    StageEntranceCinematicMath.EvaluateWalkProgress(
                        t, WalkSeconds, 0f),
                    Is.EqualTo(
                        StageEntranceCinematicMath.EvaluateWalkProgress(
                            t, WalkSeconds)).Within(0.0001f));
            }
        }

        [Test]
        public void AStillLongerThanTheWalkDoesNotDivideByZero()
        {
            Assert.That(
                StageEntranceCinematicMath.EvaluateWalkProgress(
                    1f, 3f, 9f),
                Is.EqualTo(1f));
        }
    }
}
