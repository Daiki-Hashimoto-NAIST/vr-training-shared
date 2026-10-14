using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Pure helpers shared by the stage feedback runtime and EditMode tests.
    /// </summary>
    public static class PoseStageFeedbackMath
    {
        public const float MinimumHapticSeconds = 0.01f;
        public const float MaximumHapticSeconds = 0.20f;

        public static float ClampHapticSeconds(float seconds)
        {
            if (!IsFinite(seconds))
            {
                return MinimumHapticSeconds;
            }

            return Mathf.Clamp(
                seconds,
                MinimumHapticSeconds,
                MaximumHapticSeconds);
        }

        public static float CalculateReaction(
            float spatialScore,
            float reactionStartScore,
            float fullReactionScore)
        {
            spatialScore = SanitizeScore(spatialScore);
            reactionStartScore =
                SanitizeScore(reactionStartScore);
            fullReactionScore =
                Mathf.Max(
                    reactionStartScore,
                    SanitizeScore(fullReactionScore));

            if (fullReactionScore <=
                reactionStartScore + Mathf.Epsilon)
            {
                return spatialScore >= fullReactionScore
                    ? 1f
                    : 0f;
            }

            var normalized = Mathf.InverseLerp(
                reactionStartScore,
                fullReactionScore,
                spatialScore);
            return Mathf.SmoothStep(0f, 1f, normalized);
        }

        public static int GetCheerTier(
            float spatialScore,
            float firstThreshold,
            float secondThreshold,
            float thirdThreshold)
        {
            spatialScore = SanitizeScore(spatialScore);
            firstThreshold = SanitizeScore(firstThreshold);
            secondThreshold = Mathf.Max(
                firstThreshold,
                SanitizeScore(secondThreshold));
            thirdThreshold = Mathf.Max(
                secondThreshold,
                SanitizeScore(thirdThreshold));

            if (spatialScore >= thirdThreshold)
            {
                return 3;
            }

            if (spatialScore >= secondThreshold)
            {
                return 2;
            }

            return spatialScore >= firstThreshold ? 1 : 0;
        }

        public static float CalculateCompletionPulse(
            float remainingSeconds,
            float durationSeconds,
            float strength)
        {
            if (!IsFinite(remainingSeconds) ||
                !IsFinite(durationSeconds) ||
                durationSeconds <= Mathf.Epsilon)
            {
                return 0f;
            }

            var normalizedRemaining = Mathf.Clamp01(
                Mathf.Max(0f, remainingSeconds) /
                durationSeconds);
            return Mathf.Clamp01(strength) *
                   Mathf.SmoothStep(
                       0f,
                       1f,
                       normalizedRemaining);
        }

        public static bool ShouldPlayPoseSuccessCue(
            PoseCompletionResult result)
        {
            if (!result.Succeeded)
            {
                return false;
            }

            // A fully cleared routine receives the larger Results cheer.
            // If the final pose itself succeeds after an earlier timeout,
            // keep the local confirmation because no Results cheer follows.
            return !result.IsFinalPose ||
                   result.ClearedPoseCount < result.PoseCount;
        }

        private static float SanitizeScore(float value)
        {
            return float.IsNaN(value)
                ? 0f
                : Mathf.Clamp(value, 0f, 100f);
        }

        private static bool IsFinite(float value)
        {
            return
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }
    }
}
