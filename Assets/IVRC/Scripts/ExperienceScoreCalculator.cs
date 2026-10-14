using System;
using UnityEngine;

namespace IVRC
{
    public enum ExperienceRank
    {
        S,
        A,
        B,
        C,
        D
    }

    [Serializable]
    public struct ExperienceTrainingWeights
    {
        [Min(0f)] public float Reps;
        [Min(0f)] public float Rhythm;
        [Min(0f)] public float Growth;
    }

    [Serializable]
    public struct ExperienceRankThresholds
    {
        [Range(0f, 100f)] public float S;
        [Range(0f, 100f)] public float A;
        [Range(0f, 100f)] public float B;
        [Range(0f, 100f)] public float C;
    }

    public readonly struct ExperienceScoreSettings
    {
        public ExperienceScoreSettings(
            int targetElbowReps,
            int targetArmReps,
            ExperienceTrainingWeights trainingWeights,
            ExperienceRankThresholds rankThresholds)
        {
            TargetElbowReps = targetElbowReps;
            TargetArmReps = targetArmReps;
            TrainingWeights = trainingWeights;
            RankThresholds = rankThresholds;
        }

        public int TargetElbowReps { get; }
        public int TargetArmReps { get; }
        public ExperienceTrainingWeights TrainingWeights { get; }
        public ExperienceRankThresholds RankThresholds { get; }
    }

    public readonly struct ExperienceScoreInput
    {
        public ExperienceScoreInput(
            int elbowReps,
            int armReps,
            float averageRhythmAccuracy,
            float growthNormalized)
        {
            ElbowReps = elbowReps;
            ArmReps = armReps;
            AverageRhythmAccuracy = averageRhythmAccuracy;
            GrowthNormalized = growthNormalized;
        }

        public int ElbowReps { get; }
        public int ArmReps { get; }
        public float AverageRhythmAccuracy { get; }
        public float GrowthNormalized { get; }
    }

    public readonly struct ExperienceScoreResult
    {
        public ExperienceScoreResult(
            float repScore,
            float rhythmScore,
            float growthScore,
            float trainingScore,
            float finalScore,
            ExperienceRank rank)
        {
            RepScore = repScore;
            RhythmScore = rhythmScore;
            GrowthScore = growthScore;
            TrainingScore = trainingScore;
            FinalScore = finalScore;
            Rank = rank;
        }

        public float RepScore { get; }
        public float RhythmScore { get; }
        public float GrowthScore { get; }
        public float TrainingScore { get; }
        public float FinalScore { get; }
        public ExperienceRank Rank { get; }
    }

    /// <summary>
    /// Stateless training-score aggregation. Invalid numeric inputs are
    /// converted to safe bounded values and all weighting is normalized by
    /// active training weights. Posing is intentionally not an input here.
    /// </summary>
    public static class ExperienceScoreCalculator
    {
        public static ExperienceScoreResult Calculate(
            in ExperienceScoreInput input,
            in ExperienceScoreSettings settings)
        {
            var repScore = CalculateRepScore(
                input.ElbowReps,
                input.ArmReps,
                settings.TargetElbowReps,
                settings.TargetArmReps);
            var rhythmScore =
                SanitizeNormalized(input.AverageRhythmAccuracy) *
                100f;
            var growthScore =
                SanitizeNormalized(input.GrowthNormalized) *
                100f;

            var trainingWeights = settings.TrainingWeights;
            var trainingScore = WeightedAverage(
                repScore,
                trainingWeights.Reps,
                rhythmScore,
                trainingWeights.Rhythm,
                growthScore,
                trainingWeights.Growth);

            // The final score is exactly the frozen training score. Pose
            // matching remains available to the posing routine, but cannot
            // add, deduct, or otherwise influence this calculation.
            var finalScore = trainingScore;
            var rank = CalculateRank(
                finalScore,
                settings.RankThresholds);

            return new ExperienceScoreResult(
                repScore,
                rhythmScore,
                growthScore,
                trainingScore,
                finalScore,
                rank);
        }

        private static float CalculateRepScore(
            int elbowReps,
            int armReps,
            int targetElbowReps,
            int targetArmReps)
        {
            targetElbowReps = Mathf.Max(0, targetElbowReps);
            targetArmReps = Mathf.Max(0, targetArmReps);
            var scoreSum = 0f;
            var activeTargetCount = 0;

            if (targetElbowReps > 0)
            {
                scoreSum += CalculateRepTargetScore(
                    elbowReps,
                    targetElbowReps);
                activeTargetCount++;
            }

            if (targetArmReps > 0)
            {
                scoreSum += CalculateRepTargetScore(
                    armReps,
                    targetArmReps);
                activeTargetCount++;
            }

            return activeTargetCount == 0
                ? 100f
                : ClampScore(scoreSum / activeTargetCount);
        }

        private static float CalculateRepTargetScore(
            int completedReps,
            int targetReps)
        {
            return ClampScore(
                Mathf.Max(0, completedReps) /
                (float)targetReps *
                100f);
        }

        private static ExperienceRank CalculateRank(
            float finalScore,
            ExperienceRankThresholds thresholds)
        {
            var s = SanitizeThreshold(thresholds.S, 90f);
            var a = Mathf.Min(
                s,
                SanitizeThreshold(thresholds.A, 80f));
            var b = Mathf.Min(
                a,
                SanitizeThreshold(thresholds.B, 70f));
            var c = Mathf.Min(
                b,
                SanitizeThreshold(thresholds.C, 60f));

            if (finalScore >= s)
            {
                return ExperienceRank.S;
            }

            if (finalScore >= a)
            {
                return ExperienceRank.A;
            }

            if (finalScore >= b)
            {
                return ExperienceRank.B;
            }

            if (finalScore >= c)
            {
                return ExperienceRank.C;
            }

            return ExperienceRank.D;
        }

        private static float WeightedAverage(
            float firstScore,
            float firstWeight,
            float secondScore,
            float secondWeight)
        {
            firstWeight = SanitizeWeight(firstWeight);
            secondWeight = SanitizeWeight(secondWeight);
            var totalWeight = firstWeight + secondWeight;
            if (!IsFinite(totalWeight) ||
                totalWeight <= Mathf.Epsilon)
            {
                return 0f;
            }

            return ClampScore(
                (firstScore * firstWeight +
                 secondScore * secondWeight) /
                totalWeight);
        }

        private static float WeightedAverage(
            float firstScore,
            float firstWeight,
            float secondScore,
            float secondWeight,
            float thirdScore,
            float thirdWeight)
        {
            firstWeight = SanitizeWeight(firstWeight);
            secondWeight = SanitizeWeight(secondWeight);
            thirdWeight = SanitizeWeight(thirdWeight);
            var totalWeight =
                firstWeight + secondWeight + thirdWeight;
            if (!IsFinite(totalWeight) ||
                totalWeight <= Mathf.Epsilon)
            {
                return 0f;
            }

            return ClampScore(
                (firstScore * firstWeight +
                 secondScore * secondWeight +
                 thirdScore * thirdWeight) /
                totalWeight);
        }

        private static float SanitizeNormalized(float value)
        {
            if (float.IsNaN(value))
            {
                return 0f;
            }

            return Mathf.Clamp(value, 0f, 1f);
        }

        private static float SanitizeWeight(float value)
        {
            return IsFinite(value) ? Mathf.Max(0f, value) : 0f;
        }

        private static float SanitizeThreshold(
            float value,
            float fallback)
        {
            return IsFinite(value)
                ? Mathf.Clamp(value, 0f, 100f)
                : fallback;
        }

        private static float ClampScore(float value)
        {
            if (float.IsNaN(value))
            {
                return 0f;
            }

            return Mathf.Clamp(value, 0f, 100f);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
