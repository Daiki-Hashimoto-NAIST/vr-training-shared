using System.Collections.Generic;
using UnityEngine;

namespace IVRC
{
    public readonly struct RhythmPoseGuideTarget
    {
        public RhythmPoseGuideTarget(
            int beatIndex,
            int blockIndex,
            BodyRepKind kind,
            RepTimingPoint timingPoint)
        {
            BeatIndex = beatIndex;
            BlockIndex = blockIndex;
            Kind = kind;
            TimingPoint = timingPoint;
        }

        public int BeatIndex { get; }
        public int BlockIndex { get; }
        public BodyRepKind Kind { get; }
        public RepTimingPoint TimingPoint { get; }
    }

    /// <summary>
    /// Builds the visual-only pose sequence from the existing scoring chart.
    /// When the chart leaves enough unscored beats before an exercise block,
    /// the guide demonstrates DOWN -> UP -> DOWN once before the first scored
    /// UP. Otherwise it falls back to one preparatory DOWN target. The chart
    /// itself and its UP/DOWN scoring points are unchanged.
    /// </summary>
    public static class RhythmPoseGuideSchedule
    {
        public static void Build(
            IReadOnlyList<TrainingRhythmNote> notes,
            List<RhythmPoseGuideTarget> output,
            bool includePreparationPreview = true)
        {
            if (output == null)
            {
                return;
            }

            output.Clear();
            if (notes == null || notes.Count <= 0)
            {
                return;
            }

            var previousBlockIndex = int.MinValue;
            var previousKind = default(BodyRepKind);
            var hasPreviousKind = false;

            for (var noteIndex = 0;
                 noteIndex < notes.Count;
                 noteIndex++)
            {
                var note = notes[noteIndex];
                var beginsBlock =
                    note.BlockIndex != previousBlockIndex ||
                    !hasPreviousKind ||
                    note.Kind != previousKind;
                if (beginsBlock)
                {
                    var addedPreview =
                        includePreparationPreview &&
                        TryAddPreparationPreview(
                            note,
                            output);
                    if (!addedPreview)
                    {
                        AddPreparatoryDown(
                            notes,
                            noteIndex,
                            note,
                            output);
                    }
                }

                output.Add(
                    new RhythmPoseGuideTarget(
                        note.UpBeat,
                        note.BlockIndex,
                        note.Kind,
                        RepTimingPoint.Up));
                output.Add(
                    new RhythmPoseGuideTarget(
                        note.DownBeat,
                        note.BlockIndex,
                        note.Kind,
                        RepTimingPoint.Down));

                previousBlockIndex = note.BlockIndex;
                previousKind = note.Kind;
                hasPreviousKind = true;
            }
        }

        private static bool TryAddPreparationPreview(
            TrainingRhythmNote note,
            List<RhythmPoseGuideTarget> output)
        {
            const int previewTargetCount = 3;
            var firstPreviewBeat =
                note.UpBeat - previewTargetCount;
            var earliestAvailableBeat = output.Count <= 0
                ? 1
                : output[output.Count - 1].BeatIndex + 1;
            if (firstPreviewBeat < earliestAvailableBeat)
            {
                return false;
            }

            output.Add(
                new RhythmPoseGuideTarget(
                    firstPreviewBeat,
                    note.BlockIndex,
                    note.Kind,
                    RepTimingPoint.Down));
            output.Add(
                new RhythmPoseGuideTarget(
                    firstPreviewBeat + 1,
                    note.BlockIndex,
                    note.Kind,
                    RepTimingPoint.Up));
            output.Add(
                new RhythmPoseGuideTarget(
                    firstPreviewBeat + 2,
                    note.BlockIndex,
                    note.Kind,
                    RepTimingPoint.Down));
            return true;
        }

        private static void AddPreparatoryDown(
            IReadOnlyList<TrainingRhythmNote> notes,
            int noteIndex,
            TrainingRhythmNote note,
            List<RhythmPoseGuideTarget> output)
        {
            var downToUpBeats = Mathf.Max(
                1,
                note.DownBeat - note.UpBeat);
            if (noteIndex + 1 < notes.Count)
            {
                var next = notes[noteIndex + 1];
                if (next.BlockIndex == note.BlockIndex &&
                    next.Kind == note.Kind)
                {
                    downToUpBeats = Mathf.Max(
                        1,
                        next.UpBeat - note.DownBeat);
                }
            }

            var preparatoryDownBeat = Mathf.Max(
                0,
                note.UpBeat - downToUpBeats);
            if (output.Count <= 0 ||
                preparatoryDownBeat >
                output[output.Count - 1].BeatIndex)
            {
                output.Add(
                    new RhythmPoseGuideTarget(
                        preparatoryDownBeat,
                        note.BlockIndex,
                        note.Kind,
                        RepTimingPoint.Down));
            }
        }

        public static float GetTargetIntervalSeconds(
            IReadOnlyList<RhythmPoseGuideTarget> targets,
            int index,
            float secondsPerBeat)
        {
            if (targets == null ||
                targets.Count <= 1 ||
                index < 0 ||
                index >= targets.Count)
            {
                return Mathf.Max(0f, secondsPerBeat);
            }

            var intervalBeats = int.MaxValue;
            if (index > 0)
            {
                var previousInterval =
                    targets[index].BeatIndex -
                    targets[index - 1].BeatIndex;
                if (previousInterval > 0)
                {
                    intervalBeats = previousInterval;
                }
            }

            if (index + 1 < targets.Count)
            {
                var nextInterval =
                    targets[index + 1].BeatIndex -
                    targets[index].BeatIndex;
                if (nextInterval > 0)
                {
                    intervalBeats = Mathf.Min(
                        intervalBeats,
                        nextInterval);
                }
            }

            if (intervalBeats == int.MaxValue)
            {
                intervalBeats = 1;
            }

            return Mathf.Max(
                0f,
                intervalBeats *
                Mathf.Max(0f, secondsPerBeat));
        }
    }

    public static class RhythmPoseGuideMath
    {
        public static float GetEffectiveLeadTime(
            float configuredLeadTime,
            float targetIntervalSeconds)
        {
            configuredLeadTime = Mathf.Max(
                0f,
                configuredLeadTime);
            targetIntervalSeconds = Mathf.Max(
                0f,
                targetIntervalSeconds);
            return Mathf.Min(
                configuredLeadTime,
                targetIntervalSeconds * 0.9f);
        }

        public static float EvaluateScaleMultiplier(
            float secondsUntilBeat,
            float effectiveLeadTime,
            float startMultiplier,
            float beatMultiplier,
            AnimationCurve scaleCurve)
        {
            startMultiplier = Mathf.Max(0f, startMultiplier);
            beatMultiplier = Mathf.Max(0f, beatMultiplier);
            if (secondsUntilBeat <= 0f ||
                effectiveLeadTime <= Mathf.Epsilon)
            {
                return beatMultiplier;
            }

            var progress = 1f - Mathf.Clamp01(
                secondsUntilBeat /
                effectiveLeadTime);
            var evaluated = scaleCurve == null
                ? progress
                : scaleCurve.Evaluate(progress);
            return Mathf.Lerp(
                startMultiplier,
                beatMultiplier,
                Mathf.Clamp01(evaluated));
        }

        /// <summary>
        /// Line art keeps the guide on the body and pulls a second contour
        /// in towards it, as a uniform outward distance in metres rather
        /// than a scale. Scaling runs about the object origin at the feet, so
        /// it lifts the head much further than it widens the body and reads
        /// as the guide dropping from above. The result never goes negative,
        /// so the approaching line cannot sink inside the goal outline and
        /// invert what the player is reading.
        /// </summary>
        public static float EvaluateContourOffset(
            float secondsUntilBeat,
            float effectiveLeadTime,
            float startOffset,
            float beatOffset,
            AnimationCurve scaleCurve)
        {
            startOffset = Mathf.Max(0f, startOffset);
            beatOffset = Mathf.Max(0f, beatOffset);
            return Mathf.Max(
                0f,
                EvaluateScaleMultiplier(
                    secondsUntilBeat,
                    effectiveLeadTime,
                    startOffset,
                    beatOffset,
                    scaleCurve));
        }

        public static Vector3 ApplySignedMultiplier(
            Vector3 baseScale,
            float multiplier)
        {
            return baseScale * Mathf.Max(0f, multiplier);
        }
    }

    public readonly struct LoopingRhythmPoseSample
    {
        public LoopingRhythmPoseSample(
            RepTimingPoint timingPoint,
            float secondsUntilBeat,
            float targetIntervalSeconds,
            bool isBeatWindow)
        {
            TimingPoint = timingPoint;
            SecondsUntilBeat = secondsUntilBeat;
            TargetIntervalSeconds = targetIntervalSeconds;
            IsBeatWindow = isBeatWindow;
        }

        public RepTimingPoint TimingPoint { get; }
        public float SecondsUntilBeat { get; }
        public float TargetIntervalSeconds { get; }
        public bool IsBeatWindow { get; }
    }

    /// <summary>
    /// Maps an existing music playhead to the next repeating UP/DOWN target.
    /// This owns no timer: callers supply beat position from the audio that
    /// is currently playing, so a looping tutorial cannot drift from its BGM.
    /// </summary>
    public static class LoopingRhythmPoseSchedule
    {
        public static bool TrySample(
            float beatPosition,
            float secondsPerBeat,
            int beatsPerRep,
            int upToDownBeats,
            float postBeatVisibleSeconds,
            out LoopingRhythmPoseSample sample)
        {
            sample = default;
            if (!IsFinite(beatPosition) ||
                !IsFinite(secondsPerBeat) ||
                secondsPerBeat <= Mathf.Epsilon)
            {
                return false;
            }

            beatsPerRep = Mathf.Max(2, beatsPerRep);
            upToDownBeats = Mathf.Clamp(
                upToDownBeats,
                1,
                beatsPerRep - 1);
            postBeatVisibleSeconds = Mathf.Max(
                0f,
                postBeatVisibleSeconds);

            var localBeat = Mathf.Repeat(
                Mathf.Max(0f, beatPosition),
                beatsPerRep);
            var afterDown = localBeat >= upToDownBeats;
            var previousPoint = afterDown
                ? RepTimingPoint.Down
                : RepTimingPoint.Up;
            var previousBeat = afterDown
                ? upToDownBeats
                : 0f;
            var secondsSincePrevious =
                (localBeat - previousBeat) * secondsPerBeat;

            var intervalBeats = Mathf.Min(
                upToDownBeats,
                beatsPerRep - upToDownBeats);
            var intervalSeconds =
                intervalBeats * secondsPerBeat;
            if (secondsSincePrevious <= postBeatVisibleSeconds)
            {
                sample = new LoopingRhythmPoseSample(
                    previousPoint,
                    -secondsSincePrevious,
                    intervalSeconds,
                    true);
                return true;
            }

            var nextPoint = afterDown
                ? RepTimingPoint.Up
                : RepTimingPoint.Down;
            var beatsUntilNext = afterDown
                ? beatsPerRep - localBeat
                : upToDownBeats - localBeat;
            sample = new LoopingRhythmPoseSample(
                nextPoint,
                beatsUntilNext * secondsPerBeat,
                intervalSeconds,
                false);
            return true;
        }

        private static bool IsFinite(float value)
        {
            return
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }
    }
}
