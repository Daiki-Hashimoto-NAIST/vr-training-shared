using System.Collections.Generic;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Immutable outcome of one resolved pose. This is emitted before the
    /// scorer advances, so presentation code never has to reconstruct the
    /// previous pose from the next pose's live state.
    /// </summary>
    public readonly struct PoseCompletionResult
    {
        public PoseCompletionResult(
            int poseIndex,
            int poseCount,
            string poseDisplayName,
            float score,
            bool succeeded,
            int clearedPoseCount)
        {
            PoseCount = Mathf.Max(0, poseCount);
            PoseIndex = PoseCount <= 0
                ? 0
                : Mathf.Clamp(poseIndex, 0, PoseCount - 1);
            PoseDisplayName = string.IsNullOrWhiteSpace(poseDisplayName)
                ? "POSE"
                : poseDisplayName.Trim();
            Score = IsFinite(score)
                ? Mathf.Clamp(score, 0f, 100f)
                : 0f;
            Succeeded = succeeded;
            ClearedPoseCount = Mathf.Clamp(
                clearedPoseCount,
                0,
                PoseCount);
        }

        public int PoseIndex { get; }
        public int PoseCount { get; }
        public int PoseNumber => PoseCount <= 0 ? 0 : PoseIndex + 1;
        public string PoseDisplayName { get; }
        public float Score { get; }
        public bool Succeeded { get; }
        public int ClearedPoseCount { get; }
        public bool IsFinalPose =>
            PoseCount > 0 && PoseIndex >= PoseCount - 1;

        private static bool IsFinite(float value)
        {
            return
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// The finished outcome of one pose inside a routine.
    /// </summary>
    public readonly struct PosingRoutineEntry
    {
        public PosingRoutineEntry(
            float score,
            bool succeeded)
        {
            Score = IsFinite(score)
                ? Mathf.Clamp(score, 0f, 100f)
                : 0f;
            Succeeded = succeeded;
        }

        public float Score { get; }
        public bool Succeeded { get; }

        private static bool IsFinite(float value)
        {
            return
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Progression and aggregation for a sequence of posing profiles. Kept
    /// free of Unity objects and elapsed time so the whole routine can be
    /// driven deterministically from tests without a headset.
    /// </summary>
    public static class PosingRoutineCalculator
    {
        /// <summary>
        /// The routine score reported to the final result. Poses are weighted
        /// equally: a routine is a set of poses held to the same standard, so
        /// no pose should count for more than another.
        /// </summary>
        public static float AverageScore(
            IReadOnlyList<PosingRoutineEntry> entries)
        {
            if (entries == null || entries.Count <= 0)
            {
                return 0f;
            }

            var total = 0f;
            for (var index = 0; index < entries.Count; index++)
            {
                total += entries[index].Score;
            }

            return Mathf.Clamp(
                total / entries.Count,
                0f,
                100f);
        }

        public static int CountCleared(
            IReadOnlyList<PosingRoutineEntry> entries)
        {
            if (entries == null)
            {
                return 0;
            }

            var cleared = 0;
            for (var index = 0; index < entries.Count; index++)
            {
                if (entries[index].Succeeded)
                {
                    cleared++;
                }
            }

            return cleared;
        }

        /// <summary>
        /// True only when every pose in the routine was actually held, not
        /// merely attempted. A routine that ran out of time on one pose is
        /// not a success.
        /// </summary>
        public static bool AllCleared(
            IReadOnlyList<PosingRoutineEntry> entries,
            int expectedCount)
        {
            if (expectedCount <= 0 ||
                entries == null ||
                entries.Count < expectedCount)
            {
                return false;
            }

            return CountCleared(entries) >= expectedCount;
        }

        /// <summary>
        /// How long the Posing phase must stay open to give every pose its
        /// countdown plus its scoring window. The phase timer is only a
        /// backstop; the routine itself advances pose by pose.
        /// </summary>
        public static float TotalSeconds(
            IReadOnlyList<float> countdownSecondsPerPose,
            float windowSecondsPerPose)
        {
            var window = IsFinite(windowSecondsPerPose)
                ? Mathf.Max(0f, windowSecondsPerPose)
                : 0f;
            if (countdownSecondsPerPose == null ||
                countdownSecondsPerPose.Count <= 0)
            {
                return 0f;
            }

            var total = 0f;
            for (var index = 0;
                 index < countdownSecondsPerPose.Count;
                 index++)
            {
                var countdown = countdownSecondsPerPose[index];
                total += window +
                    (IsFinite(countdown)
                        ? Mathf.Max(0f, countdown)
                        : 0f);
            }

            return total;
        }

        /// <summary>
        /// The next pose index, or the count itself once the routine is over.
        /// </summary>
        public static int NextIndex(
            int currentIndex,
            int poseCount)
        {
            if (poseCount <= 0)
            {
                return 0;
            }

            return Mathf.Clamp(
                currentIndex + 1,
                0,
                poseCount);
        }

        public static bool IsRoutineComplete(
            int currentIndex,
            int poseCount)
        {
            return poseCount <= 0 || currentIndex >= poseCount;
        }

        private static bool IsFinite(float value)
        {
            return
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }
    }
}
