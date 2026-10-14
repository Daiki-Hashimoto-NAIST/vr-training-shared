using NUnit.Framework;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class TutorialPoseGuideTests
    {
        /// <summary>
        /// A practice step wants a target to move towards; a line to read does
        /// not. Showing a pose during the talking would ask the participant to
        /// act while they are being told what the action is.
        /// </summary>
        [Test]
        public void OnlyPracticeStepsWantAPose()
        {
            var host = new GameObject("TutorialStepKindTest");
            try
            {
                var tutorial =
                    host.AddComponent<TutorialController>();
                SetSteps(
                    tutorial,
                    Message("one"),
                    Practice(BodyRepKind.ElbowFlexion, 3),
                    Message("two"));
                tutorial.BeginTutorial();

                Assert.That(
                    tutorial.CurrentStepIsPractice,
                    Is.False,
                    "The first step is a line.");

                tutorial.SkipAll();
                Assert.That(
                    tutorial.CurrentStepIsPractice,
                    Is.False,
                    "Nothing is being taught once it is over.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TheExerciseComesFromTheCurrentStep()
        {
            var host = new GameObject("TutorialExerciseTest");
            try
            {
                var tutorial =
                    host.AddComponent<TutorialController>();
                SetSteps(
                    tutorial,
                    Practice(BodyRepKind.ArmRaise, 2));
                tutorial.BeginTutorial();

                Assert.That(
                    tutorial.CurrentStepIsPractice,
                    Is.True);
                Assert.That(
                    tutorial.CurrentExercise,
                    Is.EqualTo(BodyRepKind.ArmRaise));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void LoopStartsOnTheUpTarget()
        {
            Assert.That(
                LoopingRhythmPoseSchedule.TrySample(
                    0f,
                    0.5f,
                    4,
                    2,
                    0.15f,
                    out var sample),
                Is.True);

            Assert.That(
                sample.TimingPoint,
                Is.EqualTo(RepTimingPoint.Up));
            Assert.That(sample.IsBeatWindow, Is.True);
            Assert.That(sample.SecondsUntilBeat, Is.Zero.Within(0.001f));
        }

        [Test]
        public void LoopApproachesDownThenReturnsToUp()
        {
            LoopingRhythmPoseSchedule.TrySample(
                1f,
                0.5f,
                4,
                2,
                0.15f,
                out var down);
            Assert.That(
                down.TimingPoint,
                Is.EqualTo(RepTimingPoint.Down));
            Assert.That(down.IsBeatWindow, Is.False);
            Assert.That(
                down.SecondsUntilBeat,
                Is.EqualTo(0.5f).Within(0.001f));

            LoopingRhythmPoseSchedule.TrySample(
                4f,
                0.5f,
                4,
                2,
                0.15f,
                out var nextRep);
            Assert.That(
                nextRep.TimingPoint,
                Is.EqualTo(RepTimingPoint.Up));
            Assert.That(nextRep.IsBeatWindow, Is.True);
        }

        [Test]
        public void LongTutorialStillUsesTheSameRepeatingSchedule()
        {
            LoopingRhythmPoseSchedule.TrySample(
                1.25f,
                0.5f,
                4,
                2,
                0.15f,
                out var first);
            LoopingRhythmPoseSchedule.TrySample(
                401.25f,
                0.5f,
                4,
                2,
                0.15f,
                out var muchLater);

            Assert.That(
                muchLater.TimingPoint,
                Is.EqualTo(first.TimingPoint));
            Assert.That(
                muchLater.SecondsUntilBeat,
                Is.EqualTo(first.SecondsUntilBeat).Within(0.001f));
        }

        private static TutorialStep Message(string text)
        {
            return new TutorialStep
            {
                Kind = TutorialStepKind.Message,
                Message = text,
                RequiredReps = 1,
                TimeLimitSeconds = 5f
            };
        }

        private static TutorialStep Practice(
            BodyRepKind exercise,
            int reps)
        {
            return new TutorialStep
            {
                Kind = TutorialStepKind.Practice,
                Exercise = exercise,
                RequiredReps = reps,
                TimeLimitSeconds = 15f
            };
        }

        private static void SetSteps(
            TutorialController tutorial,
            params TutorialStep[] steps)
        {
            var so = new UnityEditor.SerializedObject(tutorial);
            var list = so.FindProperty("_steps");
            list.arraySize = steps.Length;
            for (var index = 0; index < steps.Length; index++)
            {
                var element =
                    list.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("Kind")
                    .enumValueIndex = (int)steps[index].Kind;
                element.FindPropertyRelative("Message")
                    .stringValue = steps[index].Message ?? string.Empty;
                element.FindPropertyRelative("Exercise")
                    .enumValueIndex = (int)steps[index].Exercise;
                element.FindPropertyRelative("RequiredReps")
                    .intValue = steps[index].RequiredReps;
                element.FindPropertyRelative("TimeLimitSeconds")
                    .floatValue = steps[index].TimeLimitSeconds;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
