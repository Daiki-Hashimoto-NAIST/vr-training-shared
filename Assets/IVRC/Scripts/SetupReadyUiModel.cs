using System;
using System.Collections.Generic;

namespace IVRC
{

/// <summary>
/// The two controller hands used by the setup checklist.
/// </summary>
public enum SetupReadyControllerHand
{
    None,
    Left,
    Right
}

/// <summary>
/// The controller buttons used by the setup checklist.
/// </summary>
public enum SetupReadyButton
{
    None,
    A,
    B,
    X,
    Y
}

/// <summary>
/// The phases that can show the setup/ready checklist.
/// </summary>
public enum SetupReadyPhase
{
    Setup,
    Ready
}

/// <summary>
/// The ordered checks shown before the workout starts.
/// </summary>
public enum SetupReadyStep
{
    Tracking,
    Alignment,
    Height
}

/// <summary>
/// Display state for one setup checklist row.
/// </summary>
public enum SetupReadyStepState
{
    Pending,
    Active,
    Complete
}

/// <summary>
/// The input and status data needed to build the setup/ready display.
/// This type intentionally contains no Unity or controller references.
/// </summary>
public readonly struct SetupReadyUiState
{
    public SetupReadyUiState(
        SetupReadyPhase phase,
        bool trackingValid,
        bool alignmentComplete,
        bool heightComplete,
        bool alignmentInProgress = false,
        bool heightInProgress = false,
        bool avatarSwapAvailable = true,
        bool trackingWasPreviouslyValid = false,
        bool motionReady = true,
        string motionWaitingReason = "",
        bool alignmentAttemptFailed = false,
        bool heightAttemptFailed = false)
    {
        Phase = phase;
        TrackingValid = trackingValid;
        AlignmentComplete = alignmentComplete;
        HeightComplete = heightComplete;
        AlignmentInProgress = alignmentInProgress;
        HeightInProgress = heightInProgress;
        AvatarSwapAvailable = avatarSwapAvailable;
        TrackingWasPreviouslyValid = trackingWasPreviouslyValid;
        MotionReady = motionReady;
        MotionWaitingReason = motionWaitingReason ?? string.Empty;
        AlignmentAttemptFailed = alignmentAttemptFailed;
        HeightAttemptFailed = heightAttemptFailed;
    }

    public SetupReadyPhase Phase { get; }
    public bool TrackingValid { get; }
    public bool AlignmentComplete { get; }
    public bool HeightComplete { get; }
    public bool AlignmentInProgress { get; }
    public bool HeightInProgress { get; }
    public bool AvatarSwapAvailable { get; }
    public bool TrackingWasPreviouslyValid { get; }
    public bool MotionReady { get; }
    public string MotionWaitingReason { get; }
    public bool AlignmentAttemptFailed { get; }
    public bool HeightAttemptFailed { get; }
}

/// <summary>
/// A controller hint that a renderer can place next to a checklist row.
/// </summary>
public readonly struct SetupReadyInputHint
{
    public SetupReadyInputHint(
        SetupReadyControllerHand hand,
        SetupReadyButton button,
        string actionText,
        bool isVisible,
        bool isPrimary,
        bool isOptional)
    {
        Hand = hand;
        Button = button;
        ActionText = actionText ?? string.Empty;
        IsVisible = isVisible;
        IsPrimary = isPrimary;
        IsOptional = isOptional;
    }

    public SetupReadyControllerHand Hand { get; }
    public SetupReadyButton Button { get; }
    public string ActionText { get; }
    public bool IsVisible { get; }
    public bool IsPrimary { get; }
    public bool IsOptional { get; }

    public bool HasInput =>
        Hand != SetupReadyControllerHand.None &&
        Button != SetupReadyButton.None;

    public string HandLabel
    {
        get
        {
            switch (Hand)
            {
                case SetupReadyControllerHand.Left:
                    return "左手";
                case SetupReadyControllerHand.Right:
                    return "右手";
                default:
                    return string.Empty;
            }
        }
    }

    public string ButtonLabel
    {
        get
        {
            switch (Button)
            {
                case SetupReadyButton.A:
                    return "A";
                case SetupReadyButton.B:
                    return "B";
                case SetupReadyButton.X:
                    return "X";
                case SetupReadyButton.Y:
                    return "Y";
                default:
                    return string.Empty;
            }
        }
    }

    /// <summary>
    /// The two face-button labels as they are physically arranged on the
    /// selected Touch controller. This gives a first-time participant a
    /// useful location cue without requiring a controller texture asset.
    /// </summary>
    public string UpperButtonLabel =>
        Hand == SetupReadyControllerHand.Left
            ? "Y"
            : Hand == SetupReadyControllerHand.Right
                ? "B"
                : string.Empty;

    public string LowerButtonLabel =>
        Hand == SetupReadyControllerHand.Left
            ? "X"
            : Hand == SetupReadyControllerHand.Right
                ? "A"
                : string.Empty;

    public bool IsUpperButtonSelected =>
        (Hand == SetupReadyControllerHand.Left &&
         Button == SetupReadyButton.Y) ||
        (Hand == SetupReadyControllerHand.Right &&
         Button == SetupReadyButton.B);

    public bool IsLowerButtonSelected =>
        (Hand == SetupReadyControllerHand.Left &&
         Button == SetupReadyButton.X) ||
        (Hand == SetupReadyControllerHand.Right &&
         Button == SetupReadyButton.A);

    public string ButtonPositionLabel =>
        IsUpperButtonSelected
            ? "上側"
            : IsLowerButtonSelected
                ? "下側"
                : string.Empty;

    public bool HasControllerButtonMap =>
        !string.IsNullOrEmpty(UpperButtonLabel) &&
        !string.IsNullOrEmpty(LowerButtonLabel);

    public string UpperButtonMapText =>
        HasControllerButtonMap
            ? string.Format(
                "{0}上[{1}]",
                IsUpperButtonSelected ? ">" : string.Empty,
                UpperButtonLabel)
            : string.Empty;

    public string LowerButtonMapText =>
        HasControllerButtonMap
            ? string.Format(
                "{0}下[{1}]",
                IsLowerButtonSelected ? ">" : string.Empty,
                LowerButtonLabel)
            : string.Empty;

    public string DisplayText
    {
        get
        {
            if (!HasInput)
            {
                return string.Empty;
            }

            return string.Format(
                "{0}の{1}：{2}",
                HandLabel,
                ButtonLabel,
                ActionText);
        }
    }

    public static SetupReadyInputHint Hidden
    {
        get
        {
            return new SetupReadyInputHint(
                SetupReadyControllerHand.None,
                SetupReadyButton.None,
                string.Empty,
                false,
                false,
                false);
        }
    }
}

/// <summary>
/// One row in the ordered tracking/alignment/height checklist.
/// </summary>
public readonly struct SetupReadyChecklistItem
{
    public SetupReadyChecklistItem(
        SetupReadyStep step,
        SetupReadyStepState state,
        string label,
        SetupReadyInputHint inputHint)
    {
        Step = step;
        State = state;
        Label = label ?? string.Empty;
        InputHint = inputHint;
    }

    public SetupReadyStep Step { get; }
    public SetupReadyStepState State { get; }
    public string Label { get; }
    public SetupReadyInputHint InputHint { get; }
    public bool IsComplete => State == SetupReadyStepState.Complete;

    public string Indicator
    {
        get
        {
            switch (State)
            {
                case SetupReadyStepState.Active:
                    return "→";
                case SetupReadyStepState.Complete:
                    return "✓";
                default:
                    return "○";
            }
        }
    }

    public string DisplayText =>
        string.Format("{0} {1}", Indicator, Label);
}

/// <summary>
/// Compact, colour-plus-marker button map used by the world-space Setup HUD.
/// Keeping this pure lets the actual Japanese font and HUD width be verified
/// by the playable-scene tests instead of relying on visual guesswork.
/// </summary>
public static class SetupReadyButtonMapFormatter
{
    public static string FormatRichText(SetupReadyInputHint hint)
    {
        if (!hint.IsVisible ||
            !hint.HasInput ||
            !hint.HasControllerButtonMap)
        {
            return string.Empty;
        }

        return
            "<size=90%><b>" +
            $"<color=#8CA0AE>{hint.HandLabel}の" +
            $"{hint.ButtonPositionLabel} </color>" +
            $"<color=#FFD36A>[{hint.ButtonLabel}]</color>を押す　" +
            $"{hint.ActionText}" +
            "</b></size>\n";
    }
}

/// <summary>
/// Pure display data for the Setup and Ready phases.
/// </summary>
public readonly struct SetupReadyUiModel
{
    internal SetupReadyUiModel(
        SetupReadyPhase phase,
        string title,
        string mainInstruction,
        string recoveryInstruction,
        bool isTrackingRecovery,
        bool hasCalibrationFailure,
        bool isCalibrationInProgress,
        bool isWaitingForMotionReadiness,
        bool isReady,
        bool showStartPrompt,
        SetupReadyChecklistItem tracking,
        SetupReadyChecklistItem alignment,
        SetupReadyChecklistItem height,
        SetupReadyInputHint primaryHint,
        SetupReadyInputHint startHint,
        SetupReadyInputHint optionalHint)
    {
        Phase = phase;
        Title = title ?? string.Empty;
        MainInstruction = mainInstruction ?? string.Empty;
        RecoveryInstruction = recoveryInstruction ?? string.Empty;
        IsTrackingRecovery = isTrackingRecovery;
        HasCalibrationFailure = hasCalibrationFailure;
        IsCalibrationInProgress = isCalibrationInProgress;
        IsWaitingForMotionReadiness = isWaitingForMotionReadiness;
        IsReady = isReady;
        ShowStartPrompt = showStartPrompt;
        Tracking = tracking;
        Alignment = alignment;
        Height = height;
        PrimaryHint = primaryHint;
        StartHint = startHint;
        OptionalHint = optionalHint;

        Checklist = new[]
        {
            Tracking,
            Alignment,
            Height
        };
    }

    public SetupReadyPhase Phase { get; }
    public string Title { get; }
    public string MainInstruction { get; }
    public string RecoveryInstruction { get; }
    public bool IsTrackingRecovery { get; }
    public bool HasCalibrationFailure { get; }
    public bool IsCalibrationInProgress { get; }
    public bool IsWaitingForMotionReadiness { get; }
    public bool IsReady { get; }
    public bool ShowStartPrompt { get; }
    public SetupReadyChecklistItem Tracking { get; }
    public SetupReadyChecklistItem Alignment { get; }
    public SetupReadyChecklistItem Height { get; }
    public SetupReadyInputHint PrimaryHint { get; }
    public SetupReadyInputHint StartHint { get; }
    public SetupReadyInputHint OptionalHint { get; }
    public IReadOnlyList<SetupReadyChecklistItem> Checklist { get; }
}

/// <summary>
/// The two calibration actions that need a short, unambiguous acceptance
/// message before the setup HUD advances to the next instruction.
/// </summary>
public enum SetupReadyAcknowledgementKind
{
    None,
    AlignmentComplete,
    HeightComplete
}

/// <summary>
/// Player-facing one-shot feedback for a newly completed calibration step.
/// Timing is owned by the presenter; this value only describes the message.
/// </summary>
public readonly struct SetupReadyAcknowledgement
{
    public SetupReadyAcknowledgement(
        SetupReadyAcknowledgementKind kind,
        string message)
    {
        Kind = kind;
        Message = message ?? string.Empty;
    }

    public SetupReadyAcknowledgementKind Kind { get; }
    public string Message { get; }
    public bool IsVisible =>
        Kind != SetupReadyAcknowledgementKind.None &&
        !string.IsNullOrWhiteSpace(Message);

    public static SetupReadyAcknowledgement Hidden =>
        new SetupReadyAcknowledgement(
            SetupReadyAcknowledgementKind.None,
            string.Empty);
}

/// <summary>
/// Detects only incomplete-to-complete edges. Priming a HUD with an already
/// completed setup therefore does not manufacture a success notification.
/// </summary>
public static class SetupReadyAcknowledgementBuilder
{
    public static SetupReadyAcknowledgement Resolve(
        bool alignmentWasComplete,
        bool alignmentIsComplete,
        bool heightWasComplete,
        bool heightIsComplete)
    {
        if (!heightWasComplete && heightIsComplete)
        {
            return new SetupReadyAcknowledgement(
                SetupReadyAcknowledgementKind.HeightComplete,
                "✓ 身長合わせ完了\n準備できました");
        }

        if (!alignmentWasComplete && alignmentIsComplete)
        {
            return new SetupReadyAcknowledgement(
                SetupReadyAcknowledgementKind.AlignmentComplete,
                heightIsComplete
                    ? "✓ 位置合わせ完了\n準備できました"
                    : "✓ 位置合わせ完了\n次は身長合わせ");
        }

        return SetupReadyAcknowledgement.Hidden;
    }
}

/// <summary>
/// Converts a small setup state snapshot into deterministic player-facing data.
/// </summary>
public static class SetupReadyUiModelBuilder
{
    private const string TrackingLabel = "頭と両手の認識";
    private const string AlignmentLabel = "正面合わせ";
    private const string HeightLabel = "身長合わせ";

    public static SetupReadyUiModel Build(SetupReadyUiState state)
    {
        var trackingState = state.TrackingValid
            ? SetupReadyStepState.Complete
            : SetupReadyStepState.Active;
        var alignmentState = state.AlignmentComplete
            ? SetupReadyStepState.Complete
            : state.TrackingValid
                ? SetupReadyStepState.Active
                : SetupReadyStepState.Pending;
        var heightState = state.HeightComplete
            ? SetupReadyStepState.Complete
            : state.TrackingValid && state.AlignmentComplete
                ? SetupReadyStepState.Active
                : SetupReadyStepState.Pending;

        var allPhysicalChecksComplete =
            state.TrackingValid &&
            state.AlignmentComplete &&
            state.HeightComplete;
        var allChecksComplete =
            allPhysicalChecksComplete &&
            state.MotionReady;
        var hasCalibrationFailure =
            (state.AlignmentAttemptFailed &&
             !state.AlignmentComplete) ||
            (state.HeightAttemptFailed &&
             !state.HeightComplete);

        var alignmentHint = new SetupReadyInputHint(
            SetupReadyControllerHand.Right,
            SetupReadyButton.B,
            "正面合わせ",
            true,
            alignmentState == SetupReadyStepState.Active,
            false);
        var heightHint = new SetupReadyInputHint(
            SetupReadyControllerHand.Left,
            SetupReadyButton.X,
            "身長合わせ",
            true,
            heightState == SetupReadyStepState.Active,
            false);
        var startHint = new SetupReadyInputHint(
            SetupReadyControllerHand.Right,
            SetupReadyButton.A,
            "スタート",
            allChecksComplete,
            allChecksComplete,
            false);
        var optionalHint = new SetupReadyInputHint(
            SetupReadyControllerHand.Left,
            SetupReadyButton.Y,
            "見た目を変更（任意）",
            state.AvatarSwapAvailable,
            false,
            true);

        var tracking = new SetupReadyChecklistItem(
            SetupReadyStep.Tracking,
            trackingState,
            TrackingLabel,
            SetupReadyInputHint.Hidden);
        var alignment = new SetupReadyChecklistItem(
            SetupReadyStep.Alignment,
            alignmentState,
            AlignmentLabel,
            alignmentHint);
        var height = new SetupReadyChecklistItem(
            SetupReadyStep.Height,
            heightState,
            HeightLabel,
            heightHint);

        var mainInstruction = BuildMainInstruction(
            state,
            allChecksComplete);
        var recoveryInstruction = state.TrackingValid
            ? string.Empty
            : state.TrackingWasPreviouslyValid
                ? "頭と両手を認識できません\nプレイ位置に戻り、正面を向いて両手を前へ"
                : "プレイ位置に立ち\n正面を向いて、両手を前へ";
        var primaryHint = BuildPrimaryHint(
            state,
            allChecksComplete,
            alignmentHint,
            heightHint,
            startHint);
        var title = allChecksComplete
            ? "準備完了"
            : hasCalibrationFailure
                ? "もう一度確認"
                : allPhysicalChecksComplete
                    ? "最終確認"
                    : "準備";

        return new SetupReadyUiModel(
            state.Phase,
            title,
            mainInstruction,
            recoveryInstruction,
            !state.TrackingValid && state.TrackingWasPreviouslyValid,
            hasCalibrationFailure,
            state.AlignmentInProgress || state.HeightInProgress,
            allPhysicalChecksComplete && !state.MotionReady,
            allChecksComplete,
            allChecksComplete,
            tracking,
            alignment,
            height,
            primaryHint,
            startHint,
            optionalHint);
    }

    private static string BuildMainInstruction(
        SetupReadyUiState state,
        bool allChecksComplete)
    {
        if (!state.TrackingValid)
        {
            return state.TrackingWasPreviouslyValid
                ? "プレイ位置に戻り\n正面を向いて、両手を前へ"
                : "プレイ位置に立ち\n正面を向いて、両手を前へ";
        }

        if (!state.AlignmentComplete)
        {
            if (state.AlignmentInProgress)
            {
                return "正面を向いたまま\n位置を合わせています";
            }

            return state.AlignmentAttemptFailed
                ? "位置を合わせられませんでした\n正面を向き、両手を前へ戻す"
                : "正面を向いて\n鏡のアバターを見る";
        }

        if (!state.HeightComplete)
        {
            if (state.HeightInProgress)
            {
                return "背すじを伸ばしたまま\n身長を測っています";
            }

            return state.HeightAttemptFailed
                ? "身長を測れませんでした\n背すじを伸ばして、もう一度"
                : "背すじを伸ばして\nまっすぐ立つ";
        }

        if (!state.MotionReady)
        {
            return BuildMotionReadinessInstruction(
                state.MotionWaitingReason);
        }

        return allChecksComplete
            ? "開始すると3秒後に\nトレーニングが始まります"
            : "次の準備を確認";
    }

    private static SetupReadyInputHint BuildPrimaryHint(
        SetupReadyUiState state,
        bool allChecksComplete,
        SetupReadyInputHint alignmentHint,
        SetupReadyInputHint heightHint,
        SetupReadyInputHint startHint)
    {
        if (!state.TrackingValid)
        {
            return SetupReadyInputHint.Hidden;
        }

        if (!state.AlignmentComplete)
        {
            return state.AlignmentInProgress
                ? SetupReadyInputHint.Hidden
                : alignmentHint;
        }

        if (!state.HeightComplete)
        {
            return state.HeightInProgress
                ? SetupReadyInputHint.Hidden
                : heightHint;
        }

        if (!state.MotionReady)
        {
            switch ((state.MotionWaitingReason ?? string.Empty)
                    .Trim()
                    .ToUpperInvariant())
            {
                case "PRESS B":
                    return alignmentHint;
                case "PRESS X":
                    return heightHint;
                default:
                    return SetupReadyInputHint.Hidden;
            }
        }

        return allChecksComplete
            ? startHint
            : SetupReadyInputHint.Hidden;
    }

    private static string BuildMotionReadinessInstruction(
        string waitingReason)
    {
        switch ((waitingReason ?? string.Empty)
                .Trim()
                .ToUpperInvariant())
        {
            case "TRACKING":
                return "頭と両手を見える位置へ戻す\n認識すると自動で再開します";
            case "PRESS B":
                return "正面合わせを確認できません\n右手 [B] でもう一度";
            case "PRESS X":
                return "身長合わせを確認できません\n左手 [X] でもう一度";
            case "CALIBRATING":
                return "姿勢を確認しています\n正面を向いて、そのまま待つ";
            case "NO METRICS":
                return "頭と両手を確認しています\n見える位置へ戻してください";
            case "INITIALIZING":
            case "SETTLING":
            default:
                return "姿勢を最終確認しています\n正面を向いて、そのまま待つ";
        }
    }
}

}
