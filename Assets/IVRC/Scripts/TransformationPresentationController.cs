using UnityEngine;

namespace IVRC
{
    public enum TransformationPresentationStage
    {
        Inactive,
        Lock,
        Reveal,
        HeroHold
    }

    /// <summary>
    /// Pure output used by the runtime presenter and EditMode tests. It only
    /// describes presentation multipliers; it never owns muscle-growth data.
    /// </summary>
    public readonly struct TransformationPresentationFrame
    {
        public TransformationPresentationFrame(
            TransformationPresentationStage stage,
            float normalizedProgress,
            float keyIntensityMultiplier,
            float fillIntensityMultiplier,
            float colorBlend)
        {
            Stage = stage;
            NormalizedProgress = Mathf.Clamp01(normalizedProgress);
            KeyIntensityMultiplier = Mathf.Max(
                0f,
                keyIntensityMultiplier);
            FillIntensityMultiplier = Mathf.Max(
                0f,
                fillIntensityMultiplier);
            ColorBlend = Mathf.Clamp01(colorBlend);
        }

        public TransformationPresentationStage Stage { get; }
        public float NormalizedProgress { get; }
        public float KeyIntensityMultiplier { get; }
        public float FillIntensityMultiplier { get; }
        public float ColorBlend { get; }
    }

    /// <summary>
    /// Three-part, flash-free light envelope for the transformation payoff.
    /// Segment boundaries are shared with the player-facing copy so the HUD
    /// cannot drift from the visual reveal.
    /// </summary>
    public static class TransformationPresentationMath
    {
        public const float LockEndNormalized = 0.15f;
        public const float RevealEndNormalized = 0.70f;

        public static TransformationPresentationStage GetStage(
            float normalizedProgress)
        {
            normalizedProgress = SanitizeProgress(
                normalizedProgress);
            if (normalizedProgress < LockEndNormalized)
            {
                return TransformationPresentationStage.Lock;
            }

            return normalizedProgress < RevealEndNormalized
                ? TransformationPresentationStage.Reveal
                : TransformationPresentationStage.HeroHold;
        }

        public static TransformationPresentationFrame Evaluate(
            float normalizedProgress,
            float lockKeyMultiplier,
            float peakKeyMultiplier,
            float heroKeyMultiplier,
            float lockFillMultiplier,
            float peakFillMultiplier,
            float heroFillMultiplier,
            float heroColorRetention)
        {
            normalizedProgress = SanitizeProgress(
                normalizedProgress);
            lockKeyMultiplier = SanitizeMultiplier(
                lockKeyMultiplier);
            peakKeyMultiplier = SanitizeMultiplier(
                peakKeyMultiplier);
            heroKeyMultiplier = SanitizeMultiplier(
                heroKeyMultiplier);
            lockFillMultiplier = SanitizeMultiplier(
                lockFillMultiplier);
            peakFillMultiplier = SanitizeMultiplier(
                peakFillMultiplier);
            heroFillMultiplier = SanitizeMultiplier(
                heroFillMultiplier);
            heroColorRetention = Mathf.Clamp01(
                float.IsNaN(heroColorRetention) ||
                float.IsInfinity(heroColorRetention)
                    ? 0.45f
                    : heroColorRetention);

            var stage = GetStage(normalizedProgress);
            if (stage == TransformationPresentationStage.Lock)
            {
                var local = Smooth(
                    Mathf.InverseLerp(
                        0f,
                        LockEndNormalized,
                        normalizedProgress));
                return new TransformationPresentationFrame(
                    stage,
                    normalizedProgress,
                    Mathf.Lerp(1f, lockKeyMultiplier, local),
                    Mathf.Lerp(1f, lockFillMultiplier, local),
                    0f);
            }

            if (stage == TransformationPresentationStage.Reveal)
            {
                var local = Smooth(
                    Mathf.InverseLerp(
                        LockEndNormalized,
                        RevealEndNormalized,
                        normalizedProgress));
                return new TransformationPresentationFrame(
                    stage,
                    normalizedProgress,
                    Mathf.Lerp(
                        lockKeyMultiplier,
                        peakKeyMultiplier,
                        local),
                    Mathf.Lerp(
                        lockFillMultiplier,
                        peakFillMultiplier,
                        local),
                    local);
            }

            var heroLocal = Smooth(
                Mathf.InverseLerp(
                    RevealEndNormalized,
                    1f,
                    normalizedProgress));
            return new TransformationPresentationFrame(
                stage,
                normalizedProgress,
                Mathf.Lerp(
                    peakKeyMultiplier,
                    heroKeyMultiplier,
                    heroLocal),
                Mathf.Lerp(
                    peakFillMultiplier,
                    heroFillMultiplier,
                    heroLocal),
                Mathf.Lerp(
                    1f,
                    heroColorRetention,
                    heroLocal));
        }

        private static float SanitizeProgress(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : Mathf.Clamp01(value);
        }

        private static float SanitizeMultiplier(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 1f
                : Mathf.Clamp(value, 0f, 2f);
        }

        private static float Smooth(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }

    public enum TransformationPresentationCue
    {
        None,
        Reveal,
        Hero
    }

    public static class TransformationPresentationCueRules
    {
        public static TransformationPresentationCue SelectCue(
            float normalizedProgress,
            bool revealPlayed,
            bool heroPlayed)
        {
            normalizedProgress =
                float.IsNaN(normalizedProgress) ||
                float.IsInfinity(normalizedProgress)
                    ? 0f
                    : Mathf.Clamp01(normalizedProgress);
            if (!heroPlayed &&
                normalizedProgress >=
                    TransformationPresentationMath
                        .RevealEndNormalized)
            {
                return TransformationPresentationCue.Hero;
            }

            if (!revealPlayed &&
                normalizedProgress >=
                    TransformationPresentationMath
                        .LockEndNormalized)
            {
                return TransformationPresentationCue.Reveal;
            }

            return TransformationPresentationCue.None;
        }
    }

    public static class TransformationPresentationSafety
    {
        public const float MinimumHeroHapticDurationSeconds = 0.01f;
        public const float MaximumHeroHapticDurationSeconds = 0.20f;

        public static float ClampHeroHapticDuration(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
            {
                return MinimumHeroHapticDurationSeconds;
            }

            return Mathf.Clamp(
                seconds,
                MinimumHeroHapticDurationSeconds,
                MaximumHeroHapticDurationSeconds);
        }
    }

    /// <summary>
    /// Presents the already-earned muscle snapshot through the existing room
    /// lights, optional audio, and one short hero pulse. It never modifies an
    /// avatar, camera, OVRCameraRig, TrackingSpace, or gameplay score.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TransformationPresentationController : MonoBehaviour
    {
        [Header("Flow")]
        [SerializeField] private GameFlowController _gameFlow;

        [Header("Existing Training Lights")]
        [SerializeField] private Light _keyLight;
        [SerializeField] private Light _fillLight;

        [Header("Light Envelope")]
        [SerializeField, Range(0f, 2f)]
        private float _lockKeyMultiplier = 0.82f;
        [SerializeField, Range(0f, 2f)]
        private float _peakKeyMultiplier = 1.35f;
        [SerializeField, Range(0f, 2f)]
        private float _heroKeyMultiplier = 1.12f;
        [SerializeField, Range(0f, 2f)]
        private float _lockFillMultiplier = 0.72f;
        [SerializeField, Range(0f, 2f)]
        private float _peakFillMultiplier = 1.22f;
        [SerializeField, Range(0f, 2f)]
        private float _heroFillMultiplier = 1.05f;
        [SerializeField, Range(0f, 1f)]
        private float _heroColorRetention = 0.45f;
        [SerializeField] private Color _revealKeyColor =
            new Color(1f, 0.72f, 0.42f, 1f);
        [SerializeField] private Color _revealFillColor =
            new Color(0.38f, 0.78f, 1f, 1f);

        [Header("Existing Mirror Frame")]
        [SerializeField] private Renderer[] _mirrorFrameRenderers =
            new Renderer[0];
        [SerializeField] private Color _mirrorFrameRevealColor =
            new Color(0.15f, 0.82f, 1f, 1f);
        [SerializeField, Range(0f, 1f)]
        private float _mirrorFrameColorStrength = 0.9f;

        [Header("Optional Audio")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _revealClip;
        [SerializeField] private AudioClip _heroClip;
        [Tooltip(
            "Generate restrained placeholder cues when production clips " +
            "are not assigned. Replace the clips for the final build.")]
        [SerializeField]
        private bool _useGeneratedPlaceholderCues = true;
        [SerializeField, Range(0f, 1f)]
        private float _revealVolume = 0.65f;
        [SerializeField, Range(0f, 1f)]
        private float _heroVolume = 0.85f;

        [Header("Hero Haptics")]
        [SerializeField, Range(0f, 1f)]
        private float _heroHapticFrequency = 1f;
        [SerializeField, Range(0f, 1f)]
        private float _heroHapticAmplitude = 0.65f;
        [SerializeField, Range(
            TransformationPresentationSafety
                .MinimumHeroHapticDurationSeconds,
            TransformationPresentationSafety
                .MaximumHeroHapticDurationSeconds)]
        private float _heroHapticDurationSeconds = 0.14f;

        private bool _sceneValuesCached;
        private float _baseKeyIntensity;
        private float _baseFillIntensity;
        private Color _baseKeyColor;
        private Color _baseFillColor;
        private bool _presentationActive;
        private bool _holdThroughOutgoingTransition;
        private bool _revealCuePlayed;
        private bool _heroCuePlayed;
        private bool _warnedMissingLights;
        private bool _leftHapticActive;
        private bool _rightHapticActive;
        private float _hapticStopTime;
        private AudioClip _generatedRevealClip;
        private AudioClip _generatedHeroClip;
        private MaterialPropertyBlock[] _mirrorFrameBlocks;
        private MaterialPropertyBlock[] _mirrorFrameOriginalBlocks;
        private Color[] _mirrorFrameBaseColors;
        private Color[] _mirrorFrameLegacyColors;
        private bool[] _mirrorFrameHasBaseColor;
        private bool[] _mirrorFrameHasLegacyColor;
        private bool[] _mirrorFrameHadPropertyBlock;

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");

        public TransformationPresentationStage CurrentStage
        {
            get;
            private set;
        } = TransformationPresentationStage.Inactive;

        public TransformationPresentationFrame CurrentFrame
        {
            get;
            private set;
        }

        public bool IsPresenting => _presentationActive;
        public bool IsUsingGeneratedCues =>
            _useGeneratedPlaceholderCues &&
            (_revealClip == null || _heroClip == null);

        private void Awake()
        {
            ResolveReferences();
            ValidateSettings();
            PrepareAudio();
            CacheSceneValues();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ValidateSettings();
            PrepareAudio();
            if (!_sceneValuesCached)
            {
                CacheSceneValues();
            }

            Subscribe();
            if (_gameFlow != null &&
                _gameFlow.CurrentPhase ==
                    ExperiencePhase.Transformation)
            {
                BeginPresentation(false);
            }
            else
            {
                RestorePresentation();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            RestorePresentation();
        }

        private void OnDestroy()
        {
            DestroyOwnedClip(ref _generatedRevealClip);
            DestroyOwnedClip(ref _generatedHeroClip);
        }

        private void Update()
        {
            UpdateHaptics();
            if (_gameFlow == null)
            {
                return;
            }

            if (_gameFlow.CurrentPhase ==
                ExperiencePhase.Transformation)
            {
                if (!_presentationActive)
                {
                    BeginPresentation(false);
                }

                ApplyProgress(
                    _gameFlow.NormalizedPhaseProgress,
                    true);
                return;
            }

            if (_gameFlow.CurrentPhase ==
                    ExperiencePhase.StageTransition &&
                _holdThroughOutgoingTransition)
            {
                return;
            }

            if (_presentationActive)
            {
                RestorePresentation();
            }
        }

        private void HandlePhaseChanged(ExperiencePhase phase)
        {
            if (phase == ExperiencePhase.Transformation)
            {
                BeginPresentation(true);
                return;
            }

            if (phase == ExperiencePhase.StageTransition &&
                _presentationActive &&
                _gameFlow != null &&
                _gameFlow.TransitionDestination ==
                    StageTransitionDestination.Judges)
            {
                ApplyProgress(1f, true);
                _holdThroughOutgoingTransition = true;
                return;
            }

            RestorePresentation();
        }

        private void BeginPresentation(bool playFutureCues)
        {
            if (!_sceneValuesCached)
            {
                CacheSceneValues();
            }

            _presentationActive = true;
            _holdThroughOutgoingTransition = false;
            var progress = _gameFlow == null
                ? 0f
                : _gameFlow.NormalizedPhaseProgress;
            _revealCuePlayed =
                !playFutureCues &&
                progress >=
                    TransformationPresentationMath
                        .LockEndNormalized;
            _heroCuePlayed =
                !playFutureCues &&
                progress >=
                    TransformationPresentationMath
                        .RevealEndNormalized;
            ApplyProgress(progress, playFutureCues);
        }

        private void ApplyProgress(
            float normalizedProgress,
            bool allowCues)
        {
            CurrentFrame = TransformationPresentationMath.Evaluate(
                normalizedProgress,
                _lockKeyMultiplier,
                _peakKeyMultiplier,
                _heroKeyMultiplier,
                _lockFillMultiplier,
                _peakFillMultiplier,
                _heroFillMultiplier,
                _heroColorRetention);
            CurrentStage = CurrentFrame.Stage;

            if (_keyLight != null)
            {
                _keyLight.intensity =
                    _baseKeyIntensity *
                    CurrentFrame.KeyIntensityMultiplier;
                _keyLight.color = Color.Lerp(
                    _baseKeyColor,
                    _revealKeyColor,
                    CurrentFrame.ColorBlend);
            }

            if (_fillLight != null)
            {
                _fillLight.intensity =
                    _baseFillIntensity *
                    CurrentFrame.FillIntensityMultiplier;
                _fillLight.color = Color.Lerp(
                    _baseFillColor,
                    _revealFillColor,
                    CurrentFrame.ColorBlend);
            }

            ApplyMirrorFrameColor(CurrentFrame.ColorBlend);

            if (!allowCues)
            {
                return;
            }

            var cue = TransformationPresentationCueRules.SelectCue(
                normalizedProgress,
                _revealCuePlayed,
                _heroCuePlayed);
            if (cue == TransformationPresentationCue.Hero)
            {
                // If a stalled frame crosses both boundaries, the completion
                // cue wins. Layering reveal and hero in one frame makes the
                // result louder and less legible without helping timing.
                _revealCuePlayed = true;
                _heroCuePlayed = true;
                // Reveal may still be playing from an earlier frame. This
                // AudioSource belongs only to the transformation, so stop it
                // before the one completion cue instead of layering both.
                if (_audioSource != null)
                {
                    _audioSource.Stop();
                }

                PlayOneShot(
                    GetHeroClip(),
                    _heroVolume);
                StartHeroHaptics();
                return;
            }

            if (cue == TransformationPresentationCue.Reveal)
            {
                _revealCuePlayed = true;
                PlayOneShot(
                    GetRevealClip(),
                    _revealVolume);
            }
        }

        private void RestorePresentation()
        {
            if (_sceneValuesCached)
            {
                if (_keyLight != null)
                {
                    _keyLight.intensity = _baseKeyIntensity;
                    _keyLight.color = _baseKeyColor;
                }

                if (_fillLight != null)
                {
                    _fillLight.intensity = _baseFillIntensity;
                    _fillLight.color = _baseFillColor;
                }

                ApplyMirrorFrameColor(0f);
            }

            if (_audioSource != null)
            {
                _audioSource.Stop();
            }

            StopHaptics();
            _presentationActive = false;
            _holdThroughOutgoingTransition = false;
            _revealCuePlayed = false;
            _heroCuePlayed = false;
            CurrentStage = TransformationPresentationStage.Inactive;
            CurrentFrame = default;
        }

        private void CacheSceneValues()
        {
            if (_keyLight != null)
            {
                _baseKeyIntensity = _keyLight.intensity;
                _baseKeyColor = _keyLight.color;
            }

            if (_fillLight != null)
            {
                _baseFillIntensity = _fillLight.intensity;
                _baseFillColor = _fillLight.color;
            }

            var hasMirrorFrame = CacheMirrorFrameValues();
            _sceneValuesCached =
                _keyLight != null ||
                _fillLight != null ||
                hasMirrorFrame;
            if (!_sceneValuesCached && !_warnedMissingLights)
            {
                _warnedMissingLights = true;
                Debug.LogWarning(
                    "[TransformationPresentation] No training lights or " +
                    "mirror-frame renderers are assigned. The " +
                    "transformation will keep its HUD but skip the visual " +
                    "envelope.",
                    this);
            }
        }

        private bool CacheMirrorFrameValues()
        {
            var count = _mirrorFrameRenderers == null
                ? 0
                : _mirrorFrameRenderers.Length;
            _mirrorFrameBlocks = new MaterialPropertyBlock[count];
            _mirrorFrameOriginalBlocks =
                new MaterialPropertyBlock[count];
            _mirrorFrameBaseColors = new Color[count];
            _mirrorFrameLegacyColors = new Color[count];
            _mirrorFrameHasBaseColor = new bool[count];
            _mirrorFrameHasLegacyColor = new bool[count];
            _mirrorFrameHadPropertyBlock = new bool[count];

            var cachedAny = false;
            for (var index = 0; index < count; index++)
            {
                var renderer = _mirrorFrameRenderers[index];
                var material = renderer == null
                    ? null
                    : renderer.sharedMaterial;
                if (renderer == null || material == null)
                {
                    continue;
                }

                var originalBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(originalBlock);
                _mirrorFrameOriginalBlocks[index] = originalBlock;
                _mirrorFrameHadPropertyBlock[index] =
                    !originalBlock.isEmpty;
                _mirrorFrameBlocks[index] =
                    new MaterialPropertyBlock();

                if (material.HasProperty(BaseColorId))
                {
                    _mirrorFrameHasBaseColor[index] = true;
                    _mirrorFrameBaseColors[index] =
                        material.GetColor(BaseColorId);
                    cachedAny = true;
                }

                if (material.HasProperty(LegacyColorId))
                {
                    _mirrorFrameHasLegacyColor[index] = true;
                    _mirrorFrameLegacyColors[index] =
                        material.GetColor(LegacyColorId);
                    cachedAny = true;
                }
            }

            return cachedAny;
        }

        private void ApplyMirrorFrameColor(float colorBlend)
        {
            if (_mirrorFrameBlocks == null ||
                _mirrorFrameRenderers == null)
            {
                return;
            }

            var count = Mathf.Min(
                _mirrorFrameBlocks.Length,
                _mirrorFrameRenderers.Length);
            var blend = Mathf.Clamp01(colorBlend) *
                        _mirrorFrameColorStrength;
            for (var index = 0; index < count; index++)
            {
                var renderer = _mirrorFrameRenderers[index];
                var block = _mirrorFrameBlocks[index];
                if (renderer == null || block == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(block);
                if (colorBlend <= Mathf.Epsilon &&
                    _mirrorFrameOriginalBlocks != null)
                {
                    renderer.SetPropertyBlock(
                        _mirrorFrameHadPropertyBlock[index]
                            ? _mirrorFrameOriginalBlocks[index]
                            : null);
                    continue;
                }

                if (_mirrorFrameHasBaseColor[index])
                {
                    block.SetColor(
                        BaseColorId,
                        Color.Lerp(
                            _mirrorFrameBaseColors[index],
                            _mirrorFrameRevealColor,
                            blend));
                }

                if (_mirrorFrameHasLegacyColor[index])
                {
                    block.SetColor(
                        LegacyColorId,
                        Color.Lerp(
                            _mirrorFrameLegacyColors[index],
                            _mirrorFrameRevealColor,
                            blend));
                }

                renderer.SetPropertyBlock(block);
            }
        }

        private void PlayOneShot(AudioClip clip, float volume)
        {
            if (_audioSource != null && clip != null)
            {
                _audioSource.PlayOneShot(
                    clip,
                    Mathf.Clamp01(volume));
            }
        }

        private AudioClip GetRevealClip()
        {
            return _revealClip != null
                ? _revealClip
                : _useGeneratedPlaceholderCues
                    ? _generatedRevealClip
                    : null;
        }

        private AudioClip GetHeroClip()
        {
            return _heroClip != null
                ? _heroClip
                : _useGeneratedPlaceholderCues
                    ? _generatedHeroClip
                    : null;
        }

        private void PrepareAudio()
        {
            if (_audioSource == null && Application.isPlaying)
            {
                _audioSource = GetComponent<AudioSource>();
                if (_audioSource == null)
                {
                    _audioSource = gameObject.AddComponent<AudioSource>();
                }
            }

            ConfigureAudioSource();
            if (!Application.isPlaying ||
                !_useGeneratedPlaceholderCues)
            {
                return;
            }

            if (_revealClip == null)
            {
                _generatedRevealClip ??= CreateOwnedTone(
                    "Transformation Reveal Placeholder",
                    1.45f,
                    220f,
                    660f,
                    0.16f);
            }

            if (_heroClip == null)
            {
                _generatedHeroClip ??= CreateOwnedTone(
                    "Transformation Hero Placeholder",
                    0.48f,
                    294f,
                    440f,
                    0.2f);
            }
        }

        private void ConfigureAudioSource()
        {
            if (_audioSource == null)
            {
                return;
            }

            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.spatialBlend = 0f;
        }

        private static AudioClip CreateOwnedTone(
            string clipName,
            float durationSeconds,
            float startFrequency,
            float endFrequency,
            float amplitude)
        {
            const int sampleRate = 48000;
            var sampleCount = Mathf.CeilToInt(
                sampleRate * Mathf.Max(0.01f, durationSeconds));
            var samples = new float[sampleCount];
            var phase = 0f;
            for (var index = 0; index < sampleCount; index++)
            {
                var normalized =
                    index / (float)Mathf.Max(1, sampleCount - 1);
                var frequency = Mathf.Lerp(
                    startFrequency,
                    endFrequency,
                    normalized * normalized);
                phase += Mathf.PI * 2f * frequency / sampleRate;
                var attack = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(normalized / 0.09f));
                var release = 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0.7f, 1f, normalized));
                var tone =
                    Mathf.Sin(phase) +
                    Mathf.Sin(phase * 1.5f) * 0.12f;
                samples[index] =
                    tone * attack * release * amplitude;
            }

            var clip = AudioClip.Create(
                clipName,
                sampleCount,
                1,
                sampleRate,
                false);
            clip.hideFlags = HideFlags.DontSave;
            clip.SetData(samples, 0);
            return clip;
        }

        private static void DestroyOwnedClip(ref AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(clip);
            }
            else
            {
                DestroyImmediate(clip);
            }

            clip = null;
        }

        private void StartHeroHaptics()
        {
            _leftHapticActive = OVRInput.IsControllerConnected(
                OVRInput.Controller.LTouch);
            _rightHapticActive = OVRInput.IsControllerConnected(
                OVRInput.Controller.RTouch);
            if (_leftHapticActive)
            {
                OVRInput.SetControllerVibration(
                    _heroHapticFrequency,
                    _heroHapticAmplitude,
                    OVRInput.Controller.LTouch);
            }

            if (_rightHapticActive)
            {
                OVRInput.SetControllerVibration(
                    _heroHapticFrequency,
                    _heroHapticAmplitude,
                    OVRInput.Controller.RTouch);
            }

            if (_leftHapticActive || _rightHapticActive)
            {
                _hapticStopTime =
                    Time.unscaledTime +
                    _heroHapticDurationSeconds;
            }
        }

        private void UpdateHaptics()
        {
            if ((_leftHapticActive || _rightHapticActive) &&
                Time.unscaledTime >= _hapticStopTime)
            {
                StopHaptics();
            }
        }

        private void StopHaptics()
        {
            if (_leftHapticActive &&
                OVRInput.IsControllerConnected(
                    OVRInput.Controller.LTouch))
            {
                OVRInput.SetControllerVibration(
                    0f,
                    0f,
                    OVRInput.Controller.LTouch);
            }

            if (_rightHapticActive &&
                OVRInput.IsControllerConnected(
                    OVRInput.Controller.RTouch))
            {
                OVRInput.SetControllerVibration(
                    0f,
                    0f,
                    OVRInput.Controller.RTouch);
            }

            _leftHapticActive = false;
            _rightHapticActive = false;
            _hapticStopTime = 0f;
        }

        private void ResolveReferences()
        {
            _gameFlow ??=
                FindFirstObjectByType<GameFlowController>();
            _audioSource ??= GetComponent<AudioSource>();
        }

        private void Subscribe()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= HandlePhaseChanged;
                _gameFlow.PhaseChanged += HandlePhaseChanged;
            }
        }

        private void Unsubscribe()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= HandlePhaseChanged;
            }
        }

        private void ValidateSettings()
        {
            _lockKeyMultiplier = Mathf.Clamp(
                _lockKeyMultiplier,
                0f,
                2f);
            _peakKeyMultiplier = Mathf.Clamp(
                _peakKeyMultiplier,
                0f,
                2f);
            _heroKeyMultiplier = Mathf.Clamp(
                _heroKeyMultiplier,
                0f,
                2f);
            _lockFillMultiplier = Mathf.Clamp(
                _lockFillMultiplier,
                0f,
                2f);
            _peakFillMultiplier = Mathf.Clamp(
                _peakFillMultiplier,
                0f,
                2f);
            _heroFillMultiplier = Mathf.Clamp(
                _heroFillMultiplier,
                0f,
                2f);
            _heroColorRetention = Mathf.Clamp01(
                _heroColorRetention);
            _mirrorFrameColorStrength = Mathf.Clamp01(
                _mirrorFrameColorStrength);
            _revealVolume = Mathf.Clamp01(_revealVolume);
            _heroVolume = Mathf.Clamp01(_heroVolume);
            _heroHapticFrequency = Mathf.Clamp01(
                _heroHapticFrequency);
            _heroHapticAmplitude = Mathf.Clamp01(
                _heroHapticAmplitude);
            _heroHapticDurationSeconds =
                TransformationPresentationSafety
                    .ClampHeroHapticDuration(
                        _heroHapticDurationSeconds);
        }

        private void OnValidate()
        {
            ResolveReferences();
            ValidateSettings();
            ConfigureAudioSource();
        }
    }
}
