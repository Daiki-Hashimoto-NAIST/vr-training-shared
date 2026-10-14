using System;
using System.Collections.Generic;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Turns confirmed pose successes into restrained stage feedback.
    /// All animated values are derived from cached scene values so retries
    /// cannot accumulate light or audience transform drift.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseStageFeedbackController : MonoBehaviour
    {
        [Header("Flow")]
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private ThreePointPosingScorer _posingScorer;

        [Header("Scene Feedback")]
        [SerializeField] private Light[] _stageLights =
            Array.Empty<Light>();
        [SerializeField] private Transform _audienceRoot;

        [Header("Light Response")]
        [SerializeField, Min(1f)]
        private float _maximumLightIntensityMultiplier = 1.5f;
        [SerializeField, Min(0.01f)]
        private float _reactionResponseSeconds = 0.25f;

        [Header("Audience Motion")]
        [SerializeField, Range(0f, 0.12f)]
        private float _audienceBobHeight = 0.06f;
        [SerializeField, Range(0.1f, 3f)]
        private float _audienceBobFrequencyHz = 1.4f;
        [SerializeField, Range(0f, 8f)]
        private float _audienceTiltDegrees = 3f;
        [SerializeField, Range(0f, 1f)]
        private float _holdingSynchrony = 0.65f;

        [Header("Pose Completion")]
        [SerializeField, Range(0f, 1f)]
        private float _poseSuccessReaction = 0.8f;
        [SerializeField, Min(0.1f)]
        private float _poseSuccessPulseSeconds = 0.65f;
        [SerializeField, Range(0f, 1f)]
        private float _poseSuccessHapticFrequency = 0.8f;
        [SerializeField, Range(0f, 1f)]
        private float _poseSuccessHapticAmplitude = 0.42f;
        [SerializeField, Range(
            PoseStageFeedbackMath.MinimumHapticSeconds,
            PoseStageFeedbackMath.MaximumHapticSeconds)]
        private float _poseSuccessHapticSeconds = 0.09f;

        [Header("Pose Completion Audio")]
        [SerializeField] private AudioSource _poseSuccessSource;
        [SerializeField] private AudioClip _poseSuccessClip;
        [Tooltip(
            "Generate a restrained placeholder when the production pose " +
            "success clip is not assigned.")]
        [SerializeField]
        private bool _useGeneratedPoseSuccessCue = true;
        [SerializeField, Range(0f, 1f)]
        private float _poseSuccessVolume = 0.22f;

        [Header("Cheer Audio")]
        [SerializeField] private AudioSource _cheerSource;
        [SerializeField] private AudioClip _cheerClipOverride;

        [Tooltip(
            "A second crowd layered over the first. Two recordings of a room " +
            "cheering read as more people than one played louder. Leave " +
            "empty to use a single crowd.")]
        [SerializeField] private AudioSource _secondCheerSource;
        [SerializeField] private AudioClip _secondCheerClip;
        [SerializeField, Range(0f, 1f)]
        private float _successCheerVolume = 0.35f;
        [SerializeField, Min(0f)]
        private float _cheerCooldownSeconds = 0.8f;
        [SerializeField, Range(0f, 1f)]
        private float _cheerSpatialBlend = 0.45f;

        private float[] _baseLightIntensities =
            Array.Empty<float>();
        private Color[] _baseLightColors =
            Array.Empty<Color>();
        private Transform[] _audienceMembers =
            Array.Empty<Transform>();
        private Vector3[] _baseAudiencePositions =
            Array.Empty<Vector3>();
        private Quaternion[] _baseAudienceRotations =
            Array.Empty<Quaternion>();
        private float[] _audiencePhaseOffsets =
            Array.Empty<float>();

        private AudioClip _generatedCheerClip;
        private AudioClip _generatedPoseSuccessClip;
        private ExperiencePhase _currentPhase;
        private float _targetReaction;
        private float _reaction;
        private float _poseSuccessPulseRemaining;
        private float _cheerCooldownRemaining;
        private bool _feedbackActive;
        private bool _acceptScoreUpdates;
        private bool _celebrating;
        private bool _pendingSuccessCheer;
        private bool _leftHapticActive;
        private bool _rightHapticActive;
        private float _hapticStopTime;

        public float ReactionNormalized => _reaction;
        public int AudienceMemberCount => _audienceMembers.Length;
        public bool IsUsingGeneratedCheer =>
            _generatedCheerClip != null &&
            _cheerClipOverride == null;
        public bool IsUsingGeneratedPoseSuccessCue =>
            _useGeneratedPoseSuccessCue &&
            _poseSuccessClip == null;

        private void Awake()
        {
            ResolveReferences();
            ValidateSettings();
            CacheSceneValues();
            PrepareCheerAudio();
            PreparePoseSuccessAudio();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (_baseLightIntensities.Length == 0 &&
                _audienceMembers.Length == 0)
            {
                CacheSceneValues();
            }

            PrepareCheerAudio();
            PreparePoseSuccessAudio();
            Subscribe();
            HandlePhaseChanged(
                _gameFlow == null
                    ? ExperiencePhase.Setup
                    : _gameFlow.CurrentPhase);
        }

        private void OnDisable()
        {
            Unsubscribe();
            ResetFeedbackImmediate();
        }

        private void OnDestroy()
        {
            if (_generatedCheerClip != null)
            {
                Destroy(_generatedCheerClip);
                _generatedCheerClip = null;
            }

            DestroyOwnedClip(ref _generatedPoseSuccessClip);
        }

        private void OnValidate()
        {
            ResolveReferences();
            ValidateSettings();
        }

        private void Update()
        {
            if (!_feedbackActive)
            {
                return;
            }

            var deltaTime = Time.unscaledDeltaTime;
            _poseSuccessPulseRemaining = Mathf.Max(
                0f,
                _poseSuccessPulseRemaining - deltaTime);
            _cheerCooldownRemaining = Mathf.Max(
                0f,
                _cheerCooldownRemaining - deltaTime);

            if (_cheerCooldownRemaining <= Mathf.Epsilon &&
                _pendingSuccessCheer)
            {
                _pendingSuccessCheer = false;
                PlayCheer(_successCheerVolume);
            }
            UpdatePoseSuccessHaptics();

            var step = deltaTime /
                       Mathf.Max(
                           0.01f,
                           _reactionResponseSeconds);
            _reaction = Mathf.MoveTowards(
                _reaction,
                _targetReaction,
                step);
            ApplyLightFeedback();
            ApplyAudienceFeedback();
        }

        private void HandlePhaseChanged(ExperiencePhase phase)
        {
            _currentPhase = phase;
            if (phase == ExperiencePhase.Posing)
            {
                BeginPoseFeedback();
                return;
            }

            if (phase == ExperiencePhase.Results)
            {
                CompletePoseFeedback();
                return;
            }

            ResetFeedbackImmediate();
        }

        private void HandlePoseCompleted(PoseCompletionResult result)
        {
            if (!_acceptScoreUpdates ||
                _currentPhase != ExperiencePhase.Posing)
            {
                return;
            }

            // A timeout quietly moves on without negative feedback. Only a
            // confirmed success can drive the stage reaction.
            _targetReaction = 0f;

            if (!PoseStageFeedbackMath
                    .ShouldPlayPoseSuccessCue(result))
            {
                return;
            }

            _poseSuccessPulseRemaining =
                _poseSuccessPulseSeconds;
            _targetReaction = _poseSuccessReaction;
            PlayPoseSuccessCue();
            StartPoseSuccessHaptics();
        }

        private void BeginPoseFeedback()
        {
            ResetFeedbackImmediate();
            _currentPhase = ExperiencePhase.Posing;
            _feedbackActive = true;
            _acceptScoreUpdates = true;
        }

        private void CompletePoseFeedback()
        {
            _feedbackActive = true;
            _acceptScoreUpdates = false;
            _celebrating =
                _gameFlow != null &&
                _gameFlow.CompletedPoseSucceeded;
            _targetReaction = _celebrating
                ? 1f
                : 0f;

            if (!_celebrating)
            {
                return;
            }

            if (_cheerCooldownRemaining <= Mathf.Epsilon)
            {
                PlayCheer(_successCheerVolume);
            }
            else
            {
                _pendingSuccessCheer = true;
            }
        }

        private void PlayCheer(float volume)
        {
            var clip = _cheerClipOverride != null
                ? _cheerClipOverride
                : _generatedCheerClip;
            if (_cheerSource == null ||
                clip == null ||
                volume <= Mathf.Epsilon)
            {
                return;
            }

            _cheerSource.Stop();
            _cheerSource.PlayOneShot(
                clip,
                Mathf.Clamp01(volume));

            // Both crowds start together. Two recordings of a room reacting
            // read as a bigger room than one recording played louder, which
            // is the only lever a single clip has.
            if (_secondCheerSource != null &&
                _secondCheerClip != null)
            {
                _secondCheerSource.Stop();
                _secondCheerSource.PlayOneShot(
                    _secondCheerClip,
                    Mathf.Clamp01(volume));
            }

            _cheerCooldownRemaining =
                _cheerCooldownSeconds;
        }

        private void ApplyLightFeedback()
        {
            var visibleReaction = GetVisibleReaction();
            var count = Mathf.Min(
                _stageLights.Length,
                _baseLightIntensities.Length);
            for (var index = 0; index < count; index++)
            {
                var light = _stageLights[index];
                if (light == null)
                {
                    continue;
                }

                light.intensity =
                    _baseLightIntensities[index] *
                    Mathf.Lerp(
                        1f,
                        _maximumLightIntensityMultiplier,
                        visibleReaction);
                if (index < _baseLightColors.Length)
                {
                    light.color = _baseLightColors[index];
                }
            }
        }

        private void ApplyAudienceFeedback()
        {
            var visibleReaction = GetVisibleReaction();
            var count = Mathf.Min(
                _audienceMembers.Length,
                _baseAudiencePositions.Length);
            if (visibleReaction <= 0.0001f)
            {
                RestoreAudience();
                return;
            }

            var cycle =
                Time.unscaledTime *
                _audienceBobFrequencyHz *
                Mathf.PI *
                2f;
            var synchronize =
                _celebrating ||
                (_posingScorer != null &&
                 _posingScorer.State ==
                 PosingRuntimeState.Holding)
                    ? _holdingSynchrony
                    : 0f;
            var sharedWave = Mathf.Sin(cycle);

            for (var index = 0; index < count; index++)
            {
                var member = _audienceMembers[index];
                if (member == null)
                {
                    continue;
                }

                var independentWave = Mathf.Sin(
                    cycle +
                    _audiencePhaseOffsets[index]);
                var wave = Mathf.Lerp(
                    independentWave,
                    sharedWave,
                    synchronize);
                member.localPosition =
                    _baseAudiencePositions[index] +
                    Vector3.up *
                    (wave *
                     _audienceBobHeight *
                     visibleReaction);

                if (index < _baseAudienceRotations.Length)
                {
                    var tiltWave = Mathf.Cos(
                        cycle +
                        _audiencePhaseOffsets[index]);
                    member.localRotation =
                        _baseAudienceRotations[index] *
                        Quaternion.Euler(
                            0f,
                            0f,
                            tiltWave *
                            _audienceTiltDegrees *
                            visibleReaction);
                }
            }
        }

        private void ResetFeedbackImmediate()
        {
            _feedbackActive = false;
            _acceptScoreUpdates = false;
            _celebrating = false;
            _pendingSuccessCheer = false;
            _poseSuccessPulseRemaining = 0f;
            _targetReaction = 0f;
            _reaction = 0f;
            _cheerCooldownRemaining = 0f;
            if (_cheerSource != null)
            {
                _cheerSource.Stop();
            }
            if (_secondCheerSource != null)
            {
                _secondCheerSource.Stop();
            }
            if (_poseSuccessSource != null)
            {
                _poseSuccessSource.Stop();
            }
            StopPoseSuccessHaptics();
            RestoreLights();
            RestoreAudience();
        }

        private float GetVisibleReaction()
        {
            var completionPulse =
                PoseStageFeedbackMath.CalculateCompletionPulse(
                    _poseSuccessPulseRemaining,
                    _poseSuccessPulseSeconds,
                    _poseSuccessReaction);
            return Mathf.Max(_reaction, completionPulse);
        }

        private void StartPoseSuccessHaptics()
        {
            StopPoseSuccessHaptics();
            _leftHapticActive = OVRInput.IsControllerConnected(
                OVRInput.Controller.LTouch);
            _rightHapticActive = OVRInput.IsControllerConnected(
                OVRInput.Controller.RTouch);
            if (_leftHapticActive)
            {
                OVRInput.SetControllerVibration(
                    _poseSuccessHapticFrequency,
                    _poseSuccessHapticAmplitude,
                    OVRInput.Controller.LTouch);
            }

            if (_rightHapticActive)
            {
                OVRInput.SetControllerVibration(
                    _poseSuccessHapticFrequency,
                    _poseSuccessHapticAmplitude,
                    OVRInput.Controller.RTouch);
            }

            if (_leftHapticActive || _rightHapticActive)
            {
                _hapticStopTime =
                    Time.unscaledTime +
                    _poseSuccessHapticSeconds;
            }
        }

        private void UpdatePoseSuccessHaptics()
        {
            if ((_leftHapticActive || _rightHapticActive) &&
                Time.unscaledTime >= _hapticStopTime)
            {
                StopPoseSuccessHaptics();
            }
        }

        private void StopPoseSuccessHaptics()
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

        private void RestoreLights()
        {
            var count = Mathf.Min(
                _stageLights.Length,
                _baseLightIntensities.Length);
            for (var index = 0; index < count; index++)
            {
                var light = _stageLights[index];
                if (light == null)
                {
                    continue;
                }

                light.intensity =
                    _baseLightIntensities[index];
                if (index < _baseLightColors.Length)
                {
                    light.color = _baseLightColors[index];
                }
            }
        }

        private void RestoreAudience()
        {
            var count = Mathf.Min(
                _audienceMembers.Length,
                _baseAudiencePositions.Length);
            for (var index = 0; index < count; index++)
            {
                var member = _audienceMembers[index];
                if (member == null)
                {
                    continue;
                }

                member.localPosition =
                    _baseAudiencePositions[index];
                if (index < _baseAudienceRotations.Length)
                {
                    member.localRotation =
                        _baseAudienceRotations[index];
                }
            }
        }

        private void CacheSceneValues()
        {
            _stageLights ??= Array.Empty<Light>();
            _baseLightIntensities =
                new float[_stageLights.Length];
            _baseLightColors =
                new Color[_stageLights.Length];
            for (var index = 0;
                 index < _stageLights.Length;
                 index++)
            {
                var light = _stageLights[index];
                if (light == null)
                {
                    continue;
                }

                _baseLightIntensities[index] =
                    light.intensity;
                _baseLightColors[index] =
                    light.color;
            }

            var members = new List<Transform>();
            if (_audienceRoot != null)
            {
                foreach (Transform child in _audienceRoot)
                {
                    if (child.name.StartsWith(
                            "Audience_R",
                            StringComparison.Ordinal))
                    {
                        members.Add(child);
                    }
                }
            }

            _audienceMembers = members.ToArray();
            _baseAudiencePositions =
                new Vector3[_audienceMembers.Length];
            _baseAudienceRotations =
                new Quaternion[_audienceMembers.Length];
            _audiencePhaseOffsets =
                new float[_audienceMembers.Length];
            var phaseStep = _audienceMembers.Length <= 0
                ? 0f
                : Mathf.PI * 2f /
                  _audienceMembers.Length;
            for (var index = 0;
                 index < _audienceMembers.Length;
                 index++)
            {
                var member = _audienceMembers[index];
                _baseAudiencePositions[index] =
                    member.localPosition;
                _baseAudienceRotations[index] =
                    member.localRotation;
                _audiencePhaseOffsets[index] =
                    index * phaseStep;
            }
        }

        private void PrepareCheerAudio()
        {
            if (_cheerSource == null &&
                _audienceRoot != null)
            {
                _cheerSource =
                    _audienceRoot.GetComponentInParent<AudioSource>();
            }

            if (_cheerSource == null)
            {
                return;
            }

            _cheerSource.playOnAwake = false;
            _cheerSource.loop = false;
            _cheerSource.spatialBlend =
                _cheerSpatialBlend;
            _cheerSource.volume = 1f;

            if (_cheerClipOverride == null &&
                _generatedCheerClip == null &&
                Application.isPlaying)
            {
                _generatedCheerClip =
                    CreatePlaceholderCheer();
            }
        }

        private void PreparePoseSuccessAudio()
        {
            if (_poseSuccessSource == null && Application.isPlaying)
            {
                _poseSuccessSource = GetComponent<AudioSource>();
                if (_poseSuccessSource == null)
                {
                    _poseSuccessSource = gameObject.AddComponent<AudioSource>();
                }
            }

            if (_poseSuccessSource != null)
            {
                _poseSuccessSource.playOnAwake = false;
                _poseSuccessSource.loop = false;
                _poseSuccessSource.spatialBlend = 0f;
            }

            if (!Application.isPlaying ||
                !_useGeneratedPoseSuccessCue ||
                _poseSuccessClip != null ||
                _generatedPoseSuccessClip != null)
            {
                return;
            }

            _generatedPoseSuccessClip =
                CreatePoseSuccessPlaceholder();
        }

        private void PlayPoseSuccessCue()
        {
            var clip = _poseSuccessClip != null
                ? _poseSuccessClip
                : _useGeneratedPoseSuccessCue
                    ? _generatedPoseSuccessClip
                    : null;
            if (_poseSuccessSource == null || clip == null)
            {
                return;
            }

            _poseSuccessSource.Stop();
            _poseSuccessSource.PlayOneShot(
                clip,
                _poseSuccessVolume);
        }

        private static AudioClip CreatePoseSuccessPlaceholder()
        {
            const int sampleRate = 48000;
            const float durationSeconds = 0.28f;
            var sampleCount = Mathf.CeilToInt(
                sampleRate * durationSeconds);
            var samples = new float[sampleCount];
            var phase = 0f;
            for (var index = 0; index < sampleCount; index++)
            {
                var normalized =
                    index / (float)Mathf.Max(1, sampleCount - 1);
                var frequency = Mathf.Lerp(
                    523f,
                    784f,
                    Mathf.SmoothStep(0f, 1f, normalized));
                phase += Mathf.PI * 2f * frequency / sampleRate;
                var attack = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(normalized / 0.08f));
                var release = 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0.55f, 1f, normalized));
                samples[index] = Mathf.Sin(phase) *
                                 attack * release * 0.16f;
            }

            var clip = AudioClip.Create(
                "Pose Success Placeholder",
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

        private static AudioClip CreatePlaceholderCheer()
        {
            const int sampleRate = 22050;
            const float durationSeconds = 1.25f;
            var sampleCount =
                Mathf.CeilToInt(
                    sampleRate *
                    durationSeconds);
            var samples = new float[sampleCount];
            uint randomState = 0x91E10DA5u;
            var filteredNoise = 0f;

            for (var index = 0;
                 index < sampleCount;
                 index++)
            {
                randomState ^= randomState << 13;
                randomState ^= randomState >> 17;
                randomState ^= randomState << 5;
                var noise =
                    randomState /
                    (float)uint.MaxValue *
                    2f -
                    1f;
                filteredNoise =
                    filteredNoise * 0.88f +
                    noise * 0.12f;

                var time = index / (float)sampleRate;
                var normalizedTime =
                    time / durationSeconds;
                var attack = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(
                        normalizedTime / 0.12f));
                var release = 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        0.65f,
                        1f,
                        normalizedTime));
                var voices =
                    Mathf.Sin(
                        time * 181f * Mathf.PI * 2f) *
                    0.08f +
                    Mathf.Sin(
                        time * 247f * Mathf.PI * 2f) *
                    0.06f +
                    Mathf.Sin(
                        time * 319f * Mathf.PI * 2f) *
                    0.04f;
                samples[index] =
                    (filteredNoise * 0.34f + voices) *
                    attack *
                    release;
            }

            var clip = AudioClip.Create(
                "Generated Crowd Cheer Placeholder",
                sampleCount,
                1,
                sampleRate,
                false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void Subscribe()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -=
                    HandlePhaseChanged;
                _gameFlow.PhaseChanged +=
                    HandlePhaseChanged;
            }

            if (_posingScorer != null)
            {
            _posingScorer.PoseCompleted -=
                    HandlePoseCompleted;
                _posingScorer.PoseCompleted +=
                    HandlePoseCompleted;
            }
        }

        private void Unsubscribe()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -=
                    HandlePhaseChanged;
            }

            if (_posingScorer != null)
            {
                _posingScorer.PoseCompleted -=
                    HandlePoseCompleted;
            }
        }

        private void ResolveReferences()
        {
            _gameFlow ??=
                FindFirstObjectByType<GameFlowController>();
            _posingScorer ??=
                FindFirstObjectByType<ThreePointPosingScorer>();
        }

        private void ValidateSettings()
        {
            _maximumLightIntensityMultiplier =
                Mathf.Max(
                    1f,
                    _maximumLightIntensityMultiplier);
            _reactionResponseSeconds =
                Mathf.Max(
                    0.01f,
                    _reactionResponseSeconds);
            _audienceBobHeight =
                Mathf.Clamp(
                    _audienceBobHeight,
                    0f,
                    0.12f);
            _audienceBobFrequencyHz =
                Mathf.Clamp(
                    _audienceBobFrequencyHz,
                    0.1f,
                    3f);
            _audienceTiltDegrees =
                Mathf.Clamp(
                    _audienceTiltDegrees,
                    0f,
                    8f);
            _poseSuccessReaction =
                Mathf.Clamp01(_poseSuccessReaction);
            _poseSuccessPulseSeconds = Mathf.Max(
                0.1f,
                _poseSuccessPulseSeconds);
            _poseSuccessHapticFrequency =
                Mathf.Clamp01(_poseSuccessHapticFrequency);
            _poseSuccessHapticAmplitude =
                Mathf.Clamp01(_poseSuccessHapticAmplitude);
            _poseSuccessHapticSeconds =
                PoseStageFeedbackMath.ClampHapticSeconds(
                    _poseSuccessHapticSeconds);
            _poseSuccessVolume = Mathf.Clamp01(
                _poseSuccessVolume);
            _cheerCooldownSeconds =
                Mathf.Max(0f, _cheerCooldownSeconds);
            _cheerSpatialBlend =
                Mathf.Clamp01(_cheerSpatialBlend);
        }
    }
}
