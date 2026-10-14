using UnityEngine;

namespace IVRC
{
    [CreateAssetMenu(
        fileName = "ExperienceScoreProfile",
        menuName = "IVRC/Experience Score Profile")]
    public sealed class ExperienceScoreProfile : ScriptableObject
    {
        [Header("Rep Targets")]
        [SerializeField, Min(0)] private int _targetElbowReps = 12;
        [SerializeField, Min(0)] private int _targetArmReps = 12;

        [Header("Training Weights")]
        [SerializeField] private ExperienceTrainingWeights
            _trainingWeights = new ExperienceTrainingWeights
            {
                Reps = 0.4f,
                Rhythm = 0.35f,
                Growth = 0.25f
            };

        [Header("Rank Thresholds")]
        [SerializeField] private ExperienceRankThresholds
            _rankThresholds = new ExperienceRankThresholds
            {
                S = 90f,
                A = 80f,
                B = 70f,
                C = 60f
            };

        public ExperienceScoreSettings ToSettings()
        {
            return new ExperienceScoreSettings(
                _targetElbowReps,
                _targetArmReps,
                _trainingWeights,
                _rankThresholds);
        }

        private void OnValidate()
        {
            _targetElbowReps = Mathf.Max(0, _targetElbowReps);
            _targetArmReps = Mathf.Max(0, _targetArmReps);

            _trainingWeights.Reps = SanitizeWeight(
                _trainingWeights.Reps);
            _trainingWeights.Rhythm = SanitizeWeight(
                _trainingWeights.Rhythm);
            _trainingWeights.Growth = SanitizeWeight(
                _trainingWeights.Growth);
            if (_trainingWeights.Reps +
                _trainingWeights.Rhythm +
                _trainingWeights.Growth <= Mathf.Epsilon)
            {
                _trainingWeights.Reps = 1f;
            }

            _rankThresholds.S = SanitizeThreshold(
                _rankThresholds.S,
                90f);
            _rankThresholds.A = Mathf.Min(
                _rankThresholds.S,
                SanitizeThreshold(_rankThresholds.A, 80f));
            _rankThresholds.B = Mathf.Min(
                _rankThresholds.A,
                SanitizeThreshold(_rankThresholds.B, 70f));
            _rankThresholds.C = Mathf.Min(
                _rankThresholds.B,
                SanitizeThreshold(_rankThresholds.C, 60f));
        }

        private static float SanitizeWeight(float value)
        {
            return float.IsNaN(value) ||
                   float.IsInfinity(value)
                ? 0f
                : Mathf.Max(0f, value);
        }

        private static float SanitizeThreshold(
            float value,
            float fallback)
        {
            return float.IsNaN(value) ||
                   float.IsInfinity(value)
                ? fallback
                : Mathf.Clamp(value, 0f, 100f);
        }
    }
}
