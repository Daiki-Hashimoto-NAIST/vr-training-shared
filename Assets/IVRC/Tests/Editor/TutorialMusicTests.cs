using NUnit.Framework;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class TutorialMusicTests
    {
        /// <summary>
        /// The introduction rehearses Training's tempo, so the loop has to be
        /// a whole number of bars at that tempo. A loop that is not wraps
        /// early and drags the beat out of step with the movement being
        /// taught.
        /// </summary>
        [TestCase(120f, 4f)]
        [TestCase(150f, 3.2f)]
        [TestCase(90f, 5.3333f)]
        public void LoopIsAWholeNumberOfBars(
            float tempo,
            float expectedSeconds)
        {
            var duration =
                TutorialDrumBed.GetLoopDurationSeconds(tempo);

            Assert.That(
                duration,
                Is.EqualTo(expectedSeconds).Within(0.001f));

            var secondsPerBeat = 60f / tempo;
            var beats = duration / secondsPerBeat;
            Assert.That(
                beats,
                Is.EqualTo(
                    TutorialDrumBed.BeatsPerBar *
                    TutorialDrumBed.BarCount).Within(0.0001f));
        }

        [TestCase(20f)]
        [TestCase(500f)]
        [TestCase(float.NaN)]
        public void AnImpossibleTempoStillProducesAUsableLoop(
            float tempo)
        {
            var duration =
                TutorialDrumBed.GetLoopDurationSeconds(tempo);

            Assert.That(duration, Is.GreaterThan(0f));
            Assert.That(
                float.IsNaN(duration) || float.IsInfinity(duration),
                Is.False);
        }

        /// <summary>
        /// The bed loops onto itself, so a non-zero sample at either end is a
        /// click once per bar — which on a beat sounds like part of the
        /// rhythm and is very hard to place afterwards.
        /// </summary>
        [Test]
        public void TheLoopSeamIsSilentAtBothEnds()
        {
            var samples = new float[TutorialDrumBed.SampleRate];
            for (var index = 0; index < samples.Length; index++)
            {
                samples[index] = 0.8f;
            }

            TutorialDrumBed.FadeLoopSeam(samples);

            Assert.That(samples[0], Is.Zero.Within(0.0001f));
            Assert.That(
                samples[samples.Length - 1],
                Is.Zero.Within(0.0001f));
            Assert.That(
                samples[samples.Length / 2],
                Is.EqualTo(0.8f).Within(0.0001f),
                "Only the seam is touched.");
        }

        [Test]
        public void AShortBufferIsLeftAloneRatherThanCrashing()
        {
            var tiny = new float[4];
            for (var index = 0; index < tiny.Length; index++)
            {
                tiny[index] = 0.5f;
            }

            Assert.DoesNotThrow(
                () => TutorialDrumBed.FadeLoopSeam(tiny));
            Assert.DoesNotThrow(
                () => TutorialDrumBed.FadeLoopSeam(null));
            Assert.That(tiny[0], Is.EqualTo(0.5f));
        }

        /// <summary>
        /// A rehearsal at a different speed teaches the wrong tempo, so the
        /// bed is written at whatever Training is running.
        /// </summary>
        [Test]
        public void TheBedIsWrittenAtTheClocksTempo()
        {
            var host = new GameObject("TutorialMusicTempoTest");
            try
            {
                var clock = host.AddComponent<RhythmClock>();
                clock.SetTempo(150f);
                var music =
                    host.AddComponent<TutorialMusicController>();

                Assert.That(
                    music.BeatsPerMinute,
                    Is.EqualTo(150f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
