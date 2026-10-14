using NUnit.Framework;

namespace IVRC.Tests
{
    public sealed class StageMusicControllerTests
    {
        [TestCase(ExperiencePhase.Setup, StageTransitionDestination.Training, false)]
        [TestCase(ExperiencePhase.Training, StageTransitionDestination.Training, false)]
        [TestCase(ExperiencePhase.Transformation, StageTransitionDestination.Training, false)]
        [TestCase(ExperiencePhase.StageTransition, StageTransitionDestination.Judges, true)]
        [TestCase(ExperiencePhase.StageEntrance, StageTransitionDestination.Judges, true)]
        [TestCase(ExperiencePhase.StageTransition, StageTransitionDestination.Stage, true)]
        [TestCase(ExperiencePhase.Posing, StageTransitionDestination.Stage, true)]
        [TestCase(ExperiencePhase.Results, StageTransitionDestination.Stage, false)]
        [TestCase(ExperiencePhase.StageTransition, StageTransitionDestination.Training, false)]
        public void StageMusicOwnsOnlyTheForwardStageSequence(
            ExperiencePhase phase,
            StageTransitionDestination destination,
            bool expected)
        {
            Assert.That(
                StageMusicEnvelope.IsStageMusicPhase(
                    phase,
                    destination),
                Is.EqualTo(expected));
        }

        [Test]
        public void TrackingPauseDucksPosingWithoutStoppingTheBed()
        {
            Assert.That(
                StageMusicEnvelope.GetTargetVolume(
                    ExperiencePhase.Posing,
                    StageTransitionDestination.Stage,
                    true,
                    0.12f,
                    0.3f),
                Is.EqualTo(0.036f).Within(0.0001f));
            Assert.That(
                StageMusicEnvelope.GetTargetVolume(
                    ExperiencePhase.Posing,
                    StageTransitionDestination.Stage,
                    false,
                    0.12f,
                    0.3f),
                Is.EqualTo(0.12f).Within(0.0001f));
        }

        [Test]
        public void ResultsAndReturnTransitionAreSilent()
        {
            Assert.That(
                StageMusicEnvelope.GetTargetVolume(
                    ExperiencePhase.Results,
                    StageTransitionDestination.Stage,
                    false,
                    0.12f,
                    0.3f),
                Is.Zero);
            Assert.That(
                StageMusicEnvelope.GetTargetVolume(
                    ExperiencePhase.StageTransition,
                    StageTransitionDestination.Training,
                    false,
                    0.12f,
                    0.3f),
                Is.Zero);
        }
    }
}
