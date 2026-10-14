using System;
using UnityEngine;

namespace IVRC
{
    public readonly struct TrainingMusicSchedule
    {
        public TrainingMusicSchedule(
            double dspTime,
            float clipTimeSeconds)
        {
            DspTime = dspTime;
            ClipTimeSeconds = clipTimeSeconds;
            IsValid = true;
        }

        public bool IsValid { get; }
        public double DspTime { get; }
        public float ClipTimeSeconds { get; }
    }

    /// <summary>
    /// Pure timing helpers for keeping a looping music bed on the same DSP
    /// timeline as the gameplay beat clock.
    /// </summary>
    public static class TrainingMusicTiming
    {
        public const int PlaceholderBarCount = 4;

        /// <summary>
        /// Where to start the bed, and how far into the clip to start it, so
        /// that joining late still lands on the beat.
        ///
        /// <paramref name="playbackRate"/> is the AudioSource pitch. Raising
        /// pitch speeds the clip up, so a second of DSP time consumes that
        /// many seconds of clip: the loop comes round in
        /// clipLength / rate, and a clip position has to be measured in clip
        /// seconds, not in elapsed ones. Without the rate the bed drifts by a
        /// little more every loop, which is the sort of error that looks like
        /// "the music feels off" rather than like a bug.
        /// </summary>
        /// <summary>
        /// Where in the clip to start so that its pick-up lands on a given
        /// beat. Derived rather than typed in, because the answer moves
        /// whenever the tempo or the lead-in does, and a stale number here
        /// is heard as the music being early.
        /// </summary>
        /// <summary>
        /// Where a clip beat falls in seconds, counting the clip's first
        /// beat as 1. Only meaningful because the clip's declared tempo and
        /// the clock's are held equal by a test — if they ever diverge the
        /// bed is being transposed and this is the smaller problem.
        /// </summary>
        public static float GetPickupSeconds(
            int clipPickupBeat,
            float secondsPerBeat)
        {
            if (float.IsNaN(secondsPerBeat) ||
                float.IsInfinity(secondsPerBeat) ||
                secondsPerBeat <= 0f ||
                clipPickupBeat < 1)
            {
                return 0f;
            }

            return (clipPickupBeat - 1) * secondsPerBeat;
        }

        public static float GetClipStartSeconds(
            float pickupSeconds,
            int pickupBeat,
            float secondsPerBeat)
        {
            if (float.IsNaN(pickupSeconds) ||
                float.IsInfinity(pickupSeconds) ||
                float.IsNaN(secondsPerBeat) ||
                float.IsInfinity(secondsPerBeat) ||
                secondsPerBeat <= 0f ||
                pickupBeat < 0)
            {
                return Mathf.Max(0f, pickupSeconds);
            }

            return Mathf.Max(
                0f,
                pickupSeconds - pickupBeat * secondsPerBeat);
        }

        public static TrainingMusicSchedule BuildSchedule(
            double firstBeatDspTime,
            double currentDspTime,
            float secondsPerBeat,
            float clipLengthSeconds,
            float schedulingLeadSeconds,
            float playbackRate = 1f,
            float clipStartSeconds = 0f)
        {
            if (double.IsNaN(firstBeatDspTime) ||
                double.IsInfinity(firstBeatDspTime) ||
                double.IsNaN(currentDspTime) ||
                double.IsInfinity(currentDspTime) ||
                secondsPerBeat <= Mathf.Epsilon ||
                clipLengthSeconds <= Mathf.Epsilon)
            {
                return default;
            }

            playbackRate =
                float.IsNaN(playbackRate) ||
                float.IsInfinity(playbackRate) ||
                playbackRate <= Mathf.Epsilon
                    ? 1f
                    : playbackRate;

            clipStartSeconds =
                float.IsNaN(clipStartSeconds) ||
                float.IsInfinity(clipStartSeconds) ||
                clipStartSeconds < 0f
                    ? 0f
                    : clipStartSeconds;

            var lead = Mathf.Max(0.02f, schedulingLeadSeconds);
            var earliestSafeStart = currentDspTime + lead;
            var scheduledDspTime = Math.Max(
                firstBeatDspTime,
                earliestSafeStart);
            var elapsedSinceFirstBeat = Math.Max(
                0d,
                scheduledDspTime - firstBeatDspTime);
            // The bed enters where the track picks up rather than at
            // its first sample, and keeps looping from there, so every lap
            // starts on the part worth training to.
            var clipTime = PositiveModulo(
                clipStartSeconds +
                elapsedSinceFirstBeat * playbackRate,
                clipLengthSeconds);

            return new TrainingMusicSchedule(
                scheduledDspTime,
                (float)clipTime);
        }

        /// <summary>
        /// The playback rate that turns a clip written at one tempo into
        /// another tempo. Pitch and speed are the same control in Unity, so
        /// this is also how much higher the music will sound: there is no
        /// time-stretch here, and a large ratio is audible as such.
        /// </summary>
        public static float GetPlaybackRateForTempo(
            float baseBeatsPerMinute,
            float targetBeatsPerMinute)
        {
            if (baseBeatsPerMinute <= Mathf.Epsilon ||
                targetBeatsPerMinute <= Mathf.Epsilon ||
                float.IsNaN(baseBeatsPerMinute) ||
                float.IsNaN(targetBeatsPerMinute) ||
                float.IsInfinity(baseBeatsPerMinute) ||
                float.IsInfinity(targetBeatsPerMinute))
            {
                return 1f;
            }

            return targetBeatsPerMinute / baseBeatsPerMinute;
        }

        /// <summary>
        /// How far the music is transposed by playing it at that rate, in
        /// semitones. Reported so the number can be judged rather than
        /// discovered on the headset.
        /// </summary>
        public static float GetTranspositionSemitones(
            float playbackRate)
        {
            return playbackRate <= Mathf.Epsilon ||
                   float.IsNaN(playbackRate) ||
                   float.IsInfinity(playbackRate)
                ? 0f
                : 12f * Mathf.Log(playbackRate, 2f);
        }

        public static float GetPlaceholderLoopDurationSeconds(
            float beatsPerMinute,
            int beatsPerBar)
        {
            var safeTempo = Mathf.Clamp(beatsPerMinute, 30f, 240f);
            var safeBeatsPerBar = Mathf.Max(1, beatsPerBar);
            return 60f / safeTempo *
                   safeBeatsPerBar *
                   PlaceholderBarCount;
        }

        public static float GetNearestWholeBeatErrorSeconds(
            float clipLengthSeconds,
            float secondsPerBeat)
        {
            if (clipLengthSeconds <= Mathf.Epsilon ||
                secondsPerBeat <= Mathf.Epsilon)
            {
                return float.PositiveInfinity;
            }

            var beatCount = clipLengthSeconds / secondsPerBeat;
            return Mathf.Abs(
                beatCount - Mathf.Round(beatCount)) *
                secondsPerBeat;
        }

        private static double PositiveModulo(
            double value,
            double modulus)
        {
            var result = value % modulus;
            return result < 0d ? result + modulus : result;
        }
    }

    /// <summary>
    /// Owns the low-priority music bed for Training. It is scheduled against
    /// RhythmClock DSP time, pauses with tracking, and never shares the
    /// one-shot AudioSource used by player-facing semantic cues.
    /// </summary>
    [DefaultExecutionOrder(11000)]
    public sealed class TrainingMusicController : MonoBehaviour
    {
        private const int PlaceholderSampleRate = 48000;

        [Header("Sources")]
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private RhythmClock _rhythmClock;
        [SerializeField] private AudioSource _musicSource;

        [Header("Music")]
        [SerializeField] private AudioClip _trainingMusicClip;
        [SerializeField] private bool _useGeneratedPlaceholder = true;
        [SerializeField, Range(0f, 1f)] private float _musicVolume = 0.14f;
        [SerializeField, Min(0f)] private float _fadeOutSeconds = 0.2f;
        [SerializeField, Range(0.02f, 0.2f)]
        private float _schedulingLeadSeconds = 0.05f;

        [Tooltip(
            "Tempo the music clip was written at. The bed is resampled from " +
            "here to whatever the clock is running, so this has to be the " +
            "clip's real tempo or everything after it is wrong.")]
        [SerializeField, Range(30f, 240f)]
        private float _clipBeatsPerMinute = 120f;

        [Tooltip(
            "The clip's own beat where the track picks up, counting the " +
            "first beat as 1. Kept in beats rather than seconds because " +
            "the drop lands on a bar line, and six seconds is beat 15.4 " +
            "at this tempo — near the right place and not on it.")]
        [SerializeField, Min(1)]
        private int _clipPickupBeat = 17;

        [Tooltip(
            "The clock beat the pick-up should land on. This is the " +
            "pattern's lead-in, so the music arrives with the movement " +
            "instead of before it.")]
        [SerializeField, Min(0)]
        private int _pickupBeat = 4;

        [Header("Foreground Ducking")]
        [SerializeField] private AudioSource _foregroundCueSource;
        [SerializeField, Range(0f, 1f)]
        private float _foregroundDuckMultiplier = 0.42f;
        [SerializeField, Min(0.01f)]
        private float _duckAttackSeconds = 0.04f;
        [SerializeField, Min(0.01f)]
        private float _duckReleaseSeconds = 0.16f;

        private AudioClip _generatedPlaceholderClip;
        private float _generatedForTempo = -1f;
        private int _generatedForBeatsPerBar = -1;
        private int _scheduledClockSessionId = -1;
        private double _scheduledStartDspTime;
        private double _lastKnownDspTime;
        private bool _hasScheduledPlayback;
        private bool _isPausedForTracking;
        private bool _isFadingOut;
        private float _fadeStartVolume;
        private float _fadeElapsed;
        private bool _reportedNonIntegralLoop;
        private float _duckMultiplier = 1f;

        public AudioClip ActiveClip =>
            _trainingMusicClip != null
                ? _trainingMusicClip
                : _generatedPlaceholderClip;
        public bool IsUsingGeneratedPlaceholder =>
            _trainingMusicClip == null &&
            _generatedPlaceholderClip != null;
        public int ScheduledClockSessionId =>
            _scheduledClockSessionId;
        public bool IsPausedForTracking =>
            _isPausedForTracking;

        private void Reset()
        {
            ResolveReferences();
            ValidateSettings();
            ConfigureAudioSource();
        }

        private void Awake()
        {
            ResolveReferences();
            ValidateSettings();
            ConfigureAudioSource();
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            ResetPlaybackImmediate();
        }

        private void Start()
        {
            // Build the temporary loop during the untimed setup phase instead
            // of spending that work on the first scored Training frame.
            GetMusicClip();
            SynchronizePlayback();
        }

        private void Update()
        {
            var currentDspTime = AudioSettings.dspTime;
            if (_hasScheduledPlayback &&
                _musicSource != null &&
                !_isPausedForTracking &&
                !_isFadingOut &&
                _musicSource.isPlaying &&
                _lastKnownDspTime > 0d &&
                currentDspTime + 0.05d < _lastKnownDspTime)
            {
                // Mobile audio devices can reset DSP time when the app is
                // suspended. The old scheduled timeline is no longer valid.
                ResetPlaybackImmediate();
            }

            _lastKnownDspTime = currentDspTime;
            UpdateDucking(Time.unscaledDeltaTime);
            if (_isFadingOut)
            {
                UpdateFadeOut(Time.unscaledDeltaTime);
                return;
            }

            SynchronizePlayback();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ResetPlaybackImmediate();
        }

        private void OnDestroy()
        {
            DestroyGeneratedPlaceholder();
        }

        private void OnValidate()
        {
            ValidateSettings();
            ConfigureAudioSource();
        }

        private void HandlePhaseChanged(ExperiencePhase phase)
        {
            if (phase == ExperiencePhase.Training)
            {
                if (_hasScheduledPlayback ||
                    _isPausedForTracking ||
                    _isFadingOut ||
                    (_musicSource != null &&
                     _musicSource.isPlaying))
                {
                    ResetPlaybackImmediate();
                }

                return;
            }

            if (_hasScheduledPlayback ||
                _isPausedForTracking ||
                (_musicSource != null && _musicSource.isPlaying))
            {
                BeginFadeOut();
            }
            else
            {
                ResetPlaybackImmediate();
            }
        }

        private void SynchronizePlayback()
        {
            if (_gameFlow == null ||
                _rhythmClock == null ||
                _musicSource == null ||
                _gameFlow.CurrentPhase != ExperiencePhase.Training ||
                !_rhythmClock.HasStarted)
            {
                return;
            }

            if (_scheduledClockSessionId !=
                _rhythmClock.ClockSessionId)
            {
                ResetPlaybackImmediate();
                _scheduledClockSessionId =
                    _rhythmClock.ClockSessionId;
            }

            if (!_rhythmClock.IsRunning)
            {
                PauseForTracking();
                return;
            }

            if (_isPausedForTracking)
            {
                _musicSource.UnPause();
                _isPausedForTracking = false;
                return;
            }

            if (!_hasScheduledPlayback)
            {
                SchedulePlayback();
            }
        }

        /// <summary>
        /// How fast the bed has to run to sit on the clock's current tempo.
        /// One number drives both the resampling and the pitch, because the
        /// two cannot be allowed to disagree: they are the same control.
        ///
        /// A generated placeholder is written at the live tempo already, so it
        /// plays at 1 and only a real clip is ever resampled.
        /// </summary>
        public float CurrentPlaybackRate
        {
            get
            {
                if (_rhythmClock == null ||
                    _trainingMusicClip == null)
                {
                    return 1f;
                }

                return TrainingMusicTiming.GetPlaybackRateForTempo(
                    _clipBeatsPerMinute,
                    _rhythmClock.BeatsPerMinute);
            }
        }

        /// <summary>
        /// How far the bed is transposed right now, in semitones, for anyone
        /// deciding whether a tempo is worth the artefact.
        /// </summary>
        public float CurrentTranspositionSemitones =>
            TrainingMusicTiming.GetTranspositionSemitones(
                CurrentPlaybackRate);

        private void SchedulePlayback()
        {
            var clip = GetMusicClip();
            if (clip == null || clip.samples <= 0)
            {
                return;
            }

            var playbackRate = CurrentPlaybackRate;
            var schedule = TrainingMusicTiming.BuildSchedule(
                _rhythmClock.GetBeatDspTime(0),
                AudioSettings.dspTime,
                _rhythmClock.SecondsPerBeat,
                clip.length,
                _schedulingLeadSeconds,
                playbackRate,
                TrainingMusicTiming.GetClipStartSeconds(
                    TrainingMusicTiming.GetPickupSeconds(
                        _clipPickupBeat,
                        _rhythmClock.SecondsPerBeat),
                    _pickupBeat,
                    _rhythmClock.SecondsPerBeat));
            if (!schedule.IsValid)
            {
                return;
            }

            ConfigureAudioSource();
            _musicSource.Stop();
            _musicSource.clip = clip;
            _musicSource.volume =
                _musicVolume * _duckMultiplier;
            _musicSource.timeSamples = Mathf.Clamp(
                Mathf.RoundToInt(
                    schedule.ClipTimeSeconds * clip.frequency),
                0,
                Mathf.Max(0, clip.samples - 1));
            _musicSource.PlayScheduled(schedule.DspTime);
            _scheduledStartDspTime = schedule.DspTime;
            _hasScheduledPlayback = true;

            ReportLoopLengthIfNeeded(clip);
        }

        private void PauseForTracking()
        {
            if (!_hasScheduledPlayback ||
                _isPausedForTracking ||
                _musicSource == null)
            {
                return;
            }

            if (!_musicSource.isPlaying &&
                AudioSettings.dspTime < _scheduledStartDspTime)
            {
                // A pause before the first scheduled sample would leave the
                // AudioSource tied to the old, unshifted clock origin.
                _musicSource.Stop();
                _hasScheduledPlayback = false;
                _scheduledStartDspTime = 0d;
                return;
            }

            _musicSource.Pause();
            _isPausedForTracking = true;
        }

        private void BeginFadeOut()
        {
            if (_musicSource == null ||
                _isPausedForTracking ||
                _fadeOutSeconds <= Mathf.Epsilon)
            {
                ResetPlaybackImmediate();
                return;
            }

            _isFadingOut = true;
            _fadeElapsed = 0f;
            _fadeStartVolume = _musicSource.volume;
            _hasScheduledPlayback = false;
            _scheduledClockSessionId = -1;
        }

        private void UpdateFadeOut(float unscaledDeltaTime)
        {
            if (_musicSource == null)
            {
                ResetPlaybackImmediate();
                return;
            }

            _fadeElapsed += Mathf.Max(0f, unscaledDeltaTime);
            var progress = Mathf.Clamp01(
                _fadeElapsed / Mathf.Max(0.001f, _fadeOutSeconds));
            _musicSource.volume = Mathf.Lerp(
                _fadeStartVolume,
                0f,
                progress);
            if (progress >= 1f)
            {
                ResetPlaybackImmediate();
            }
        }

        private void ResetPlaybackImmediate()
        {
            if (_musicSource != null)
            {
                _musicSource.Stop();
                _musicSource.volume =
                    _musicVolume * _duckMultiplier;
            }

            _scheduledClockSessionId = -1;
            _scheduledStartDspTime = 0d;
            _lastKnownDspTime = AudioSettings.dspTime;
            _hasScheduledPlayback = false;
            _isPausedForTracking = false;
            _isFadingOut = false;
            _fadeStartVolume = 0f;
            _fadeElapsed = 0f;
            _duckMultiplier = 1f;
        }

        private void UpdateDucking(float unscaledDeltaTime)
        {
            var target = AudioDuckingMath.GetTargetMultiplier(
                _foregroundCueSource != null &&
                _foregroundCueSource.isPlaying,
                _foregroundDuckMultiplier);
            _duckMultiplier = AudioDuckingMath.MoveMultiplier(
                _duckMultiplier,
                target,
                unscaledDeltaTime,
                _duckAttackSeconds,
                _duckReleaseSeconds);
            if (_musicSource != null &&
                !_isFadingOut &&
                (_musicSource.isPlaying || _hasScheduledPlayback))
            {
                _musicSource.volume =
                    _musicVolume * _duckMultiplier;
            }
        }

        private AudioClip GetMusicClip()
        {
            if (_trainingMusicClip != null)
            {
                return _trainingMusicClip;
            }

            if (!_useGeneratedPlaceholder ||
                !Application.isPlaying)
            {
                return null;
            }

            var tempo = _rhythmClock != null
                ? _rhythmClock.BeatsPerMinute
                : 120f;
            var beatsPerBar = _rhythmClock != null
                ? _rhythmClock.BeatsPerBar
                : 4;
            if (_generatedPlaceholderClip != null &&
                Mathf.Approximately(_generatedForTempo, tempo) &&
                _generatedForBeatsPerBar == beatsPerBar)
            {
                return _generatedPlaceholderClip;
            }

            DestroyGeneratedPlaceholder();
            _generatedPlaceholderClip =
                CreatePlaceholderLoop(tempo, beatsPerBar);
            _generatedForTempo = tempo;
            _generatedForBeatsPerBar = beatsPerBar;
            return _generatedPlaceholderClip;
        }

        private static AudioClip CreatePlaceholderLoop(
            float beatsPerMinute,
            int beatsPerBar)
        {
            var secondsPerBeat = 60f /
                                 Mathf.Clamp(
                                     beatsPerMinute,
                                     30f,
                                     240f);
            var duration =
                TrainingMusicTiming.GetPlaceholderLoopDurationSeconds(
                    beatsPerMinute,
                    beatsPerBar);
            var sampleCount = Mathf.Max(
                1,
                Mathf.RoundToInt(
                    duration * PlaceholderSampleRate));
            var samples = new float[sampleCount];
            var safeBeatsPerBar = Mathf.Max(1, beatsPerBar);
            var padFrequencyA = QuantizeLoopFrequency(220f, duration);
            var padFrequencyB = QuantizeLoopFrequency(275f, duration);
            var padFrequencyC = QuantizeLoopFrequency(330f, duration);

            for (var index = 0; index < sampleCount; index++)
            {
                var time = index / (float)PlaceholderSampleRate;
                var beatPosition = time / secondsPerBeat;
                var beatIndex = Mathf.FloorToInt(beatPosition);
                var beatPhase = beatPosition - beatIndex;
                var beatInBar = beatIndex % safeBeatsPerBar;
                var localBeatTime = beatPhase * secondsPerBeat;

                var pulseFrequency = beatInBar == 0 ? 165f : 220f;
                var pulseEnvelope = Mathf.Exp(-beatPhase * 11f);
                var pulse = Mathf.Sin(
                    Mathf.PI * 2f *
                    pulseFrequency * localBeatTime) *
                    pulseEnvelope *
                    (beatInBar == 0 ? 0.16f : 0.1f);

                // Quantized frequencies complete whole cycles over the loop,
                // so tempo changes do not introduce a boundary click.
                var pad =
                    Mathf.Sin(
                        Mathf.PI * 2f * padFrequencyA * time) * 0.018f +
                    Mathf.Sin(
                        Mathf.PI * 2f * padFrequencyB * time) * 0.012f +
                    Mathf.Sin(
                        Mathf.PI * 2f * padFrequencyC * time) * 0.01f;
                samples[index] = pulse + pad;
            }

            var clip = AudioClip.Create(
                "Training Pulse Bed Placeholder",
                sampleCount,
                1,
                PlaceholderSampleRate,
                false);
            clip.hideFlags = HideFlags.DontSave;
            clip.SetData(samples, 0);
            return clip;
        }

        private static float QuantizeLoopFrequency(
            float desiredFrequency,
            float loopDurationSeconds)
        {
            var cycles = Mathf.Max(
                1,
                Mathf.RoundToInt(
                    desiredFrequency * loopDurationSeconds));
            return cycles / Mathf.Max(0.001f, loopDurationSeconds);
        }

        private void ReportLoopLengthIfNeeded(AudioClip clip)
        {
            if (_reportedNonIntegralLoop ||
                clip == null ||
                ReferenceEquals(clip, _generatedPlaceholderClip))
            {
                return;
            }

            var error =
                TrainingMusicTiming.GetNearestWholeBeatErrorSeconds(
                    clip.length,
                    _rhythmClock.SecondsPerBeat);
            if (error <= 0.01f)
            {
                return;
            }

            _reportedNonIntegralLoop = true;
            Debug.LogWarning(
                "[TrainingMusic] The assigned loop is not an integer " +
                "number of RhythmClock beats. Its loop boundary may drift " +
                "against the chart.",
                this);
        }

        private void DestroyGeneratedPlaceholder()
        {
            if (_generatedPlaceholderClip != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_generatedPlaceholderClip);
                }
                else
                {
                    DestroyImmediate(_generatedPlaceholderClip);
                }
            }

            _generatedPlaceholderClip = null;
            _generatedForTempo = -1f;
            _generatedForBeatsPerBar = -1;
        }

        private void ConfigureAudioSource()
        {
            if (_musicSource == null)
            {
                return;
            }

            _musicSource.playOnAwake = false;
            _musicSource.loop = true;
            _musicSource.pitch = CurrentPlaybackRate;
            _musicSource.spatialBlend = 0f;
            _musicSource.dopplerLevel = 0f;
            if (!_musicSource.isPlaying && !_isFadingOut)
            {
                _musicSource.volume = _musicVolume;
            }
        }

        private void Subscribe()
        {
            if (_gameFlow == null)
            {
                return;
            }

            _gameFlow.PhaseChanged -= HandlePhaseChanged;
            _gameFlow.PhaseChanged += HandlePhaseChanged;
        }

        private void Unsubscribe()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= HandlePhaseChanged;
            }
        }

        private void ResolveReferences()
        {
            _gameFlow ??= FindFirstObjectByType<GameFlowController>();
            _rhythmClock ??= FindFirstObjectByType<RhythmClock>();
            if (_musicSource != null)
            {
                return;
            }

            var localSources = GetComponents<AudioSource>();
            if (localSources.Length == 0)
            {
                return;
            }

            // ExerciseSystem already owns a one-shot AudioSource. When this
            // component is newly added, never mutate that sole source into a
            // looping music channel; scene wiring must provide a second one.
            if (GetComponent<TrainingAudioFeedbackController>() != null)
            {
                if (localSources.Length > 1)
                {
                    _musicSource =
                        localSources[localSources.Length - 1];
                }

                return;
            }

            _musicSource = localSources[0];
        }

        private void ValidateSettings()
        {
            _musicVolume = Mathf.Clamp01(_musicVolume);
            _foregroundDuckMultiplier = Mathf.Clamp01(
                _foregroundDuckMultiplier);
            _fadeOutSeconds = Mathf.Max(0f, _fadeOutSeconds);
            _schedulingLeadSeconds = Mathf.Clamp(
                _schedulingLeadSeconds,
                0.02f,
                0.2f);
            _duckAttackSeconds = Mathf.Max(
                0.01f,
                _duckAttackSeconds);
            _duckReleaseSeconds = Mathf.Max(
                0.01f,
                _duckReleaseSeconds);
        }
    }
}
