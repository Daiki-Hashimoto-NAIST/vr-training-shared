using UnityEngine;

namespace IVRC
{
    public static class StageMusicEnvelope
    {
        public static bool IsStageMusicPhase(
            ExperiencePhase phase,
            StageTransitionDestination transitionDestination)
        {
            if (phase == ExperiencePhase.StageEntrance ||
                phase == ExperiencePhase.Posing)
            {
                return true;
            }

            return phase == ExperiencePhase.StageTransition &&
                   (transitionDestination ==
                        StageTransitionDestination.Judges ||
                    transitionDestination ==
                        StageTransitionDestination.Stage);
        }

        public static float GetTargetVolume(
            ExperiencePhase phase,
            StageTransitionDestination transitionDestination,
            bool pausedForTracking,
            float stageVolume,
            float trackingVolumeMultiplier)
        {
            if (!IsStageMusicPhase(phase, transitionDestination))
            {
                return 0f;
            }

            var volume = Mathf.Clamp01(stageVolume);
            if (phase == ExperiencePhase.Posing &&
                pausedForTracking)
            {
                volume *= Mathf.Clamp01(trackingVolumeMultiplier);
            }

            return volume;
        }
    }

    /// <summary>
    /// Owns the low-priority music bed from the judges transition through
    /// the posing routine. It never shares sources with pose confirmation or
    /// audience cheers and does not drive gameplay timing.
    /// </summary>
    [DefaultExecutionOrder(11010)]
    [DisallowMultipleComponent]
    public sealed class StageMusicController : MonoBehaviour
    {
        private const int PlaceholderSampleRate = 48000;
        private const float PlaceholderDurationSeconds = 12f;

        [Header("Sources")]
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private AudioSource _musicSource;

        [Header("Music")]
        [SerializeField] private AudioClip _stageMusicClip;
        [SerializeField] private bool _useGeneratedPlaceholder = true;
        [SerializeField, Range(0f, 1f)] private float _stageVolume = 0.12f;
        [SerializeField, Range(0f, 1f)]
        private float _trackingVolumeMultiplier = 0.3f;
        [SerializeField, Min(0.01f)] private float _fadeInSeconds = 0.45f;
        [SerializeField, Min(0.01f)] private float _fadeOutSeconds = 0.25f;
        [SerializeField, Min(0.01f)]
        private float _trackingFadeSeconds = 0.18f;

        [Header("Foreground Ducking")]
        [SerializeField] private AudioSource[] _foregroundSources =
            System.Array.Empty<AudioSource>();
        [SerializeField, Range(0f, 1f)]
        private float _foregroundDuckMultiplier = 0.4f;
        [SerializeField, Min(0.01f)]
        private float _duckAttackSeconds = 0.04f;
        [SerializeField, Min(0.01f)]
        private float _duckReleaseSeconds = 0.22f;

        private AudioClip _generatedPlaceholderClip;
        private float _currentVolume;
        private float _duckMultiplier = 1f;
        private bool _runHasStarted;

        public AudioClip ActiveClip =>
            _stageMusicClip != null
                ? _stageMusicClip
                : _generatedPlaceholderClip;
        public bool IsUsingGeneratedPlaceholder =>
            _stageMusicClip == null &&
            _generatedPlaceholderClip != null;
        public bool RunHasStarted => _runHasStarted;
        public float CurrentVolume => _currentVolume;

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
            // Generate the temporary loop during Setup rather than when the
            // participant first appears in the judges' view.
            GetMusicClip();
            SynchronizeCurrentPhase(true);
        }

        private void Update()
        {
            SynchronizeCurrentPhase(false);
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
            if (phase == ExperiencePhase.Setup ||
                phase == ExperiencePhase.Ready ||
                phase == ExperiencePhase.Countdown ||
                phase == ExperiencePhase.Training ||
                phase == ExperiencePhase.Transformation ||
                phase == ExperiencePhase.Results)
            {
                if (phase == ExperiencePhase.Results ||
                    phase == ExperiencePhase.Setup)
                {
                    _runHasStarted = false;
                }
            }

            SynchronizeCurrentPhase(false);
        }

        private void SynchronizeCurrentPhase(bool immediate)
        {
            if (_gameFlow == null || _musicSource == null)
            {
                return;
            }

            var phase = _gameFlow.CurrentPhase;
            var baseTargetVolume = StageMusicEnvelope.GetTargetVolume(
                phase,
                _gameFlow.TransitionDestination,
                _gameFlow.IsPausedForTracking,
                _stageVolume,
                _trackingVolumeMultiplier);
            var foregroundIsPlaying = false;
            foreach (var foreground in _foregroundSources)
            {
                if (foreground != null && foreground.isPlaying)
                {
                    foregroundIsPlaying = true;
                    break;
                }
            }
            var duckTarget = AudioDuckingMath.GetTargetMultiplier(
                foregroundIsPlaying,
                _foregroundDuckMultiplier);
            _duckMultiplier = immediate
                ? duckTarget
                : AudioDuckingMath.MoveMultiplier(
                    _duckMultiplier,
                    duckTarget,
                    Time.unscaledDeltaTime,
                    _duckAttackSeconds,
                    _duckReleaseSeconds);
            var targetVolume =
                baseTargetVolume * _duckMultiplier;
            if (baseTargetVolume > Mathf.Epsilon &&
                !_runHasStarted)
            {
                StartMusic();
            }

            var responseSeconds = targetVolume > _currentVolume
                ? _fadeInSeconds
                : phase == ExperiencePhase.Posing &&
                  _gameFlow.IsPausedForTracking
                    ? _trackingFadeSeconds
                    : _fadeOutSeconds;
            var maximumDelta = immediate
                ? 1f
                : Time.unscaledDeltaTime /
                  Mathf.Max(0.01f, responseSeconds);
            _currentVolume = Mathf.MoveTowards(
                _currentVolume,
                targetVolume,
                maximumDelta * Mathf.Max(0.01f, _stageVolume));
            _musicSource.volume = _currentVolume;

            if (targetVolume <= Mathf.Epsilon &&
                _currentVolume <= Mathf.Epsilon)
            {
                StopMusic();
            }
        }

        private void StartMusic()
        {
            var clip = GetMusicClip();
            if (clip == null || clip.samples <= 0)
            {
                return;
            }

            ConfigureAudioSource();
            _musicSource.Stop();
            _musicSource.clip = clip;
            _musicSource.timeSamples = 0;
            _musicSource.volume = _currentVolume;
            _musicSource.Play();
            _runHasStarted = true;
        }

        private void StopMusic()
        {
            if (_musicSource != null)
            {
                _musicSource.Stop();
                _musicSource.volume = 0f;
            }

            _currentVolume = 0f;
            _duckMultiplier = 1f;
            _runHasStarted = false;
        }

        private void ResetPlaybackImmediate()
        {
            if (_musicSource != null)
            {
                _musicSource.Stop();
                _musicSource.volume = 0f;
            }

            _currentVolume = 0f;
            _duckMultiplier = 1f;
            _runHasStarted = false;
        }

        private AudioClip GetMusicClip()
        {
            if (_stageMusicClip != null)
            {
                return _stageMusicClip;
            }

            if (!_useGeneratedPlaceholder ||
                !Application.isPlaying)
            {
                return null;
            }

            _generatedPlaceholderClip ??=
                CreatePlaceholderLoop();
            return _generatedPlaceholderClip;
        }

        private static AudioClip CreatePlaceholderLoop()
        {
            var sampleCount = Mathf.RoundToInt(
                PlaceholderSampleRate * PlaceholderDurationSeconds);
            var samples = new float[sampleCount];
            for (var index = 0; index < sampleCount; index++)
            {
                var time = index / (float)PlaceholderSampleRate;
                var slowPulse =
                    0.72f +
                    Mathf.Sin(
                        Mathf.PI * 2f *
                        (100f / 60f) * time) * 0.28f;
                var pad =
                    Mathf.Sin(Mathf.PI * 2f * 110f * time) * 0.025f +
                    Mathf.Sin(Mathf.PI * 2f * 137.5f * time) * 0.017f +
                    Mathf.Sin(Mathf.PI * 2f * 165f * time) * 0.012f;
                samples[index] = pad * slowPulse;
            }

            var clip = AudioClip.Create(
                "Stage Bed Placeholder",
                sampleCount,
                1,
                PlaceholderSampleRate,
                false);
            clip.hideFlags = HideFlags.DontSave;
            clip.SetData(samples, 0);
            return clip;
        }

        private void DestroyGeneratedPlaceholder()
        {
            if (_generatedPlaceholderClip == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_generatedPlaceholderClip);
            }
            else
            {
                DestroyImmediate(_generatedPlaceholderClip);
            }

            _generatedPlaceholderClip = null;
        }

        private void ConfigureAudioSource()
        {
            if (_musicSource == null)
            {
                return;
            }

            _musicSource.playOnAwake = false;
            _musicSource.loop = true;
            _musicSource.spatialBlend = 0f;
            _musicSource.dopplerLevel = 0f;
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
            _musicSource ??= GetComponent<AudioSource>();
        }

        private void ValidateSettings()
        {
            _stageVolume = Mathf.Clamp01(_stageVolume);
            _foregroundDuckMultiplier = Mathf.Clamp01(
                _foregroundDuckMultiplier);
            _trackingVolumeMultiplier = Mathf.Clamp01(
                _trackingVolumeMultiplier);
            _fadeInSeconds = Mathf.Max(0.01f, _fadeInSeconds);
            _fadeOutSeconds = Mathf.Max(0.01f, _fadeOutSeconds);
            _trackingFadeSeconds = Mathf.Max(
                0.01f,
                _trackingFadeSeconds);
            _duckAttackSeconds = Mathf.Max(
                0.01f,
                _duckAttackSeconds);
            _duckReleaseSeconds = Mathf.Max(
                0.01f,
                _duckReleaseSeconds);
        }
    }
}
