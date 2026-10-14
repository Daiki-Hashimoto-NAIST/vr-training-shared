using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Implemented by avatar-specific muscle visual adapters and safe mocks.
    /// Unity scene references are stored as MonoBehaviours and validated as
    /// this interface at runtime.
    /// </summary>
    public interface IMuscleGrowthOutput
    {
        string OutputLabel { get; }
        bool IsAvailable { get; }
        bool IsMock { get; }
        float LastAppliedGrowthNormalized { get; }
        void ApplyGrowth(float normalizedGrowth);
        void ResetGrowth();
    }

    /// <summary>
    /// One drivable muscle group. Overall keeps the whole-body signal; the
    /// remaining entries map one exercise (<see cref="BodyRepKind"/>) and one
    /// body side to the shape that exercise trains.
    /// </summary>
    public enum MuscleGrowthChannel
    {
        Overall,
        ElbowFlexionLeft,
        ElbowFlexionRight,
        ArmRaiseLeft,
        ArmRaiseRight,

        // The chest is worked by both arm exercises, so these follow the
        // combined progress of that side rather than a single exercise.
        // Appended last so existing serialized selections keep their meaning.
        ChestLeft,
        ChestRight
    }

    /// <summary>
    /// One immutable snapshot of every growth channel, dispatched together so
    /// an output never mixes values sampled at different times.
    /// </summary>
    public readonly struct MuscleGrowthChannels
    {
        public MuscleGrowthChannels(
            float overall,
            float elbowFlexionLeft,
            float elbowFlexionRight,
            float armRaiseLeft,
            float armRaiseRight,
            float chestLeft,
            float chestRight)
        {
            Overall = overall;
            ElbowFlexionLeft = elbowFlexionLeft;
            ElbowFlexionRight = elbowFlexionRight;
            ArmRaiseLeft = armRaiseLeft;
            ArmRaiseRight = armRaiseRight;
            ChestLeft = chestLeft;
            ChestRight = chestRight;
        }

        public float Overall { get; }
        public float ElbowFlexionLeft { get; }
        public float ElbowFlexionRight { get; }
        public float ArmRaiseLeft { get; }
        public float ArmRaiseRight { get; }
        public float ChestLeft { get; }
        public float ChestRight { get; }

        public float Get(MuscleGrowthChannel channel)
        {
            return channel switch
            {
                MuscleGrowthChannel.ElbowFlexionLeft => ElbowFlexionLeft,
                MuscleGrowthChannel.ElbowFlexionRight => ElbowFlexionRight,
                MuscleGrowthChannel.ArmRaiseLeft => ArmRaiseLeft,
                MuscleGrowthChannel.ArmRaiseRight => ArmRaiseRight,
                MuscleGrowthChannel.ChestLeft => ChestLeft,
                MuscleGrowthChannel.ChestRight => ChestRight,
                _ => Overall
            };
        }

        /// <summary>
        /// True when the channel is derived from other channels rather than
        /// credited directly by a scored rep.
        /// </summary>
        public static bool IsDerived(MuscleGrowthChannel channel)
        {
            return channel == MuscleGrowthChannel.ChestLeft ||
                   channel == MuscleGrowthChannel.ChestRight;
        }

        /// <summary>Maps one scored rep to the channel it trains.</summary>
        public static MuscleGrowthChannel FromRep(
            BodyRepKind kind,
            BodySide side)
        {
            if (kind == BodyRepKind.ElbowFlexion)
            {
                return side == BodySide.Left
                    ? MuscleGrowthChannel.ElbowFlexionLeft
                    : MuscleGrowthChannel.ElbowFlexionRight;
            }

            return side == BodySide.Left
                ? MuscleGrowthChannel.ArmRaiseLeft
                : MuscleGrowthChannel.ArmRaiseRight;
        }
    }

    /// <summary>
    /// Immutable, sanitized muscle state captured at the Training boundary.
    /// Every presentation phase reads this value instead of the live scorer,
    /// so transformation, entrance, posing, and results cannot diverge.
    /// </summary>
    public readonly struct MuscleGrowthSnapshot
    {
        public MuscleGrowthSnapshot(
            MuscleGrowthChannels channels)
        {
            Channels = new MuscleGrowthChannels(
                MuscleGrowthMath.SanitizeNormalized(
                    channels.Overall),
                MuscleGrowthMath.SanitizeNormalized(
                    channels.ElbowFlexionLeft),
                MuscleGrowthMath.SanitizeNormalized(
                    channels.ElbowFlexionRight),
                MuscleGrowthMath.SanitizeNormalized(
                    channels.ArmRaiseLeft),
                MuscleGrowthMath.SanitizeNormalized(
                    channels.ArmRaiseRight),
                MuscleGrowthMath.SanitizeNormalized(
                    channels.ChestLeft),
                MuscleGrowthMath.SanitizeNormalized(
                    channels.ChestRight));
        }

        public MuscleGrowthChannels Channels { get; }
        public float Overall => Channels.Overall;

        public float Get(MuscleGrowthChannel channel)
        {
            return Channels.Get(channel);
        }

        public static MuscleGrowthSnapshot Zero => default;
    }

    /// <summary>
    /// Implemented by outputs that can drive individual muscle groups.
    /// <see cref="MuscleGrowthController"/> prefers this over
    /// <see cref="IMuscleGrowthOutput.ApplyGrowth"/> when available.
    /// </summary>
    public interface IMuscleGrowthChannelOutput
    {
        void ApplyChannelGrowth(MuscleGrowthChannels channels);
    }

    /// <summary>
    /// Implemented by outputs that can flash the body for one repetition.
    /// Kept apart from <see cref="IMuscleGrowthChannelOutput"/> so an output
    /// that only shapes the body is not forced to answer for light.
    /// </summary>
    public interface IMusclePulseOutput
    {
        /// <summary>
        /// <paramref name="overshoot"/> carries one value per muscle group,
        /// added to that group's growth for presentation only. It is kept out
        /// of <see cref="MuscleGrowthChannels"/> proper because growth is
        /// normalized and scored, while an overshoot is allowed to drive the
        /// shape past fully trained.
        /// </summary>
        void ApplyPulse(
            float glow,
            TrainingTimingGrade grade,
            MuscleGrowthChannels overshoot);
    }

    public static class MuscleGrowthMath
    {
        public static float SelectTarget(
            ExperiencePhase phase,
            float liveGrowth,
            float completedGrowth)
        {
            return phase switch
            {
                ExperiencePhase.Training =>
                    SanitizeNormalized(liveGrowth),
                ExperiencePhase.Transformation or
                ExperiencePhase.StageTransition or
                ExperiencePhase.StageEntrance or
                ExperiencePhase.Posing or
                ExperiencePhase.Results =>
                    SanitizeNormalized(completedGrowth),
                _ => 0f
            };
        }

        public static float SanitizeNormalized(float value)
        {
            return float.IsNaN(value) ||
                   float.IsInfinity(value)
                ? 0f
                : Mathf.Clamp01(value);
        }
    }
}
