using NUnit.Framework;

namespace IVRC.Tests
{
    public sealed class TrainingMusicControllerTests
    {
        /// <summary>
        /// A sped-up bed consumes clip faster than wall time, so joining late
        /// has to seek proportionally further in. Ignoring the rate leaves the
        /// music a little further out on every loop, which reads as "the music
        /// feels off" rather than as a bug worth looking for.
        /// </summary>
        [Test]
        public void ASpedUpBedSeeksFurtherIntoTheClip()
        {
            const double origin = 10d;

            // BuildSchedule enforces a 0.02 s minimum lead, so joining at
            // 11.98 is scheduled at exactly 12.00 and the arithmetic below
            // stays readable.
            const double joinAt = 11.98d;

            var atNormalSpeed = TrainingMusicTiming.BuildSchedule(
                origin,
                joinAt,
                0.5f,
                8f,
                0f,
                1f);
            var atRaisedSpeed = TrainingMusicTiming.BuildSchedule(
                origin,
                joinAt,
                0.5f,
                8f,
                0f,
                1.25f);

            Assert.That(
                atNormalSpeed.ClipTimeSeconds,
                Is.EqualTo(2f).Within(0.0001f));
            Assert.That(
                atRaisedSpeed.ClipTimeSeconds,
                Is.EqualTo(2.5f).Within(0.0001f),
                "Two seconds of DSP at 1.25x is 2.5 seconds of clip.");
        }

        [Test]
        public void ASpedUpBedWrapsSooner()
        {
            // Scheduled at 10.00 after the minimum lead. Ten seconds of DSP
            // at 1.25x is 12.5 seconds of clip, which is one loop of eight
            // and 4.5 over.
            var schedule = TrainingMusicTiming.BuildSchedule(
                0d,
                9.98d,
                0.5f,
                8f,
                0f,
                1.25f);

            Assert.That(
                schedule.ClipTimeSeconds,
                Is.EqualTo(4.5f).Within(0.0001f));
        }

        [TestCase(120f, 120f, 1f)]
        [TestCase(120f, 150f, 1.25f)]
        [TestCase(120f, 132f, 1.1f)]
        [TestCase(0f, 150f, 1f)]
        [TestCase(120f, 0f, 1f)]
        public void PlaybackRateIsTheTempoRatio(
            float clipTempo,
            float targetTempo,
            float expected)
        {
            Assert.That(
                TrainingMusicTiming.GetPlaybackRateForTempo(
                    clipTempo,
                    targetTempo),
                Is.EqualTo(expected).Within(0.0001f));
        }

        /// <summary>
        /// Unity has no time-stretch, so tempo and pitch are one control. The
        /// figure is worth having in front of you before choosing a tempo:
        /// 120 to 150 is most of a major third, which is audible as a
        /// different recording rather than as a faster one.
        /// </summary>
        [Test]
        public void RaisingTheTempoTransposesTheMusic()
        {
            Assert.That(
                TrainingMusicTiming.GetTranspositionSemitones(1f),
                Is.Zero.Within(0.0001f));
            Assert.That(
                TrainingMusicTiming.GetTranspositionSemitones(1.1f),
                Is.EqualTo(1.65f).Within(0.02f));
            Assert.That(
                TrainingMusicTiming.GetTranspositionSemitones(1.25f),
                Is.EqualTo(3.86f).Within(0.02f));
            Assert.That(
                TrainingMusicTiming.GetTranspositionSemitones(2f),
                Is.EqualTo(12f).Within(0.0001f));
        }

        /// <summary>
        /// Whether a clip is a whole number of beats does not change with
        /// speed: the beats scale with it. A bed that looped cleanly at one
        /// tempo still loops cleanly at another.
        /// </summary>
        [Test]
        public void WholeBeatFitSurvivesASpeedChange()
        {
            const float clipLength = 8f;
            const float secondsPerBeat = 0.5f;
            const float rate = 1.25f;

            Assert.That(
                TrainingMusicTiming.GetNearestWholeBeatErrorSeconds(
                    clipLength,
                    secondsPerBeat),
                Is.Zero.Within(0.0001f));
            Assert.That(
                TrainingMusicTiming.GetNearestWholeBeatErrorSeconds(
                    clipLength / rate,
                    secondsPerBeat / rate),
                Is.Zero.Within(0.0001f));
        }

        [Test]
        public void FutureClockOriginSchedulesAtFirstBeat()
        {
            var schedule = TrainingMusicTiming.BuildSchedule(
                10.1d,
                10d,
                0.5f,
                8f,
                0.05f);

            Assert.That(schedule.IsValid, Is.True);
            Assert.That(schedule.DspTime, Is.EqualTo(10.1d).Within(0.0001d));
            Assert.That(schedule.ClipTimeSeconds, Is.Zero.Within(0.0001f));
        }

        [Test]
        public void LateAttachmentSchedulesAheadAtMatchingMusicPosition()
        {
            var schedule = TrainingMusicTiming.BuildSchedule(
                10d,
                11.2d,
                0.5f,
                8f,
                0.05f);

            Assert.That(schedule.IsValid, Is.True);
            Assert.That(schedule.DspTime, Is.EqualTo(11.25d).Within(0.0001d));
            Assert.That(
                schedule.ClipTimeSeconds,
                Is.EqualTo(1.25f).Within(0.0001f));
        }

        [Test]
        public void LateScheduleWrapsAtLoopBoundary()
        {
            var schedule = TrainingMusicTiming.BuildSchedule(
                2d,
                19.1d,
                0.5f,
                8f,
                0.05f);

            Assert.That(schedule.IsValid, Is.True);
            Assert.That(
                schedule.ClipTimeSeconds,
                Is.EqualTo(1.15f).Within(0.0001f));
        }

        [Test]
        public void PlaceholderIsExactlyFourBarsLong()
        {
            Assert.That(
                TrainingMusicTiming.GetPlaceholderLoopDurationSeconds(
                    120f,
                    4),
                Is.EqualTo(8f).Within(0.0001f));
            Assert.That(
                TrainingMusicTiming.GetNearestWholeBeatErrorSeconds(
                    8f,
                    0.5f),
                Is.Zero.Within(0.0001f));
        }

        [Test]
        public void InvalidTimingDoesNotProduceSchedule()
        {
            Assert.That(
                TrainingMusicTiming.BuildSchedule(
                    double.NaN,
                    1d,
                    0.5f,
                    8f,
                    0.05f).IsValid,
                Is.False);
            Assert.That(
                TrainingMusicTiming.BuildSchedule(
                    1d,
                    1d,
                    0f,
                    8f,
                    0.05f).IsValid,
                Is.False);
            Assert.That(
                TrainingMusicTiming.BuildSchedule(
                    1d,
                    1d,
                    0.5f,
                    0f,
                    0.05f).IsValid,
                Is.False);
        }

        [Test]
        public void ForegroundCueDucksThenReleasesSmoothly()
        {
            Assert.That(
                AudioDuckingMath.GetTargetMultiplier(true, 0.42f),
                Is.EqualTo(0.42f).Within(0.0001f));
            Assert.That(
                AudioDuckingMath.GetTargetMultiplier(false, 0.42f),
                Is.EqualTo(1f).Within(0.0001f));

            var attacked = AudioDuckingMath.MoveMultiplier(
                1f,
                0.4f,
                0.02f,
                0.04f,
                0.2f);
            var released = AudioDuckingMath.MoveMultiplier(
                attacked,
                1f,
                0.02f,
                0.04f,
                0.2f);
            Assert.That(attacked, Is.LessThan(1f));
            Assert.That(released, Is.GreaterThan(attacked));
            Assert.That(released, Is.LessThanOrEqualTo(1f));
        }
        [Test]
        public void BedJoinsWhereTheTrackPicksUp()
        {
            // Joining exactly on the first beat should start six seconds in,
            // not at the clip's first sample.
            var schedule = TrainingMusicTiming.BuildSchedule(
                firstBeatDspTime: 100d,
                currentDspTime: 100d,
                secondsPerBeat: 60f / 154f,
                clipLengthSeconds: 94.145f,
                schedulingLeadSeconds: 0.05f,
                playbackRate: 1f,
                clipStartSeconds: 6f);

            Assert.That(schedule.IsValid, Is.True);
            Assert.That(
                schedule.ClipTimeSeconds,
                Is.EqualTo(6.05f).Within(0.001f),
                "Six seconds in, plus the lead the scheduler had " +
                "to wait.");
        }

        [Test]
        public void TheOffsetCarriesThroughEveryLap()
        {
            // A lap later the bed must be back at the pick-up point rather
            // than drifting six seconds further in each time.
            const float clipLength = 94.145f;
            var schedule = TrainingMusicTiming.BuildSchedule(
                firstBeatDspTime: 100d,
                currentDspTime: 100d + clipLength,
                secondsPerBeat: 60f / 154f,
                clipLengthSeconds: clipLength,
                schedulingLeadSeconds: 0.02f,
                playbackRate: 1f,
                clipStartSeconds: 6f);

            Assert.That(
                schedule.ClipTimeSeconds,
                Is.EqualTo(6.02f).Within(0.001f));
        }

        [Test]
        public void ABrokenOffsetStartsAtTheBeginning()
        {
            foreach (var broken in new[]
                     {
                         float.NaN,
                         float.PositiveInfinity,
                         -4f
                     })
            {
                var schedule = TrainingMusicTiming.BuildSchedule(
                    firstBeatDspTime: 100d,
                    currentDspTime: 100d,
                    secondsPerBeat: 0.5f,
                    clipLengthSeconds: 60f,
                    schedulingLeadSeconds: 0.02f,
                    playbackRate: 1f,
                    clipStartSeconds: broken);

                Assert.That(
                    schedule.ClipTimeSeconds,
                    Is.EqualTo(0.02f).Within(0.001f));
            }
        }

        [Test]
        public void OffsetIsWrappedWhenItOvershootsTheClip()
        {
            var schedule = TrainingMusicTiming.BuildSchedule(
                firstBeatDspTime: 100d,
                currentDspTime: 100d,
                secondsPerBeat: 0.5f,
                clipLengthSeconds: 10f,
                schedulingLeadSeconds: 0.02f,
                playbackRate: 1f,
                clipStartSeconds: 26f);

            Assert.That(
                schedule.ClipTimeSeconds,
                Is.EqualTo(6.02f).Within(0.001f));
        }

        [Test]
        public void TheDropIsOnABarLineRatherThanARoundNumberOfSeconds()
        {
            // The track picks up on the seventeenth beat, which is four
            // bars in. At 154 BPM that is 6.2338 s, not the 6.0 s it was
            // first taken to be — 6.0 s is beat 15.4, which is between two
            // beats and belongs to neither.
            var spb = 60f / 154f;
            Assert.That(
                TrainingMusicTiming.GetPickupSeconds(17, spb),
                Is.EqualTo(6.2338f).Within(0.001f));

            var start = TrainingMusicTiming.GetClipStartSeconds(
                TrainingMusicTiming.GetPickupSeconds(17, spb),
                4,
                spb);
            Assert.That(
                start,
                Is.EqualTo(4.6753f).Within(0.001f),
                "Twelve beats of clip play before the count-in starts.");
            Assert.That(
                start / spb,
                Is.EqualTo(12f).Within(0.001f));
        }

        [Test]
        public void ThePickUpBeatIsOneBased()
        {
            Assert.That(
                TrainingMusicTiming.GetPickupSeconds(1, 0.5f),
                Is.EqualTo(0f),
                "The first beat of the clip is beat 1, at time zero.");
            Assert.That(
                TrainingMusicTiming.GetPickupSeconds(0, 0.5f),
                Is.EqualTo(0f));
        }

        [Test]
        public void ThePickUpLandsOnTheBeatTheMovementStartsOn()
        {
            // 154 BPM, a four beat lead-in, and a track that picks up six
            // seconds in: the bed has to enter 1.558 s of music earlier so
            // the pick-up and the first repetition arrive together.
            var start = TrainingMusicTiming.GetClipStartSeconds(
                pickupSeconds: 6f,
                pickupBeat: 4,
                secondsPerBeat: 60f / 154f);

            Assert.That(start, Is.EqualTo(4.4416f).Within(0.001f));
            Assert.That(
                start + 4 * (60f / 154f),
                Is.EqualTo(6f).Within(0.001f),
                "Four beats after the bed enters, the track picks up.");
        }

        [Test]
        public void TheOffsetFollowsTheTempoRatherThanBeingTypedIn()
        {
            // Halve the tempo and the lead-in takes twice as long, so the
            // bed has to enter that much further back.
            var fast = TrainingMusicTiming.GetClipStartSeconds(6f, 4, 0.4f);
            var slow = TrainingMusicTiming.GetClipStartSeconds(6f, 4, 0.8f);

            Assert.That(fast, Is.EqualTo(4.4f).Within(0.001f));
            Assert.That(slow, Is.EqualTo(2.8f).Within(0.001f));
        }

        [Test]
        public void APickUpEarlierThanTheLeadInStartsAtTheHead()
        {
            Assert.That(
                TrainingMusicTiming.GetClipStartSeconds(1f, 8, 0.4f),
                Is.EqualTo(0f),
                "Never a negative clip position.");
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(0f)]
        public void ABrokenTempoLeavesThePickUpWhereItIs(
            float secondsPerBeat)
        {
            Assert.That(
                TrainingMusicTiming.GetClipStartSeconds(
                    6f, 4, secondsPerBeat),
                Is.EqualTo(6f));
        }

    }
}
