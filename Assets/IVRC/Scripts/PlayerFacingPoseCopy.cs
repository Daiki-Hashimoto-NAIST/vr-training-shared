using System;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// The small set of states that a player needs to understand while
    /// posing. This is intentionally separate from the scorer's technical
    /// state so a UI cannot accidentally expose internal angle terminology.
    /// </summary>
    public enum PlayerFacingPoseDisplayState
    {
        Inactive,
        Countdown,
        Matching,
        Holding,
        TrackingLost,
        Settling,
        Unavailable,
        Complete,
        Success
    }

    public enum PoseAdaptiveHintAction
    {
        None,
        Show,
        Hide
    }

    /// <summary>
    /// One-shot, per-pose guidance state. Time advances only while the pose
    /// is actively scorable and tracking is valid, so a hardware dropout
    /// can never be presented as participant confusion.
    /// </summary>
    public sealed class PoseAdaptiveHintState
    {
        public int ActivePoseIndex { get; private set; } = -1;
        public int ShownPoseIndex { get; private set; } = -1;
        public float LowMatchSeconds { get; private set; }
        public bool IsActive { get; private set; }

        public PoseAdaptiveHintAction Observe(
            int poseIndex,
            PosingRuntimeState runtimeState,
            bool trackingValid,
            float spatialScore,
            float matchThreshold,
            float delaySeconds,
            float deltaTime)
        {
            if (poseIndex < 0)
            {
                return Suspend();
            }

            var poseChanged = ActivePoseIndex != poseIndex;
            if (poseChanged)
            {
                ActivePoseIndex = poseIndex;
                LowMatchSeconds = 0f;
                IsActive = false;
            }

            if (!trackingValid ||
                runtimeState != PosingRuntimeState.Matching)
            {
                return Suspend();
            }

            if (!IsFinite(spatialScore) ||
                spatialScore >= Mathf.Clamp(matchThreshold, 0f, 100f))
            {
                LowMatchSeconds = 0f;
                if (!IsActive)
                {
                    return PoseAdaptiveHintAction.None;
                }

                IsActive = false;
                return PoseAdaptiveHintAction.Hide;
            }

            if (ShownPoseIndex == poseIndex)
            {
                return PoseAdaptiveHintAction.None;
            }

            LowMatchSeconds += IsFinite(deltaTime)
                ? Mathf.Max(0f, deltaTime)
                : 0f;
            if (LowMatchSeconds < Mathf.Max(0f, delaySeconds))
            {
                return PoseAdaptiveHintAction.None;
            }

            ShownPoseIndex = poseIndex;
            IsActive = true;
            return PoseAdaptiveHintAction.Show;
        }

        public PoseAdaptiveHintAction Suspend()
        {
            LowMatchSeconds = 0f;
            if (!IsActive)
            {
                return PoseAdaptiveHintAction.None;
            }

            IsActive = false;
            return PoseAdaptiveHintAction.Hide;
        }

        public PoseAdaptiveHintAction Expire()
        {
            if (!IsActive)
            {
                return PoseAdaptiveHintAction.None;
            }

            IsActive = false;
            return PoseAdaptiveHintAction.Hide;
        }

        public void ResetSession()
        {
            ActivePoseIndex = -1;
            ShownPoseIndex = -1;
            LowMatchSeconds = 0f;
            IsActive = false;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// A value snapshot made only from the public state of the live scorer.
    /// Keeping this value type separate makes the copy formatter deterministic
    /// in EditMode tests and keeps UI code free to poll or subscribe later.
    /// </summary>
    public readonly struct PlayerFacingPosingState
    {
        public PlayerFacingPosingState(
            PosingRuntimeState runtimeState,
            bool isTrackingValid,
            bool hasCompletedRoutine,
            bool hasSucceeded,
            int currentPoseIndex,
            int poseCount,
            string targetDisplayName,
            string targetInstruction,
            float countdownRemaining,
            float holdRemainingSeconds,
            float spatialScore,
            float holdProgressNormalized,
            string waitingReason)
        {
            RuntimeState = runtimeState;
            IsTrackingValid = isTrackingValid;
            HasCompletedRoutine = hasCompletedRoutine;
            HasSucceeded = hasSucceeded;
            CurrentPoseIndex = currentPoseIndex;
            PoseCount = poseCount;
            TargetDisplayName = targetDisplayName ?? string.Empty;
            TargetInstruction = targetInstruction ?? string.Empty;
            CountdownRemaining = countdownRemaining;
            HoldRemainingSeconds = holdRemainingSeconds;
            SpatialScore = spatialScore;
            HoldProgressNormalized = holdProgressNormalized;
            WaitingReason = waitingReason ?? string.Empty;
        }

        public PosingRuntimeState RuntimeState { get; }
        public bool IsTrackingValid { get; }
        public bool HasCompletedRoutine { get; }
        public bool HasSucceeded { get; }
        public int CurrentPoseIndex { get; }
        public int PoseCount { get; }
        public string TargetDisplayName { get; }
        public string TargetInstruction { get; }
        public float CountdownRemaining { get; }
        public float HoldRemainingSeconds { get; }
        public float SpatialScore { get; }
        public float HoldProgressNormalized { get; }
        public string WaitingReason { get; }

        public static PlayerFacingPosingState FromScorer(
            ThreePointPosingScorer scorer)
        {
            if (scorer == null)
            {
                return new PlayerFacingPosingState(
                    PosingRuntimeState.Unavailable,
                    false,
                    false,
                    false,
                    0,
                    0,
                    "POSE",
                    "",
                    0f,
                    0f,
                    0f,
                    0f,
                    "SCORER MISSING");
            }

            return new PlayerFacingPosingState(
                scorer.State,
                scorer.IsTrackingValid,
                scorer.HasCompletedRoutine,
                scorer.HasSucceeded,
                scorer.CurrentPoseIndex,
                scorer.PoseCount,
                scorer.TargetDisplayName,
                scorer.TargetInstruction,
                scorer.CountdownRemaining,
                scorer.HoldRemainingSeconds,
                scorer.CurrentResult.SpatialScore,
                scorer.HoldProgressNormalized,
                scorer.WaitingReason);
        }
    }

    /// <summary>
    /// Player-facing posing copy. Japanese is the primary language and the
    /// fields are deliberately presentation-sized: a live UI can select the
    /// labels it needs without exposing POS/ROT/SYM or untracked body parts.
    /// </summary>
    public readonly struct PlayerFacingPosingView
    {
        public PlayerFacingPosingView(
            PlayerFacingPoseDisplayState state,
            string title,
            string poseName,
            string poseProgress,
            string yourLabel,
            string referenceLabel,
            string instruction,
            string flow,
            string action,
            string status,
            string match,
            string hold,
            string recovery,
            string safety,
            string firstPoseGuide,
            bool showFirstPoseGuide,
            bool isTrackingLost,
            bool isSuccess)
        {
            State = state;
            Title = title;
            PoseName = poseName;
            PoseProgress = poseProgress;
            YourLabel = yourLabel;
            ReferenceLabel = referenceLabel;
            Instruction = instruction;
            Flow = flow;
            Action = action;
            Status = status;
            Match = match;
            Hold = hold;
            Recovery = recovery;
            Safety = safety;
            FirstPoseGuide = firstPoseGuide;
            ShowFirstPoseGuide = showFirstPoseGuide;
            IsTrackingLost = isTrackingLost;
            IsSuccess = isSuccess;
        }

        public PlayerFacingPoseDisplayState State { get; }
        public string Title { get; }
        public string PoseName { get; }
        public string PoseProgress { get; }
        public string YourLabel { get; }
        public string ReferenceLabel { get; }
        public string Instruction { get; }
        public string Flow { get; }
        public string Action { get; }
        public string Status { get; }
        public string Match { get; }
        public string Hold { get; }
        public string Recovery { get; }
        public string Safety { get; }
        public string FirstPoseGuide { get; }
        public bool ShowFirstPoseGuide { get; }
        public bool IsTrackingLost { get; }
        public bool IsSuccess { get; }
    }

    /// <summary>
    /// A short-lived acknowledgement for the pose that just ended. It is a
    /// snapshot rather than live scorer state so the next pose can begin its
    /// countdown without erasing the acknowledgement.
    /// </summary>
    public readonly struct PlayerFacingPoseCompletionView
    {
        public PlayerFacingPoseCompletionView(
            string poseProgress,
            string poseName,
            string status,
            string nextAction,
            bool isSuccess,
            bool isFinalPose)
        {
            PoseProgress = poseProgress ?? string.Empty;
            PoseName = poseName ?? string.Empty;
            Status = status ?? string.Empty;
            NextAction = nextAction ?? string.Empty;
            IsSuccess = isSuccess;
            IsFinalPose = isFinalPose;
        }

        public string PoseProgress { get; }
        public string PoseName { get; }
        public string Status { get; }
        public string NextAction { get; }
        public bool IsSuccess { get; }
        public bool IsFinalPose { get; }
    }

    /// <summary>
    /// Player-facing copy for the frozen result screen. The score values are
    /// the same immutable values used by ExperienceResultController.
    /// </summary>
    public readonly struct PlayerFacingResultsView
    {
        public PlayerFacingResultsView(
            bool hasResult,
            string title,
            string rank,
            string finalScore,
            string successfulReps,
            string accuracy,
            string poseSuccesses,
            string summary,
            string exitInstruction,
            string retry)
        {
            HasResult = hasResult;
            Title = title;
            Rank = rank;
            FinalScore = finalScore;
            SuccessfulReps = successfulReps;
            Accuracy = accuracy;
            PoseSuccesses = poseSuccesses;
            Summary = summary;
            ExitInstruction = exitInstruction;
            Retry = retry;
        }

        public bool HasResult { get; }
        public string Title { get; }
        public string Rank { get; }
        public string FinalScore { get; }
        public string SuccessfulReps { get; }
        public string Accuracy { get; }
        public string PoseSuccesses { get; }
        public string Summary { get; }
        public string ExitInstruction { get; }
        public string Retry { get; }
    }

    /// <summary>
    /// Player-facing result colours remain celebratory at every rank. Red is
    /// reserved for safety stops and unrecoverable errors elsewhere in the
    /// experience, not for an ordinary first-time participant score.
    /// </summary>
    public static class PlayerFacingResultsStyle
    {
        public const string MissingColor = "#8CA0AE";

        public static string GetRankColor(ExperienceRank rank)
        {
            return rank switch
            {
                ExperienceRank.S => "#70FFB0",
                ExperienceRank.A => "#55D8FF",
                ExperienceRank.B => "#FFD36A",
                ExperienceRank.C => "#FFB86A",
                _ => "#C6B7FF"
            };
        }
    }

    /// <summary>
    /// One Results-phase presentation session. Entering Results arms the
    /// reveal, but the timer starts only after the frozen result exists.
    /// Repeated result notifications cannot restart the same reveal.
    /// </summary>
    public sealed class PlayerFacingResultsRevealSession
    {
        public bool IsInResults { get; private set; }
        public bool HasStarted { get; private set; }
        public float StartedAt { get; private set; }

        public void EnterResults()
        {
            if (IsInResults)
            {
                return;
            }

            IsInResults = true;
            HasStarted = false;
            StartedAt = 0f;
        }

        public bool TryStart(bool hasFrozenResult, float unscaledTime)
        {
            if (!IsInResults ||
                !hasFrozenResult ||
                HasStarted)
            {
                return false;
            }

            HasStarted = true;
            StartedAt = SanitizeTime(unscaledTime);
            return true;
        }

        public void LeaveResults()
        {
            IsInResults = false;
            HasStarted = false;
            StartedAt = 0f;
        }

        private static float SanitizeTime(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : Mathf.Max(0f, value);
        }
    }

    public readonly struct PlayerFacingResultsRevealFrame
    {
        public PlayerFacingResultsRevealFrame(
            float normalizedProgress,
            int displayedFinalScore,
            bool showRank,
            bool isComplete)
        {
            NormalizedProgress = normalizedProgress;
            DisplayedFinalScore = displayedFinalScore;
            ShowRank = showRank;
            IsComplete = isComplete;
        }

        public float NormalizedProgress { get; }
        public int DisplayedFinalScore { get; }
        public bool ShowRank { get; }
        public bool IsComplete { get; }
    }

    /// <summary>
    /// Pure time-based result reveal. It only changes the score presentation;
    /// frozen result data, lights, audio, haptics, HMD, and tracking remain
    /// untouched.
    /// </summary>
    public static class PlayerFacingResultsRevealMath
    {
        public static PlayerFacingResultsRevealFrame Evaluate(
            float elapsedSeconds,
            float durationSeconds,
            float rankRevealAtNormalized,
            float finalScore)
        {
            finalScore = SanitizeScore(finalScore);
            if (durationSeconds <= Mathf.Epsilon ||
                float.IsNaN(durationSeconds) ||
                float.IsInfinity(durationSeconds))
            {
                return new PlayerFacingResultsRevealFrame(
                    1f,
                    Mathf.RoundToInt(finalScore),
                    true,
                    true);
            }

            elapsedSeconds =
                float.IsNaN(elapsedSeconds) ||
                float.IsInfinity(elapsedSeconds)
                    ? 0f
                    : Mathf.Max(0f, elapsedSeconds);
            var progress = Mathf.Clamp01(
                elapsedSeconds / durationSeconds);
            var rankAt = Mathf.Clamp(
                rankRevealAtNormalized,
                0.05f,
                1f);
            var scoreProgress = Mathf.Clamp01(
                progress / rankAt);
            var easedScoreProgress = Mathf.SmoothStep(
                0f,
                1f,
                scoreProgress);

            return new PlayerFacingResultsRevealFrame(
                progress,
                Mathf.RoundToInt(
                    finalScore * easedScoreProgress),
                progress + 0.0001f >= rankAt,
                progress >= 1f);
        }

        private static float SanitizeScore(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : Mathf.Clamp(value, 0f, 100f);
        }
    }

    public static class PlayerFacingResultsRevealFormatter
    {
        public static PlayerFacingResultsView Apply(
            in PlayerFacingResultsView view,
            in PlayerFacingResultsRevealFrame frame)
        {
            if (!view.HasResult)
            {
                return view;
            }

            return new PlayerFacingResultsView(
                view.HasResult,
                view.Title,
                frame.ShowRank ? view.Rank : "ランク 判定中",
                $"総合 {frame.DisplayedFinalScore}",
                view.SuccessfulReps,
                view.Accuracy,
                view.PoseSuccesses,
                view.Summary,
                view.ExitInstruction,
                view.Retry);
        }
    }

    public static class PlayerFacingResultsRichTextFormatter
    {
        public static string Format(
            in PlayerFacingResultsView view,
            string rankColor)
        {
            rankColor = string.IsNullOrWhiteSpace(rankColor)
                ? PlayerFacingResultsStyle.MissingColor
                : rankColor;
            return
                $"<b>{view.Title}</b>\n" +
                $"<size=175%><color={rankColor}>{view.Rank}</color></size>　" +
                $"{view.FinalScore}\n\n" +
                $"<size=110%><b>{view.SuccessfulReps}</b>　" +
                $"<b>{view.Accuracy}</b></size>\n" +
                $"<size=110%><b><color=#70FFB0>" +
                $"{view.PoseSuccesses}</color></b></size>\n\n" +
                $"<size=70%><color=#CBD5DC>{view.Summary}</color></size>\n" +
                $"<size=105%><b>{view.ExitInstruction}</b></size>\n" +
                $"<size=72%><color=#8CA0AE>{view.Retry}</color></size>";
        }
    }

    /// <summary>
    /// Pure formatting boundary for Posing and Results. It performs no UI,
    /// scene, tracking, scoring, or avatar work; an existing presenter can
    /// call it from Update, an event handler, or a future view-model.
    /// </summary>
    public static class PlayerFacingPoseCopyFormatter
    {
        private const string PoseFlow =
            "合わせる → 保持 → 成功";
        private const string FirstPoseGuide =
            "大きい人影＝あなた　小さい人影＝お手本\n" +
            "足はその場・腕だけ合わせる\n" +
            "合ったら、そのまま3秒";

        public const string AdaptivePoseGuide =
            "小さいお手本の腕を見る\n" +
            "自分の腕を同じ位置へ合わせる\n" +
            "合ったら、そのまま3秒";

        public static PlayerFacingPosingView FormatPosing(
            ThreePointPosingScorer scorer)
        {
            return FormatPosing(
                PlayerFacingPosingState.FromScorer(scorer));
        }

        public static PlayerFacingPosingView FormatPosing(
            in PlayerFacingPosingState state)
        {
            var displayState = ResolveDisplayState(state);
            var poseName = LocalizePoseName(
                state.TargetDisplayName);
            var instruction = LocalizeInstruction(
                state.TargetDisplayName,
                state.TargetInstruction);
            var poseProgress = FormatPoseProgress(
                state.CurrentPoseIndex,
                state.PoseCount);
            var isTrackingLost =
                displayState ==
                PlayerFacingPoseDisplayState.TrackingLost;
            var isSuccess =
                displayState ==
                PlayerFacingPoseDisplayState.Success;
            var showFirstPoseGuide =
                displayState ==
                    PlayerFacingPoseDisplayState.Countdown &&
                state.CurrentPoseIndex == 0 &&
                state.PoseCount > 0;

            return new PlayerFacingPosingView(
                displayState,
                "ポージング",
                poseName,
                poseProgress,
                "あなた",
                "お手本",
                instruction,
                PoseFlow,
                FormatAction(displayState),
                FormatStatus(
                    displayState,
                    state.CountdownRemaining),
                FormatMatch(state, displayState),
                FormatHold(state, displayState),
                FormatRecovery(state, displayState),
                isTrackingLost ||
                displayState ==
                    PlayerFacingPoseDisplayState.Settling ||
                displayState ==
                    PlayerFacingPoseDisplayState.Unavailable
                    ? "痛みがあれば腕を下ろして止まる"
                    : "痛みのない範囲で、無理に腕を伸ばさない",
                showFirstPoseGuide
                    ? FirstPoseGuide
                    : string.Empty,
                showFirstPoseGuide,
                isTrackingLost,
                isSuccess);
        }

        public static PlayerFacingPoseCompletionView FormatPoseCompletion(
            in PoseCompletionResult result)
        {
            return new PlayerFacingPoseCompletionView(
                FormatPoseProgress(
                    result.PoseIndex,
                    result.PoseCount),
                LocalizePoseName(result.PoseDisplayName),
                result.Succeeded ? "成功！" : "時間です",
                result.IsFinalPose
                    ? "3ポーズ完了・結果を表示します"
                    : result.Succeeded
                        ? "次のポーズを準備"
                        : "次のポーズへ",
                result.Succeeded,
                result.IsFinalPose);
        }

        public static PlayerFacingResultsView FormatResults(
            ExperienceResultController resultController)
        {
            if (resultController == null)
            {
                return FormatMissingResults();
            }

            return FormatResults(
                resultController.HasResult,
                resultController.FinalResult,
                resultController.LastInput,
                resultController.HasTrainingTimingData,
                resultController.PoseSuccessCount,
                resultController.PoseCount);
        }

        public static PlayerFacingResultsView FormatResults(
            bool hasResult,
            in ExperienceScoreResult result,
            in ExperienceScoreInput input,
            bool hasTrainingTimingData,
            int poseSuccessCount,
            int poseCount)
        {
            if (!hasResult)
            {
                return FormatMissingResults();
            }

            return new PlayerFacingResultsView(
                true,
                "結果",
                $"ランク {FormatRank(result.Rank)}",
                $"総合 {FormatScore(result.FinalScore)}",
                FormatSuccessfulReps(input),
                FormatAccuracy(input, hasTrainingTimingData),
                FormatPoseSuccesses(poseSuccessCount, poseCount),
                "最終スコアはトレーニング成績のみで決まります",
                // Broken by hand rather than wrapped. Results is authored so
                // that no line wraps, and on one line this is 814.6px against
                // 716px of panel. The break falls after the announcement so
                // the instruction stays whole; shortening it instead would
                // have to drop either the staff cue or the word headset, and
                // both are load-bearing when a stranger is wearing it.
                "体験終了\nスタッフの案内でヘッドセットを外してください",
                "もう一度遊ぶ：右手 [B]");
        }

        private static string FormatSuccessfulReps(
            in ExperienceScoreInput input)
        {
            var reps = Math.Max(0, input.ElbowReps) +
                       Math.Max(0, input.ArmReps);
            return $"成功Rep数 {reps}";
        }

        private static string FormatAccuracy(
            in ExperienceScoreInput input,
            bool hasTrainingTimingData)
        {
            if (!hasTrainingTimingData)
            {
                return "Accuracy --";
            }

            var accuracy = Mathf.RoundToInt(
                Mathf.Clamp01(input.AverageRhythmAccuracy) * 100f);
            return $"Accuracy {accuracy}%";
        }

        private static string FormatPoseSuccesses(
            int poseSuccessCount,
            int poseCount)
        {
            poseCount = Math.Max(0, poseCount);
            poseSuccessCount = Math.Min(
                Math.Max(0, poseSuccessCount),
                poseCount);
            return $"ポーズ成功 {poseSuccessCount} / {poseCount}";
        }

        private static PlayerFacingResultsView FormatMissingResults()
        {
            return new PlayerFacingResultsView(
                false,
                "結果",
                "ランク --",
                "総合 --",
                "成功Rep数 --",
                "Accuracy --",
                "ポーズ成功 -- / --",
                "結果を準備中です",
                "結果が表示されるまで、その場でお待ちください",
                "もう一度遊ぶ：右手 [B]");
        }

        private static PlayerFacingPoseDisplayState ResolveDisplayState(
            in PlayerFacingPosingState state)
        {
            if (state.RuntimeState ==
                    PosingRuntimeState.Completed &&
                state.HasCompletedRoutine &&
                state.HasSucceeded)
            {
                return PlayerFacingPoseDisplayState.Success;
            }

            return state.RuntimeState switch
            {
                PosingRuntimeState.Countdown =>
                    PlayerFacingPoseDisplayState.Countdown,
                PosingRuntimeState.Matching =>
                    state.IsTrackingValid
                        ? PlayerFacingPoseDisplayState.Matching
                        : PlayerFacingPoseDisplayState.TrackingLost,
                PosingRuntimeState.Holding =>
                    state.IsTrackingValid
                        ? PlayerFacingPoseDisplayState.Holding
                        : PlayerFacingPoseDisplayState.TrackingLost,
                PosingRuntimeState.TrackingLost =>
                    PlayerFacingPoseDisplayState.TrackingLost,
                PosingRuntimeState.Settling =>
                    PlayerFacingPoseDisplayState.Settling,
                PosingRuntimeState.Unavailable =>
                    PlayerFacingPoseDisplayState.Unavailable,
                PosingRuntimeState.Completed =>
                    PlayerFacingPoseDisplayState.Complete,
                _ => PlayerFacingPoseDisplayState.Inactive
            };
        }

        private static string FormatPoseProgress(
            int currentPoseIndex,
            int poseCount)
        {
            if (poseCount <= 0)
            {
                return "POSE --/--";
            }

            var safeCount = Math.Max(1, poseCount);
            var safeIndex = Math.Max(
                0,
                Math.Min(currentPoseIndex, safeCount - 1));
            return $"POSE {safeIndex + 1}/{safeCount}";
        }

        private static string FormatAction(
            PlayerFacingPoseDisplayState state)
        {
            return state switch
            {
                PlayerFacingPoseDisplayState.Countdown => "準備",
                PlayerFacingPoseDisplayState.Matching => "合わせる",
                PlayerFacingPoseDisplayState.Holding => "保持",
                PlayerFacingPoseDisplayState.Success => "成功",
                PlayerFacingPoseDisplayState.TrackingLost =>
                    "頭と両手を確認",
                PlayerFacingPoseDisplayState.Settling =>
                    "そのまま待つ",
                PlayerFacingPoseDisplayState.Unavailable => "確認中",
                PlayerFacingPoseDisplayState.Complete => "完了",
                _ => "待機"
            };
        }

        private static string FormatStatus(
            PlayerFacingPoseDisplayState state,
            float countdownRemaining)
        {
            return state switch
            {
                PlayerFacingPoseDisplayState.Countdown =>
                    $"準備 {FormatCountdown(countdownRemaining)}",
                PlayerFacingPoseDisplayState.Matching =>
                    "お手本に合わせてください",
                PlayerFacingPoseDisplayState.Holding =>
                    "そのままキープ",
                PlayerFacingPoseDisplayState.Success =>
                    "ポーズ成功",
                PlayerFacingPoseDisplayState.TrackingLost =>
                    "採点を一時停止中",
                PlayerFacingPoseDisplayState.Settling =>
                    "認識が戻りました",
                PlayerFacingPoseDisplayState.Unavailable =>
                    "お手本を準備中です",
                PlayerFacingPoseDisplayState.Complete =>
                    "ポーズルーチン完了",
                _ => "ポーズを準備中です"
            };
        }

        private static string FormatMatch(
            in PlayerFacingPosingState state,
            PlayerFacingPoseDisplayState displayState)
        {
            // Matching remains live inside ThreePointPosingScorer, but its
            // numeric value is no longer player-facing score feedback.
            return string.Empty;
        }

        private static string FormatHold(
            in PlayerFacingPosingState state,
            PlayerFacingPoseDisplayState displayState)
        {
            if (displayState ==
                PlayerFacingPoseDisplayState.Holding)
            {
                return $"あと {FormatSeconds(state.HoldRemainingSeconds)}秒";
            }

            if (displayState ==
                PlayerFacingPoseDisplayState.Success)
            {
                return "保持完了";
            }

            return "保持の準備";
        }

        private static string FormatRecovery(
            in PlayerFacingPosingState state,
            PlayerFacingPoseDisplayState displayState)
        {
            if (displayState ==
                PlayerFacingPoseDisplayState.TrackingLost)
            {
                var reason = (state.WaitingReason ?? string.Empty)
                    .ToUpperInvariant();
                if (reason.Contains("HMD TRACKING LOST",
                        StringComparison.Ordinal))
                {
                    return
                        "頭をゆっくり動かして、ヘッドセットの認識を戻してください";
                }

                if (reason.Contains("LEFT CONTROLLER LOST",
                        StringComparison.Ordinal))
                {
                    return
                        "左コントローラーを見える位置に戻してください";
                }

                if (reason.Contains("RIGHT CONTROLLER LOST",
                        StringComparison.Ordinal))
                {
                    return
                        "右コントローラーを見える位置に戻してください";
                }

                if (reason.Contains("INVALID POSE SAMPLE",
                        StringComparison.Ordinal))
                {
                    return
                        "両手を体の前へ戻し、コントローラーを見える位置にしてください";
                }

                return
                    "頭と両手をゆっくり動かして、認識を戻してください";
            }

            if (displayState ==
                PlayerFacingPoseDisplayState.Settling)
            {
                return "採点を再開します。そのまま少しお待ちください";
            }

            if (displayState ==
                PlayerFacingPoseDisplayState.Unavailable)
            {
                var reason = (state.WaitingReason ?? string.Empty)
                    .ToUpperInvariant();
                if (reason.Contains("CAMERA RIG",
                        StringComparison.Ordinal))
                {
                    return "カメラを確認しています。少し待ってください";
                }

                if (reason.Contains("PROFILE",
                        StringComparison.Ordinal))
                {
                    return "お手本を準備しています。少し待ってください";
                }

                return "頭・両手・お手本を確認しています。少し待ってください";
            }

            return string.Empty;
        }

        private static string LocalizePoseName(string displayName)
        {
            var normalized = (displayName ?? string.Empty)
                .Trim()
                .ToUpperInvariant();
            if (normalized.Contains("FRONT", StringComparison.Ordinal) &&
                normalized.Contains("DOUBLE", StringComparison.Ordinal) &&
                normalized.Contains("BICEP", StringComparison.Ordinal))
            {
                return "フロント・ダブルバイセップス";
            }

            if (normalized.Contains("SINGLE", StringComparison.Ordinal) &&
                normalized.Contains("BICEP", StringComparison.Ordinal))
            {
                return "シングル・バイセップス";
            }

            if (normalized.Contains("MOST", StringComparison.Ordinal) &&
                normalized.Contains("MUSCULAR", StringComparison.Ordinal))
            {
                return "モスト・マスキュラー";
            }

            if (ContainsJapanese(displayName))
            {
                return displayName.Trim();
            }

            return string.IsNullOrWhiteSpace(displayName)
                ? "ポーズ"
                : $"ポーズ「{displayName.Trim()}」";
        }

        private static string LocalizeInstruction(
            string displayName,
            string instruction)
        {
            var normalizedName = (displayName ?? string.Empty)
                .ToUpperInvariant();
            if (normalizedName.Contains("FRONT", StringComparison.Ordinal) &&
                normalizedName.Contains("DOUBLE", StringComparison.Ordinal))
            {
                return "こぶしを頭の横へ・肘を高く外へ";
            }

            if (normalizedName.Contains("SINGLE", StringComparison.Ordinal))
            {
                return "右こぶしを高く・左手を腰の前へ";
            }

            if (normalizedName.Contains("MOST", StringComparison.Ordinal))
            {
                return "腹の前でこぶしを離す・肘を外へ";
            }

            if (ContainsJapanese(instruction) &&
                !RequestsUntrackedBody(instruction))
            {
                return instruction.Trim();
            }

            return "お手本に合わせてください";
        }

        private static bool RequestsUntrackedBody(string instruction)
        {
            var text = instruction ?? string.Empty;
            return text.Contains("脚", StringComparison.Ordinal) ||
                   text.Contains("足", StringComparison.Ordinal) ||
                   text.Contains("胴", StringComparison.Ordinal) ||
                   text.Contains("ひね", StringComparison.Ordinal) ||
                   text.Contains("ねじ", StringComparison.Ordinal) ||
                   text.Contains("肩を", StringComparison.Ordinal);
        }

        private static string FormatCountdown(float seconds)
        {
            if (!IsFinite(seconds))
            {
                return "--";
            }

            return Math.Max(0, Mathf.CeilToInt(seconds)).ToString();
        }

        private static string FormatSeconds(float seconds)
        {
            if (!IsFinite(seconds))
            {
                return "--";
            }

            return Mathf.Max(0f, seconds).ToString("0.0");
        }

        private static string FormatScore(float score)
        {
            if (!IsFinite(score))
            {
                return "0";
            }

            return Mathf.Clamp(
                    Mathf.RoundToInt(score),
                    0,
                    100)
                .ToString();
        }

        private static string FormatRank(ExperienceRank rank)
        {
            return rank switch
            {
                ExperienceRank.S => "S",
                ExperienceRank.A => "A",
                ExperienceRank.B => "B",
                ExperienceRank.C => "C",
                _ => "D"
            };
        }

        private static bool ContainsJapanese(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if ((character >= '\u3040' && character <= '\u30ff') ||
                    (character >= '\u3400' && character <= '\u9fff'))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
