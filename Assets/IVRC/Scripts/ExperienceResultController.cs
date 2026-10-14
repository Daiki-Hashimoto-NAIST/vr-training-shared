using System;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Builds and freezes one final result from GameFlow's completed snapshots.
    /// HUD, stage feedback, logging, and later integrations can consume the
    /// same immutable value without reading live training systems.
    /// </summary>
    public sealed class ExperienceResultController : MonoBehaviour
    {
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private ExperienceScoreProfile _profile;

        public event Action<ExperienceScoreResult> ResultCompleted;

        public bool HasResult { get; private set; }
        public ExperienceScoreInput LastInput { get; private set; }
        public ExperienceScoreResult FinalResult { get; private set; }
        public int PoseSuccessCount { get; private set; }
        public int PoseCount { get; private set; }

        public bool HasTrainingTimingData =>
            LastInput.AverageRhythmAccuracy > 0f ||
            (_gameFlow != null &&
             _gameFlow.CompletedTimingPointCount > 0);

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            if (_gameFlow != null &&
                _gameFlow.CurrentPhase == ExperiencePhase.Results)
            {
                BuildResult();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnValidate()
        {
            ResolveReferences();
        }

        [ContextMenu("Build Final Result")]
        public void BuildResult()
        {
            ResolveReferences();
            if (_gameFlow == null || _profile == null)
            {
                HasResult = false;
                FinalResult = default;
                Debug.LogError(
                    "[ExperienceResult] GameFlow and score profile " +
                    "must both be assigned.",
                    this);
                return;
            }

            LastInput = new ExperienceScoreInput(
                _gameFlow.CompletedElbowReps,
                _gameFlow.CompletedArmRaiseReps,
                _gameFlow.CompletedTimingAccuracy,
                _gameFlow.CompletedGrowthNormalized);
            PoseSuccessCount = _gameFlow.CompletedPoseSuccessCount;
            PoseCount = _gameFlow.CompletedPoseCount;
            FinalResult = ExperienceScoreCalculator.Calculate(
                LastInput,
                _profile.ToSettings());
            HasResult = true;
            ResultCompleted?.Invoke(FinalResult);

            Debug.Log(
                $"[ExperienceResult] Training/final " +
                $"{FinalResult.FinalScore:F0}, rank " +
                $"{FinalResult.Rank}.",
                this);
        }

        [ContextMenu("Reset Final Result")]
        public void ResetResult()
        {
            HasResult = false;
            LastInput = default;
            FinalResult = default;
            PoseSuccessCount = 0;
            PoseCount = 0;
        }

        private void HandlePhaseChanged(ExperiencePhase phase)
        {
            if (phase == ExperiencePhase.Setup)
            {
                ResetResult();
            }
            else if (phase == ExperiencePhase.Results)
            {
                BuildResult();
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
            _gameFlow ??=
                FindFirstObjectByType<GameFlowController>();
        }
    }
}
