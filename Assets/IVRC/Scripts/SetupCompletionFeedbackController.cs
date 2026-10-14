using System.Collections.Generic;
using UnityEngine;

namespace IVRC
{
    public static class SetupCompletionFeedbackSafety
    {
        public const float MinimumHapticSeconds = 0.01f;
        public const float MaximumHapticSeconds = 0.12f;

        public static float ClampHapticSeconds(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
            {
                return MinimumHapticSeconds;
            }

            return Mathf.Clamp(
                seconds,
                MinimumHapticSeconds,
                MaximumHapticSeconds);
        }
    }

    public enum SetupCompletionCue
    {
        None,
        AlignmentComplete,
        HeightComplete,
        Ready
    }

    public sealed class SetupCompletionFeedbackState
    {
        private bool _alignmentAnnounced;
        private bool _heightAnnounced;
        private bool _readyAnnounced;

        public void ResetAll()
        {
            _alignmentAnnounced = false;
            _heightAnnounced = false;
            _readyAnnounced = false;
        }

        public SetupCompletionCue Observe(
            bool alignmentComplete,
            bool heightComplete,
            bool ready)
        {
            if (!alignmentComplete)
            {
                _alignmentAnnounced = false;
                _heightAnnounced = false;
                _readyAnnounced = false;
                return SetupCompletionCue.None;
            }

            if (!_alignmentAnnounced)
            {
                _alignmentAnnounced = true;
                return SetupCompletionCue.AlignmentComplete;
            }

            if (!heightComplete)
            {
                _heightAnnounced = false;
                _readyAnnounced = false;
                return SetupCompletionCue.None;
            }

            if (!_heightAnnounced)
            {
                _heightAnnounced = true;
                return SetupCompletionCue.HeightComplete;
            }

            if (!ready)
            {
                _readyAnnounced = false;
                return SetupCompletionCue.None;
            }

            if (_readyAnnounced)
            {
                return SetupCompletionCue.None;
            }

            _readyAnnounced = true;
            return SetupCompletionCue.Ready;
        }
    }

    /// <summary>
    /// Adds one restrained confirmation for successful A and X calibration.
    /// Failed attempts stay silent, and each success is announced only once
    /// until calibration is invalidated (for example by avatar switching).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SetupCompletionFeedbackController : MonoBehaviour
    {
        [Header("Systems")]
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private AvatarCalibrationController
            _alignmentCalibration;
        [SerializeField] private HeightCalibrationController
            _heightCalibration;
        [SerializeField] private AudioSource _audioSource;

        [Header("Audio")]
        [SerializeField] private AudioClip _alignmentCompleteClip;
        [SerializeField] private AudioClip _heightCompleteClip;
        [SerializeField] private AudioClip _readyClip;
        [SerializeField] private bool _useGeneratedPlaceholders = true;
        [SerializeField, Range(0f, 1f)]
        private float _alignmentVolume = 0.14f;
        [SerializeField, Range(0f, 1f)]
        private float _heightVolume = 0.14f;
        [SerializeField, Range(0f, 1f)]
        private float _readyVolume = 0.18f;

        [Header("Haptics")]
        [SerializeField, Range(0f, 1f)]
        private float _hapticFrequency = 0.75f;
        [SerializeField, Range(0f, 1f)]
        private float _hapticAmplitude = 0.28f;
        [SerializeField, Range(
            SetupCompletionFeedbackSafety.MinimumHapticSeconds,
            SetupCompletionFeedbackSafety.MaximumHapticSeconds)]
        private float _hapticSeconds = 0.06f;

        private readonly SetupCompletionFeedbackState _state =
            new SetupCompletionFeedbackState();
        private readonly List<AudioClip> _ownedGeneratedClips =
            new List<AudioClip>();
        private AudioClip _generatedAlignmentClip;
        private AudioClip _generatedHeightClip;
        private AudioClip _generatedReadyClip;
        private bool _leftHapticActive;
        private bool _rightHapticActive;
        private float _hapticStopTime;

        public bool IsUsingGeneratedPlaceholders =>
            _useGeneratedPlaceholders &&
            (_alignmentCompleteClip == null ||
             _heightCompleteClip == null ||
             _readyClip == null);

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
            PrepareAudio();
            _state.ResetAll();
            StopHaptics();
        }

        private void Update()
        {
            UpdateHapticStop();
            if (_gameFlow == null ||
                (_gameFlow.CurrentPhase != ExperiencePhase.Setup &&
                 _gameFlow.CurrentPhase != ExperiencePhase.Ready))
            {
                return;
            }

            var alignmentComplete =
                _alignmentCalibration != null &&
                _alignmentCalibration.HasCalibrated;
            var heightComplete =
                _heightCalibration != null &&
                _heightCalibration.LastCalibrationSucceeded;
            var cue = _state.Observe(
                alignmentComplete,
                heightComplete,
                _gameFlow.CurrentPhase == ExperiencePhase.Ready);
            PlayCue(cue);
        }

        private void OnDisable()
        {
            if (_audioSource != null)
            {
                _audioSource.Stop();
            }

            StopHaptics();
            _state.ResetAll();
        }

        private void OnDestroy()
        {
            foreach (var clip in _ownedGeneratedClips)
            {
                if (clip != null)
                {
                    Destroy(clip);
                }
            }

            _ownedGeneratedClips.Clear();
        }

        private void OnValidate()
        {
            ValidateSettings();
            ConfigureAudioSource();
        }

        private void PlayCue(SetupCompletionCue cue)
        {
            if (cue == SetupCompletionCue.None || _audioSource == null)
            {
                return;
            }

            AudioClip clip;
            float volume;
            OVRInput.Controller controller;
            switch (cue)
            {
                case SetupCompletionCue.AlignmentComplete:
                    clip = _alignmentCompleteClip ??
                           _generatedAlignmentClip;
                    volume = _alignmentVolume;
                    controller = OVRInput.Controller.RTouch;
                    break;
                case SetupCompletionCue.HeightComplete:
                    clip = _heightCompleteClip ??
                           _generatedHeightClip;
                    volume = _heightVolume;
                    controller = OVRInput.Controller.LTouch;
                    break;
                case SetupCompletionCue.Ready:
                    clip = _readyClip ?? _generatedReadyClip;
                    volume = _readyVolume;
                    controller = OVRInput.Controller.RTouch;
                    break;
                default:
                    return;
            }

            if (clip != null)
            {
                _audioSource.PlayOneShot(clip, volume);
            }

            StartHaptic(controller);
        }

        private void StartHaptic(OVRInput.Controller controller)
        {
            if (!OVRInput.IsControllerConnected(controller))
            {
                return;
            }

            OVRInput.SetControllerVibration(
                _hapticFrequency,
                _hapticAmplitude,
                controller);
            if (controller == OVRInput.Controller.LTouch)
            {
                _leftHapticActive = true;
            }
            else if (controller == OVRInput.Controller.RTouch)
            {
                _rightHapticActive = true;
            }
            _hapticStopTime = Time.unscaledTime + _hapticSeconds;
        }

        private void UpdateHapticStop()
        {
            if ((!_leftHapticActive && !_rightHapticActive) ||
                Time.unscaledTime < _hapticStopTime)
            {
                return;
            }

            StopHaptics();
        }

        private void StopHaptics()
        {
            if (_leftHapticActive)
            {
                OVRInput.SetControllerVibration(
                    0f,
                    0f,
                    OVRInput.Controller.LTouch);
            }

            if (_rightHapticActive)
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

        private void PrepareAudio()
        {
            ConfigureAudioSource();
            if (!_useGeneratedPlaceholders ||
                !Application.isPlaying)
            {
                return;
            }

            _generatedAlignmentClip ??= CreateOwnedTone(
                "Setup Alignment Complete Placeholder",
                0.12f,
                440f,
                554f);
            _generatedHeightClip ??= CreateOwnedTone(
                "Setup Height Complete Placeholder",
                0.12f,
                494f,
                622f);
            _generatedReadyClip ??= CreateOwnedTone(
                "Setup Ready Placeholder",
                0.18f,
                523f,
                784f);
        }

        private AudioClip CreateOwnedTone(
            string name,
            float duration,
            float startFrequency,
            float endFrequency)
        {
            const int sampleRate = 48000;
            var sampleCount = Mathf.CeilToInt(sampleRate * duration);
            var samples = new float[sampleCount];
            var phase = 0f;
            for (var index = 0; index < sampleCount; index++)
            {
                var normalized = index /
                    (float)Mathf.Max(1, sampleCount - 1);
                var frequency = Mathf.Lerp(
                    startFrequency,
                    endFrequency,
                    normalized);
                phase += Mathf.PI * 2f * frequency / sampleRate;
                var attack = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(normalized / 0.1f));
                var release = 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0.68f, 1f, normalized));
                samples[index] =
                    Mathf.Sin(phase) * attack * release * 0.18f;
            }

            var clip = AudioClip.Create(
                name,
                sampleCount,
                1,
                sampleRate,
                false);
            clip.hideFlags = HideFlags.DontSave;
            clip.SetData(samples, 0);
            _ownedGeneratedClips.Add(clip);
            return clip;
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

        private void ResolveReferences()
        {
            _gameFlow ??= FindFirstObjectByType<GameFlowController>();
            _alignmentCalibration ??=
                FindFirstObjectByType<AvatarCalibrationController>();
            _heightCalibration ??=
                FindFirstObjectByType<HeightCalibrationController>();
            _audioSource ??= GetComponent<AudioSource>();
        }

        private void ValidateSettings()
        {
            _alignmentVolume = Mathf.Clamp01(_alignmentVolume);
            _heightVolume = Mathf.Clamp01(_heightVolume);
            _readyVolume = Mathf.Clamp01(_readyVolume);
            _hapticFrequency = Mathf.Clamp01(_hapticFrequency);
            _hapticAmplitude = Mathf.Clamp01(_hapticAmplitude);
            _hapticSeconds = SetupCompletionFeedbackSafety
                .ClampHapticSeconds(_hapticSeconds);
        }
    }
}
