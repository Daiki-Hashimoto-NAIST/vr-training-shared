using NUnit.Framework;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class StageSpotlightTrackerTests
    {
        [Test]
        public void AimPointIsOffsetVertically()
        {
            Assert.That(
                Vector3.Distance(
                    StageSpotlightTrackingMath.ResolveAimPoint(
                        new Vector3(1f, 1.7f, 15f),
                        -0.4f),
                    new Vector3(1f, 1.3f, 15f)),
                Is.LessThan(0.000001f));
        }

        [Test]
        public void AimPointIgnoresNonFiniteOffset()
        {
            var target = new Vector3(0f, 1f, 2f);
            Assert.That(
                StageSpotlightTrackingMath.ResolveAimPoint(
                    target,
                    float.NaN),
                Is.EqualTo(target));
        }

        [TestCase(0f, 0.1f, 1f)]
        [TestCase(7f, 0f, 0f)]
        [TestCase(7f, -1f, 0f)]
        public void TurnBlendHandlesEdgeCases(
            float response,
            float deltaTime,
            float expected)
        {
            Assert.That(
                StageSpotlightTrackingMath.EvaluateTurnBlend(
                    response,
                    deltaTime),
                Is.EqualTo(expected).Within(0.000001f));
        }

        [Test]
        public void TurnBlendIsFrameRateIndependent()
        {
            // Two half steps must close the same share of the
            // angle as one full step, so the sweep speed cannot
            // depend on the frame rate.
            var single =
                StageSpotlightTrackingMath.EvaluateTurnBlend(
                    7f,
                    0.2f);
            var first =
                StageSpotlightTrackingMath.EvaluateTurnBlend(
                    7f,
                    0.1f);
            var combined = first + (1f - first) * first;
            Assert.That(
                combined,
                Is.EqualTo(single).Within(0.000001f));
        }

        [Test]
        public void AimRotationPointsAtTheTargetWhenSnapping()
        {
            var rotation =
                StageSpotlightTrackingMath.EvaluateAimRotation(
                    new Vector3(-3f, 4.2f, 15.5f),
                    new Vector3(0f, 1.3f, 15f),
                    Quaternion.identity,
                    0f,
                    0.02f);
            var expected =
                (new Vector3(0f, 1.3f, 15f) -
                 new Vector3(-3f, 4.2f, 15.5f)).normalized;
            Assert.That(
                Vector3.Angle(rotation * Vector3.forward, expected),
                Is.LessThan(0.001f));
        }

        [Test]
        public void AimRotationApproachesTargetGradually()
        {
            var light = new Vector3(-3f, 4.2f, 15.5f);
            var aim = new Vector3(0f, 1.3f, 15f);
            var direction = (aim - light).normalized;
            var rotation = Quaternion.identity;
            var startAngle = Vector3.Angle(
                rotation * Vector3.forward,
                direction);

            rotation =
                StageSpotlightTrackingMath.EvaluateAimRotation(
                    light,
                    aim,
                    rotation,
                    7f,
                    0.02f);
            var steppedAngle = Vector3.Angle(
                rotation * Vector3.forward,
                direction);
            Assert.That(steppedAngle, Is.LessThan(startAngle));
            Assert.That(steppedAngle, Is.GreaterThan(0.001f));

            for (var step = 0; step < 200; step++)
            {
                rotation =
                    StageSpotlightTrackingMath
                        .EvaluateAimRotation(
                            light,
                            aim,
                            rotation,
                            7f,
                            0.02f);
            }

            Assert.That(
                Vector3.Angle(
                    rotation * Vector3.forward,
                    direction),
                Is.LessThan(0.01f));
        }

        [Test]
        public void AimRotationKeepsCurrentWhenDegenerate()
        {
            var current = Quaternion.Euler(46f, 80f, 0f);
            Assert.That(
                StageSpotlightTrackingMath.EvaluateAimRotation(
                    new Vector3(0f, 4f, 15f),
                    new Vector3(0f, 4f, 15f),
                    current,
                    7f,
                    0.02f),
                Is.EqualTo(current));
        }

        [Test]
        public void AimRotationSurvivesStraightDownBeam()
        {
            var rotation =
                StageSpotlightTrackingMath.EvaluateAimRotation(
                    new Vector3(0f, 4.2f, 15f),
                    new Vector3(0f, 1.3f, 15f),
                    Quaternion.identity,
                    0f,
                    0.02f);
            Assert.That(
                Vector3.Angle(
                    rotation * Vector3.forward,
                    Vector3.down),
                Is.LessThan(0.001f));
        }
    }
}
