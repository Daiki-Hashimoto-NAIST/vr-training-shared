using NUnit.Framework;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class StageEntranceWalkBlendTests
    {
        private const float WalkSeconds = 5.2f;
        private const float WalkClip = 1.03f;
        private const float StopClip = 3.0f;
        private const float Blend = 0.25f;

        private static StageEntranceWalkBlend At(float elapsed)
        {
            return StageEntranceCinematicMath.EvaluateWalkBlend(
                elapsed,
                WalkSeconds,
                1f,
                WalkClip,
                StopClip,
                Blend);
        }

        [Test]
        public void TheLoopCarriesTheFirstPartOfTheWalk()
        {
            // 5.2 s of path, 3 s of stop, so the loop owns the first 2.2 s.
            Assert.That(At(0f).StopWeight, Is.EqualTo(0f));
            Assert.That(At(1f).StopWeight, Is.EqualTo(0f));
            Assert.That(At(2.19f).StopWeight, Is.EqualTo(0f));
        }

        [Test]
        public void TheStopTakesOverByCrossFadeRatherThanByCutting()
        {
            var handover = At(2.2f);
            Assert.That(handover.StopWeight, Is.EqualTo(0f).Within(0.001f));

            var midBlend = At(2.2f + Blend * 0.5f);
            Assert.That(
                midBlend.StopWeight,
                Is.EqualTo(0.5f).Within(0.01f),
                "Half in, half out: the legs carry through.");

            Assert.That(
                At(2.2f + Blend).StopWeight,
                Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void TheStopFinishesExactlyWhenThePathDoes()
        {
            Assert.That(
                At(WalkSeconds).StopTime,
                Is.EqualTo(StopClip).Within(0.001f),
                "The last frame of the stop lands on the last step.");
        }

        [Test]
        public void AfterTheWalkTheAvatarStaysStanding()
        {
            // The old behaviour rewound the loop to frame zero here, which
            // is a person mid-stride standing still at the arrival point.
            foreach (var t in new[] { 5.2f, 6f, 20f })
            {
                var after = At(t);
                Assert.That(after.StopWeight, Is.EqualTo(1f).Within(0.001f));
                Assert.That(
                    after.StopTime,
                    Is.EqualTo(StopClip).Within(0.001f));
            }
        }

        [Test]
        public void WithNoStopClipNothingChanges()
        {
            var blend = StageEntranceCinematicMath.EvaluateWalkBlend(
                3f, WalkSeconds, 1f, WalkClip, 0f, Blend);

            Assert.That(blend.StopWeight, Is.EqualTo(0f));
            Assert.That(
                blend.WalkTime,
                Is.EqualTo(
                    StageEntranceCinematicMath.EvaluateWalkAnimationTime(
                        3f, WalkSeconds, 1f, WalkClip)));
        }

        [Test]
        public void AStopLongerThanTheWalkIsTrimmedRatherThanStartingEarly()
        {
            var blend = StageEntranceCinematicMath.EvaluateWalkBlend(
                0f, 2f, 1f, WalkClip, 9f, Blend);

            Assert.That(
                blend.StopWeight,
                Is.EqualTo(0f).Within(0.001f),
                "At time zero the stop has only just begun, not finished.");
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void ABrokenClockWalksRatherThanStops(float elapsed)
        {
            Assert.That(
                StageEntranceCinematicMath.EvaluateWalkBlend(
                    elapsed, WalkSeconds, 1f, WalkClip, StopClip, Blend)
                    .StopWeight,
                Is.EqualTo(0f));
        }
    }
}
