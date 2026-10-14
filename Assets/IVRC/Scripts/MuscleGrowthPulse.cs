using System;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// How hard and how long one repetition rings. Serialized on the
    /// controller so the values can be tuned against the headset rather than
    /// guessed here.
    /// </summary>
    [Serializable]
    public struct MusclePulseSettings
    {
        [Tooltip(
            "Seconds for a rep's glow to fall to half. A repetition takes " +
            "four beats, so at 120 BPM they land two seconds apart and the " +
            "flash has room; it stays short because a flash is a punctuation " +
            "mark, not a state.")]
        [Min(0.01f)] public float GlowHalfLifeSeconds;

        [Tooltip(
            "Seconds for the whole milestone move: the dip before it, the " +
            "swell, the hold, and the return. Unlike a decay this has an end, " +
            "so the body is back where it was before the next milestone " +
            "arrives and each one reads as its own event.")]
        [Min(0.05f)] public float OvershootSeconds;

        [Tooltip(
            "Glow for a completed rep whose timing was missed. Above zero " +
            "so the movement is still acknowledged, well below a clean hit " +
            "so it cannot be mistaken for one.")]
        [Range(0f, 1f)] public float MissedTimingGlow;

        [Tooltip("Glow for a perfectly timed rep.")]
        [Range(0f, 1f)] public float PerfectGlow;

        [Tooltip(
            "How far the body swells on an ordinary repetition. A rep takes " +
            "two seconds, so a move of well under half of that leaves the " +
            "body still for most of the gap: the muscle answers each rep " +
            "rather than bouncing continuously. Set to 0 to go back to " +
            "milestones being the only time the shape moves.")]
        [Range(0f, 1f)] public float RepOvershoot;

        [Tooltip(
            "Reps of one muscle group between the larger milestone swells. " +
            "The milestone has to stay rarer than the ordinary rep swell, " +
            "because a thing that happens every time is not an event.")]
        [Min(1)] public int RepsPerMilestone;

        [Tooltip(
            "How far past its earned growth a group is pushed at a " +
            "milestone, in normalized growth, where 1 is the fully trained " +
            "shape. It competes with the growth already arriving every rep " +
            "(0.0625 on the current chart), so a milestone has to be several " +
            "times that before it reads as an event rather than as more of " +
            "the same. A group already at full growth is driven past the " +
            "trained shape rather than being left with nowhere to go.")]
        [Range(0f, 1f)] public float MilestoneOvershoot;

        [Tooltip(
            "How much of the ordinary rep flash a milestone keeps. Left at 1 " +
            "now that the flash is quiet enough overall not to wash out the " +
            "silhouette; lower it again if a bright setting brings the " +
            "problem back.")]
        [Range(0f, 1f)] public float MilestoneGlowScale;

        public static MusclePulseSettings Default =>
            new MusclePulseSettings
            {
                GlowHalfLifeSeconds = 0.16f,
                OvershootSeconds = 0.55f,
                MissedTimingGlow = 0.18f,
                PerfectGlow = 1f,
                RepOvershoot = 0.15f,
                RepsPerMilestone = 4,
                MilestoneOvershoot = 0.4f,
                MilestoneGlowScale = 1f
            };

        /// <summary>
        /// A copy with every field forced back into a usable range. Called
        /// before use so an unset or hand-edited struct cannot stall the
        /// decay or drive the shape past its target.
        /// </summary>
        public MusclePulseSettings Sanitized()
        {
            return new MusclePulseSettings
            {
                GlowHalfLifeSeconds = SanitizePositive(
                    GlowHalfLifeSeconds,
                    0.16f),
                OvershootSeconds = Mathf.Max(
                    0.05f,
                    SanitizePositive(
                        OvershootSeconds,
                        0.55f)),
                MissedTimingGlow = SanitizeNormalized(
                    MissedTimingGlow,
                    0.18f),
                PerfectGlow = SanitizeNormalized(
                    PerfectGlow,
                    1f),
                RepOvershoot = SanitizeNormalized(
                    RepOvershoot,
                    0.15f),
                RepsPerMilestone = Mathf.Max(
                    1,
                    RepsPerMilestone),
                MilestoneOvershoot = SanitizeNormalized(
                    MilestoneOvershoot,
                    0.4f),
                MilestoneGlowScale = SanitizeNormalized(
                    MilestoneGlowScale,
                    1f)
            };
        }

        private static float SanitizePositive(
            float value,
            float fallback)
        {
            return float.IsNaN(value) ||
                   float.IsInfinity(value) ||
                   value <= 0f
                ? fallback
                : Mathf.Max(0.01f, value);
        }

        private static float SanitizeNormalized(
            float value,
            float fallback)
        {
            return float.IsNaN(value) ||
                   float.IsInfinity(value)
                ? fallback
                : Mathf.Clamp01(value);
        }
    }

    /// <summary>
    /// One muscle group's response to a single scored repetition. The light
    /// rises instantly and decays; the shape runs a move with a beginning and
    /// an end.
    ///
    /// The underlying growth stays continuous, because the cuff squeezes the
    /// arm for the whole exercise and a body that did not change while it was
    /// being squeezed would contradict what the participant feels. What this
    /// adds is the moment. A value that climbs steadily goes unnoticed; one
    /// that jumps and settles is seen.
    ///
    /// Owns no Unity object and reads no clock, so every path is reachable
    /// from an EditMode test.
    /// </summary>
    public struct MuscleGrowthPulse
    {
        private const float NotRunning = -1f;

        private float _overshootPeak;
        private float _overshootSeconds;
        private float _overshootElapsed;

        /// <summary>Emission strength, 0 to 1.</summary>
        public float Glow { get; private set; }

        /// <summary>
        /// Timing of the repetition that last struck this group, kept so the
        /// glow can be coloured by the same verdict the sound reports.
        /// </summary>
        public float Accuracy { get; private set; }

        /// <summary>
        /// Shape offset on top of what this group has earned. Negative during
        /// the dip that precedes a milestone and again just after it, so a
        /// caller must not assume this is positive.
        /// </summary>
        public float Overshoot =>
            HasOvershoot
                ? MusclePulseMath.EvaluateOvershoot(
                    _overshootElapsed,
                    _overshootPeak,
                    _overshootSeconds)
                : 0f;

        public bool HasOvershoot =>
            _overshootElapsed >= 0f && _overshootPeak > 0f;

        public bool IsActive => Glow > 0f || HasOvershoot;

        public TrainingTimingGrade Grade =>
            TrainingRhythmMath.GetTimingGrade(Accuracy);

        /// <summary>
        /// A repetition landed on this group. The light rises immediately,
        /// because the participant has just finished the movement and any ramp
        /// would read as lag.
        ///
        /// Brightness only ever increases: a weak rep arriving while a strong
        /// one is still ringing must not darken the body mid-flash. The
        /// colour, in contrast, always follows the newest rep, because that
        /// is the verdict being reported.
        ///
        /// A milestone restarts the shape move from the top rather than
        /// blending into whatever was still running. Two overlapping swells
        /// read as one long wobble, which is what this replaced.
        /// </summary>
        public void Strike(
            float glow,
            float overshoot,
            float accuracy,
            float overshootSeconds)
        {
            Accuracy = Sanitize(accuracy, 0f);
            Glow = Mathf.Max(
                Glow,
                Sanitize(glow, 0f));

            overshoot = Sanitize(overshoot, 0f);
            if (overshoot <= 0f)
            {
                return;
            }

            // Set rather than keep the larger. The move restarts from the top,
            // so the new repetition owns it; carrying the previous peak over
            // would latch every rep after the first milestone at milestone
            // size and flatten the distinction the two sizes exist to draw.
            _overshootPeak = overshoot;
            _overshootSeconds =
                float.IsNaN(overshootSeconds) ||
                float.IsInfinity(overshootSeconds)
                    ? 0.42f
                    : Mathf.Max(0.05f, overshootSeconds);
            _overshootElapsed = 0f;
        }

        /// <summary>
        /// Advances by elapsed time rather than per frame, so the pulse looks
        /// the same whether the headset is holding 72 fps or dropping frames.
        /// </summary>
        public void Advance(
            float deltaTime,
            float glowHalfLifeSeconds)
        {
            if (float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime) ||
                deltaTime <= 0f)
            {
                return;
            }

            Glow = MusclePulseMath.Decay(
                Glow,
                deltaTime,
                glowHalfLifeSeconds);

            if (!HasOvershoot)
            {
                return;
            }

            _overshootElapsed += deltaTime;
            if (_overshootElapsed >= _overshootSeconds)
            {
                // Finished, and finished exactly: the shape is back where the
                // earned growth alone would put it.
                _overshootElapsed = NotRunning;
                _overshootPeak = 0f;
            }
        }

        public void Clear()
        {
            Glow = 0f;
            Accuracy = 0f;
            _overshootPeak = 0f;
            _overshootSeconds = 0f;
            _overshootElapsed = NotRunning;
        }

        private static float Sanitize(
            float value,
            float fallback)
        {
            return float.IsNaN(value) ||
                   float.IsInfinity(value)
                ? fallback
                : Mathf.Clamp01(value);
        }
    }

    public static class MusclePulseMath
    {
        /// <summary>
        /// Below this the pulse is snapped to zero. Without it the value
        /// approaches zero forever and the presentation never settles, which
        /// keeps property blocks being rewritten every frame for nothing.
        /// </summary>
        public const float SilenceThreshold = 0.001f;

        public static float Decay(
            float value,
            float deltaTime,
            float halfLifeSeconds)
        {
            if (float.IsNaN(value) ||
                float.IsInfinity(value) ||
                value <= SilenceThreshold)
            {
                return 0f;
            }

            if (float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime) ||
                deltaTime <= 0f)
            {
                return value;
            }

            var halfLife =
                float.IsNaN(halfLifeSeconds) ||
                float.IsInfinity(halfLifeSeconds)
                    ? 0.16f
                    : Mathf.Max(0.01f, halfLifeSeconds);
            var decayed = value * Mathf.Exp(
                -Mathf.Log(2f) * deltaTime / halfLife);
            return decayed <= SilenceThreshold
                ? 0f
                : decayed;
        }

        // The milestone move, as fractions of its total length. A pop is read
        // from its edges, so it is given edges: a dip that says something is
        // coming, a rise too fast to follow, a hold long enough to actually
        // see at 72 fps, and a return that goes slightly too far before it
        // settles. Exponential decay had none of these — it left immediately
        // and never quite arrived, so the body sat permanently half-inflated
        // and no single milestone ever finished.
        private const float AnticipationShare = 0.14f;
        private const float AttackShare = 0.05f;
        private const float HoldShare = 0.21f;
        private const float ReturnShare = 0.60f;

        /// <summary>
        /// How far below the resting shape the anticipation dips, as a
        /// fraction of the peak. Costs nothing and makes the same peak look
        /// bigger, because the eye measures the whole distance travelled.
        /// </summary>
        private const float AnticipationDepth = 0.15f;

        /// <summary>
        /// How far past resting the return goes before settling. The overshoot
        /// on the way back is what makes it read as flesh rather than as a
        /// slider being dragged.
        /// </summary>
        private const float ReturnUndershoot = 0.08f;

        /// <summary>
        /// The shape offset at one moment of a milestone, which may be
        /// negative: the body is briefly thinner both just before the swell
        /// and just after it.
        ///
        /// Returns 0 outside the move, so a caller can tell it has finished.
        /// </summary>
        public static float EvaluateOvershoot(
            float secondsSinceStrike,
            float peak,
            float totalSeconds)
        {
            if (float.IsNaN(secondsSinceStrike) ||
                float.IsInfinity(secondsSinceStrike) ||
                float.IsNaN(peak) ||
                float.IsInfinity(peak) ||
                secondsSinceStrike < 0f ||
                peak <= 0f)
            {
                return 0f;
            }

            totalSeconds =
                float.IsNaN(totalSeconds) ||
                float.IsInfinity(totalSeconds)
                    ? 0.42f
                    : Mathf.Max(0.05f, totalSeconds);
            var t = secondsSinceStrike / totalSeconds;
            if (t >= 1f)
            {
                return 0f;
            }

            var dip = -AnticipationDepth * peak;
            var undershoot = -ReturnUndershoot * peak;

            if (t < AnticipationShare)
            {
                return Mathf.Lerp(
                    0f,
                    dip,
                    Smooth(t / AnticipationShare));
            }

            t -= AnticipationShare;
            if (t < AttackShare)
            {
                return Mathf.Lerp(
                    dip,
                    peak,
                    Smooth(t / AttackShare));
            }

            t -= AttackShare;
            if (t < HoldShare)
            {
                return peak;
            }

            t -= HoldShare;
            var returnProgress = t / ReturnShare;

            // Most of the return falls past resting, the rest eases back up.
            const float FallShare = 0.7f;
            if (returnProgress < FallShare)
            {
                return Mathf.Lerp(
                    peak,
                    undershoot,
                    Smooth(returnProgress / FallShare));
            }

            return Mathf.Lerp(
                undershoot,
                0f,
                Smooth(
                    (returnProgress - FallShare) /
                    (1f - FallShare)));
        }

        private static float Smooth(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        /// <summary>
        /// Brightness for one repetition's timing. A completed but mistimed
        /// rep still lights up, faintly: the movement did happen, and an
        /// exhibition floor is loud enough that the participant may not hear
        /// the verdict.
        /// </summary>
        public static float GlowForAccuracy(
            float accuracy,
            float missedTimingGlow,
            float perfectGlow)
        {
            accuracy =
                float.IsNaN(accuracy) ||
                float.IsInfinity(accuracy)
                    ? 0f
                    : Mathf.Clamp01(accuracy);
            return Mathf.Lerp(
                Mathf.Clamp01(missedTimingGlow),
                Mathf.Clamp01(perfectGlow),
                accuracy);
        }

        /// <summary>
        /// True on every n-th repetition of one muscle group, counting from
        /// the first. Reps before the first milestone glow but do not move
        /// the body.
        /// </summary>
        public static bool IsMilestone(
            int repCount,
            int repsPerMilestone)
        {
            repsPerMilestone = Mathf.Max(1, repsPerMilestone);
            return repCount > 0 &&
                   repCount % repsPerMilestone == 0;
        }
    }

    /// <summary>
    /// Every muscle group's pulse, advanced together.
    ///
    /// Groups are kept apart on purpose. A whole body flashing at once says
    /// only "something counted"; one arm flashing says which movement counted,
    /// which is the correspondence the experience is built on.
    /// </summary>
    public sealed class MusclePulseState
    {
        private static readonly int ChannelCount =
            Enum.GetValues(
                typeof(MuscleGrowthChannel)).Length;

        private readonly MuscleGrowthPulse[] _pulses =
            new MuscleGrowthPulse[ChannelCount];
        private readonly int[] _repCounts =
            new int[ChannelCount];

        /// <summary>
        /// True while anything is still ringing, so a caller can stop
        /// republishing once the body has settled.
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>Brightest group, for whole-body presentation.</summary>
        public float StrongestGlow { get; private set; }

        /// <summary>Timing that lit the brightest group.</summary>
        public float StrongestGlowAccuracy { get; private set; }

        public int GetRepCount(MuscleGrowthChannel channel)
        {
            return IsAddressable(channel)
                ? _repCounts[(int)channel]
                : 0;
        }

        public float GetGlow(MuscleGrowthChannel channel)
        {
            return IsAddressable(channel)
                ? _pulses[(int)channel].Glow
                : 0f;
        }

        public float GetOvershoot(MuscleGrowthChannel channel)
        {
            return IsAddressable(channel)
                ? _pulses[(int)channel].Overshoot
                : 0f;
        }

        public float GetAccuracy(MuscleGrowthChannel channel)
        {
            return IsAddressable(channel)
                ? _pulses[(int)channel].Accuracy
                : 0f;
        }

        public TrainingTimingGrade GetGrade(
            MuscleGrowthChannel channel)
        {
            return TrainingRhythmMath.GetTimingGrade(
                GetAccuracy(channel));
        }

        /// <summary>
        /// Every group's overshoot together, for an output that shapes the
        /// body. Overall is left at zero: it drives the whole-body shape, and
        /// a body swelling as a unit is the restlessness the milestone was
        /// meant to replace.
        ///
        /// This travels beside the growth channels rather than added into
        /// them, because growth is normalized and scored, while an overshoot
        /// is allowed to push the shape past fully trained.
        /// </summary>
        public MuscleGrowthChannels CaptureOvershoot()
        {
            return new MuscleGrowthChannels(
                0f,
                GetOvershoot(
                    MuscleGrowthChannel.ElbowFlexionLeft),
                GetOvershoot(
                    MuscleGrowthChannel.ElbowFlexionRight),
                GetOvershoot(
                    MuscleGrowthChannel.ArmRaiseLeft),
                GetOvershoot(
                    MuscleGrowthChannel.ArmRaiseRight),
                GetOvershoot(
                    MuscleGrowthChannel.ChestLeft),
                GetOvershoot(
                    MuscleGrowthChannel.ChestRight));
        }

        /// <summary>
        /// Credits one completed repetition to the group it trained, and
        /// returns true when that rep was a milestone.
        ///
        /// Derived groups such as the chest are not struck. They are worked by
        /// both arm exercises, so a flash there would not name a movement, and
        /// naming the movement is the whole point.
        /// </summary>
        public bool ReportRep(
            MuscleGrowthChannel channel,
            float accuracy,
            MusclePulseSettings settings)
        {
            if (!IsAddressable(channel) ||
                MuscleGrowthChannels.IsDerived(channel))
            {
                return false;
            }

            settings = settings.Sanitized();
            var index = (int)channel;
            _repCounts[index]++;

            var isMilestone = MusclePulseMath.IsMilestone(
                _repCounts[index],
                settings.RepsPerMilestone);
            var glow = MusclePulseMath.GlowForAccuracy(
                accuracy,
                settings.MissedTimingGlow,
                settings.PerfectGlow);
            _pulses[index].Strike(
                // A milestone dims its own flash. Emission washes out the
                // silhouette, and the one rep that also changes the body is
                // the worst rep to hide the body on.
                isMilestone
                    ? glow * settings.MilestoneGlowScale
                    : glow,
                // Every rep answers, a milestone answers louder. The shape
                // says the movement happened; how well it was timed is the
                // light's job, so this does not scale with accuracy.
                isMilestone
                    ? settings.MilestoneOvershoot
                    : settings.RepOvershoot,
                accuracy,
                settings.OvershootSeconds);
            RefreshSummary();
            return isMilestone;
        }

        public void Advance(
            float deltaTime,
            MusclePulseSettings settings)
        {
            if (!IsActive)
            {
                return;
            }

            settings = settings.Sanitized();
            for (var index = 0;
                 index < _pulses.Length;
                 index++)
            {
                _pulses[index].Advance(
                    deltaTime,
                    settings.GlowHalfLifeSeconds);
            }

            RefreshSummary();
        }

        /// <summary>
        /// Silences every group and forgets the rep counts, so a retry starts
        /// its milestones from the beginning instead of inheriting them.
        /// </summary>
        public void Clear()
        {
            for (var index = 0;
                 index < _pulses.Length;
                 index++)
            {
                _pulses[index].Clear();
                _repCounts[index] = 0;
            }

            IsActive = false;
            StrongestGlow = 0f;
            StrongestGlowAccuracy = 0f;
        }

        private void RefreshSummary()
        {
            var active = false;
            var strongestGlow = 0f;
            var strongestAccuracy = 0f;
            for (var index = 0;
                 index < _pulses.Length;
                 index++)
            {
                var pulse = _pulses[index];
                active |= pulse.IsActive;
                if (pulse.Glow <= strongestGlow)
                {
                    continue;
                }

                strongestGlow = pulse.Glow;
                strongestAccuracy = pulse.Accuracy;
            }

            IsActive = active;
            StrongestGlow = strongestGlow;
            StrongestGlowAccuracy = strongestAccuracy;
        }

        private static bool IsAddressable(
            MuscleGrowthChannel channel)
        {
            var index = (int)channel;
            return index >= 0 && index < ChannelCount;
        }
    }
}
