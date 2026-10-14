using NUnit.Framework;

namespace IVRC.Tests
{
    public sealed class PoseStageFeedbackMathTests
    {
        [TestCase(0f, 0f)]
        [TestCase(45f, 0f)]
        [TestCase(67.5f, 0.5f)]
        [TestCase(90f, 1f)]
        [TestCase(100f, 1f)]
        public void ReactionUsesSmoothBoundedScoreWindow(
            float score,
            float expected)
        {
            var reaction =
                PoseStageFeedbackMath.CalculateReaction(
                    score,
                    45f,
                    90f);

            Assert.That(
                reaction,
                Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void InvalidReactionWindowRemainsDeterministic()
        {
            Assert.That(
                PoseStageFeedbackMath.CalculateReaction(
                    50f,
                    80f,
                    40f),
                Is.EqualTo(0f));
            Assert.That(
                PoseStageFeedbackMath.CalculateReaction(
                    80f,
                    80f,
                    40f),
                Is.EqualTo(1f));
        }

        [TestCase(59.99f, 0)]
        [TestCase(60f, 1)]
        [TestCase(75f, 2)]
        [TestCase(90f, 3)]
        [TestCase(100f, 3)]
        public void CheerTiersAreInclusive(
            float score,
            int expectedTier)
        {
            Assert.That(
                PoseStageFeedbackMath.GetCheerTier(
                    score,
                    60f,
                    75f,
                    90f),
                Is.EqualTo(expectedTier));
        }

        [Test]
        public void NaNInputsProduceNoReaction()
        {
            Assert.That(
                PoseStageFeedbackMath.CalculateReaction(
                    float.NaN,
                    45f,
                    90f),
                Is.EqualTo(0f));
            Assert.That(
                PoseStageFeedbackMath.GetCheerTier(
                    float.NaN,
                    60f,
                    75f,
                    90f),
                Is.EqualTo(0));
        }

        [TestCase(0.65f, 0.8f)]
        [TestCase(0.325f, 0.4f)]
        [TestCase(0f, 0f)]
        [TestCase(-1f, 0f)]
        public void CompletionPulseFadesWithoutAccumulating(
            float remainingSeconds,
            float expected)
        {
            Assert.That(
                PoseStageFeedbackMath.CalculateCompletionPulse(
                    remainingSeconds,
                    0.65f,
                    0.8f),
                Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void InvalidCompletionPulseIsSilent()
        {
            Assert.That(
                PoseStageFeedbackMath.CalculateCompletionPulse(
                    float.NaN,
                    0.65f,
                    0.8f),
                Is.Zero);
            Assert.That(
                PoseStageFeedbackMath.CalculateCompletionPulse(
                    0.65f,
                    0f,
                    0.8f),
                Is.Zero);
        }

        [TestCase(true, 0, 3, 1, true)]
        [TestCase(true, 1, 3, 2, true)]
        [TestCase(true, 2, 3, 3, false)]
        [TestCase(true, 2, 3, 2, true)]
        [TestCase(false, 0, 3, 0, false)]
        public void PoseSuccessCueDefersOnlyFullyClearedFinalPoseToResults(
            bool succeeded,
            int poseIndex,
            int poseCount,
            int clearedPoseCount,
            bool expected)
        {
            var result = new PoseCompletionResult(
                poseIndex,
                poseCount,
                "POSE",
                80f,
                succeeded,
                clearedPoseCount);

            Assert.That(
                PoseStageFeedbackMath
                    .ShouldPlayPoseSuccessCue(result),
                Is.EqualTo(expected));
        }

        [TestCase(0f, 0.01f)]
        [TestCase(0.09f, 0.09f)]
        [TestCase(0.20f, 0.20f)]
        [TestCase(1f, 0.20f)]
        [TestCase(float.NaN, 0.01f)]
        [TestCase(float.PositiveInfinity, 0.01f)]
        public void PoseHapticAlwaysRemainsAShortFinitePulse(
            float configuredSeconds,
            float expectedSeconds)
        {
            Assert.That(
                PoseStageFeedbackMath.ClampHapticSeconds(
                    configuredSeconds),
                Is.EqualTo(expectedSeconds).Within(0.0001f));
        }
    }
}
