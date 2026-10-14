using NUnit.Framework;

namespace IVRC.Tests
{
    public sealed class SetupCompletionFeedbackTests
    {
        [Test]
        public void SuccessfulStepsAnnounceInOrderOnlyOnce()
        {
            var state = new SetupCompletionFeedbackState();

            Assert.That(
                state.Observe(false, false, false),
                Is.EqualTo(SetupCompletionCue.None));
            Assert.That(
                state.Observe(true, false, false),
                Is.EqualTo(SetupCompletionCue.AlignmentComplete));
            Assert.That(
                state.Observe(true, false, false),
                Is.EqualTo(SetupCompletionCue.None));
            Assert.That(
                state.Observe(true, true, false),
                Is.EqualTo(SetupCompletionCue.HeightComplete));
            Assert.That(
                state.Observe(true, true, true),
                Is.EqualTo(SetupCompletionCue.Ready));
            Assert.That(
                state.Observe(true, true, true),
                Is.EqualTo(SetupCompletionCue.None));
        }

        [Test]
        public void InvalidatingAlignmentRearmsTheWholeChecklist()
        {
            var state = new SetupCompletionFeedbackState();
            state.Observe(true, false, false);
            state.Observe(true, true, false);
            state.Observe(true, true, true);

            Assert.That(
                state.Observe(false, false, false),
                Is.EqualTo(SetupCompletionCue.None));
            Assert.That(
                state.Observe(true, false, false),
                Is.EqualTo(SetupCompletionCue.AlignmentComplete));
            Assert.That(
                state.Observe(true, true, false),
                Is.EqualTo(SetupCompletionCue.HeightComplete));
            Assert.That(
                state.Observe(true, true, true),
                Is.EqualTo(SetupCompletionCue.Ready));
        }

        [Test]
        public void FailedHeightDoesNotProduceSuccessCue()
        {
            var state = new SetupCompletionFeedbackState();
            state.Observe(true, false, false);

            Assert.That(
                state.Observe(true, false, false),
                Is.EqualTo(SetupCompletionCue.None));
            Assert.That(
                state.Observe(true, false, true),
                Is.EqualTo(SetupCompletionCue.None));
        }

        [TestCase(0f, 0.01f)]
        [TestCase(0.06f, 0.06f)]
        [TestCase(0.12f, 0.12f)]
        [TestCase(1f, 0.12f)]
        [TestCase(float.NaN, 0.01f)]
        [TestCase(float.PositiveInfinity, 0.01f)]
        public void SetupHapticAlwaysRemainsAShortFinitePulse(
            float configuredSeconds,
            float expectedSeconds)
        {
            Assert.That(
                SetupCompletionFeedbackSafety
                    .ClampHapticSeconds(configuredSeconds),
                Is.EqualTo(expectedSeconds).Within(0.0001f));
        }
    }
}
