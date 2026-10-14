using NUnit.Framework;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class PoseReferenceElbowTests
    {
        private const float Upper = 37.2f;
        private const float Fore = 29.2f;

        [Test]
        public void ElbowKeepsBothBoneLengths()
        {
            var shoulder = new Vector2(-24f, 19f);
            var hand = new Vector2(-60f, 41f);
            var elbow = PoseReferenceGuide.CalculateElbow(
                shoulder,
                hand,
                Upper,
                Fore,
                true);

            Assert.That(
                Vector2.Distance(shoulder, elbow),
                Is.EqualTo(Upper).Within(0.01f));
            Assert.That(
                Vector2.Distance(elbow, hand),
                Is.EqualTo(Fore).Within(0.01f));
        }

        [Test]
        public void ElbowActuallyBendsOffTheStraightLine()
        {
            var shoulder = new Vector2(-24f, 19f);
            var hand = new Vector2(-60f, 41f);
            var elbow = PoseReferenceGuide.CalculateElbow(
                shoulder,
                hand,
                Upper,
                Fore,
                true);

            // Distance from the elbow to the shoulder-hand line. A straight
            // arm would leave this at zero and the figure would read as a
            // pointer rather than a bent arm.
            var axis = (hand - shoulder).normalized;
            var toElbow = elbow - shoulder;
            var offAxis = Mathf.Abs(
                toElbow.x * axis.y - toElbow.y * axis.x);
            Assert.That(offAxis, Is.GreaterThan(5f));
        }

        [Test]
        public void BendFlagMirrorsTheElbowAcrossTheArm()
        {
            var shoulder = new Vector2(-24f, 19f);
            var hand = new Vector2(-60f, 41f);
            var positive = PoseReferenceGuide.CalculateElbow(
                shoulder, hand, Upper, Fore, true);
            var negative = PoseReferenceGuide.CalculateElbow(
                shoulder, hand, Upper, Fore, false);

            Assert.That(
                Vector2.Distance(positive, negative),
                Is.GreaterThan(1f));

            var midpoint = (positive + negative) * 0.5f;
            var axis = (hand - shoulder).normalized;
            var toMid = midpoint - shoulder;
            var offAxis = Mathf.Abs(
                toMid.x * axis.y - toMid.y * axis.x);
            Assert.That(
                offAxis,
                Is.LessThan(0.01f),
                "The two solutions must mirror across the arm axis.");
        }

        [Test]
        public void ElbowPoleChoosesTheSolutionNearestThePole()
        {
            var shoulder = new Vector2(-24f, 19f);
            var hand = new Vector2(-44f, 8f);
            var outwardPole = new Vector2(-100f, 8f);

            var elbow =
                PoseReferenceGuide.CalculateElbowTowardPole(
                    shoulder,
                    hand,
                    Upper,
                    Fore,
                    outwardPole);

            Assert.That(elbow.x, Is.LessThan(shoulder.x));
        }

        [Test]
        public void ArmStraightensWhenTheHandIsOutOfReach()
        {
            var shoulder = Vector2.zero;
            var hand = new Vector2(400f, 0f);
            var elbow = PoseReferenceGuide.CalculateElbow(
                shoulder,
                hand,
                Upper,
                Fore,
                true);

            var axis = (hand - shoulder).normalized;
            var offAxis = Mathf.Abs(
                elbow.x * axis.y - elbow.y * axis.x);
            Assert.That(
                offAxis,
                Is.LessThan(0.01f),
                "An unreachable target must straighten, not tear.");
            Assert.That(
                elbow.x,
                Is.EqualTo(Upper).Within(0.01f));
        }

        [Test]
        public void CoincidentShoulderAndHandFallBackToTheMidpoint()
        {
            var point = new Vector2(12f, -5f);
            var elbow = PoseReferenceGuide.CalculateElbow(
                point,
                point,
                Upper,
                Fore,
                true);
            Assert.That(
                Vector2.Distance(elbow, point),
                Is.LessThan(0.001f));
        }

        [Test]
        public void ElbowSurvivesBrokenInput()
        {
            var elbow = PoseReferenceGuide.CalculateElbow(
                new Vector2(float.NaN, 0f),
                new Vector2(0f, float.PositiveInfinity),
                float.NaN,
                -8f,
                true);
            Assert.That(float.IsNaN(elbow.x), Is.False);
            Assert.That(float.IsNaN(elbow.y), Is.False);
            Assert.That(float.IsInfinity(elbow.x), Is.False);
            Assert.That(float.IsInfinity(elbow.y), Is.False);
        }
    }
}
