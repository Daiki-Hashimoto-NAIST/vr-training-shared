using NUnit.Framework;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class RhythmPoseGuideLineArtTests
    {
        private const float StartOffset = 0.12f;
        private const float MetOffset = 0f;

        private static AnimationCurve Linear =>
            AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Test]
        public void ContourStartsOutsideAndMeetsOnTheBeat()
        {
            var atStart =
                RhythmPoseGuideMath.EvaluateContourOffset(
                    1f,
                    1f,
                    StartOffset,
                    MetOffset,
                    Linear);
            var onBeat =
                RhythmPoseGuideMath.EvaluateContourOffset(
                    0f,
                    1f,
                    StartOffset,
                    MetOffset,
                    Linear);
            Assert.That(
                atStart,
                Is.EqualTo(StartOffset).Within(0.000001f));
            Assert.That(
                onBeat,
                Is.EqualTo(MetOffset).Within(0.000001f));
        }

        [Test]
        public void ContourShrinksMonotonically()
        {
            var previous = float.MaxValue;
            for (var step = 0; step <= 20; step++)
            {
                var secondsUntilBeat = 1f - step / 20f;
                var offset =
                    RhythmPoseGuideMath.EvaluateContourOffset(
                        secondsUntilBeat,
                        1f,
                        StartOffset,
                        MetOffset,
                        Linear);
                Assert.That(
                    offset,
                    Is.LessThanOrEqualTo(previous));
                previous = offset;
            }
        }

        [Test]
        public void ContourNeverSinksInsideTheGoalOutline()
        {
            // A mis-authored inspector value must not pull the approaching
            // line inside the body, which would invert the read.
            Assert.That(
                RhythmPoseGuideMath.EvaluateContourOffset(
                    0.5f,
                    1f,
                    -0.4f,
                    -0.2f,
                    Linear),
                Is.GreaterThanOrEqualTo(0f));
            Assert.That(
                RhythmPoseGuideMath.EvaluateContourOffset(
                    0f,
                    1f,
                    StartOffset,
                    -1f,
                    Linear),
                Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void ContourHoldsStartBeforeTheLeadWindow()
        {
            Assert.That(
                RhythmPoseGuideMath.EvaluateContourOffset(
                    5f,
                    1f,
                    StartOffset,
                    MetOffset,
                    Linear),
                Is.EqualTo(StartOffset).Within(0.000001f));
        }

        [Test]
        public void ContourMeetsWhenThereIsNoLeadTime()
        {
            Assert.That(
                RhythmPoseGuideMath.EvaluateContourOffset(
                    0.2f,
                    0f,
                    StartOffset,
                    MetOffset,
                    Linear),
                Is.EqualTo(MetOffset).Within(0.000001f));
        }

        [Test]
        public void VisualStateDefaultsToAMetContour()
        {
            var state = new RhythmPoseGuideVisualState(
                null,
                1f,
                0.5f,
                true,
                false);
            Assert.That(
                state.ContourOffset,
                Is.EqualTo(0f).Within(0.000001f));
        }

        [Test]
        public void VisualStateClampsContourToTheGoalOutline()
        {
            var state = new RhythmPoseGuideVisualState(
                null,
                1f,
                0.5f,
                true,
                false,
                -0.3f);
            Assert.That(
                state.ContourOffset,
                Is.EqualTo(0f).Within(0.000001f));
        }

        [Test]
        public void VisualStateKeepsTheApproachingContour()
        {
            var state = new RhythmPoseGuideVisualState(
                null,
                1f,
                0.25f,
                true,
                false,
                0.09f);
            Assert.That(
                state.ContourOffset,
                Is.EqualTo(0.09f).Within(0.000001f));
        }

        [Test]
        public void LineArtIsSeparateFromTheFilledGhost()
        {
            Assert.That(
                RhythmGuideDisplayMode.LineArt &
                RhythmGuideDisplayMode.GhostAvatar,
                Is.EqualTo(RhythmGuideDisplayMode.None));
            Assert.That(
                RhythmGuideDisplayMode.LineArt &
                RhythmGuideDisplayMode.Ring,
                Is.EqualTo(RhythmGuideDisplayMode.None));
            Assert.That(
                RhythmGuideDisplayMode.Both,
                Is.EqualTo(
                    RhythmGuideDisplayMode.Ring |
                    RhythmGuideDisplayMode.GhostAvatar));
        }
    }
}
