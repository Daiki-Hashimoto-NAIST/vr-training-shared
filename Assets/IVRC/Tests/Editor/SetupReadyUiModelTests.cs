using NUnit.Framework;
using IVRC;

namespace IVRC.Tests
{
    public sealed class SetupReadyUiModelTests
    {
        [Test]
        public void SetupSafetyCopyExplainsBoundaryAndHowToStop()
        {
            Assert.That(
                PlayerFacingFlowHudText.SetupSafetyInstruction,
                Does.Contain("歩かない")
                    .And.Contain("周囲")
                    .And.Contain("気分が悪い")
                    .And.Contain("腕を下ろし")
                    .And.Contain("スタッフ"));
        }

        [Test]
        public void TrackingRecoveryIsTheFirstConcreteAction()
        {
            var model = SetupReadyUiModelBuilder.Build(
                new SetupReadyUiState(
                    SetupReadyPhase.Setup,
                    false,
                    false,
                    false));

            Assert.That(
                model.Checklist.Count,
                Is.EqualTo(3));
            Assert.That(
                model.Checklist[0].Step,
                Is.EqualTo(SetupReadyStep.Tracking));
            Assert.That(
                model.Tracking.State,
                Is.EqualTo(SetupReadyStepState.Active));
            Assert.That(
                model.Tracking.Label,
                Is.EqualTo("頭と両手の認識"),
                "A first-time participant should not need to understand " +
                "the technical term tracking.");
            Assert.That(
                model.Alignment.State,
                Is.EqualTo(SetupReadyStepState.Pending));
            Assert.That(
                model.Height.State,
                Is.EqualTo(SetupReadyStepState.Pending));
            Assert.That(model.IsTrackingRecovery, Is.False);
            Assert.That(
                model.MainInstruction,
                Does.Contain("プレイ位置")
                    .And.Contain("正面")
                    .And.Contain("両手")
                    .And.Contain("前へ"));
            Assert.That(
                model.RecoveryInstruction,
                Does.Not.Contain("認識できません")
                    .And.Contain("プレイ位置に立ち"));
            Assert.That(
                model.PrimaryHint.HasInput,
                Is.False);
        }

        [Test]
        public void AlignmentFollowsTrackingAndUsesRightB()
        {
            var model = SetupReadyUiModelBuilder.Build(
                new SetupReadyUiState(
                    SetupReadyPhase.Setup,
                    true,
                    false,
                    false));

            Assert.That(model.Tracking.IsComplete, Is.True);
            Assert.That(
                model.Tracking.Indicator,
                Is.EqualTo("✓"));
            Assert.That(
                model.Alignment.State,
                Is.EqualTo(SetupReadyStepState.Active));
            Assert.That(
                model.Height.State,
                Is.EqualTo(SetupReadyStepState.Pending));
            Assert.That(
                model.PrimaryHint.Hand,
                Is.EqualTo(SetupReadyControllerHand.Right));
            Assert.That(
                model.PrimaryHint.Button,
                Is.EqualTo(SetupReadyButton.B));
            Assert.That(
                model.MainInstruction,
                Does.Contain("正面")
                    .And.Contain("鏡"));
            Assert.That(
                model.Alignment.InputHint.DisplayText,
                Is.EqualTo("右手のB：正面合わせ"));
            Assert.That(
                model.PrimaryHint.DisplayText,
                Is.EqualTo("右手のB：正面合わせ"));
            Assert.That(model.ShowStartPrompt, Is.False);
        }

        [Test]
        public void HeightFollowsAlignmentAndUsesLeftX()
        {
            var model = SetupReadyUiModelBuilder.Build(
                new SetupReadyUiState(
                    SetupReadyPhase.Setup,
                    true,
                    true,
                    false));

            Assert.That(model.Tracking.IsComplete, Is.True);
            Assert.That(model.Alignment.IsComplete, Is.True);
            Assert.That(
                model.Alignment.Indicator,
                Is.EqualTo("✓"));
            Assert.That(
                model.Height.State,
                Is.EqualTo(SetupReadyStepState.Active));
            Assert.That(
                model.PrimaryHint.Hand,
                Is.EqualTo(SetupReadyControllerHand.Left));
            Assert.That(
                model.PrimaryHint.Button,
                Is.EqualTo(SetupReadyButton.X));
            Assert.That(
                model.MainInstruction,
                Does.Contain("背すじ")
                    .And.Contain("まっすぐ"));
            Assert.That(
                model.Height.InputHint.DisplayText,
                Is.EqualTo("左手のX：身長合わせ"));
        }

        [Test]
        public void AlignmentCompletionGetsOneClearAcknowledgement()
        {
            var acknowledgement =
                SetupReadyAcknowledgementBuilder.Resolve(
                    false,
                    true,
                    false,
                    false);

            Assert.That(acknowledgement.IsVisible, Is.True);
            Assert.That(
                acknowledgement.Kind,
                Is.EqualTo(
                    SetupReadyAcknowledgementKind
                        .AlignmentComplete));
            Assert.That(
                acknowledgement.Message,
                Does.Contain("✓")
                    .And.Contain("位置合わせ完了")
                    .And.Contain("次は身長合わせ"));
        }

        [Test]
        public void HeightCompletionGetsTheFinalReadyAcknowledgement()
        {
            var acknowledgement =
                SetupReadyAcknowledgementBuilder.Resolve(
                    true,
                    true,
                    false,
                    true);

            Assert.That(acknowledgement.IsVisible, Is.True);
            Assert.That(
                acknowledgement.Kind,
                Is.EqualTo(
                    SetupReadyAcknowledgementKind
                        .HeightComplete));
            Assert.That(
                acknowledgement.Message,
                Does.Contain("身長合わせ完了")
                    .And.Contain("準備できました"));
        }

        [Test]
        public void PrimedOrUnchangedSetupDoesNotInventSuccess()
        {
            Assert.That(
                SetupReadyAcknowledgementBuilder.Resolve(
                    true,
                    true,
                    true,
                    true).IsVisible,
                Is.False);
            Assert.That(
                SetupReadyAcknowledgementBuilder.Resolve(
                    false,
                    false,
                    false,
                    false).IsVisible,
                Is.False);
        }

        [Test]
        public void RealignmentAfterHeightDoesNotAskForHeightAgain()
        {
            var acknowledgement =
                SetupReadyAcknowledgementBuilder.Resolve(
                    false,
                    true,
                    true,
                    true);

            Assert.That(
                acknowledgement.Message,
                Does.Contain("位置合わせ完了")
                    .And.Contain("準備できました")
                    .And.Not.Contain("次は身長合わせ"));
        }

        [TestCase(
            SetupReadyControllerHand.Right,
            SetupReadyButton.A,
            "B",
            "A",
            false,
            true,
            "下側")]
        [TestCase(
            SetupReadyControllerHand.Right,
            SetupReadyButton.B,
            "B",
            "A",
            true,
            false,
            "上側")]
        [TestCase(
            SetupReadyControllerHand.Left,
            SetupReadyButton.X,
            "Y",
            "X",
            false,
            true,
            "下側")]
        [TestCase(
            SetupReadyControllerHand.Left,
            SetupReadyButton.Y,
            "Y",
            "X",
            true,
            false,
            "上側")]
        public void TouchButtonMapShowsPhysicalUpperAndLowerPositions(
            SetupReadyControllerHand hand,
            SetupReadyButton button,
            string expectedUpper,
            string expectedLower,
            bool expectedUpperSelected,
            bool expectedLowerSelected,
            string expectedPosition)
        {
            var hint = new SetupReadyInputHint(
                hand,
                button,
                "ACTION",
                true,
                true,
                false);

            Assert.That(hint.HasControllerButtonMap, Is.True);
            Assert.That(
                hint.UpperButtonLabel,
                Is.EqualTo(expectedUpper));
            Assert.That(
                hint.LowerButtonLabel,
                Is.EqualTo(expectedLower));
            Assert.That(
                hint.IsUpperButtonSelected,
                Is.EqualTo(expectedUpperSelected));
            Assert.That(
                hint.IsLowerButtonSelected,
                Is.EqualTo(expectedLowerSelected));
            Assert.That(
                hint.ButtonPositionLabel,
                Is.EqualTo(expectedPosition));
            Assert.That(
                hint.UpperButtonMapText,
                Does.Contain(expectedUpper)
                    .And.Contain("上"));
            Assert.That(
                hint.LowerButtonMapText,
                Does.Contain(expectedLower)
                    .And.Contain("下"));
            Assert.That(
                hint.IsUpperButtonSelected
                    ? hint.UpperButtonMapText.StartsWith(">")
                    : hint.LowerButtonMapText.StartsWith(">"),
                Is.True,
                "The selected physical button needs a non-colour marker.");
            var richText =
                SetupReadyButtonMapFormatter.FormatRichText(hint);
            Assert.That(
                richText,
                Does.Contain("#FFD36A")
                    .And.Contain(hint.HandLabel)
                    .And.Contain(expectedPosition)
                    .And.Contain($"[{hint.ButtonLabel}]")
                    .And.Contain("押す"));
        }

        [Test]
        public void ReadyShowsCompletedChecksAndRightAStart()
        {
            var model = SetupReadyUiModelBuilder.Build(
                new SetupReadyUiState(
                    SetupReadyPhase.Ready,
                    true,
                    true,
                    true));

            Assert.That(model.Title, Is.EqualTo("準備完了"));
            Assert.That(model.IsReady, Is.True);
            Assert.That(model.ShowStartPrompt, Is.True);
            Assert.That(
                model.MainInstruction,
                Does.Contain("3秒後")
                    .And.Contain("トレーニング"));
            Assert.That(
                model.StartHint.Hand,
                Is.EqualTo(SetupReadyControllerHand.Right));
            Assert.That(
                model.StartHint.Button,
                Is.EqualTo(SetupReadyButton.A));
            Assert.That(model.StartHint.IsVisible, Is.True);
            Assert.That(
                model.Checklist[0].Indicator,
                Is.EqualTo("✓"));
            Assert.That(
                model.Checklist[1].Indicator,
                Is.EqualTo("✓"));
            Assert.That(
                model.Checklist[2].Indicator,
                Is.EqualTo("✓"));
        }

        [Test]
        public void CalibrationProgressKeepsTheCurrentStepWithoutSkippingAhead()
        {
            var alignmentModel = SetupReadyUiModelBuilder.Build(
                new SetupReadyUiState(
                    SetupReadyPhase.Setup,
                    true,
                    false,
                    false,
                    alignmentInProgress: true));
            var heightModel = SetupReadyUiModelBuilder.Build(
                new SetupReadyUiState(
                    SetupReadyPhase.Setup,
                    true,
                    true,
                    false,
                    heightInProgress: true));

            Assert.That(
                alignmentModel.MainInstruction,
                Is.EqualTo("正面を向いたまま\n位置を合わせています"));
            Assert.That(
                alignmentModel.PrimaryHint.HasInput,
                Is.False,
                "計測中に同じボタンを再度促さない。");
            Assert.That(
                heightModel.MainInstruction,
                Is.EqualTo("背すじを伸ばしたまま\n身長を測っています"));
            Assert.That(
                heightModel.PrimaryHint.HasInput,
                Is.False,
                "計測中に同じボタンを再度促さない。");
            Assert.That(heightModel.ShowStartPrompt, Is.False);
        }

        [Test]
        public void FailedAlignmentExplainsRecoveryAndOffersRightBAgain()
        {
            var model = SetupReadyUiModelBuilder.Build(
                new SetupReadyUiState(
                    SetupReadyPhase.Setup,
                    true,
                    false,
                    false,
                    alignmentAttemptFailed: true));

            Assert.That(model.Title, Is.EqualTo("もう一度確認"));
            Assert.That(model.HasCalibrationFailure, Is.True);
            Assert.That(
                model.MainInstruction,
                Does.Contain("合わせられませんでした")
                    .And.Contain("正面")
                    .And.Contain("両手"));
            Assert.That(
                model.PrimaryHint.Button,
                Is.EqualTo(SetupReadyButton.B));
            Assert.That(model.ShowStartPrompt, Is.False);
        }

        [Test]
        public void FailedHeightExplainsRecoveryAndOffersLeftXAgain()
        {
            var model = SetupReadyUiModelBuilder.Build(
                new SetupReadyUiState(
                    SetupReadyPhase.Setup,
                    true,
                    true,
                    false,
                    heightAttemptFailed: true));

            Assert.That(model.Title, Is.EqualTo("もう一度確認"));
            Assert.That(model.HasCalibrationFailure, Is.True);
            Assert.That(
                model.MainInstruction,
                Does.Contain("測れませんでした")
                    .And.Contain("背すじ")
                    .And.Contain("もう一度"));
            Assert.That(
                model.PrimaryHint.Button,
                Is.EqualTo(SetupReadyButton.X));
            Assert.That(model.ShowStartPrompt, Is.False);
        }

        [Test]
        public void FinalSettlingNamesWhyStartIsNotAvailableYet()
        {
            var model = SetupReadyUiModelBuilder.Build(
                new SetupReadyUiState(
                    SetupReadyPhase.Setup,
                    true,
                    true,
                    true,
                    motionReady: false,
                    motionWaitingReason: "SETTLING"));

            Assert.That(model.Title, Is.EqualTo("最終確認"));
            Assert.That(model.IsWaitingForMotionReadiness, Is.True);
            Assert.That(model.IsReady, Is.False);
            Assert.That(model.StartHint.IsVisible, Is.False);
            Assert.That(model.PrimaryHint.HasInput, Is.False);
            Assert.That(
                model.MainInstruction,
                Does.Contain("最終確認")
                    .And.Contain("そのまま待つ"));
        }

        [Test]
        public void MissingBodyDataAvoidsTechnicalTrackingJargon()
        {
            var model = SetupReadyUiModelBuilder.Build(
                new SetupReadyUiState(
                    SetupReadyPhase.Setup,
                    true,
                    true,
                    true,
                    motionReady: false,
                    motionWaitingReason: "NO METRICS"));

            Assert.That(
                model.MainInstruction,
                Does.Contain("頭と両手")
                    .And.Contain("見える位置")
                    .And.Not.Contain("トラッキング"));
        }

        [TestCase("PRESS B", SetupReadyButton.B, "正面合わせ")]
        [TestCase("PRESS X", SetupReadyButton.X, "身長合わせ")]
        public void FinalReadinessCanRequestAConcreteCalibrationRetry(
            string reason,
            SetupReadyButton expectedButton,
            string expectedInstruction)
        {
            var model = SetupReadyUiModelBuilder.Build(
                new SetupReadyUiState(
                    SetupReadyPhase.Setup,
                    true,
                    true,
                    true,
                    motionReady: false,
                    motionWaitingReason: reason));

            Assert.That(model.PrimaryHint.Button, Is.EqualTo(expectedButton));
            Assert.That(model.PrimaryHint.IsVisible, Is.True);
            Assert.That(model.MainInstruction, Does.Contain(expectedInstruction));
            Assert.That(model.ShowStartPrompt, Is.False);
        }

        [Test]
        public void OptionalYIsAWeakAuxiliaryHint()
        {
            var model = SetupReadyUiModelBuilder.Build(
                new SetupReadyUiState(
                    SetupReadyPhase.Setup,
                    true,
                    false,
                    false,
                    avatarSwapAvailable: true));

            Assert.That(model.OptionalHint.IsVisible, Is.True);
            Assert.That(model.OptionalHint.IsOptional, Is.True);
            Assert.That(
                model.OptionalHint.Hand,
                Is.EqualTo(SetupReadyControllerHand.Left));
            Assert.That(
                model.OptionalHint.Button,
                Is.EqualTo(SetupReadyButton.Y));
            Assert.That(
                model.OptionalHint.DisplayText,
                Does.Contain("任意"));
            Assert.That(
                model.MainInstruction,
                Does.Not.Contain("Y"));

            var hiddenModel = SetupReadyUiModelBuilder.Build(
                new SetupReadyUiState(
                    SetupReadyPhase.Setup,
                    true,
                    false,
                    false,
                    avatarSwapAvailable: false));
            Assert.That(hiddenModel.OptionalHint.IsVisible, Is.False);
        }

        [Test]
        public void TrackingRecoveryDoesNotEraseCompletedChecks()
        {
            var model = SetupReadyUiModelBuilder.Build(
                new SetupReadyUiState(
                    SetupReadyPhase.Ready,
                    false,
                    true,
                    true,
                    trackingWasPreviouslyValid: true));

            Assert.That(model.IsTrackingRecovery, Is.True);
            Assert.That(model.Tracking.IsComplete, Is.False);
            Assert.That(model.Alignment.IsComplete, Is.True);
            Assert.That(model.Height.IsComplete, Is.True);
            Assert.That(model.ShowStartPrompt, Is.False);
            Assert.That(
                model.MainInstruction,
                Does.Contain("両手")
                    .And.Contain("前へ"));
            Assert.That(model.StartHint.IsVisible, Is.False);
        }

        [Test]
        public void TrackingLossUsesDistinctRecoveryCopy()
        {
            var model = SetupReadyUiModelBuilder.Build(
                new SetupReadyUiState(
                    SetupReadyPhase.Ready,
                    false,
                    true,
                    true,
                    trackingWasPreviouslyValid: true));

            Assert.That(model.IsTrackingRecovery, Is.True);
            Assert.That(
                model.RecoveryInstruction,
                Does.Contain("認識できません")
                    .And.Contain("プレイ位置に戻り")
                    .And.Contain("両手")
                    .And.Contain("前へ"));
        }

        [Test]
        public void ExperienceOverviewExplainsTheWholePayoffBeforeStart()
        {
            Assert.That(
                PlayerFacingFlowHudText.ExperienceOverview,
                Does.Contain("鍛える")
                    .And.Contain("変身")
                    .And.Contain("3ポーズ"));
        }

        [Test]
        public void PassivePhasesAlwaysTellTheParticipantWhetherToMove()
        {
            var transformation =
                PlayerFacingFlowHudText.GetPhaseMessage(
                    ExperiencePhase.Transformation,
                    StageTransitionDestination.Judges);
            var transition =
                PlayerFacingFlowHudText.GetPhaseMessage(
                    ExperiencePhase.StageTransition,
                    StageTransitionDestination.Judges);
            var entrance =
                PlayerFacingFlowHudText.GetPhaseMessage(
                    ExperiencePhase.StageEntrance,
                    StageTransitionDestination.Stage);
            var retry =
                PlayerFacingFlowHudText.GetPhaseMessage(
                    ExperiencePhase.StageTransition,
                    StageTransitionDestination.Training);

            Assert.That(
                transformation.Instruction,
                Does.Contain("その場")
                    .And.Contain("鏡"));
            Assert.That(
                transition.Instruction,
                Does.Contain("歩かず")
                    .And.Contain("正面"));
            Assert.That(
                entrance.Instruction,
                Does.Contain("歩かず")
                    .And.Contain("ステージ"));
            Assert.That(
                entrance.Detail,
                Does.Contain("3ポーズ"));
            Assert.That(
                retry.Title,
                Does.Contain("鏡"));
            Assert.That(
                retry.Detail,
                Does.Contain("最初の準備"));
        }

        [TestCase(ExperiencePhase.Setup, false)]
        [TestCase(ExperiencePhase.Countdown, false)]
        [TestCase(ExperiencePhase.Training, false)]
        [TestCase(ExperiencePhase.StageTransition, false)]
        [TestCase(ExperiencePhase.StageEntrance, false)]
        [TestCase(ExperiencePhase.Transformation, true)]
        [TestCase(ExperiencePhase.Posing, true)]
        [TestCase(ExperiencePhase.Results, true)]
        public void ProgressMeterAppearsOnlyWhenItHasAPlayerMeaning(
            ExperiencePhase phase,
            bool expected)
        {
            Assert.That(
                PlayerFacingFlowHudText.ShouldShowProgressMeter(phase),
                Is.EqualTo(expected));
        }

        [Test]
        public void TransformationCopyExplainsEachPassiveStep()
        {
            var lockMessage =
                PlayerFacingFlowHudText.GetTransformationMessage(0f);
            var revealMessage =
                PlayerFacingFlowHudText.GetTransformationMessage(0.4f);
            var heroMessage =
                PlayerFacingFlowHudText.GetTransformationMessage(1f);

            Assert.That(lockMessage.Title, Does.Contain("確定"));
            Assert.That(
                lockMessage.Instruction,
                Does.Contain("その場").And.Contain("鏡"));
            Assert.That(
                revealMessage.Title,
                Does.Contain("体").Or.Contain("チェック"));
            Assert.That(
                revealMessage.Instruction,
                Does.Contain("鏡").And.Contain("上半身"));
            // The last panel of Transformation points at the stage
            // rather than celebrating the change. The whistle and the
            // lane have already said the set is over, and what the
            // participant needs next is where to go.
            Assert.That(
                heroMessage.Title,
                Does.Contain("ステージ"));
            Assert.That(
                heroMessage.Detail,
                Does.Contain("3ポーズ"));
        }
    }
}
