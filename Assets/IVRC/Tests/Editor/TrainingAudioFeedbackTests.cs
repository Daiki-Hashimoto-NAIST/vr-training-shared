using NUnit.Framework;

namespace IVRC.Tests
{
    public sealed class TrainingAudioFeedbackTests
    {
        [TestCase(true, true, TrainingAudioCue.RhythmClear)]
        [TestCase(true, false, TrainingAudioCue.MotionComplete)]
        [TestCase(false, false, TrainingAudioCue.None)]
        public void RepResultMapsToOneSemanticCue(
            bool motionCompleted,
            bool rhythmCleared,
            TrainingAudioCue expected)
        {
            var up = new TrainingTimingFeedback(
                0,
                1,
                BodyRepKind.ElbowFlexion,
                RepTimingPoint.Up,
                rhythmCleared ? 1f : 0f,
                rhythmCleared);
            var down = new TrainingTimingFeedback(
                0,
                1,
                BodyRepKind.ElbowFlexion,
                RepTimingPoint.Down,
                motionCompleted ? 1f : 0f,
                motionCompleted);
            var result = new TrainingRepTimingResult(
                12,
                0,
                1,
                BodyRepKind.ElbowFlexion,
                TrainingSideMask.Both,
                up,
                down,
                motionCompleted
                    ? TrainingRepFinalizationReason.Completed
                    : TrainingRepFinalizationReason.TimedOut);

            Assert.That(
                TrainingAudioFeedbackRules.GetRepCue(result),
                Is.EqualTo(expected));
        }

        [Test]
        public void CountdownAndGoAreAnnouncedOncePerRun()
        {
            var state = new TrainingAudioCueState();
            state.ResetRun();

            Assert.That(state.TryAnnounceCountdown(3), Is.True);
            Assert.That(state.TryAnnounceCountdown(3), Is.False);
            Assert.That(state.TryAnnounceCountdown(2), Is.True);
            Assert.That(state.TryAnnounceCountdown(2), Is.False);
            Assert.That(state.TryAnnounceCountdown(1), Is.True);
            Assert.That(state.TryAnnounceCountdown(1), Is.False);
            Assert.That(state.TryAnnounceGo(), Is.True);
            Assert.That(state.TryAnnounceGo(), Is.False);

            state.ResetRun();
            Assert.That(state.TryAnnounceCountdown(3), Is.True);
            Assert.That(state.TryAnnounceGo(), Is.True);
        }

        [Test]
        public void RepetitionIsDeduplicatedBySessionAndNote()
        {
            var state = new TrainingAudioCueState();
            state.ResetRun();

            Assert.That(
                state.TryAnnounceRepetition(4, 0),
                Is.True);
            Assert.That(
                state.TryAnnounceRepetition(4, 0),
                Is.False);
            Assert.That(
                state.TryAnnounceRepetition(4, 1),
                Is.True);
            Assert.That(
                state.TryAnnounceRepetition(5, 0),
                Is.True);

            state.ResetRun();
            Assert.That(
                state.TryAnnounceRepetition(4, 0),
                Is.True);
        }

        [Test]
        public void FirstBlockIsSilentAndLaterBlocksAnnounceOnce()
        {
            var state = new TrainingAudioCueState();
            state.ResetRun();

            Assert.That(
                state.TryAnnounceBlockSwitch(7, 0),
                Is.False);
            Assert.That(
                state.TryAnnounceBlockSwitch(7, 0),
                Is.False);
            Assert.That(
                state.TryAnnounceBlockSwitch(7, 1),
                Is.True);
            Assert.That(
                state.TryAnnounceBlockSwitch(7, 1),
                Is.False);
            Assert.That(
                state.TryAnnounceBlockSwitch(7, 2),
                Is.True);

            Assert.That(
                state.TryAnnounceBlockSwitch(8, 0),
                Is.False,
                "A new clock session must initialize silently instead " +
                "of replaying a stale switch cue.");
        }

        [Test]
        public void StrongerMeaningHasHigherAudioPriority()
        {
            Assert.That(
                TrainingAudioFeedbackRules.GetPriority(
                    TrainingAudioCue.Go),
                Is.GreaterThan(
                    TrainingAudioFeedbackRules.GetPriority(
                        TrainingAudioCue.RhythmClear)));
            Assert.That(
                TrainingAudioFeedbackRules.GetPriority(
                    TrainingAudioCue.RhythmClear),
                Is.GreaterThan(
                    TrainingAudioFeedbackRules.GetPriority(
                        TrainingAudioCue.ExerciseSwitch)));
            Assert.That(
                TrainingAudioFeedbackRules.GetPriority(
                    TrainingAudioCue.ExerciseSwitch),
                Is.GreaterThan(
                    TrainingAudioFeedbackRules.GetPriority(
                        TrainingAudioCue.MotionComplete)));
            Assert.That(
                TrainingAudioFeedbackRules.GetPriority(
                    TrainingAudioCue.MotionComplete),
                Is.GreaterThan(
                    TrainingAudioFeedbackRules.GetPriority(
                    TrainingAudioCue.CountdownTick)));
        }

        [TestCase(0f, 0.01f)]
        [TestCase(0.08f, 0.08f)]
        [TestCase(0.20f, 0.20f)]
        [TestCase(1f, 0.20f)]
        [TestCase(float.NaN, 0.01f)]
        [TestCase(float.PositiveInfinity, 0.01f)]
        public void BeatHapticDurationAlwaysRemainsAShortPulse(
            float configuredSeconds,
            float expectedSeconds)
        {
            Assert.That(
                BeatHapticsSafety.ClampPulseDuration(
                    configuredSeconds),
                Is.EqualTo(expectedSeconds).Within(0.0001f));
        }
    }
}
