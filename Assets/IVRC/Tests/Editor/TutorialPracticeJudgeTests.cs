using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class TutorialPracticeJudgeTests
    {
        private TutorialPracticeJudge _judge;
        private int _successes;
        private readonly List<string> _feedback = new();

        [SetUp]
        public void SetUp()
        {
            _successes = 0;
            _feedback.Clear();
            _judge = new TutorialPracticeJudge();
            _judge.RepetitionSucceeded += _ => _successes++;
            _judge.FeedbackReady += _feedback.Add;
            Begin();
        }

        private void Begin(float beat = 0f, TrainingSideMask sides = TrainingSideMask.Both,
            float perfect = 0.08f, float maximum = 0.35f)
        {
            _judge.Begin(new TrainingRhythmBlock(BodyRepKind.ElbowFlexion,
                3, 4, 2, 0, sides), beat, 0.5f, perfect, maximum);
        }

        private void Both(RepTimingPoint point, float beat)
        {
            _judge.Report(BodyRepKind.ElbowFlexion, BodySide.Left, point, beat);
            _judge.Report(BodyRepKind.ElbowFlexion, BodySide.Right, point, beat);
        }

        [Test]
        public void BothArmsAndBothPointsProduceExactlyOneSuccess()
        {
            Both(RepTimingPoint.Up, 4f);
            Assert.That(_successes, Is.Zero);
            _judge.Report(BodyRepKind.ElbowFlexion, BodySide.Left, RepTimingPoint.Down, 6f);
            Assert.That(_successes, Is.Zero);
            _judge.Report(BodyRepKind.ElbowFlexion, BodySide.Right, RepTimingPoint.Down, 6f);
            Assert.That(_successes, Is.EqualTo(1));
            Both(RepTimingPoint.Down, 6f);
            _judge.Tick(7f);
            Assert.That(_successes, Is.EqualTo(1));
        }

        [TestCase(-0.6f, TutorialPracticeJudge.EarlyMessage)]
        [TestCase(0.6f, TutorialPracticeJudge.LateMessage)]
        public void BadTimingDoesNotCountEvenWhenMovementCompletes(float offsetBeats, string message)
        {
            Both(RepTimingPoint.Up, 4f + offsetBeats);
            Both(RepTimingPoint.Down, 6f);
            _judge.Tick(7f);
            Assert.That(_successes, Is.Zero);
            Assert.That(_feedback, Does.Contain(message));
        }

        [Test]
        public void OneArmCannotClearABothArmsBlock()
        {
            _judge.Report(BodyRepKind.ElbowFlexion, BodySide.Left, RepTimingPoint.Up, 4f);
            _judge.Report(BodyRepKind.ElbowFlexion, BodySide.Left, RepTimingPoint.Down, 6f);
            _judge.Tick(7f);
            Assert.That(_successes, Is.Zero);
            Assert.That(_feedback, Does.Contain(TutorialPracticeJudge.PoseMessage));
        }

        [Test]
        public void MissingMotionAndWrongExerciseShowPoseGuidance()
        {
            _judge.Report(BodyRepKind.ArmRaise, BodySide.Left, RepTimingPoint.Up, 4f);
            _judge.Report(BodyRepKind.ArmRaise, BodySide.Right, RepTimingPoint.Down, 6f);
            _judge.Tick(7f);
            Assert.That(_successes, Is.Zero);
            Assert.That(_feedback, Does.Contain(TutorialPracticeJudge.PoseMessage));
        }

        [Test]
        public void MissingUpCannotBeRecoveredByDownAlone()
        {
            Both(RepTimingPoint.Down, 6f);
            _judge.Tick(7f);
            Assert.That(_successes, Is.Zero);
            Both(RepTimingPoint.Up, 8f);
            Both(RepTimingPoint.Down, 10f);
            Assert.That(_successes, Is.EqualTo(1), "A failed loop must not block the next one.");
        }

        [Test]
        public void DifferentLoopsCannotBeCombined()
        {
            Both(RepTimingPoint.Up, 4f);
            _judge.Tick(7f);
            Both(RepTimingPoint.Down, 10f);
            _judge.Tick(11f);
            Assert.That(_successes, Is.Zero);
        }

        [Test]
        public void TrackingOrStepResetDiscardsPartialAttempt()
        {
            Both(RepTimingPoint.Up, 4f);
            _judge.Reset();
            Begin(5f);
            Both(RepTimingPoint.Down, 6f);
            Assert.That(_successes, Is.Zero);
            Both(RepTimingPoint.Up, 8f);
            Both(RepTimingPoint.Down, 10f);
            Assert.That(_successes, Is.EqualTo(1));
        }

        [Test]
        public void RequiredSidesAreReadFromTrainingBlock()
        {
            Begin(sides: TrainingSideMask.Right);
            _judge.Report(BodyRepKind.ElbowFlexion, BodySide.Right, RepTimingPoint.Up, 4f);
            _judge.Report(BodyRepKind.ElbowFlexion, BodySide.Right, RepTimingPoint.Down, 6f);
            Assert.That(_successes, Is.EqualTo(1));
        }

        [TestCase(0.68f, true)]
        [TestCase(0.69f, false)]
        public void UsesTrainingAccuracyThresholdWithSceneTimingWindows(float offsetSeconds, bool expected)
        {
            Begin(perfect: 0.1f, maximum: 1f);
            Both(RepTimingPoint.Up, 4f + offsetSeconds / 0.5f);
            Both(RepTimingPoint.Down, 6f + offsetSeconds / 0.5f);
            Assert.That(_successes, Is.EqualTo(expected ? 1 : 0));
        }

        [Test]
        public void ThreeSuccessfulLoopsAdvancePracticeAndFailuresDoNot()
        {
            var sequence = new TutorialSequence(new[] {
                new TutorialStep { Kind = TutorialStepKind.Practice,
                    Exercise = BodyRepKind.ElbowFlexion, RequiredReps = 3, TimeLimitSeconds = 1f }
            });
            sequence.Restart();
            _judge.RepetitionSucceeded += kind => sequence.ReportSuccessfulRep(kind);
            _judge.Tick(7f); // missed first attempt
            sequence.Tick(100f);
            Assert.That(sequence.RepsInStep, Is.Zero);
            Assert.That(sequence.IsComplete, Is.False);
            for (var beat = 8; beat <= 16; beat += 4)
            {
                Both(RepTimingPoint.Up, beat);
                Assert.That(sequence.IsComplete, Is.False);
                Both(RepTimingPoint.Down, beat + 2);
            }
            Assert.That(sequence.IsComplete, Is.True);
            Assert.That(_successes, Is.EqualTo(3));
        }

        [Test]
        public void LongPracticeKeepsCountingAcrossMusicLoops()
        {
            for (var beat = 4; beat <= 64; beat += 4)
            {
                Both(RepTimingPoint.Up, beat);
                Both(RepTimingPoint.Down, beat + 2);
            }
            Assert.That(_successes, Is.EqualTo(16));
        }

        [Test]
        public void ControllerDisplaysDiagnosisAndOnlyCountsJudgedSuccesses()
        {
            var host = new GameObject("Tutorial judged feedback test");
            host.SetActive(false);
            try
            {
                var counter = host.AddComponent<BodyRepCounter>();
                var tutorial = host.AddComponent<TutorialController>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var type = typeof(TutorialController);
                type.GetField("_repCounter", flags).SetValue(tutorial, counter);
                type.GetField("_steps", flags).SetValue(tutorial, new List<TutorialStep> {
                    new TutorialStep { Kind = TutorialStepKind.Practice,
                        Exercise = BodyRepKind.ElbowFlexion, RequiredReps = 3 },
                    new TutorialStep { Kind = TutorialStepKind.Practice,
                        Exercise = BodyRepKind.ArmRaise, RequiredReps = 3 }
                });
                type.GetField("_sequence", flags).SetValue(tutorial, null);
                host.SetActive(true);
                // EditMode does not depend on Unity invoking lifecycle methods.
                type.GetMethod("Subscribe", flags).Invoke(tutorial, null);
                tutorial.BeginTutorial();
                var rawRep = typeof(BodyRepCounter).GetField("RepCounted", flags)
                    .GetValue(counter) as System.Action<BodyRepKind, BodySide, int>;
                rawRep?.Invoke(BodyRepKind.ElbowFlexion, BodySide.Left, 10);
                Assert.That(tutorial.ProgressText, Is.EqualTo("あと 3回"));
                var judge = (TutorialPracticeJudge)type.GetField("_practiceJudge", flags).GetValue(tutorial);
                type.GetField("_practiceStep", flags).SetValue(tutorial, 0);
                judge.Begin(new TrainingRhythmBlock(BodyRepKind.ElbowFlexion, 3, 4, 2, 0),
                    0f, 0.5f, 0.08f, 0.35f);
                judge.Report(BodyRepKind.ElbowFlexion, BodySide.Left, RepTimingPoint.Up, 3.4f);
                Assert.That(tutorial.BuildHudBlock(), Does.Contain(TutorialPracticeJudge.EarlyMessage));
                judge.Tick(7f);
                Assert.That(tutorial.ProgressText, Is.EqualTo("あと 3回"));
                for (var beat = 8; beat <= 16; beat += 4)
                {
                    foreach (var side in new[] { BodySide.Left, BodySide.Right })
                        judge.Report(BodyRepKind.ElbowFlexion, side, RepTimingPoint.Up, beat);
                    foreach (var side in new[] { BodySide.Left, BodySide.Right })
                        judge.Report(BodyRepKind.ElbowFlexion, side, RepTimingPoint.Down, beat + 2);
                }
                Assert.That(tutorial.CurrentExercise, Is.EqualTo(BodyRepKind.ArmRaise));
                Assert.That(tutorial.ProgressText, Is.EqualTo("あと 3回"));
                Assert.That(tutorial.PracticeFeedback, Is.Empty);
                Assert.That(judge.IsRunning, Is.False, "Changing exercise clears the old attempt.");
            }
            finally { Object.DestroyImmediate(host); }
        }
    }
}
