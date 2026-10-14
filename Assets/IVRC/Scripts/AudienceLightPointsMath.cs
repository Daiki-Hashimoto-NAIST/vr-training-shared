using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Pure helpers shared by the audience light points and EditMode tests.
    ///
    /// The crowd is a field of small lights rather than bodies, so the only
    /// thing it can say is how bright each point is and when. A single
    /// excitement value per point carries that, and the component decides
    /// what excitement looks like.
    /// </summary>
    public static class AudienceLightPointsMath
    {
        /// <summary>
        /// Where a point sits in the wave, taken from how far back it is.
        ///
        /// Phase from distance rather than from array index means the
        /// shimmer rolls away from the stage as one movement, which is what
        /// a room reacting looks like. Index order would sweep along
        /// whatever order the seats happened to be captured in.
        /// </summary>
        public static float CalculatePhase(
            float distanceFromStage,
            float metresPerCycle,
            float jitterTurns)
        {
            if (!IsFinite(distanceFromStage) ||
                !IsFinite(metresPerCycle) ||
                metresPerCycle <= Mathf.Epsilon)
            {
                return 0f;
            }

            var turns = distanceFromStage / metresPerCycle;
            if (IsFinite(jitterTurns))
            {
                turns += jitterTurns;
            }

            return turns * Mathf.PI * 2f;
        }

        /// <summary>
        /// A repeatable offset per seat, so neighbours do not pulse in
        /// lockstep. Derived from the position rather than stored, because
        /// the same seat must look the same on every run.
        /// </summary>
        public static float CalculateJitterTurns(
            Vector3 seatPosition,
            float amount)
        {
            if (!IsFinite(amount) || amount <= 0f)
            {
                return 0f;
            }

            if (!IsFinite(seatPosition.x) ||
                !IsFinite(seatPosition.y) ||
                !IsFinite(seatPosition.z))
            {
                return 0f;
            }

            // Fractional part of an irrational-ish mix of the coordinates.
            // Any hash would do; this one needs no allocation and no state.
            var raw =
                seatPosition.x * 12.9898f +
                seatPosition.y * 4.1414f +
                seatPosition.z * 78.233f;
            var unit = raw - Mathf.Floor(raw);
            return (unit - 0.5f) * amount;
        }

        /// <summary>
        /// How lit one point is right now, from 0 for a resting room to 1
        /// at the top of the wave while the crowd is fully behind the
        /// performer.
        ///
        /// At rest the whole field holds still: a crowd that shimmers when
        /// nothing is happening reads as decoration, not as a response.
        /// </summary>
        public static float CalculateExcitement(
            float reaction,
            float timeSeconds,
            float phase,
            float waveFrequencyHz)
        {
            if (!IsFinite(reaction) || reaction <= 0f)
            {
                return 0f;
            }

            reaction = Mathf.Clamp01(reaction);
            if (!IsFinite(timeSeconds) ||
                !IsFinite(phase) ||
                !IsFinite(waveFrequencyHz))
            {
                return reaction;
            }

            var wave = Mathf.Sin(
                timeSeconds * waveFrequencyHz * Mathf.PI * 2f -
                phase);
            return reaction * (0.5f + 0.5f * wave);
        }

        /// <summary>
        /// The seated head the light stands in for, from the seat it sits
        /// on. Kept separate from the captured positions so the height can
        /// be retuned without capturing the seats again.
        /// </summary>
        public static Vector3 CalculatePointPosition(
            Vector3 seatPosition,
            float seatedHeadHeight)
        {
            if (!IsFinite(seatedHeadHeight))
            {
                return seatPosition;
            }

            seatPosition.y += seatedHeadHeight;
            return seatPosition;
        }

        /// <summary>
        /// A small sideways nudge per seat, so the rows do not read as
        /// drawn with a ruler. Deterministic from the seat, and on a
        /// different mix of the coordinates than the wave's jitter, so a
        /// point that sits left is not also the one that pulses early.
        ///
        /// Applied at runtime rather than baked, so the amount can be
        /// turned down to nothing without capturing the seats again.
        /// </summary>
        public static Vector3 ApplyLateralJitter(
            Vector3 point,
            float metres)
        {
            if (!IsFinite(metres) || metres <= 0f)
            {
                return point;
            }

            if (!IsFinite(point.x) ||
                !IsFinite(point.y) ||
                !IsFinite(point.z))
            {
                return point;
            }

            var raw =
                point.x * 39.3468f +
                point.y * 11.1353f +
                point.z * 27.6119f;
            var unit = raw - Mathf.Floor(raw);
            point.x += (unit - 0.5f) * metres;
            return point;
        }

        private static bool IsFinite(float value)
        {
            return
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }
    }
}
