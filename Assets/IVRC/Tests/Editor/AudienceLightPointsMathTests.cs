using NUnit.Framework;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class AudienceLightPointsMathTests
    {
        [Test]
        public void RestingRoomHoldsStill()
        {
            // A crowd that shimmers while nothing is happening reads as
            // decoration. At zero reaction every point must be flat,
            // whatever the clock says.
            for (var time = 0f; time < 4f; time += 0.37f)
            {
                Assert.That(
                    AudienceLightPointsMath.CalculateExcitement(
                        0f,
                        time,
                        1.2f,
                        0.9f),
                    Is.EqualTo(0f));
            }
        }

        [Test]
        public void ExcitementStaysWithinTheReaction()
        {
            for (var time = 0f; time < 6f; time += 0.13f)
            {
                var excitement =
                    AudienceLightPointsMath.CalculateExcitement(
                        0.4f,
                        time,
                        0.7f,
                        1.3f);
                Assert.That(
                    excitement,
                    Is.InRange(0f, 0.4f),
                    "A half-hearted room must never look " +
                    "louder than a full one.");
            }
        }

        [Test]
        public void FullReactionReachesBothEndsOfTheWave()
        {
            var lowest = float.MaxValue;
            var highest = float.MinValue;
            for (var time = 0f; time < 4f; time += 0.01f)
            {
                var excitement =
                    AudienceLightPointsMath.CalculateExcitement(
                        1f,
                        time,
                        0f,
                        1f);
                lowest = Mathf.Min(lowest, excitement);
                highest = Mathf.Max(highest, excitement);
            }

            Assert.That(lowest, Is.LessThan(0.02f));
            Assert.That(highest, Is.GreaterThan(0.98f));
        }

        [Test]
        public void ReactionAboveOneIsClamped()
        {
            for (var time = 0f; time < 3f; time += 0.05f)
            {
                Assert.That(
                    AudienceLightPointsMath.CalculateExcitement(
                        4f,
                        time,
                        0.3f,
                        1f),
                    Is.InRange(0f, 1f));
            }
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void BrokenReactionDoesNotLightTheRoom(
            float reaction)
        {
            Assert.That(
                AudienceLightPointsMath.CalculateExcitement(
                    reaction,
                    1.5f,
                    0.5f,
                    1f),
                Is.EqualTo(0f));
        }

        [Test]
        public void PhaseRollsBackwardsThroughTheRoom()
        {
            // Two rows a full cycle apart must land on the same phase, and
            // a row halfway between must be half a cycle behind, so the
            // shimmer travels rather than blinking as one.
            var near = AudienceLightPointsMath.CalculatePhase(
                0f,
                8f,
                0f);
            var half = AudienceLightPointsMath.CalculatePhase(
                4f,
                8f,
                0f);
            var far = AudienceLightPointsMath.CalculatePhase(
                8f,
                8f,
                0f);

            Assert.That(near, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(
                half,
                Is.EqualTo(Mathf.PI).Within(0.0001f));
            Assert.That(
                far,
                Is.EqualTo(Mathf.PI * 2f).Within(0.0001f));
        }

        [Test]
        public void PhaseSurvivesAnImpossibleCycleLength()
        {
            Assert.That(
                AudienceLightPointsMath.CalculatePhase(
                    5f,
                    0f,
                    0f),
                Is.EqualTo(0f));
            Assert.That(
                AudienceLightPointsMath.CalculatePhase(
                    5f,
                    float.NaN,
                    0f),
                Is.EqualTo(0f));
        }

        [Test]
        public void JitterIsRepeatableAndBounded()
        {
            var seat = new Vector3(1.25f, 0.4f, 19.5f);
            var first =
                AudienceLightPointsMath.CalculateJitterTurns(
                    seat,
                    0.2f);
            var again =
                AudienceLightPointsMath.CalculateJitterTurns(
                    seat,
                    0.2f);

            Assert.That(
                first,
                Is.EqualTo(again),
                "The same seat must look the same on every run.");
            Assert.That(first, Is.InRange(-0.1f, 0.1f));
        }

        [Test]
        public void NeighbouringSeatsDoNotShareJitter()
        {
            var left =
                AudienceLightPointsMath.CalculateJitterTurns(
                    new Vector3(-0.62f, 0.4f, 19.5f),
                    0.3f);
            var right =
                AudienceLightPointsMath.CalculateJitterTurns(
                    new Vector3(0f, 0.4f, 19.5f),
                    0.3f);

            Assert.That(left, Is.Not.EqualTo(right));
        }

        [Test]
        public void NoJitterRequestedMeansNoJitter()
        {
            Assert.That(
                AudienceLightPointsMath.CalculateJitterTurns(
                    new Vector3(3f, 1f, 22f),
                    0f),
                Is.EqualTo(0f));
        }

        [Test]
        public void HeadSitsAboveTheSeatAndNowhereElse()
        {
            var seat = new Vector3(2f, 0.3f, 21f);
            var head =
                AudienceLightPointsMath.CalculatePointPosition(
                    seat,
                    0.35f);

            Assert.That(head.x, Is.EqualTo(seat.x));
            Assert.That(head.z, Is.EqualTo(seat.z));
            Assert.That(
                head.y,
                Is.EqualTo(0.65f).Within(0.0001f));
        }

        [Test]
        public void BrokenHeightLeavesTheSeatWhereItIs()
        {
            var seat = new Vector3(2f, 0.3f, 21f);
            Assert.That(
                AudienceLightPointsMath.CalculatePointPosition(
                    seat,
                    float.NaN),
                Is.EqualTo(seat));
        }
        [Test]
        public void ZeroScatterLeavesTheRowsExact()
        {
            var seat = new Vector3(1.2f, 0.5f, 20f);
            Assert.That(
                AudienceLightPointsMath.ApplyLateralJitter(seat, 0f),
                Is.EqualTo(seat));
        }

        [Test]
        public void ScatterMovesSidewaysOnlyAndStaysWithinTheAmount()
        {
            var seat = new Vector3(1.2f, 0.5f, 20f);
            var moved =
                AudienceLightPointsMath.ApplyLateralJitter(seat, 0.2f);

            Assert.That(moved.y, Is.EqualTo(seat.y));
            Assert.That(moved.z, Is.EqualTo(seat.z));
            Assert.That(
                Mathf.Abs(moved.x - seat.x),
                Is.LessThanOrEqualTo(0.1f),
                "Half the amount either side.");
        }

        [Test]
        public void ScatterIsRepeatableAndDiffersBetweenSeats()
        {
            var a = new Vector3(1.2f, 0.5f, 20f);
            var b = new Vector3(1.82f, 0.5f, 20f);
            Assert.That(
                AudienceLightPointsMath.ApplyLateralJitter(a, 0.2f),
                Is.EqualTo(
                    AudienceLightPointsMath.ApplyLateralJitter(a, 0.2f)));
            Assert.That(
                AudienceLightPointsMath.ApplyLateralJitter(a, 0.2f).x,
                Is.Not.EqualTo(
                    AudienceLightPointsMath.ApplyLateralJitter(b, 0.2f).x));
        }

    }
}
