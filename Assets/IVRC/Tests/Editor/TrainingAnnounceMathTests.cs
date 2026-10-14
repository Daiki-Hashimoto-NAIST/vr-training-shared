using NUnit.Framework;

namespace IVRC.Tests
{
    public sealed class TrainingAnnounceMathTests
    {
        [TestCase(4f, "3")]
        [TestCase(3.5f, "3")]
        [TestCase(3f, "2")]
        [TestCase(2.5f, "2")]
        [TestCase(2f, "1")]
        [TestCase(1.5f, "1")]
        [TestCase(1f, "GO")]
        [TestCase(0.25f, "GO")]
        public void OneLabelPerBeatEndingOnGo(
            float beatsUntilUp,
            string expected)
        {
            Assert.That(
                TrainingAnnounceMath.GetCountdownLabel(beatsUntilUp),
                Is.EqualTo(expected));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(4.01f)]
        [TestCase(40f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void NothingIsShownOutsideTheWindow(float beatsUntilUp)
        {
            Assert.That(
                TrainingAnnounceMath.GetCountdownLabel(beatsUntilUp),
                Is.Empty,
                "A stale number left on the panel is worse than none.");
        }

        [Test]
        public void TheWindowMatchesTheBeatsTheChartLeavesForIt()
        {
            // The pattern's lead-in and its gaps between blocks are both
            // four beats. If either changes, this is the number to revisit.
            Assert.That(TrainingAnnounceMath.CountdownBeats, Is.EqualTo(4));
        }
        [Test]
        public void TheWhistleGetsTheHeadlineToItself()
        {
            // 終了！ lands with the whistle; the instruction to leave only
            // arrives once the hold is out, so the words do not overtake
            // the sound that ends the set.
            Assert.That(
                TrainingAnnounceMath.GetCompletionHeadline(0f, 1.2f),
                Is.EqualTo(TrainingAnnounceMath.CompletedHeadline));
            Assert.That(
                TrainingAnnounceMath.GetCompletionHeadline(1.19f, 1.2f),
                Is.EqualTo(TrainingAnnounceMath.CompletedHeadline));
            Assert.That(
                TrainingAnnounceMath.GetCompletionHeadline(1.2f, 1.2f),
                Is.EqualTo(TrainingAnnounceMath.LeaveHeadline));
            Assert.That(
                TrainingAnnounceMath.GetCompletionHeadline(9f, 1.2f),
                Is.EqualTo(TrainingAnnounceMath.LeaveHeadline));
        }

        [Test]
        public void TheLeaveLinePointsAtTheStage()
        {
            Assert.That(
                TrainingAnnounceMath.LeaveHeadline,
                Does.Contain("ステージ"));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void ABrokenClockHoldsOnTheFirstLine(float seconds)
        {
            Assert.That(
                TrainingAnnounceMath.GetCompletionHeadline(seconds, 1.2f),
                Is.EqualTo(TrainingAnnounceMath.CompletedHeadline));
        }

    }
}
