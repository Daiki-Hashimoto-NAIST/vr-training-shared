using System.Collections.Generic;
using NUnit.Framework;

namespace IVRC.Tests
{
    public sealed class TrainingUiVisibilityTests
    {
        [TestCase(
            TrainingRhythmLanePreparationState.Countdown,
            "",
            "")]
        [TestCase(
            TrainingRhythmLanePreparationState.LeadIn,
            "",
            "")]
        [TestCase(
            TrainingRhythmLanePreparationState.Transition,
            "",
            "")]
        public void RhythmLanePreparationIsSilent(
            TrainingRhythmLanePreparationState state,
            string expectedHeading,
            string expectedInstruction)
        {
            Assert.That(
                TrainingRhythmLaneHudText.GetPreparationHeading(state),
                Is.EqualTo(expectedHeading));
            Assert.That(
                TrainingRhythmLaneHudText.GetPreparationInstruction(state),
                Is.EqualTo(expectedInstruction));
        }

        [TestCase(true, "リングが重なる瞬間に  上げる / 下ろす")]
        [TestCase(false, "線画が重なる瞬間に  上げる / 下ろす")]
        public void RhythmLaneTimingInstructionMatchesVisualMode(
            bool ringVisualsEnabled,
            string expectedInstruction)
        {
            var instruction =
                TrainingRhythmLaneHudText.GetActiveTimingInstruction(
                    ringVisualsEnabled);

            Assert.That(instruction, Is.EqualTo(expectedInstruction));
            if (!ringVisualsEnabled)
            {
                Assert.That(instruction, Does.Not.Contain("MATCH THE RING"));
            }
        }

        [Test]
        public void ActiveTrainingKeepsTheSafetyStopVisible()
        {
            var text = TrainingRhythmLaneHudText.WithSafetyFooter(
                "両手を肩へ近づける");

            Assert.That(text, Does.StartWith("両手を肩へ近づける"));
            Assert.That(
                text,
                Does.Contain(TrainingRhythmLaneHudText.SafetyInstruction));
            Assert.That(
                text,
                Does.Contain("痛み、しびれは係に報告"));
        }

        [Test]
        public void RhythmLaneTrackingRecoveryNamesConcreteStartPose()
        {
            Assert.That(
                TrainingRhythmLaneHudText.GetTrackingRecoveryInstruction(),
                Does.Contain("正面")
                    .And.Contain("両手")
                    .And.Contain("判定は止まります")
                    .And.Contain(
                        TrainingRhythmLaneHudText.SafetyInstruction));
        }

        [Test]
        public void AdaptiveHintUsesOneShortInstruction()
        {
            Assert.That(
                TrainingRhythmLaneHudText.GetAdaptiveHintHeading(
                    BodyRepKind.ElbowFlexion),
                Is.EqualTo("リズムと線画に合わせて！"));
        }

        [Test]
        public void AdaptiveHintAppearsOnceAfterTwoConsecutiveMisses()
        {
            var state = new TrainingAdaptiveHintState();

            Assert.That(
                state.ObserveTimingPoint(0, false, 2),
                Is.EqualTo(TrainingAdaptiveHintAction.None));
            Assert.That(state.ConsecutiveMisses, Is.EqualTo(1));
            Assert.That(
                state.ObserveTimingPoint(0, false, 2),
                Is.EqualTo(TrainingAdaptiveHintAction.Show));
            Assert.That(state.IsActive, Is.True);

            Assert.That(
                state.ObserveTimingPoint(0, true, 2),
                Is.EqualTo(TrainingAdaptiveHintAction.Hide));
            Assert.That(state.IsActive, Is.False);
            Assert.That(
                state.ObserveTimingPoint(0, false, 2),
                Is.EqualTo(TrainingAdaptiveHintAction.None));
            Assert.That(
                state.ObserveTimingPoint(0, false, 2),
                Is.EqualTo(TrainingAdaptiveHintAction.None),
                "The same exercise block must not nag repeatedly.");
        }

        [Test]
        public void AdaptiveHintRearmsForNextExerciseBlock()
        {
            var state = new TrainingAdaptiveHintState();
            state.ObserveTimingPoint(0, false, 2);
            state.ObserveTimingPoint(0, false, 2);
            state.Expire();

            Assert.That(
                state.ObserveTimingPoint(1, false, 2),
                Is.EqualTo(TrainingAdaptiveHintAction.None));
            Assert.That(
                state.ObserveTimingPoint(1, false, 2),
                Is.EqualTo(TrainingAdaptiveHintAction.Show));
        }

        [Test]
        public void TrackingPauseClearsMissStreakAndVisibleHint()
        {
            var state = new TrainingAdaptiveHintState();
            state.ObserveTimingPoint(0, false, 2);

            Assert.That(
                state.Suspend(),
                Is.EqualTo(TrainingAdaptiveHintAction.None));
            Assert.That(state.ConsecutiveMisses, Is.Zero);
            Assert.That(
                state.ObserveTimingPoint(0, false, 2),
                Is.EqualTo(TrainingAdaptiveHintAction.None));
            Assert.That(
                state.ObserveTimingPoint(0, false, 2),
                Is.EqualTo(TrainingAdaptiveHintAction.Show));
            Assert.That(
                state.Suspend(),
                Is.EqualTo(TrainingAdaptiveHintAction.Hide));
            Assert.That(state.IsActive, Is.False);
        }

        [TestCase(BodyRepKind.ElbowFlexion, "アームカール")]
        [TestCase(BodyRepKind.ArmRaise, "サイドレイズ")]
        public void RhythmLaneUsesPlayerFacingExerciseNames(
            BodyRepKind kind,
            string expected)
        {
            Assert.That(
                TrainingRhythmLaneHudText.GetExerciseName(kind),
                Is.EqualTo(expected));
        }

        [TestCase(RepTimingPoint.Up, "上げる")]
        [TestCase(RepTimingPoint.Down, "下ろす")]
        public void RhythmPulseUsesJapaneseActionLabels(
            RepTimingPoint point,
            string expected)
        {
            Assert.That(
                TrainingRhythmLaneHudText.GetTimingPointLabel(point),
                Is.EqualTo(expected));
        }

        [TestCase(
            TrainingTimingVisualState.Pending,
            RepTimingPoint.Up)]
        [TestCase(
            TrainingTimingVisualState.Hit,
            RepTimingPoint.Down)]
        [TestCase(
            TrainingTimingVisualState.Miss,
            RepTimingPoint.Down)]
        public void ActiveTrainingNamesTheNextRequiredAction(
            TrainingTimingVisualState upState,
            RepTimingPoint expected)
        {
            Assert.That(
                TrainingRhythmLaneHudText.GetPendingTimingPoint(
                    upState),
                Is.EqualTo(expected));
        }

        [TestCase(
            BodyRepKind.ElbowFlexion,
            true,
            true,
            "01 回目 成功\n上腕が成長！")]
        [TestCase(
            BodyRepKind.ArmRaise,
            true,
            true,
            "01 回目 成功\n肩が成長！")]
        [TestCase(
            BodyRepKind.ElbowFlexion,
            false,
            true,
            "リズムに合わせて！")]
        [TestCase(
            BodyRepKind.ArmRaise,
            false,
            true,
            "リズムに合わせて！")]
        [TestCase(
            BodyRepKind.ElbowFlexion,
            false,
            false,
            "")]
        public void RepResultCopyUsesShortSuccessAndRhythmPrompts(
            BodyRepKind kind,
            bool rhythmClear,
            bool motionComplete,
            string expected)
        {
            var up = new TrainingTimingFeedback(
                0,
                1,
                kind,
                RepTimingPoint.Up,
                rhythmClear ? 0.8f : 0f,
                rhythmClear);
            var down = new TrainingTimingFeedback(
                0,
                1,
                kind,
                RepTimingPoint.Down,
                motionComplete ? 0.8f : 0f,
                motionComplete);
            var result = new TrainingRepTimingResult(
                4,
                0,
                1,
                kind,
                TrainingSideMask.Both,
                up,
                down,
                motionComplete
                    ? TrainingRepFinalizationReason.Completed
                    : TrainingRepFinalizationReason.TimedOut);

            Assert.That(
                TrainingRhythmLaneHudText.GetRepResultText(result),
                Is.EqualTo(expected));
        }

        [Test]
        public void RhythmLanePreparationUsesChartBlockBoundaries()
        {
            var notes = new List<TrainingRhythmNote>
            {
                new TrainingRhythmNote(
                    0,
                    0,
                    0,
                    BodyRepKind.ElbowFlexion,
                    TrainingSideMask.Both,
                    4,
                    6),
                new TrainingRhythmNote(
                    1,
                    0,
                    1,
                    BodyRepKind.ElbowFlexion,
                    TrainingSideMask.Both,
                    8,
                    10),
                new TrainingRhythmNote(
                    2,
                    1,
                    0,
                    BodyRepKind.ArmRaise,
                    TrainingSideMask.Both,
                    16,
                    18)
            };

            Assert.That(
                TrainingRhythmLaneHudText.GetPreparationState(
                    notes,
                    0,
                    0f),
                Is.EqualTo(
                    TrainingRhythmLanePreparationState.LeadIn));
            Assert.That(
                TrainingRhythmLaneHudText.GetPreparationState(
                    notes,
                    0,
                    4f),
                Is.EqualTo(
                    TrainingRhythmLanePreparationState.None));
            Assert.That(
                TrainingRhythmLaneHudText.GetPreparationState(
                    notes,
                    1,
                    7f),
                Is.EqualTo(
                    TrainingRhythmLanePreparationState.None));
            Assert.That(
                TrainingRhythmLaneHudText.GetPreparationState(
                    notes,
                    2,
                    12f),
                Is.EqualTo(
                    TrainingRhythmLanePreparationState.Transition));
            Assert.That(
                TrainingRhythmLaneHudText.GetPreparationState(
                    notes,
                    2,
                    16f),
                Is.EqualTo(
                    TrainingRhythmLanePreparationState.None));
        }

        [TestCase(ExperiencePhase.Setup, true, true, false)]
        [TestCase(ExperiencePhase.Ready, true, true, false)]
        [TestCase(ExperiencePhase.Countdown, false, false, true)]
        [TestCase(ExperiencePhase.Training, false, false, true)]
        [TestCase(ExperiencePhase.Transformation, false, true, false)]
        [TestCase(ExperiencePhase.StageTransition, false, true, false)]
        [TestCase(ExperiencePhase.StageEntrance, false, false, false)]
        [TestCase(ExperiencePhase.Posing, false, false, false)]
        [TestCase(ExperiencePhase.Results, false, false, false)]
        public void PolicyKeepsDebugHudOutOfExerciseView(
            ExperiencePhase phase,
            bool bodyMetrics,
            bool flowHud,
            bool rhythmLane)
        {
            var visibility =
                TrainingUiVisibilityPolicy.ForPhase(phase);

            Assert.That(
                visibility.BodyMetrics,
                Is.EqualTo(bodyMetrics));
            Assert.That(
                visibility.FlowHud,
                Is.EqualTo(flowHud));
            Assert.That(
                visibility.RhythmLane,
                Is.EqualTo(rhythmLane));
        }
        [Test]
        public void EveryPhaseShowsSomethingOrIsDeliberatelyBlank()
        {
            // Tutorial once fell through to the default and hid every HUD
            // root, which looked exactly like the tutorial not running. Only
            // the phases that intentionally clear the view may be blank.
            var deliberatelyBlank = new[]
            {
                // The introduction draws its own wide panel instead.
                ExperiencePhase.Tutorial,
                ExperiencePhase.StageEntrance,
                ExperiencePhase.Posing,
                ExperiencePhase.Results
            };

            foreach (ExperiencePhase phase in
                     System.Enum.GetValues(typeof(ExperiencePhase)))
            {
                var visibility =
                    TrainingUiVisibilityPolicy.ForPhase(phase);
                var showsSomething =
                    visibility.BodyMetrics ||
                    visibility.FlowHud ||
                    visibility.RhythmLane;
                if (System.Array.IndexOf(deliberatelyBlank, phase) >= 0)
                {
                    continue;
                }

                Assert.That(
                    showsSomething,
                    Is.True,
                    $"{phase} hides every HUD root.");
            }
        }

        [Test]
        public void PreparationNamesTheExerciseInsteadOfCallingItPractice()
        {
            // The four beats before a block stay, but they are spent
            // reading which exercise is next rather than being told a
            // rehearsal is happening. The name goes on the announcement
            // panel; the right-hand column stops repeating it.
            Assert.That(
                TrainingRhythmLaneHudText.GetAnnouncement(
                    TrainingRhythmLanePreparationState.LeadIn,
                    BodyRepKind.ElbowFlexion),
                Is.EqualTo("まずはアームカール！"));
            Assert.That(
                TrainingRhythmLaneHudText.GetAnnouncement(
                    TrainingRhythmLanePreparationState.Transition,
                    BodyRepKind.ArmRaise),
                Is.EqualTo("つぎはサイドレイズ！"));
            Assert.That(
                TrainingRhythmLaneHudText.GetPreparationHeading(
                    TrainingRhythmLanePreparationState.LeadIn),
                Is.Empty);
        }

        [Test]
        public void EveryAnnouncementIsBuiltTheSameWay()
        {
            // A lead-in word, the thing, an exclamation. The set's last
            // line already read そろそろ時間だ！ and the exercise ones did
            // not match it.
            foreach (var line in new[]
                     {
                         TrainingRhythmLaneHudText.GetAnnouncement(
                             TrainingRhythmLanePreparationState.LeadIn,
                             BodyRepKind.ElbowFlexion),
                         TrainingRhythmLaneHudText.GetAnnouncement(
                             TrainingRhythmLanePreparationState.Transition,
                             BodyRepKind.ArmRaise),
                         TrainingAnnounceMath.LeaveHeadline
                     })
            {
                Assert.That(
                    line,
                    Does.Contain("は").Or.Contain("だ"),
                    "Each line opens with a lead-in word.");
                Assert.That(line, Does.Contain("！"));
            }
        }

        [Test]
        public void NothingIsAnnouncedOnceTheMovementIsUnderway()
        {
            Assert.That(
                TrainingRhythmLaneHudText.GetAnnouncement(
                    TrainingRhythmLanePreparationState.None,
                    BodyRepKind.ElbowFlexion),
                Is.Empty);
            Assert.That(
                TrainingRhythmLaneHudText.GetAnnouncement(
                    TrainingRhythmLanePreparationState.Countdown,
                    BodyRepKind.ElbowFlexion),
                Is.Empty);
        }

        [Test]
        public void TheSetEndsByPointingAtTheStage()
        {
            Assert.That(
                TrainingRhythmLaneHudText.CompletedAnnouncement,
                Does.Contain("ステージ"));
        }

    }
}
