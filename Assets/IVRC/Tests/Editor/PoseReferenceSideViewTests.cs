using NUnit.Framework;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class PoseReferenceSideViewTests
    {
        private static PosingScoreTarget FrontDoubleBiceps =>
            new PosingScoreTarget
            {
                LeftHandPositionFromHeadNormalized =
                    new Vector3(-0.3f, -0.02f, 0.05f),
                RightHandPositionFromHeadNormalized =
                    new Vector3(0.3f, -0.02f, 0.05f)
            };

        private static PosingScoreTarget SingleBiceps =>
            new PosingScoreTarget
            {
                LeftHandPositionFromHeadNormalized =
                    new Vector3(0.05f, -0.42f, 0.2f),
                RightHandPositionFromHeadNormalized =
                    new Vector3(0.28f, 0f, 0.08f)
            };

        [Test]
        public void SideViewPlotsDepthNotWidth()
        {
            // The two hands of a symmetric pose sit at the same depth, so
            // the profile view must collapse them onto one another. The
            // front view is what separates them.
            var side =
                PoseReferenceGuide.CalculateSideLayout(
                    FrontDoubleBiceps,
                    Vector2.zero,
                    200f,
                    0f,
                    0.13f);
            Assert.That(
                Vector2.Distance(
                    side.LeftHandPosition,
                    side.RightHandPosition),
                Is.LessThan(0.001f));
            Assert.That(
                side.LeftHandPosition.x,
                Is.EqualTo(0.05f * 200f).Within(0.001f));
            Assert.That(
                side.LeftHandPosition.y,
                Is.EqualTo(-0.02f * 200f).Within(0.001f));
        }

        [Test]
        public void SideViewSeparatesHandsAtDifferentDepths()
        {
            var side =
                PoseReferenceGuide.CalculateSideLayout(
                    SingleBiceps,
                    Vector2.zero,
                    200f,
                    0f,
                    0.13f);
            Assert.That(
                side.LeftHandPosition.x,
                Is.GreaterThan(side.RightHandPosition.x),
                "The hand held further forward must plot further right.");
            Assert.That(
                side.LeftHandPosition.y,
                Is.LessThan(side.RightHandPosition.y),
                "The lowered hand must plot lower.");
        }

        [Test]
        public void ForwardIsPositiveAndBackwardIsNegative()
        {
            // A hand behind the chest is exactly the case that loses Quest
            // controller tracking, so it has to read as clearly behind.
            var behind = new PosingScoreTarget
            {
                LeftHandPositionFromHeadNormalized =
                    new Vector3(-0.2f, -0.1f, -0.18f),
                RightHandPositionFromHeadNormalized =
                    new Vector3(0.2f, -0.1f, 0.18f)
            };
            var side =
                PoseReferenceGuide.CalculateSideLayout(
                    behind,
                    Vector2.zero,
                    200f,
                    0f,
                    0.13f);
            Assert.That(side.LeftHandPosition.x, Is.LessThan(0f));
            Assert.That(side.RightHandPosition.x, Is.GreaterThan(0f));
        }

        [Test]
        public void SideShouldersCollapseOntoOnePoint()
        {
            var side =
                PoseReferenceGuide.CalculateSideLayout(
                    SingleBiceps,
                    new Vector2(10f, 30f),
                    200f,
                    0f,
                    0.13f);
            Assert.That(
                side.LeftShoulderPosition,
                Is.EqualTo(side.RightShoulderPosition));
            Assert.That(
                side.LeftShoulderPosition.y,
                Is.LessThan(side.HeadPosition.y));
        }

        [Test]
        public void SideViewSurvivesBrokenInput()
        {
            var broken = new PosingScoreTarget
            {
                LeftHandPositionFromHeadNormalized =
                    new Vector3(0f, float.NaN, float.PositiveInfinity),
                RightHandPositionFromHeadNormalized =
                    new Vector3(0f, 0f, 0f)
            };
            var side =
                PoseReferenceGuide.CalculateSideLayout(
                    broken,
                    new Vector2(float.NaN, 30f),
                    float.PositiveInfinity,
                    float.NaN,
                    -5f);
            foreach (var value in new[]
                     {
                         side.HeadPosition,
                         side.LeftShoulderPosition,
                         side.RightShoulderPosition,
                         side.LeftHandPosition,
                         side.RightHandPosition
                     })
            {
                Assert.That(float.IsNaN(value.x), Is.False);
                Assert.That(float.IsNaN(value.y), Is.False);
                Assert.That(float.IsInfinity(value.x), Is.False);
                Assert.That(float.IsInfinity(value.y), Is.False);
            }
        }
    }
}
