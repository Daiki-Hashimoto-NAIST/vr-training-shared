using NUnit.Framework;

namespace IVRC.Tests
{
    public sealed class ExperienceScoreCalculatorTests
    {
        [Test]
        public void PerfectTrainingProducesOneHundredAndSRank()
        {
            var result = Calculate(new ExperienceScoreInput(10, 8, 1f, 1f));

            Assert.That(result.RepScore, Is.EqualTo(100f));
            Assert.That(result.RhythmScore, Is.EqualTo(100f));
            Assert.That(result.GrowthScore, Is.EqualTo(100f));
            Assert.That(result.TrainingScore, Is.EqualTo(100f));
            Assert.That(result.FinalScore, Is.EqualTo(100f));
            Assert.That(result.Rank, Is.EqualTo(ExperienceRank.S));
        }

        [Test]
        public void FinalScoreIsExactlyTheTrainingScore()
        {
            var result = Calculate(new ExperienceScoreInput(10, 0, 0.5f, 1f));

            Assert.That(result.TrainingScore,
                Is.EqualTo(66.66667f).Within(0.0001f));
            Assert.That(result.FinalScore, Is.EqualTo(result.TrainingScore));
            Assert.That(result.Rank, Is.EqualTo(ExperienceRank.B));
        }

        [Test]
        public void TrainingWeightsAreNormalized()
        {
            var settings = new ExperienceScoreSettings(
                10,
                10,
                new ExperienceTrainingWeights
                {
                    Reps = 1f,
                    Rhythm = 3f,
                    Growth = 0f
                },
                Thresholds);
            var result = ExperienceScoreCalculator.Calculate(
                new ExperienceScoreInput(10, 10, 0f, 1f),
                settings);

            Assert.That(result.TrainingScore, Is.EqualTo(25f));
            Assert.That(result.FinalScore, Is.EqualTo(25f));
        }

        [Test]
        public void InputsAreClampedToTrainingScoreRanges()
        {
            var high = Calculate(new ExperienceScoreInput(
                int.MaxValue, int.MaxValue, 2f, float.PositiveInfinity));
            var low = Calculate(new ExperienceScoreInput(
                int.MinValue, int.MinValue, float.NaN,
                float.NegativeInfinity));

            Assert.That(high.FinalScore, Is.EqualTo(100f));
            Assert.That(low.FinalScore, Is.EqualTo(0f));
        }

        private static ExperienceScoreResult Calculate(
            ExperienceScoreInput input)
        {
            return ExperienceScoreCalculator.Calculate(
                input,
                new ExperienceScoreSettings(
                    10,
                    8,
                    new ExperienceTrainingWeights
                    {
                        Reps = 1f,
                        Rhythm = 1f,
                        Growth = 1f
                    },
                    Thresholds));
        }

        private static ExperienceRankThresholds Thresholds =>
            new ExperienceRankThresholds
            {
                S = 90f,
                A = 80f,
                B = 70f,
                C = 60f
            };
    }
}
