using System.Collections.Generic;
using NUnit.Framework;

namespace IVRC.Tests
{
    public sealed class TutorialSequenceTests
    {
        private static TutorialStep Message(
            string text,
            float limit = 6f)
        {
            return new TutorialStep
            {
                Kind = TutorialStepKind.Message,
                Message = text,
                TimeLimitSeconds = limit
            };
        }

        private static TutorialStep Practice(
            BodyRepKind exercise,
            int reps = 3,
            float limit = 20f)
        {
            return new TutorialStep
            {
                Kind = TutorialStepKind.Practice,
                Exercise = exercise,
                RequiredReps = reps,
                TimeLimitSeconds = limit
            };
        }

        private static TutorialSequence Build(
            params TutorialStep[] steps)
        {
            var sequence = new TutorialSequence(
                new List<TutorialStep>(steps));
            sequence.Restart();
            return sequence;
        }

        [Test]
        public void ConfirmWalksThroughMessages()
        {
            var sequence = Build(
                Message("one"),
                Message("two"));

            Assert.That(sequence.CurrentStep.Message, Is.EqualTo("one"));
            Assert.That(sequence.Confirm(), Is.True);
            Assert.That(sequence.CurrentStep.Message, Is.EqualTo("two"));
            Assert.That(sequence.Confirm(), Is.True);
            Assert.That(sequence.IsComplete, Is.True);
        }

        [Test]
        public void MessagesAdvanceWithoutAnyInput()
        {
            // Some visitors never press anything. Silence must not strand
            // them on the first line.
            var sequence = Build(Message("one", 5f), Message("two", 5f));

            Assert.That(sequence.Tick(4.9f), Is.False);
            Assert.That(sequence.Tick(0.2f), Is.True);
            Assert.That(sequence.CurrentStep.Message, Is.EqualTo("two"));
        }

        [Test]
        public void PracticeAdvancesOnTheRequiredReps()
        {
            var sequence = Build(
                Practice(BodyRepKind.ElbowFlexion, 3),
                Message("done"));

            Assert.That(
                sequence.ReportRep(BodyRepKind.ElbowFlexion, BodySide.Left),
                Is.False);
            Assert.That(
                sequence.ReportRep(BodyRepKind.ElbowFlexion, BodySide.Left),
                Is.False);
            Assert.That(
                sequence.ReportRep(BodyRepKind.ElbowFlexion, BodySide.Left),
                Is.True);
            Assert.That(
                sequence.CurrentStep.Message,
                Is.EqualTo("done"));
        }

        [Test]
        public void PracticeDoesNotEndBeforeThreeSuccessfulReps()
        {
            var sequence = Build(
                Practice(BodyRepKind.ArmRaise, 3, 20f),
                Message("done"));

            Assert.That(sequence.Tick(120f), Is.False);
            Assert.That(
                sequence.CurrentStep.Kind,
                Is.EqualTo(TutorialStepKind.Practice));

            sequence.ReportRep(
                BodyRepKind.ArmRaise,
                BodySide.Left);
            sequence.ReportRep(
                BodyRepKind.ArmRaise,
                BodySide.Left);
            Assert.That(sequence.RepsInStep, Is.EqualTo(2));
            Assert.That(sequence.Tick(120f), Is.False);
            Assert.That(
                sequence.ReportRep(
                    BodyRepKind.ArmRaise,
                    BodySide.Left),
                Is.True);
            Assert.That(sequence.CurrentStep.Message, Is.EqualTo("done"));
        }

        [Test]
        public void OnlyTheTaughtMovementCounts()
        {
            // Waving the other arm around must not skip the movement being
            // demonstrated.
            var sequence = Build(
                Practice(BodyRepKind.ElbowFlexion, 2),
                Message("done"));

            sequence.ReportRep(BodyRepKind.ArmRaise, BodySide.Left);
            sequence.ReportRep(BodyRepKind.ArmRaise, BodySide.Left);
            sequence.ReportRep(BodyRepKind.ArmRaise, BodySide.Left);
            Assert.That(sequence.RepsInStep, Is.Zero);
            Assert.That(
                sequence.CurrentStep.Kind,
                Is.EqualTo(TutorialStepKind.Practice));

            sequence.ReportRep(BodyRepKind.ElbowFlexion, BodySide.Left);
            Assert.That(
                sequence.ReportRep(BodyRepKind.ElbowFlexion, BodySide.Left),
                Is.True);
        }

        [Test]
        public void ConfirmDoesNotSkipPractice()
        {
            var sequence = Build(
                Practice(BodyRepKind.ElbowFlexion, 2),
                Message("done"));

            Assert.That(sequence.Confirm(), Is.False);
            Assert.That(
                sequence.CurrentStep.Kind,
                Is.EqualTo(TutorialStepKind.Practice));
        }

        [Test]
        public void ProgressResetsBetweenSteps()
        {
            var sequence = Build(
                Practice(BodyRepKind.ElbowFlexion, 2, 20f),
                Practice(BodyRepKind.ArmRaise, 2, 20f));

            sequence.Tick(5f);
            sequence.ReportRep(BodyRepKind.ElbowFlexion, BodySide.Left);
            Assert.That(sequence.RepsInStep, Is.EqualTo(1));

            sequence.ReportRep(BodyRepKind.ElbowFlexion, BodySide.Left);
            Assert.That(sequence.RepsInStep, Is.Zero);
            Assert.That(sequence.ElapsedInStep, Is.Zero);
            Assert.That(
                sequence.CurrentStep.Exercise,
                Is.EqualTo(BodyRepKind.ArmRaise));
        }

        [Test]
        public void StaffCanSkipTheWholeIntroduction()
        {
            var sequence = Build(
                Message("one"),
                Practice(BodyRepKind.ElbowFlexion),
                Message("two"));
            var completed = 0;
            sequence.Completed += () => completed++;

            sequence.SkipAll();
            Assert.That(sequence.IsComplete, Is.True);
            Assert.That(completed, Is.EqualTo(1));
            Assert.That(sequence.SkipAll, Throws.Nothing);
            Assert.That(completed, Is.EqualTo(1));
        }

        [Test]
        public void WorstCaseIsTheSumOfEveryTimeLimit()
        {
            // This is the number the five minute budget has to be checked
            // against, not the time a confident participant takes.
            var sequence = Build(
                Message("one", 6f),
                Practice(BodyRepKind.ElbowFlexion, 3, 20f),
                Message("two", 6f),
                Practice(BodyRepKind.ArmRaise, 3, 20f));

            Assert.That(
                sequence.WorstCaseSeconds(),
                Is.EqualTo(52f).Within(0.001f));
        }

        [Test]
        public void EmptySequenceIsAlreadyDone()
        {
            var sequence = new TutorialSequence(null);
            sequence.Restart();
            Assert.That(sequence.IsComplete, Is.True);
            Assert.That(sequence.Tick(1f), Is.False);
            Assert.That(sequence.Confirm(), Is.False);
            Assert.That(
                sequence.ReportRep(BodyRepKind.ArmRaise, BodySide.Left),
                Is.False);
        }

        [Test]
        public void BrokenDeltaTimeCannotAdvanceOrStall()
        {
            var sequence = Build(Message("one", 5f));

            Assert.That(sequence.Tick(float.NaN), Is.False);
            Assert.That(sequence.Tick(float.PositiveInfinity), Is.False);
            Assert.That(sequence.Tick(-3f), Is.False);
            Assert.That(sequence.ElapsedInStep, Is.Zero);
            Assert.That(sequence.Tick(5f), Is.True);
        }

        [Test]
        public void ATooShortLimitIsRaisedRatherThanSkippingInstantly()
        {
            var sequence = Build(Message("one", 0f));
            Assert.That(sequence.Tick(0.4f), Is.False);
            Assert.That(sequence.WorstCaseSeconds(), Is.EqualTo(0.5f));
        }

        /// <summary>
        /// The rep counter fires once per arm. A participant curling both arms
        /// has done one repetition, and on the headset this counted two: a
        /// three-rep step ran 3 → 1 → done in two movements.
        /// </summary>
        [Test]
        public void CurlingBothArmsIsOneRepetition()
        {
            var sequence = Build(
                Practice(BodyRepKind.ElbowFlexion, 3),
                Message("done"));

            for (var rep = 0; rep < 2; rep++)
            {
                sequence.ReportRep(
                    BodyRepKind.ElbowFlexion,
                    BodySide.Left);
                sequence.ReportRep(
                    BodyRepKind.ElbowFlexion,
                    BodySide.Right);
            }

            Assert.That(
                sequence.RepsInStep,
                Is.EqualTo(2),
                "Two movements of both arms are two repetitions.");
            Assert.That(
                sequence.CurrentStep.Kind,
                Is.EqualTo(TutorialStepKind.Practice));

            sequence.ReportRep(
                BodyRepKind.ElbowFlexion,
                BodySide.Left);
            Assert.That(
                sequence.CurrentStep.Message,
                Is.EqualTo("done"));
        }

        [Test]
        public void TheArmsMayArriveInEitherOrder()
        {
            var sequence = Build(
                Practice(BodyRepKind.ArmRaise, 2),
                Message("done"));

            sequence.ReportRep(
                BodyRepKind.ArmRaise,
                BodySide.Right);
            sequence.ReportRep(
                BodyRepKind.ArmRaise,
                BodySide.Left);
            Assert.That(sequence.RepsInStep, Is.EqualTo(1));

            Assert.That(
                sequence.ReportRep(
                    BodyRepKind.ArmRaise,
                    BodySide.Right),
                Is.True);
        }

        /// <summary>
        /// Either arm will do, so a participant who can only move one still
        /// finishes rather than waiting out the time limit.
        /// </summary>
        [Test]
        public void OneArmAloneStillCounts()
        {
            var sequence = Build(
                Practice(BodyRepKind.ElbowFlexion, 3, 20f),
                Message("done"));

            sequence.ReportRep(
                BodyRepKind.ElbowFlexion,
                BodySide.Right);
            sequence.ReportRep(
                BodyRepKind.ElbowFlexion,
                BodySide.Right);
            Assert.That(sequence.RepsInStep, Is.EqualTo(2));

            Assert.That(
                sequence.ReportRep(
                    BodyRepKind.ElbowFlexion,
                    BodySide.Right),
                Is.True);
            Assert.That(
                sequence.CurrentStep.Message,
                Is.EqualTo("done"));
        }

        [Test]
        public void APairingInProgressDoesNotSurviveTheStep()
        {
            var sequence = Build(
                Practice(BodyRepKind.ElbowFlexion, 1, 20f),
                Practice(BodyRepKind.ElbowFlexion, 2, 20f));

            // Left opens and completes the first step while right is still
            // outstanding; the next step must not treat it as half counted.
            sequence.ReportRep(
                BodyRepKind.ElbowFlexion,
                BodySide.Left);
            Assert.That(sequence.RepsInStep, Is.Zero);

            sequence.ReportRep(
                BodyRepKind.ElbowFlexion,
                BodySide.Right);
            Assert.That(
                sequence.RepsInStep,
                Is.EqualTo(1),
                "The new step starts a fresh pairing, so this counts.");
        }

        [Test]
        public void StepEnteredFiresOncePerStep()
        {
            var entered = new List<int>();
            var sequence = new TutorialSequence(
                new List<TutorialStep>
                {
                    Message("one"),
                    Message("two")
                });
            sequence.StepEntered += entered.Add;
            sequence.Restart();
            sequence.Confirm();

            Assert.That(entered, Is.EqualTo(new[] { 0, 1 }));
        }
    }
}
