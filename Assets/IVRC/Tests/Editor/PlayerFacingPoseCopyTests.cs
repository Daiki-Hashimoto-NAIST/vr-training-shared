using System;
using NUnit.Framework;

namespace IVRC.Tests
{
    public sealed class PlayerFacingPoseCopyTests
    {
        [Test]
        public void CountdownUsesJapaneseCopyAndThreePoseProgress()
        {
            var view =
                PlayerFacingPoseCopyFormatter.FormatPosing(
                    CreateState(
                        PosingRuntimeState.Countdown,
                        trackingValid: false,
                        poseIndex: 0,
                        poseCount: 3,
                        poseName: "FRONT DOUBLE BICEPS",
                        instruction:
                            "FISTS BESIDE THE HEAD - ELBOWS HIGH AND WIDE",
                        countdown: 2.1f));

            Assert.That(view.State, Is.EqualTo(
                PlayerFacingPoseDisplayState.Countdown));
            Assert.That(view.Title, Is.EqualTo("ポージング"));
            Assert.That(view.PoseName, Is.EqualTo(
                "フロント・ダブルバイセップス"));
            Assert.That(view.PoseProgress, Is.EqualTo("POSE 1/3"));
            Assert.That(view.YourLabel, Is.EqualTo("あなた"));
            Assert.That(view.ReferenceLabel, Is.EqualTo("お手本"));
            Assert.That(view.Instruction, Is.EqualTo(
                "こぶしを頭の横へ・肘を高く外へ"));
            Assert.That(view.Flow, Is.EqualTo(
                "合わせる → 保持 → 成功"));
            Assert.That(view.Action, Is.EqualTo("準備"));
            Assert.That(view.Status, Is.EqualTo("準備 3"));
            Assert.That(view.Match, Is.Empty);
            Assert.That(view.Recovery, Is.Empty);
            Assert.That(view.Safety, Is.EqualTo(
                "痛みのない範囲で、無理に腕を伸ばさない"));
            Assert.That(view.ShowFirstPoseGuide, Is.True);
            Assert.That(view.FirstPoseGuide, Is.EqualTo(
                "大きい人影＝あなた　小さい人影＝お手本\n" +
                "足はその場・腕だけ合わせる\n" +
                "合ったら、そのまま3秒"));
        }

        [Test]
        public void FirstPoseGuideDoesNotRepeatOrCoverRecovery()
        {
            var secondCountdown =
                PlayerFacingPoseCopyFormatter.FormatPosing(
                    CreateState(
                        PosingRuntimeState.Countdown,
                        trackingValid: true,
                        poseIndex: 1,
                        poseCount: 3,
                        poseName: "SINGLE BICEPS",
                        instruction: "unused",
                        countdown: 2f));
            var firstMatching =
                PlayerFacingPoseCopyFormatter.FormatPosing(
                    CreateState(
                        PosingRuntimeState.Matching,
                        trackingValid: true,
                        poseIndex: 0,
                        poseCount: 3,
                        poseName: "FRONT DOUBLE BICEPS",
                        instruction: "unused"));
            var trackingLost =
                PlayerFacingPoseCopyFormatter.FormatPosing(
                    CreateState(
                        PosingRuntimeState.TrackingLost,
                        trackingValid: false,
                        poseIndex: 0,
                        poseCount: 3,
                        poseName: "FRONT DOUBLE BICEPS",
                        instruction: "unused",
                        waitingReason: "LEFT CONTROLLER LOST"));

            Assert.That(secondCountdown.ShowFirstPoseGuide, Is.False);
            Assert.That(secondCountdown.FirstPoseGuide, Is.Empty);
            Assert.That(firstMatching.ShowFirstPoseGuide, Is.False);
            Assert.That(trackingLost.ShowFirstPoseGuide, Is.False);
            Assert.That(trackingLost.Recovery, Is.Not.Empty);
            Assert.That(trackingLost.Safety, Is.EqualTo(
                "痛みがあれば腕を下ろして止まる"));
        }

        [Test]
        public void AdaptivePoseGuideUsesExistingReferenceAndHoldRule()
        {
            Assert.That(
                PlayerFacingPoseCopyFormatter.AdaptivePoseGuide,
                Does.Contain("小さいお手本の腕を見る")
                    .And.Contain("自分の腕を同じ位置へ")
                    .And.Contain("そのまま3秒"));
        }

        [Test]
        public void AdaptivePoseGuideUsesTheActualHoldThreshold()
        {
            var profile =
                UnityEngine.ScriptableObject.CreateInstance<
                    PosingScoreProfile>();
            try
            {
                var authoredThreshold =
                    profile.ToSettings().Hold.MinimumSpatialScore;

                Assert.That(
                    ExperienceFlowHud
                        .ResolvePoseAdaptiveHintMatchThreshold(
                            profile,
                            12f),
                    Is.EqualTo(authoredThreshold)
                        .Within(0.0001f),
                    "Recovery guidance must not disappear before the " +
                    "current pose is eligible to start its hold.");
                Assert.That(
                    ExperienceFlowHud
                        .ResolvePoseAdaptiveHintMatchThreshold(
                            null,
                            123f),
                    Is.EqualTo(100f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void AdaptivePoseHintAppearsOnceAfterLowMatchDelay()
        {
            var state = new PoseAdaptiveHintState();

            Assert.That(
                state.Observe(
                    0,
                    PosingRuntimeState.Matching,
                    true,
                    20f,
                    70f,
                    3f,
                    1f),
                Is.EqualTo(PoseAdaptiveHintAction.None));
            Assert.That(
                state.Observe(
                    0,
                    PosingRuntimeState.Matching,
                    true,
                    20f,
                    70f,
                    3f,
                    2f),
                Is.EqualTo(PoseAdaptiveHintAction.Show));
            Assert.That(state.IsActive, Is.True);

            Assert.That(
                state.Observe(
                    0,
                    PosingRuntimeState.Matching,
                    true,
                    75f,
                    70f,
                    3f,
                    0.1f),
                Is.EqualTo(PoseAdaptiveHintAction.Hide));
            Assert.That(state.IsActive, Is.False);

            state.Observe(
                0,
                PosingRuntimeState.Matching,
                true,
                10f,
                70f,
                3f,
                5f);
            Assert.That(state.IsActive, Is.False,
                "The same pose must not repeat its reminder.");
        }

        [Test]
        public void AdaptivePoseHintPausesForTrackingAndHolding()
        {
            var state = new PoseAdaptiveHintState();
            state.Observe(
                0,
                PosingRuntimeState.Matching,
                true,
                10f,
                70f,
                3f,
                2f);

            Assert.That(
                state.Observe(
                    0,
                    PosingRuntimeState.TrackingLost,
                    false,
                    10f,
                    70f,
                    3f,
                    10f),
                Is.EqualTo(PoseAdaptiveHintAction.None));
            Assert.That(state.LowMatchSeconds, Is.Zero);

            state.Observe(
                0,
                PosingRuntimeState.Matching,
                true,
                10f,
                70f,
                3f,
                3f);
            Assert.That(state.IsActive, Is.True);
            Assert.That(
                state.Observe(
                    0,
                    PosingRuntimeState.Holding,
                    true,
                    80f,
                    70f,
                    3f,
                    0.1f),
                Is.EqualTo(PoseAdaptiveHintAction.Hide));
        }

        [Test]
        public void AdaptivePoseHintRearmsForEveryNewPose()
        {
            var state = new PoseAdaptiveHintState();
            state.Observe(
                0,
                PosingRuntimeState.Matching,
                true,
                0f,
                70f,
                0f,
                0f);
            state.Expire();

            Assert.That(
                state.Observe(
                    1,
                    PosingRuntimeState.Matching,
                    true,
                    0f,
                    70f,
                    0f,
                    0f),
                Is.EqualTo(PoseAdaptiveHintAction.Show));
            Assert.That(state.ActivePoseIndex, Is.EqualTo(1));
        }

        [Test]
        public void MatchingAndHoldingExposeTheNextPlayerAction()
        {
            var matching =
                PlayerFacingPoseCopyFormatter.FormatPosing(
                    CreateState(
                        PosingRuntimeState.Matching,
                        trackingValid: true,
                        poseIndex: 1,
                        poseCount: 3,
                        poseName: "SINGLE BICEPS",
                        instruction:
                            "RIGHT FIST HIGH - LEFT HAND ACROSS THE WAIST",
                        spatialScore: 81.4f));

            Assert.That(matching.PoseProgress, Is.EqualTo("POSE 2/3"));
            Assert.That(matching.Action, Is.EqualTo("合わせる"));
            Assert.That(matching.Status, Is.EqualTo(
                "お手本に合わせてください"));
            Assert.That(matching.Match, Is.Empty);
            Assert.That(matching.Hold, Is.EqualTo("保持の準備"));
            Assert.That(matching.Instruction, Is.EqualTo(
                "右こぶしを高く・左手を腰の前へ"));

            var holding =
                PlayerFacingPoseCopyFormatter.FormatPosing(
                    CreateState(
                        PosingRuntimeState.Holding,
                        trackingValid: true,
                        poseIndex: 1,
                        poseCount: 3,
                        poseName: "SINGLE BICEPS",
                        instruction: "unused",
                        spatialScore: 92f,
                        holdRemaining: 1.24f,
                        holdProgress: 0.4f));

            Assert.That(holding.Action, Is.EqualTo("保持"));
            Assert.That(holding.Status, Is.EqualTo("そのままキープ"));
            Assert.That(holding.Match, Is.Empty);
            Assert.That(holding.Hold, Is.EqualTo("あと 1.2秒"));
        }

        [TestCase("HMD TRACKING LOST",
            "頭をゆっくり動かして、ヘッドセットの認識を戻してください")]
        [TestCase("LEFT CONTROLLER LOST",
            "左コントローラーを見える位置に戻してください")]
        [TestCase("RIGHT CONTROLLER LOST",
            "右コントローラーを見える位置に戻してください")]
        [TestCase("INVALID POSE SAMPLE",
            "両手を体の前へ戻し、コントローラーを見える位置にしてください")]
        public void TrackingLossGivesConcreteRecoveryCopy(
            string waitingReason,
            string expectedRecovery)
        {
            var view =
                PlayerFacingPoseCopyFormatter.FormatPosing(
                    CreateState(
                        PosingRuntimeState.TrackingLost,
                        trackingValid: false,
                        poseIndex: 2,
                        poseCount: 3,
                        poseName: "MOST MUSCULAR",
                        instruction: "unused",
                        waitingReason: waitingReason));

            Assert.That(view.State, Is.EqualTo(
                PlayerFacingPoseDisplayState.TrackingLost));
            Assert.That(view.IsTrackingLost, Is.True);
            Assert.That(view.Action, Is.EqualTo("頭と両手を確認"));
            Assert.That(view.Status, Is.EqualTo("採点を一時停止中"));
            Assert.That(view.Recovery, Is.EqualTo(expectedRecovery));
        }

        [Test]
        public void TrackingRecoverySettlingIsDistinctFromFailure()
        {
            var view =
                PlayerFacingPoseCopyFormatter.FormatPosing(
                    CreateState(
                        PosingRuntimeState.Settling,
                        trackingValid: true,
                        poseIndex: 1,
                        poseCount: 3,
                        poseName: "SINGLE BICEPS",
                        instruction: "unused",
                        waitingReason: "TRACKING SETTLING"));

            Assert.That(view.State,
                Is.EqualTo(PlayerFacingPoseDisplayState.Settling));
            Assert.That(view.IsTrackingLost, Is.False);
            Assert.That(view.Action, Is.EqualTo("そのまま待つ"));
            Assert.That(view.Status, Is.EqualTo("認識が戻りました"));
            Assert.That(view.Match, Is.Empty);
            Assert.That(
                view.Recovery,
                Does.Contain("採点を再開")
                    .And.Contain("お待ちください"));
            Assert.That(
                view.Safety,
                Does.Contain("痛みがあれば"));
        }

        [Test]
        public void CompletedSuccessfulRoutineShowsSuccessOnlyAtTheEnd()
        {
            var view =
                PlayerFacingPoseCopyFormatter.FormatPosing(
                    CreateState(
                        PosingRuntimeState.Completed,
                        trackingValid: true,
                        poseIndex: 3,
                        poseCount: 3,
                        poseName: "MOST MUSCULAR",
                        instruction: "unused",
                        completedRoutine: true,
                        succeeded: true,
                        spatialScore: 94f));

            Assert.That(view.State, Is.EqualTo(
                PlayerFacingPoseDisplayState.Success));
            Assert.That(view.PoseProgress, Is.EqualTo("POSE 3/3"));
            Assert.That(view.Action, Is.EqualTo("成功"));
            Assert.That(view.Status, Is.EqualTo("ポーズ成功"));
            Assert.That(view.Hold, Is.EqualTo("保持完了"));
            Assert.That(view.IsSuccess, Is.True);

            var matching =
                PlayerFacingPoseCopyFormatter.FormatPosing(
                    CreateState(
                        PosingRuntimeState.Matching,
                        trackingValid: true,
                        poseIndex: 2,
                        poseCount: 3,
                        poseName: "MOST MUSCULAR",
                        instruction: "unused",
                        completedRoutine: false,
                        succeeded: false,
                        spatialScore: 100f));

            Assert.That(matching.IsSuccess, Is.False);
            Assert.That(matching.Action, Is.EqualTo("合わせる"));
        }

        [Test]
        public void CompletedPoseAcknowledgementSurvivesTheNextCountdown()
        {
            var view =
                PlayerFacingPoseCopyFormatter.FormatPoseCompletion(
                    new PoseCompletionResult(
                        0,
                        3,
                        "FRONT DOUBLE BICEPS",
                        88f,
                        true,
                        1));

            Assert.That(view.PoseProgress, Is.EqualTo("POSE 1/3"));
            Assert.That(view.PoseName, Is.EqualTo(
                "フロント・ダブルバイセップス"));
            Assert.That(view.Status, Is.EqualTo("成功！"));
            Assert.That(view.NextAction,
                Is.EqualTo("次のポーズを準備"));
            Assert.That(view.IsSuccess, Is.True);
            Assert.That(view.IsFinalPose, Is.False);
        }

        [Test]
        public void PoseTimeoutMovesOnWithoutPunitiveCopy()
        {
            var view =
                PlayerFacingPoseCopyFormatter.FormatPoseCompletion(
                    new PoseCompletionResult(
                        1,
                        3,
                        "SINGLE BICEPS",
                        42f,
                        false,
                        0));

            Assert.That(view.Status, Is.EqualTo("時間です"));
            Assert.That(view.NextAction, Is.EqualTo("次のポーズへ"));
            Assert.That(view.IsSuccess, Is.False);
            StringAssert.DoesNotContain("MISS", view.Status);
            StringAssert.DoesNotContain("失敗", view.Status);
        }

        [Test]
        public void FinalPoseAcknowledgementHandsOffToResults()
        {
            var view =
                PlayerFacingPoseCopyFormatter.FormatPoseCompletion(
                    new PoseCompletionResult(
                        2,
                        3,
                        "MOST MUSCULAR",
                        91f,
                        true,
                        3));

            Assert.That(view.Status, Is.EqualTo("成功！"));
            Assert.That(view.NextAction,
                Is.EqualTo("3ポーズ完了・結果を表示します"));
            Assert.That(view.IsFinalPose, Is.True);
        }

        [Test]
        public void UnknownPoseDoesNotInventTorsoOrLegRequirements()
        {
            var view =
                PlayerFacingPoseCopyFormatter.FormatPosing(
                    CreateState(
                        PosingRuntimeState.Matching,
                        trackingValid: true,
                        poseIndex: 0,
                        poseCount: 1,
                        poseName: "CUSTOM POSE",
                        instruction: "TWIST YOUR TORSO AND MOVE YOUR LEGS"));

            Assert.That(view.Instruction, Is.EqualTo(
                "お手本に合わせてください"));
            StringAssert.DoesNotContain("胴", view.Instruction);
            StringAssert.DoesNotContain("脚", view.Instruction);
            StringAssert.DoesNotContain("ひね", view.Instruction);

            var japaneseView =
                PlayerFacingPoseCopyFormatter.FormatPosing(
                    CreateState(
                        PosingRuntimeState.Matching,
                        trackingValid: true,
                        poseIndex: 0,
                        poseCount: 1,
                        poseName: "CUSTOM POSE",
                        instruction: "胴体をひねって脚を開いてください"));

            Assert.That(japaneseView.Instruction, Is.EqualTo(
                "お手本に合わせてください"));
        }

        [Test]
        public void ResultsShowTrainingMetricsAndPoseSuccessCount()
        {
            var result = new ExperienceScoreResult(
                88f,
                76f,
                64f,
                81.2f,
                78.1f,
                ExperienceRank.A);

            var view = PlayerFacingPoseCopyFormatter.FormatResults(
                true,
                result,
                new ExperienceScoreInput(16, 14, 0.82f, 0.64f),
                hasTrainingTimingData: true,
                poseSuccessCount: 2,
                poseCount: 3);

            Assert.That(view.HasResult, Is.True);
            Assert.That(view.Title, Is.EqualTo("結果"));
            Assert.That(view.Rank, Is.EqualTo("ランク A"));
            Assert.That(view.FinalScore, Is.EqualTo("総合 78"));
            Assert.That(view.SuccessfulReps, Is.EqualTo("成功Rep数 30"));
            Assert.That(view.Accuracy, Is.EqualTo("Accuracy 82%"));
            Assert.That(view.PoseSuccesses,
                Is.EqualTo("ポーズ成功 2 / 3"));
            StringAssert.Contains("トレーニング", view.Summary);
            StringAssert.Contains("体験終了", view.ExitInstruction);
            StringAssert.Contains("ヘッドセット", view.ExitInstruction);
            StringAssert.DoesNotContain("HMD", view.ExitInstruction);
            StringAssert.Contains("右手", view.Retry);
            StringAssert.Contains("B", view.Retry);
        }

        [Test]
        public void MissingResultsStaySafeAndRetryRemainsObvious()
        {
            var emptyResult = default(ExperienceScoreResult);
            var view =
                PlayerFacingPoseCopyFormatter.FormatResults(
                    false,
                    emptyResult,
                    default,
                    hasTrainingTimingData: false,
                    poseSuccessCount: 0,
                    poseCount: 0);

            Assert.That(view.HasResult, Is.False);
            Assert.That(view.Rank, Is.EqualTo("ランク --"));
            Assert.That(view.FinalScore, Is.EqualTo("総合 --"));
            Assert.That(
                view.ExitInstruction,
                Does.Contain("その場").And.Contain("待ち"));
            Assert.That(view.Retry,
                Is.EqualTo("もう一度遊ぶ：右手 [B]"));
        }

        [TestCase(ExperienceRank.S, "#70FFB0")]
        [TestCase(ExperienceRank.A, "#55D8FF")]
        [TestCase(ExperienceRank.B, "#FFD36A")]
        [TestCase(ExperienceRank.C, "#FFB86A")]
        [TestCase(ExperienceRank.D, "#C6B7FF")]
        public void EveryResultRankUsesAPlayerFacingNonErrorColour(
            ExperienceRank rank,
            string expected)
        {
            var colour =
                PlayerFacingResultsStyle.GetRankColor(rank);

            Assert.That(colour, Is.EqualTo(expected));
            Assert.That(
                colour,
                Is.Not.EqualTo("#FF7070"),
                "Red is reserved for a safety stop or unrecoverable error, " +
                "not an ordinary participant result.");
        }

        [Test]
        public void ResultRichTextPreservesScoreMeaningAndExitPriority()
        {
            var result = new ExperienceScoreResult(
                88f,
                76f,
                64f,
                81.2f,
                78.1f,
                ExperienceRank.A);
            var input = new ExperienceScoreInput(
                16,
                14,
                0.82f,
                0.64f);
            var view =
                PlayerFacingPoseCopyFormatter.FormatResults(
                    true,
                    result,
                    input,
                    hasTrainingTimingData: true,
                    poseSuccessCount: 2,
                    poseCount: 3);

            var richText =
                PlayerFacingResultsRichTextFormatter.Format(
                    view,
                    PlayerFacingResultsStyle.GetRankColor(
                        result.Rank));

            Assert.That(
                richText,
                Does.Contain("ランク A")
                    .And.Contain("総合 78")
                    .And.Contain("成功Rep数 30")
                    .And.Contain("Accuracy 82%")
                    .And.Contain("ポーズ成功 2 / 3")
                    .And.Contain("体験終了")
                    .And.Contain("もう一度遊ぶ"));
            Assert.That(
                richText.IndexOf("体験終了", StringComparison.Ordinal),
                Is.LessThan(
                    richText.IndexOf(
                        "もう一度遊ぶ",
                        StringComparison.Ordinal)),
                "The safe exit instruction must precede optional retry.");
        }

        [Test]
        public void ResultRevealStartsOnceAfterFrozenResultAndResetsOnExit()
        {
            var session =
                new PlayerFacingResultsRevealSession();

            session.EnterResults();
            Assert.That(session.IsInResults, Is.True);
            Assert.That(session.HasStarted, Is.False);
            Assert.That(
                session.TryStart(false, 2f),
                Is.False,
                "The reveal must wait for the frozen result.");
            Assert.That(
                session.TryStart(true, 2.5f),
                Is.True);
            Assert.That(session.HasStarted, Is.True);
            Assert.That(session.StartedAt, Is.EqualTo(2.5f));

            session.EnterResults();
            Assert.That(
                session.TryStart(true, 9f),
                Is.False,
                "Repeated phase/result notifications must not restart " +
                "the same reveal.");
            Assert.That(session.StartedAt, Is.EqualTo(2.5f));

            session.LeaveResults();
            session.EnterResults();
            Assert.That(
                session.TryStart(true, 12f),
                Is.True,
                "A new run may reveal its own frozen result.");
            Assert.That(session.StartedAt, Is.EqualTo(12f));
        }

        [Test]
        public void ResultRevealCountsScoreBeforeShowingRank()
        {
            var start = PlayerFacingResultsRevealMath.Evaluate(
                0f,
                0.6f,
                0.75f,
                78.1f);
            var rankMoment =
                PlayerFacingResultsRevealMath.Evaluate(
                    0.45f,
                    0.6f,
                    0.75f,
                    78.1f);
            var complete =
                PlayerFacingResultsRevealMath.Evaluate(
                    0.6f,
                    0.6f,
                    0.75f,
                    78.1f);

            Assert.That(start.DisplayedFinalScore, Is.Zero);
            Assert.That(start.ShowRank, Is.False);
            Assert.That(start.IsComplete, Is.False);
            Assert.That(rankMoment.DisplayedFinalScore,
                Is.EqualTo(78));
            Assert.That(rankMoment.ShowRank, Is.True);
            Assert.That(rankMoment.IsComplete, Is.False);
            Assert.That(complete.DisplayedFinalScore,
                Is.EqualTo(78));
            Assert.That(complete.ShowRank, Is.True);
            Assert.That(complete.IsComplete, Is.True);
        }

        [Test]
        public void ResultRevealMasksOnlyRankAndFinalScore()
        {
            var result = new ExperienceScoreResult(
                88f,
                76f,
                64f,
                81.2f,
                78.1f,
                ExperienceRank.A);
            var finalView =
                PlayerFacingPoseCopyFormatter.FormatResults(
                    true,
                    result,
                    default,
                    hasTrainingTimingData: false,
                    poseSuccessCount: 1,
                    poseCount: 1);
            var frame = PlayerFacingResultsRevealMath.Evaluate(
                0f,
                0.6f,
                0.75f,
                result.FinalScore);

            var revealView =
                PlayerFacingResultsRevealFormatter.Apply(
                    finalView,
                    frame);

            Assert.That(revealView.Rank,
                Is.EqualTo("ランク 判定中"));
            Assert.That(revealView.FinalScore,
                Is.EqualTo("総合 0"));
            Assert.That(revealView.SuccessfulReps,
                Is.EqualTo(finalView.SuccessfulReps));
            Assert.That(revealView.Accuracy,
                Is.EqualTo(finalView.Accuracy));
            Assert.That(revealView.PoseSuccesses,
                Is.EqualTo(finalView.PoseSuccesses));
            Assert.That(revealView.ExitInstruction,
                Is.EqualTo(finalView.ExitInstruction));
            Assert.That(revealView.Retry,
                Is.EqualTo(finalView.Retry));
        }

        [Test]
        public void ZeroDurationResultRevealCompletesImmediatelyAndSafely()
        {
            var frame = PlayerFacingResultsRevealMath.Evaluate(
                float.NaN,
                0f,
                float.NaN,
                150f);

            Assert.That(frame.NormalizedProgress, Is.EqualTo(1f));
            Assert.That(frame.DisplayedFinalScore,
                Is.EqualTo(100));
            Assert.That(frame.ShowRank, Is.True);
            Assert.That(frame.IsComplete, Is.True);
        }

        private static PlayerFacingPosingState CreateState(
            PosingRuntimeState runtimeState,
            bool trackingValid,
            int poseIndex,
            int poseCount,
            string poseName,
            string instruction,
            float countdown = 0f,
            float holdRemaining = 0f,
            float spatialScore = 0f,
            float holdProgress = 0f,
            string waitingReason = "",
            bool completedRoutine = false,
            bool succeeded = false)
        {
            return new PlayerFacingPosingState(
                runtimeState,
                trackingValid,
                completedRoutine,
                succeeded,
                poseIndex,
                poseCount,
                poseName,
                instruction,
                countdown,
                holdRemaining,
                spatialScore,
                holdProgress,
                waitingReason);
        }
    }
}
