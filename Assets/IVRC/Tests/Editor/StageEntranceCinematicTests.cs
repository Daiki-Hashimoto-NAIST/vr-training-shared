using NUnit.Framework;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class StageEntranceCinematicTests
    {
        [TestCase(-1f, 0f)]
        [TestCase(0f, 0f)]
        [TestCase(2.5f, 0.5f)]
        [TestCase(5f, 1f)]
        [TestCase(10f, 1f)]
        public void WalkProgressIsClampedAndSmoothed(
            float elapsed,
            float expected)
        {
            Assert.That(
                StageEntranceCinematicMath
                    .EvaluateWalkProgress(elapsed, 5f),
                Is.EqualTo(expected).Within(0.000001f));
        }

        [Test]
        public void GaitFadesAtBothEnds()
        {
            Assert.That(
                StageEntranceCinematicMath
                    .EvaluateGaitStrength(0f),
                Is.Zero);
            Assert.That(
                StageEntranceCinematicMath
                    .EvaluateGaitStrength(0.5f),
                Is.EqualTo(1f).Within(0.000001f));
            Assert.That(
                StageEntranceCinematicMath
                    .EvaluateGaitStrength(1f),
                Is.Zero);
        }

        [Test]
        public void GaitPhaseTreatsEachHalfCycleAsOneStep()
        {
            Assert.That(
                StageEntranceCinematicMath
                    .EvaluateGaitPhase(1f, 1.8f),
                Is.EqualTo(Mathf.PI * 1.8f)
                    .Within(0.000001f));
            Assert.That(
                StageEntranceCinematicMath
                    .EvaluateGaitPhase(-1f, 1.8f),
                Is.Zero);
        }

        [TestCase(0f, 5f, 1f, 1f, 0f)]
        [TestCase(0.25f, 5f, 2f, 1f, 0.5f)]
        [TestCase(0.75f, 5f, 2f, 1f, 0.5f)]
        [TestCase(5f, 5f, 2f, 1f, 0f)]
        [TestCase(10f, 5f, 2f, 1f, 0f)]
        public void WalkAnimationTimeLoopsAndStopsAtWalkEnd(
            float elapsed,
            float walkSeconds,
            float playbackSpeed,
            float clipLength,
            float expected)
        {
            Assert.That(
                StageEntranceCinematicMath
                    .EvaluateWalkAnimationTime(
                        elapsed,
                        walkSeconds,
                        playbackSpeed,
                        clipLength),
                Is.EqualTo(expected).Within(0.000001f));
        }

        [TestCase(float.NaN, 5f, 1f, 1f)]
        [TestCase(1f, 0f, 1f, 1f)]
        [TestCase(1f, 5f, 0f, 1f)]
        [TestCase(1f, 5f, 1f, 0f)]
        public void InvalidWalkAnimationTimingReturnsZero(
            float elapsed,
            float walkSeconds,
            float playbackSpeed,
            float clipLength)
        {
            Assert.That(
                StageEntranceCinematicMath
                    .EvaluateWalkAnimationTime(
                        elapsed,
                        walkSeconds,
                        playbackSpeed,
                        clipLength),
                Is.Zero);
        }

        [TestCase(0f, 0)]
        [TestCase(2.299f, 0)]
        [TestCase(2.3f, 1)]
        [TestCase(4.399f, 1)]
        [TestCase(4.4f, 2)]
        public void CameraCutsHaveStableBoundaries(
            float elapsed,
            int expectedShot)
        {
            Assert.That(
                StageEntranceCinematicMath.SelectShot(
                    elapsed,
                    2.3f,
                    2.1f),
                Is.EqualTo(expectedShot));
        }

        [TestCase(true, true, false,
            StageTransitionDestination.Stage, true)]
        [TestCase(true, true, true,
            StageTransitionDestination.Stage, false)]
        [TestCase(true, false, false,
            StageTransitionDestination.Stage, false)]
        [TestCase(true, true, false,
            StageTransitionDestination.Judges, false)]
        [TestCase(false, true, false,
            StageTransitionDestination.Stage, false)]
        public void OutgoingStageTransitionKeepsAvatarUntilRelocation(
            bool isRunning,
            bool isTransitioning,
            bool hasRelocated,
            StageTransitionDestination destination,
            bool expected)
        {
            Assert.That(
                StageEntranceCinematicMath
                    .ShouldKeepCinematicDuringTransition(
                        isRunning,
                        isTransitioning,
                        hasRelocated,
                        destination),
                Is.EqualTo(expected));
        }

        [Test]
        public void WalkPathStartsAndEndsExactlyAtAnchors()
        {
            var start = new Vector3(-3.35f, 0f, -2.15f);
            var middle = new Vector3(-1.75f, 0f, -0.9f);
            var end = Vector3.zero;

            Assert.That(
                StageEntranceCinematicMath
                    .EvaluateQuadraticPath(
                        start,
                        middle,
                        end,
                        0f),
                Is.EqualTo(start));
            Assert.That(
                StageEntranceCinematicMath
                    .EvaluateQuadraticPath(
                        start,
                        middle,
                        end,
                        1f),
                Is.EqualTo(end));
        }
    }
}
