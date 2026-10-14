using UnityEngine;

namespace IVRC
{
    public static class AudioDuckingMath
    {
        public static float GetTargetMultiplier(
            bool foregroundIsPlaying,
            float duckMultiplier)
        {
            return foregroundIsPlaying
                ? Mathf.Clamp01(duckMultiplier)
                : 1f;
        }

        public static float MoveMultiplier(
            float current,
            float target,
            float unscaledDeltaTime,
            float attackSeconds,
            float releaseSeconds)
        {
            current = Mathf.Clamp01(current);
            target = Mathf.Clamp01(target);
            var duration = target < current
                ? Mathf.Max(0.01f, attackSeconds)
                : Mathf.Max(0.01f, releaseSeconds);
            return Mathf.MoveTowards(
                current,
                target,
                Mathf.Max(0f, unscaledDeltaTime) / duration);
        }
    }
}
