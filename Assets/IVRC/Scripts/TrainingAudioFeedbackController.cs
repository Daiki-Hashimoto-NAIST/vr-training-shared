using System;
using System.Collections.Generic;
using UnityEngine;

namespace IVRC
{
    public enum TrainingAudioCue
    {
        None,
        CountdownTick,
        Go,
        MotionComplete,
        RhythmClear,
        ExerciseSwitch,

        // One per verdict, so the sound and the colour on the body name the
        // same judgement. An exhibition floor is loud enough that a
        // participant may only get one of the two.
        JudgementPerfect,
        JudgementGood,
        JudgementMiss,

        /// <summary>The whistle that ends the set.</summary>
        TrainingEnd
    }

    /// <summary>
    /// Pure mapping and priority rules for player-facing training sounds.
    /// A complete repetition owns one sound; UP and DOWN timing-point events
    /// deliberately do not produce additional confirmation sounds here.
    /// </summary>
    public static class TrainingAudioFeedbackRules
    {
        public static TrainingAudioCue GetRepCue(
            TrainingRepTimingResult result)
        {
            if (!TrainingSuccessFeedbackRules
                    .IsMotionCompleted(result))
            {
                return TrainingAudioCue.None;
            }

            return TrainingSuccessFeedbackRules
                    .IsRhythmClear(result)
                ? TrainingAudioCue.RhythmClear
                : TrainingAudioCue.MotionComplete;
        }

        /// <summary>
        /// The verdict sound for one completed repetition, read from the same
        /// accuracy the body's flash is coloured by — the weaker of the two
        /// timing points — so the two can never disagree about how the
        /// repetition went.
        ///
        /// A repetition that never completed makes no sound: there is no
        /// movement to report on.
        /// </summary>
        public static TrainingAudioCue GetJudgementCue(
            TrainingRepTimingResult result)
        {
            if (!TrainingSuccessFeedbackRules
                    .IsMotionCompleted(result))
            {
                return TrainingAudioCue.None;
            }

            var accuracy = Mathf.Min(
                result.Up.Accuracy,
                result.Down.Accuracy);
            return TrainingRhythmMath.GetTimingGrade(accuracy)
                switch
                {
                    TrainingTimingGrade.Perfect =>
                        TrainingAudioCue.JudgementPerfect,
                    TrainingTimingGrade.Good =>
                        TrainingAudioCue.JudgementGood,
                    _ => TrainingAudioCue.JudgementMiss
                };
        }

        public static int GetPriority(TrainingAudioCue cue)
        {
            return cue switch
            {
                // Above everything: the set is over, and a verdict for a
                // repetition that just landed must not talk over it.
                TrainingAudioCue.TrainingEnd => 100,
                TrainingAudioCue.Go => 80,
                // Above the generic rep sounds: the verdict is the more
                // specific statement about the same repetition.
                TrainingAudioCue.JudgementPerfect => 70,
                TrainingAudioCue.JudgementGood => 68,
                TrainingAudioCue.JudgementMiss => 66,
                TrainingAudioCue.RhythmClear => 60,
                TrainingAudioCue.ExerciseSwitch => 50,
                TrainingAudioCue.MotionComplete => 40,
                TrainingAudioCue.CountdownTick => 20,
                _ => 0
            };
        }
    }

    /// <summary>
    /// Run-local one-shot state kept separate from Unity audio so reset and
    /// duplicate behaviour can be covered by deterministic EditMode tests.
    /// </summary>
    public sealed class TrainingAudioCueState
    {
        private readonly HashSet<long> _announcedRepetitions =
            new HashSet<long>();

        private int _lastCountdownNumber = -1;
        private bool _goAnnounced;
        private int _observedClockSessionId = -1;
        private int _observedBlockIndex = -1;

        public void ResetRun()
        {
            _lastCountdownNumber = -1;
            _goAnnounced = false;
            _observedClockSessionId = -1;
            _observedBlockIndex = -1;
            _announcedRepetitions.Clear();
        }

        public bool TryAnnounceCountdown(int countdownNumber)
        {
            if (countdownNumber < 1 || countdownNumber > 3 ||
                countdownNumber == _lastCountdownNumber)
            {
                return false;
            }

            _lastCountdownNumber = countdownNumber;
            return true;
        }

        public bool TryAnnounceGo()
        {
            if (_goAnnounced)
            {
                return false;
            }

            _goAnnounced = true;
            return true;
        }

        public bool TryAnnounceRepetition(
            int clockSessionId,
            int noteIndex)
        {
            if (clockSessionId < 0 || noteIndex < 0)
            {
                return false;
            }

            var key = ((long)clockSessionId << 32) |
                      (uint)noteIndex;
            return _announcedRepetitions.Add(key);
        }

        public bool TryAnnounceBlockSwitch(
            int clockSessionId,
            int blockIndex)
        {
            if (clockSessionId < 0 || blockIndex < 0)
            {
                return false;
            }

            if (_observedClockSessionId != clockSessionId)
            {
                _observedClockSessionId = clockSessionId;
                _observedBlockIndex = blockIndex;
                return false;
            }

            if (_observedBlockIndex == blockIndex)
            {
                return false;
            }

            _observedBlockIndex = blockIndex;
            return true;
        }
    }

    /// <summary>
    /// Presents the smallest useful 2D audio vocabulary for Training:
    /// countdown, GO, one result per repetition, and exercise changes.
    /// Missing production clips use quiet generated placeholders so the
    /// interaction can be tested before final audio is delivered.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10250)]
    public sealed class TrainingAudioFeedbackController : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private RhythmClock _rhythmClock;
        [SerializeField] private TrainingRhythmChart _rhythmChart;
        [SerializeField] private TrainingRhythmScorer _rhythmScorer;
        [SerializeField] private AudioSource _audioSource;

        [Header("Replaceable Audio Clips")]
        [SerializeField] private AudioClip _countdownTickClip;
        [SerializeField] private AudioClip _goClip;
        [SerializeField] private AudioClip _motionCompleteClip;
        [SerializeField] private AudioClip _rhythmClearClip;
        [SerializeField] private AudioClip _exerciseSwitchClip;

        [Header("Verdicts")]
        [Tooltip(
            "Played instead of the generic repetition sound when set. Keyed " +
            "to the same judgement the body's flash is coloured by.")]
        [SerializeField] private AudioClip _judgementPerfectClip;
        [SerializeField] private AudioClip _judgementGoodClip;
        [SerializeField] private AudioClip _judgementMissClip;
        [SerializeField, Range(0f, 1f)]
        private float _judgementVolume = 0.7f;

        [Tooltip("Sounds once when the set ends.")]
        [SerializeField] private AudioClip _trainingEndClip;
        [SerializeField, Range(0f, 1f)]
        private float _trainingEndVolume = 0.8f;

        [Header("Mix")]
        [SerializeField, Range(0f, 1f)]
        private float _countdownTickVolume = 0.14f;
        [SerializeField, Range(0f, 1f)]
        private float _goVolume = 0.2f;
        [SerializeField, Range(0f, 1f)]
        private float _motionCompleteVolume = 0.12f;
        [SerializeField, Range(0f, 1f)]
        private float _rhythmClearVolume = 0.18f;
        [SerializeField, Range(0f, 1f)]
        private float _exerciseSwitchVolume = 0.16f;
        [SerializeField, Min(0f)]
        private float _switchDelayAfterResultSeconds = 0.05f;

        private readonly TrainingAudioCueState _cueState =
            new TrainingAudioCueState();
        private readonly List<AudioClip> _ownedGeneratedClips =
            new List<AudioClip>();

        private AudioClip _generatedCountdownTickClip;
        private AudioClip _generatedGoClip;
        private bool _wasInTraining;
        private bool _trainingEndPlayed;
        private AudioClip _generatedMotionCompleteClip;
        private AudioClip _generatedRhythmClearClip;
        private AudioClip _generatedExerciseSwitchClip;
        private bool _wasPausedForTracking;
        private bool _switchCuePending;
        private double _switchCueDueDspTime;
        private int _activeCuePriority;
        private double _activeCueUntilDspTime;

        public bool IsUsingGeneratedCues =>
            _countdownTickClip == null ||
            _goClip == null ||
            _motionCompleteClip == null ||
            _rhythmClearClip == null ||
            _exerciseSwitchClip == null;

        private void Awake()
        {
            ResolveReferences();
            ValidateSettings();
            PrepareAudio();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ValidateSettings();
            PrepareAudio();
            _cueState.ResetRun();
            _wasPausedForTracking = false;
            Subscribe();
        }

        private void Update()
        {
            if (_gameFlow == null)
            {
                return;
            }

            ExpireActiveCue();
            if (_gameFlow.CurrentPhase == ExperiencePhase.Countdown)
            {
                if (_cueState.TryAnnounceCountdown(
                        _gameFlow.CountdownNumber))
                {
                    TryPlayCue(TrainingAudioCue.CountdownTick);
                }

                return;
            }

            if (_gameFlow.CurrentPhase != ExperiencePhase.Training)
            {
                return;
            }

            if (_gameFlow.IsPausedForTracking)
            {
                if (!_wasPausedForTracking)
                {
                    _wasPausedForTracking = true;
                    StopPlayback();
                }

                return;
            }

            _wasPausedForTracking = false;
            TryPlayTrainingEnd();
            ObserveActiveExerciseBlock();
            FlushPendingSwitchCue();
        }

        private void OnDisable()
        {
            Unsubscribe();
            StopPlayback();
            _cueState.ResetRun();
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
            ResolveReferences();
            ValidateSettings();
            ConfigureAudioSource();
        }

        private void HandlePhaseChanged(ExperiencePhase phase)
        {
            var leftTraining =
                _wasInTraining &&
                phase != ExperiencePhase.Training;
            _wasInTraining = phase == ExperiencePhase.Training;

            // The whistle now sounds the moment the last repetition lands,
            // a beat or so before the phase actually changes, because that
            // is when the set ends as far as the participant is concerned.
            // Stopping the source here would cut the sound it was moved
            // early to deliver, so the stop is skipped when the whistle is
            // the thing playing.
            StopPlayback(!(leftTraining && _trainingEndPlayed));

            // Only if the set never reached its end — a retry or an abort
            // still wants the transition announced.
            if (leftTraining &&
                !_trainingEndPlayed &&
                _trainingEndClip != null &&
                _audioSource != null)
            {
                _audioSource.PlayOneShot(
                    _trainingEndClip,
                    _trainingEndVolume);
            }

            if (leftTraining)
            {
                _trainingEndPlayed = false;
            }

            if (phase == ExperiencePhase.Countdown ||
                phase == ExperiencePhase.Setup)
            {
                _cueState.ResetRun();
                _wasPausedForTracking = false;
                return;
            }

            if (phase == ExperiencePhase.Training &&
                _cueState.TryAnnounceGo())
            {
                TryPlayCue(TrainingAudioCue.Go);
            }
        }

        private void HandleRepTimingResult(
            TrainingRepTimingResult result)
        {
            if (_gameFlow == null ||
                _gameFlow.CurrentPhase != ExperiencePhase.Training ||
                _gameFlow.IsPausedForTracking ||
                !_cueState.TryAnnounceRepetition(
                    result.ClockSessionId,
                    result.NoteIndex))
            {
                return;
            }

            // The verdict is the more specific thing to say about this
            // repetition, so it speaks when a clip has been provided and the
            // generic rep sound covers for it when one has not.
            var judgement =
                TrainingAudioFeedbackRules.GetJudgementCue(result);
            if (judgement != TrainingAudioCue.None &&
                GetClip(judgement) != null)
            {
                TryPlayCue(judgement);
                return;
            }

            var cue = TrainingAudioFeedbackRules.GetRepCue(result);
            if (cue != TrainingAudioCue.None)
            {
                TryPlayCue(cue);
            }
        }

        private void ObserveActiveExerciseBlock()
        {
            if (_rhythmClock == null ||
                _rhythmChart == null ||
                !_rhythmChart.HasSession)
            {
                return;
            }

            var notes = _rhythmChart.ScheduledNotes;
            for (var index = 0; index < notes.Count; index++)
            {
                if (_rhythmChart.GetTimingVisualState(
                        index,
                        RepTimingPoint.Down) !=
                    TrainingTimingVisualState.Pending)
                {
                    continue;
                }

                if (_cueState.TryAnnounceBlockSwitch(
                        _rhythmClock.ClockSessionId,
                        notes[index].BlockIndex))
                {
                    ScheduleSwitchCue();
                }

                return;
            }
        }

        private void ScheduleSwitchCue()
        {
            var now = AudioSettings.dspTime;
            _switchCuePending = true;
            _switchCueDueDspTime = Math.Max(
                now + _switchDelayAfterResultSeconds,
                _activeCueUntilDspTime +
                _switchDelayAfterResultSeconds);
        }

        private void FlushPendingSwitchCue()
        {
            if (!_switchCuePending)
            {
                return;
            }

            var now = AudioSettings.dspTime;
            if (now < _switchCueDueDspTime)
            {
                return;
            }

            if (now < _activeCueUntilDspTime)
            {
                _switchCueDueDspTime =
                    _activeCueUntilDspTime +
                    _switchDelayAfterResultSeconds;
                return;
            }

            _switchCuePending = false;
            TryPlayCue(TrainingAudioCue.ExerciseSwitch);
        }

        private void TryPlayCue(TrainingAudioCue cue)
        {
            var clip = GetClip(cue);
            if (_audioSource == null || clip == null)
            {
                return;
            }

            var priority = TrainingAudioFeedbackRules.GetPriority(cue);
            var now = AudioSettings.dspTime;
            if (now < _activeCueUntilDspTime &&
                priority < _activeCuePriority)
            {
                return;
            }

            if (_audioSource.isPlaying)
            {
                _audioSource.Stop();
            }

            _audioSource.PlayOneShot(clip, GetVolume(cue));
            _activeCuePriority = priority;
            _activeCueUntilDspTime =
                now + Math.Max(0.01d, clip.length);
        }

        private AudioClip GetClip(TrainingAudioCue cue)
        {
            return cue switch
            {
                TrainingAudioCue.CountdownTick =>
                    _countdownTickClip ??
                    _generatedCountdownTickClip,
                TrainingAudioCue.Go =>
                    _goClip ?? _generatedGoClip,
                TrainingAudioCue.MotionComplete =>
                    _motionCompleteClip ??
                    _generatedMotionCompleteClip,
                TrainingAudioCue.RhythmClear =>
                    _rhythmClearClip ??
                    _generatedRhythmClearClip,
                TrainingAudioCue.ExerciseSwitch =>
                    _exerciseSwitchClip ??
                    _generatedExerciseSwitchClip,
                TrainingAudioCue.JudgementPerfect =>
                    _judgementPerfectClip,
                TrainingAudioCue.JudgementGood =>
                    _judgementGoodClip,
                TrainingAudioCue.JudgementMiss =>
                    _judgementMissClip,
                TrainingAudioCue.TrainingEnd =>
                    _trainingEndClip,
                _ => null
            };
        }

        private float GetVolume(TrainingAudioCue cue)
        {
            return cue switch
            {
                TrainingAudioCue.JudgementPerfect or
                TrainingAudioCue.JudgementGood or
                TrainingAudioCue.JudgementMiss =>
                    _judgementVolume,
                TrainingAudioCue.TrainingEnd =>
                    _trainingEndVolume,
                TrainingAudioCue.CountdownTick =>
                    _countdownTickVolume,
                TrainingAudioCue.Go => _goVolume,
                TrainingAudioCue.MotionComplete =>
                    _motionCompleteVolume,
                TrainingAudioCue.RhythmClear =>
                    _rhythmClearVolume,
                TrainingAudioCue.ExerciseSwitch =>
                    _exerciseSwitchVolume,
                _ => 0f
            };
        }

        private void ExpireActiveCue()
        {
            if (AudioSettings.dspTime < _activeCueUntilDspTime)
            {
                return;
            }

            _activeCuePriority = 0;
            _activeCueUntilDspTime = 0d;
        }

        /// <summary>
        /// The whistle that ends the set, played on the repetition that
        /// ends it rather than on the phase change that follows.
        /// </summary>
        private void TryPlayTrainingEnd()
        {
            if (_trainingEndPlayed ||
                _rhythmChart == null ||
                !_rhythmChart.IsCompleted)
            {
                return;
            }

            _trainingEndPlayed = true;
            if (_trainingEndClip != null &&
                _audioSource != null)
            {
                _audioSource.PlayOneShot(
                    _trainingEndClip,
                    _trainingEndVolume);
            }
        }

        private void StopPlayback(bool stopSource = true)
        {
            if (stopSource && _audioSource != null)
            {
                _audioSource.Stop();
            }

            _switchCuePending = false;
            _switchCueDueDspTime = 0d;
            _activeCuePriority = 0;
            _activeCueUntilDspTime = 0d;
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
            if (!Application.isPlaying)
            {
                return;
            }

            _generatedCountdownTickClip ??= CreateOwnedTone(
                "Training Countdown Tick Placeholder",
                0.08f,
                520f,
                520f);
            _generatedGoClip ??= CreateOwnedTone(
                "Training GO Placeholder",
                0.18f,
                620f,
                880f);
            _generatedMotionCompleteClip ??= CreateOwnedTone(
                "Training Motion Complete Placeholder",
                0.1f,
                440f,
                520f);
            _generatedRhythmClearClip ??= CreateOwnedTone(
                "Training Rhythm Clear Placeholder",
                0.16f,
                660f,
                990f);
            _generatedExerciseSwitchClip ??= CreateOwnedTone(
                "Training Exercise Switch Placeholder",
                0.2f,
                392f,
                659f);
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

        private AudioClip CreateOwnedTone(
            string clipName,
            float durationSeconds,
            float startFrequency,
            float endFrequency)
        {
            const int sampleRate = 48000;
            var sampleCount = Mathf.CeilToInt(
                sampleRate * durationSeconds);
            var samples = new float[sampleCount];
            var phase = 0f;

            for (var index = 0; index < sampleCount; index++)
            {
                var normalized =
                    index / (float)Mathf.Max(1, sampleCount - 1);
                var frequency = Mathf.Lerp(
                    startFrequency,
                    endFrequency,
                    normalized);
                phase += Mathf.PI * 2f * frequency / sampleRate;

                var attack = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(normalized / 0.12f));
                var release = 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0.72f, 1f, normalized));
                var tone =
                    Mathf.Sin(phase) +
                    Mathf.Sin(phase * 2f) * 0.16f;
                samples[index] =
                    tone * attack * release * 0.24f;
            }

            var clip = AudioClip.Create(
                clipName,
                sampleCount,
                1,
                sampleRate,
                false);
            clip.hideFlags = HideFlags.DontSave;
            clip.SetData(samples, 0);
            _ownedGeneratedClips.Add(clip);
            return clip;
        }

        private void Subscribe()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= HandlePhaseChanged;
                _gameFlow.PhaseChanged += HandlePhaseChanged;
            }

            if (_rhythmScorer != null)
            {
                _rhythmScorer.RepTimingResultReady -=
                    HandleRepTimingResult;
                _rhythmScorer.RepTimingResultReady +=
                    HandleRepTimingResult;
            }
        }

        private void Unsubscribe()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= HandlePhaseChanged;
            }

            if (_rhythmScorer != null)
            {
                _rhythmScorer.RepTimingResultReady -=
                    HandleRepTimingResult;
            }
        }

        private void ResolveReferences()
        {
            _gameFlow ??= FindFirstObjectByType<GameFlowController>();
            _rhythmClock ??= FindFirstObjectByType<RhythmClock>();
            _rhythmChart ??=
                FindFirstObjectByType<TrainingRhythmChart>();
            _rhythmScorer ??=
                FindFirstObjectByType<TrainingRhythmScorer>();
            _audioSource ??= GetComponent<AudioSource>();
        }

        private void ValidateSettings()
        {
            _countdownTickVolume = Mathf.Clamp01(
                _countdownTickVolume);
            _goVolume = Mathf.Clamp01(_goVolume);
            _motionCompleteVolume = Mathf.Clamp01(
                _motionCompleteVolume);
            _rhythmClearVolume = Mathf.Clamp01(
                _rhythmClearVolume);
            _exerciseSwitchVolume = Mathf.Clamp01(
                _exerciseSwitchVolume);
            _switchDelayAfterResultSeconds = Mathf.Max(
                0f,
                _switchDelayAfterResultSeconds);
        }
    }
}
