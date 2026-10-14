using System;
using System.Collections.Generic;
using UnityEngine;

namespace IVRC
{
    [Flags]
    public enum TrainingSideMask
    {
        None = 0,
        Left = 1 << 0,
        Right = 1 << 1,
        Both = Left | Right
    }

    [Serializable]
    public sealed class TrainingRhythmBlock
    {
        [SerializeField] private BodyRepKind _kind =
            BodyRepKind.ElbowFlexion;
        [SerializeField, Min(1)] private int _repetitions = 8;
        [SerializeField, Min(2)] private int _beatsPerRep = 4;
        [SerializeField, Min(1)] private int _upToDownBeats = 2;
        [SerializeField, Min(0)] private int _transitionBeats = 4;
        [SerializeField] private TrainingSideMask _requiredSides =
            TrainingSideMask.Both;

        public TrainingRhythmBlock()
        {
        }

        public TrainingRhythmBlock(
            BodyRepKind kind,
            int repetitions,
            int beatsPerRep,
            int upToDownBeats,
            int transitionBeats,
            TrainingSideMask requiredSides =
                TrainingSideMask.Both)
        {
            _kind = kind;
            _repetitions = repetitions;
            _beatsPerRep = beatsPerRep;
            _upToDownBeats = upToDownBeats;
            _transitionBeats = transitionBeats;
            _requiredSides = requiredSides;
            Validate();
        }

        public BodyRepKind Kind => _kind;
        public int Repetitions => Mathf.Max(1, _repetitions);
        public int BeatsPerRep => Mathf.Max(2, _beatsPerRep);
        public int UpToDownBeats => Mathf.Clamp(
            _upToDownBeats,
            1,
            BeatsPerRep - 1);
        public int TransitionBeats =>
            Mathf.Max(0, _transitionBeats);
        public TrainingSideMask RequiredSides =>
            SanitizeSides(_requiredSides);
        public string DisplayName =>
            TrainingRhythmPattern.GetDisplayName(_kind);

        public void Validate()
        {
            _repetitions = Repetitions;
            _beatsPerRep = BeatsPerRep;
            _upToDownBeats = UpToDownBeats;
            _transitionBeats = TransitionBeats;
            _requiredSides = RequiredSides;
        }

        private static TrainingSideMask SanitizeSides(
            TrainingSideMask sides)
        {
            sides &= TrainingSideMask.Both;
            return sides == TrainingSideMask.None
                ? TrainingSideMask.Both
                : sides;
        }
    }

    public readonly struct TrainingRhythmNote
    {
        public TrainingRhythmNote(
            int index,
            int blockIndex,
            int repetitionInBlock,
            BodyRepKind kind,
            TrainingSideMask requiredSides,
            int upBeat,
            int downBeat)
        {
            Index = index;
            BlockIndex = blockIndex;
            RepetitionInBlock = repetitionInBlock;
            Kind = kind;
            RequiredSides = requiredSides;
            UpBeat = upBeat;
            DownBeat = downBeat;
        }

        public int Index { get; }
        public int BlockIndex { get; }
        public int RepetitionInBlock { get; }
        public BodyRepKind Kind { get; }
        public TrainingSideMask RequiredSides { get; }
        public int UpBeat { get; }
        public int DownBeat { get; }

        public int GetBeat(RepTimingPoint timingPoint)
        {
            return timingPoint == RepTimingPoint.Up
                ? UpBeat
                : DownBeat;
        }

        public bool Requires(BodySide side)
        {
            var mask = side == BodySide.Left
                ? TrainingSideMask.Left
                : TrainingSideMask.Right;
            return (RequiredSides & mask) != 0;
        }
    }

    /// <summary>
    /// Reusable chart data. The first prototype renders the notes in one lane,
    /// while side requirements remain explicit for a future multi-lane view.
    /// </summary>
    [CreateAssetMenu(
        fileName = "TrainingRhythmPattern",
        menuName = "IVRC/Training Rhythm Pattern")]
    public sealed class TrainingRhythmPattern : ScriptableObject
    {
        [SerializeField, Min(0)] private int _leadInBeats = 4;
        [SerializeField] private TrainingRhythmBlock[] _blocks =
        {
            new TrainingRhythmBlock(
                BodyRepKind.ElbowFlexion,
                8,
                4,
                2,
                4),
            new TrainingRhythmBlock(
                BodyRepKind.ArmRaise,
                8,
                4,
                2,
                4),
            new TrainingRhythmBlock(
                BodyRepKind.ElbowFlexion,
                8,
                4,
                2,
                4),
            new TrainingRhythmBlock(
                BodyRepKind.ArmRaise,
                8,
                4,
                2,
                0)
        };
        [SerializeField, Min(0)] private int _outroBeats = 4;

        public int LeadInBeats => Mathf.Max(0, _leadInBeats);
        public int OutroBeats => Mathf.Max(0, _outroBeats);
        public IReadOnlyList<TrainingRhythmBlock> Blocks =>
            _blocks ?? Array.Empty<TrainingRhythmBlock>();

        public int TotalRepetitions
        {
            get
            {
                var total = 0;
                foreach (var block in Blocks)
                {
                    if (block != null)
                    {
                        total += block.Repetitions;
                    }
                }

                return total;
            }
        }

        public int TotalRequiredSideRepetitions
        {
            get
            {
                var total = 0;
                foreach (var block in Blocks)
                {
                    if (block == null)
                    {
                        continue;
                    }

                    total +=
                        block.Repetitions *
                        GetRequiredSideCount(
                            block.RequiredSides);
                }

                return total;
            }
        }

        /// <summary>
        /// Required side repetitions for one muscle group. Used to normalize
        /// per-part growth so a group reaches full growth when its own blocks
        /// are complete, not when the whole chart is.
        /// </summary>
        public int GetRequiredSideRepetitions(
            MuscleGrowthChannel channel)
        {
            if (channel == MuscleGrowthChannel.Overall)
            {
                return TotalRequiredSideRepetitions;
            }

            var total = 0;
            foreach (var block in Blocks)
            {
                if (block == null)
                {
                    continue;
                }

                foreach (var side in EnumerateSides(
                             block.RequiredSides))
                {
                    if (MuscleGrowthChannels.FromRep(
                            block.Kind,
                            side) == channel)
                    {
                        total += block.Repetitions;
                    }
                }
            }

            return total;
        }

        private static IEnumerable<BodySide> EnumerateSides(
            TrainingSideMask mask)
        {
            if ((mask & TrainingSideMask.Left) != 0)
            {
                yield return BodySide.Left;
            }

            if ((mask & TrainingSideMask.Right) != 0)
            {
                yield return BodySide.Right;
            }
        }

        public int TotalBeats
        {
            get
            {
                var cursor = LeadInBeats;
                for (var index = 0;
                     index < Blocks.Count;
                     index++)
                {
                    var block = Blocks[index];
                    if (block == null)
                    {
                        continue;
                    }

                    cursor +=
                        block.Repetitions *
                        block.BeatsPerRep;
                    if (index < Blocks.Count - 1)
                    {
                        cursor += block.TransitionBeats;
                    }
                }

                return cursor + OutroBeats;
            }
        }

        public List<TrainingRhythmNote> BuildSchedule()
        {
            var notes = new List<TrainingRhythmNote>(
                TotalRepetitions);
            var beatCursor = LeadInBeats;
            var noteIndex = 0;

            for (var blockIndex = 0;
                 blockIndex < Blocks.Count;
                 blockIndex++)
            {
                var block = Blocks[blockIndex];
                if (block == null)
                {
                    continue;
                }

                for (var repetition = 0;
                     repetition < block.Repetitions;
                     repetition++)
                {
                    var upBeat =
                        beatCursor +
                        repetition * block.BeatsPerRep;
                    notes.Add(
                        new TrainingRhythmNote(
                            noteIndex++,
                            blockIndex,
                            repetition,
                            block.Kind,
                            block.RequiredSides,
                            upBeat,
                            upBeat +
                            block.UpToDownBeats));
                }

                beatCursor +=
                    block.Repetitions *
                    block.BeatsPerRep;
                if (blockIndex < Blocks.Count - 1)
                {
                    beatCursor += block.TransitionBeats;
                }
            }

            return notes;
        }

        public static string GetDisplayName(BodyRepKind kind)
        {
            return kind == BodyRepKind.ElbowFlexion
                ? "ARM CURL"
                : "SIDE RAISE";
        }

        public static int GetRequiredSideCount(
            TrainingSideMask sides)
        {
            var count = 0;
            if ((sides & TrainingSideMask.Left) != 0)
            {
                count++;
            }

            if ((sides & TrainingSideMask.Right) != 0)
            {
                count++;
            }

            return count;
        }

        private void OnValidate()
        {
            _leadInBeats = LeadInBeats;
            _outroBeats = OutroBeats;
            if (_blocks == null)
            {
                _blocks = Array.Empty<TrainingRhythmBlock>();
                return;
            }

            foreach (var block in _blocks)
            {
                block?.Validate();
            }
        }
    }
}
