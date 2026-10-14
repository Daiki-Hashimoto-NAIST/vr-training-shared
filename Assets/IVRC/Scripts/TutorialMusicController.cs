using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Builds a plain eight-beat drum loop in memory, so the introduction can
    /// have a pulse before any audio has been sourced.
    ///
    /// Written at whatever tempo Training runs at. The point of hearing a beat
    /// during the introduction is to arrive at the real thing already moving
    /// at its tempo, and a rehearsal at a different speed teaches the wrong
    /// one.
    /// </summary>
    public static class TutorialDrumBed
    {
        public const int SampleRate = 48000;
        public const int BeatsPerBar = 4;
        public const int BarCount = 2;

        public static float GetLoopDurationSeconds(
            float beatsPerMinute)
        {
            return 60f / SanitizeTempo(beatsPerMinute) *
                   BeatsPerBar * BarCount;
        }

        /// <summary>
        /// Mathf.Clamp returns NaN for a NaN input rather than a bound, so a
        /// broken tempo would otherwise produce a NaN-length clip and no
        /// sound at all, silently.
        /// </summary>
        public static float SanitizeTempo(float beatsPerMinute)
        {
            return float.IsNaN(beatsPerMinute) ||
                   float.IsInfinity(beatsPerMinute)
                ? 120f
                : Mathf.Clamp(beatsPerMinute, 30f, 240f);
        }

        /// <summary>
        /// Kick on 1 and 3, snare on 2 and 4, hats on every eighth: the
        /// pattern that reads as "keep time" to anyone who has heard music,
        /// which is the whole requirement.
        /// </summary>
        public static AudioClip CreateLoop(float beatsPerMinute)
        {
            var secondsPerBeat = 60f / SanitizeTempo(beatsPerMinute);
            var duration = GetLoopDurationSeconds(beatsPerMinute);
            var sampleCount = Mathf.Max(
                1,
                Mathf.RoundToInt(duration * SampleRate));
            var samples = new float[sampleCount];
            var totalBeats = BeatsPerBar * BarCount;

            // Deterministic noise: a fixed seed keeps the loop identical
            // between runs, so the hats cannot click differently each time.
            var random = new System.Random(20260817);
            var noise = new float[sampleCount];
            for (var index = 0; index < sampleCount; index++)
            {
                noise[index] = (float)(random.NextDouble() * 2d - 1d);
            }

            for (var index = 0; index < sampleCount; index++)
            {
                var time = index / (float)SampleRate;
                var beatPosition = time / secondsPerBeat;
                var beatIndex =
                    Mathf.FloorToInt(beatPosition) % totalBeats;
                var beatPhase = beatPosition - Mathf.FloorToInt(beatPosition);
                var beatTime = beatPhase * secondsPerBeat;
                var eighthPhase = (beatPosition * 2f) % 1f;
                var eighthTime = eighthPhase * secondsPerBeat * 0.5f;

                var value = 0f;

                if (beatIndex % 2 == 0)
                {
                    // Kick: a short downward sweep, which reads as low and
                    // percussive without needing a sample.
                    var sweep = Mathf.Lerp(
                        110f,
                        48f,
                        Mathf.Clamp01(beatTime * 24f));
                    value += Mathf.Sin(
                                 Mathf.PI * 2f * sweep * beatTime) *
                             Mathf.Exp(-beatTime * 18f) * 0.5f;
                }
                else
                {
                    // Snare: noise plus a body tone.
                    value += noise[index] *
                             Mathf.Exp(-beatTime * 32f) * 0.22f;
                    value += Mathf.Sin(
                                 Mathf.PI * 2f * 190f * beatTime) *
                             Mathf.Exp(-beatTime * 30f) * 0.12f;
                }

                // Hats on every eighth, quieter off the beat.
                var hatLevel = eighthPhase < 0.5f ? 0.06f : 0.04f;
                value += noise[sampleCount - 1 - index] *
                         Mathf.Exp(-eighthTime * 90f) * hatLevel;

                samples[index] = Mathf.Clamp(value, -1f, 1f);
            }

            FadeLoopSeam(samples);

            var clip = AudioClip.Create(
                "Tutorial Drum Bed",
                sampleCount,
                1,
                SampleRate,
                false);
            clip.hideFlags = HideFlags.DontSave;
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// The loop wraps to its own start, so a non-zero first or last sample
        /// is an audible click once per bar. Two milliseconds of fade at each
        /// end is inaudible and removes it.
        /// </summary>
        public static void FadeLoopSeam(float[] samples)
        {
            if (samples == null || samples.Length < 8)
            {
                return;
            }

            var fade = Mathf.Min(
                samples.Length / 2,
                Mathf.RoundToInt(SampleRate * 0.002f));
            for (var index = 0; index < fade; index++)
            {
                var gain = index / (float)fade;
                samples[index] *= gain;
                samples[samples.Length - 1 - index] *= gain;
            }
        }
    }

    /// <summary>
    /// Plays the introduction's beat.
    ///
    /// The clip uses Training's tempo. Tutorial visuals read this AudioSource's
    /// playhead directly, so sound and ghost share one time source even though
    /// Countdown still separates this bed from Training's RhythmClock session.
    /// </summary>
    [DefaultExecutionOrder(11000)]
    [DisallowMultipleComponent]
    public sealed class TutorialMusicController : MonoBehaviour
    {
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private RhythmClock _rhythmClock;
        [SerializeField] private AudioSource _musicSource;

        [Tooltip(
            "Leave empty to use the generated drum loop, which needs no " +
            "audio asset and is written at the live tempo.")]
        [SerializeField] private AudioClip _tutorialMusicClip;

        [Tooltip(
            "Quieter than Training's bed, so arriving at the real thing is a " +
            "lift rather than a continuation.")]
        [SerializeField, Range(0f, 1f)]
        private float _musicVolume = 0.1f;

        [SerializeField, Min(0f)] private float _fadeOutSeconds = 0.25f;

        [Tooltip(
            "Used when no clock is available. The introduction should " +
            "rehearse Training's tempo, so this has to match it.")]
        [SerializeField, Range(30f, 240f)]
        private float _fallbackBeatsPerMinute = 120f;

        private AudioClip _generatedClip;
        private float _generatedForTempo = -1f;
        private bool _isFadingOut;
        private float _fadeElapsed;
        private float _fadeStartVolume;
        private int _lastReadSample;
        private long _completedLoopSamples;

        public float BeatsPerMinute
        {
            get
            {
                ResolveReferences();
                return _rhythmClock != null
                    ? TutorialDrumBed.SanitizeTempo(
                        _rhythmClock.BeatsPerMinute)
                    : TutorialDrumBed.SanitizeTempo(
                        _fallbackBeatsPerMinute);
            }
        }

        public bool IsPlaying =>
            _musicSource != null && _musicSource.isPlaying;

        public float SecondsPerBeat => 60f / BeatsPerMinute;

        /// <summary>
        /// Reads the tutorial beat directly from the AudioSource playhead.
        /// The guide therefore follows the sound that is actually playing
        /// instead of advancing a second timer that can drift away from it.
        /// </summary>
        public bool TryGetBeatPosition(out float beatPosition)
        {
            beatPosition = 0f;
            ResolveReferences();
            if (!IsPlaying ||
                _musicSource.clip == null ||
                _musicSource.clip.frequency <= 0)
            {
                return false;
            }

            var sample = _musicSource.timeSamples;
            if (sample < _lastReadSample)
            {
                _completedLoopSamples += _musicSource.clip.samples;
            }
            _lastReadSample = sample;
            var elapsedSeconds =
                (_completedLoopSamples + sample) /
                (double)_musicSource.clip.frequency;
            beatPosition = (float)(
                elapsedSeconds / SecondsPerBeat);
            return
                !float.IsNaN(beatPosition) &&
                !float.IsInfinity(beatPosition);
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= HandlePhaseChanged;
                _gameFlow.PhaseChanged += HandlePhaseChanged;
                HandlePhaseChanged(_gameFlow.CurrentPhase);
            }
        }

        private void OnDisable()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= HandlePhaseChanged;
            }

            StopImmediate();
        }

        private void OnDestroy()
        {
            ReleaseGeneratedClip();
        }

        private void Update()
        {
            // Keep the loop count current even during message steps.
            if (IsPlaying) TryGetBeatPosition(out _);
            if (!_isFadingOut || _musicSource == null)
            {
                return;
            }

            _fadeElapsed += Time.unscaledDeltaTime;
            var duration = Mathf.Max(0.01f, _fadeOutSeconds);
            _musicSource.volume = Mathf.Lerp(
                _fadeStartVolume,
                0f,
                Mathf.Clamp01(_fadeElapsed / duration));
            if (_fadeElapsed < duration)
            {
                return;
            }

            StopImmediate();
        }

        private void HandlePhaseChanged(ExperiencePhase phase)
        {
            if (phase == ExperiencePhase.Tutorial)
            {
                Play();
                return;
            }

            if (IsPlaying && !_isFadingOut)
            {
                BeginFadeOut();
            }
        }

        private void Play()
        {
            if (_musicSource == null)
            {
                return;
            }

            var clip = GetClip();
            if (clip == null)
            {
                return;
            }

            _isFadingOut = false;
            _fadeElapsed = 0f;
            _musicSource.playOnAwake = false;
            _musicSource.loop = true;
            _musicSource.spatialBlend = 0f;
            _musicSource.dopplerLevel = 0f;
            _musicSource.pitch = 1f;
            _musicSource.clip = clip;
            _musicSource.volume = _musicVolume;
            _musicSource.time = 0f;
            _lastReadSample = 0;
            _completedLoopSamples = 0;
            _musicSource.Play();
        }

        private void BeginFadeOut()
        {
            _isFadingOut = true;
            _fadeElapsed = 0f;
            _fadeStartVolume = _musicSource == null
                ? 0f
                : _musicSource.volume;
        }

        private void StopImmediate()
        {
            _isFadingOut = false;
            _fadeElapsed = 0f;
            if (_musicSource == null)
            {
                return;
            }

            _musicSource.Stop();
            _musicSource.volume = _musicVolume;
        }

        private AudioClip GetClip()
        {
            if (_tutorialMusicClip != null)
            {
                return _tutorialMusicClip;
            }

            var tempo = BeatsPerMinute;
            if (_generatedClip != null &&
                Mathf.Approximately(_generatedForTempo, tempo))
            {
                return _generatedClip;
            }

            ReleaseGeneratedClip();
            _generatedClip = TutorialDrumBed.CreateLoop(tempo);
            _generatedForTempo = tempo;
            return _generatedClip;
        }

        private void ReleaseGeneratedClip()
        {
            if (_generatedClip == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_generatedClip);
            }
            else
            {
                DestroyImmediate(_generatedClip);
            }

            _generatedClip = null;
            _generatedForTempo = -1f;
        }

        private void ResolveReferences()
        {
            _gameFlow ??=
                FindFirstObjectByType<GameFlowController>();
            _rhythmClock ??=
                FindFirstObjectByType<RhythmClock>();
            _musicSource ??= GetComponent<AudioSource>();
        }
    }
}
