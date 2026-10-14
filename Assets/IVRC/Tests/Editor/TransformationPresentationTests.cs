using IVRC;
using NUnit.Framework;

namespace IVRC.Tests
{
    public sealed class TransformationPresentationTests
    {
        [TestCase(0f, TransformationPresentationStage.Lock)]
        [TestCase(0.149f, TransformationPresentationStage.Lock)]
        [TestCase(0.15f, TransformationPresentationStage.Reveal)]
        [TestCase(0.699f, TransformationPresentationStage.Reveal)]
        [TestCase(0.70f, TransformationPresentationStage.HeroHold)]
        [TestCase(1f, TransformationPresentationStage.HeroHold)]
        public void StageBoundariesAreDeterministic(
            float progress,
            TransformationPresentationStage expected)
        {
            Assert.That(
                TransformationPresentationMath.GetStage(progress),
                Is.EqualTo(expected));
        }

        [Test]
        public void EnvelopeStartsAtTheUnmodifiedSceneLighting()
        {
            var frame = Evaluate(0f);

            Assert.That(frame.Stage,
                Is.EqualTo(TransformationPresentationStage.Lock));
            Assert.That(frame.KeyIntensityMultiplier,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(frame.FillIntensityMultiplier,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(frame.ColorBlend,
                Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void EnvelopeEndsOnTheConfiguredHeroLook()
        {
            var frame = Evaluate(1f);

            Assert.That(frame.Stage,
                Is.EqualTo(TransformationPresentationStage.HeroHold));
            Assert.That(frame.KeyIntensityMultiplier,
                Is.EqualTo(1.12f).Within(0.0001f));
            Assert.That(frame.FillIntensityMultiplier,
                Is.EqualTo(1.05f).Within(0.0001f));
            Assert.That(frame.ColorBlend,
                Is.EqualTo(0.45f).Within(0.0001f));
        }

        [Test]
        public void InvalidInputsCannotProduceUnsafeValues()
        {
            var frame = TransformationPresentationMath.Evaluate(
                float.NaN,
                float.PositiveInfinity,
                -5f,
                5f,
                float.NaN,
                20f,
                -20f,
                float.NegativeInfinity);

            Assert.That(frame.NormalizedProgress,
                Is.InRange(0f, 1f));
            Assert.That(frame.KeyIntensityMultiplier,
                Is.InRange(0f, 2f));
            Assert.That(frame.FillIntensityMultiplier,
                Is.InRange(0f, 2f));
            Assert.That(frame.ColorBlend,
                Is.InRange(0f, 1f));
        }

        [Test]
        public void CueRulesNeverLayerRevealAndHeroOnProgressJump()
        {
            Assert.That(
                TransformationPresentationCueRules.SelectCue(
                    1f,
                    false,
                    false),
                Is.EqualTo(TransformationPresentationCue.Hero));
            Assert.That(
                TransformationPresentationCueRules.SelectCue(
                    0.4f,
                    false,
                    false),
                Is.EqualTo(TransformationPresentationCue.Reveal));
            Assert.That(
                TransformationPresentationCueRules.SelectCue(
                    1f,
                    true,
                    true),
                Is.EqualTo(TransformationPresentationCue.None));
            Assert.That(
                TransformationPresentationCueRules.SelectCue(
                    0.70f,
                    true,
                    false),
                Is.EqualTo(TransformationPresentationCue.Hero));
            Assert.That(
                TransformationPresentationCueRules.SelectCue(
                    1f,
                    true,
                    false),
                Is.EqualTo(TransformationPresentationCue.Hero));
        }

        [TestCase(-1f, 0.01f)]
        [TestCase(0.14f, 0.14f)]
        [TestCase(1f, 0.20f)]
        [TestCase(float.NaN, 0.01f)]
        [TestCase(float.PositiveInfinity, 0.01f)]
        public void HeroHapticDurationIsAlwaysShortAndFinite(
            float configuredSeconds,
            float expectedSeconds)
        {
            Assert.That(
                TransformationPresentationSafety
                    .ClampHeroHapticDuration(configuredSeconds),
                Is.EqualTo(expectedSeconds).Within(0.0001f));
        }

        private static TransformationPresentationFrame Evaluate(
            float progress)
        {
            return TransformationPresentationMath.Evaluate(
                progress,
                0.82f,
                1.35f,
                1.12f,
                0.72f,
                1.22f,
                1.05f,
                0.45f);
        }
    }
}
