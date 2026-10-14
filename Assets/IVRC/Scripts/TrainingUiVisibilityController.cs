using UnityEngine;

namespace IVRC
{
    public readonly struct TrainingUiVisibility
    {
        public TrainingUiVisibility(
            bool bodyMetrics,
            bool flowHud,
            bool rhythmLane)
        {
            BodyMetrics = bodyMetrics;
            FlowHud = flowHud;
            RhythmLane = rhythmLane;
        }

        public bool BodyMetrics { get; }
        public bool FlowHud { get; }
        public bool RhythmLane { get; }
    }

    public static class TrainingUiVisibilityPolicy
    {
        public static TrainingUiVisibility ForPhase(
            ExperiencePhase phase)
        {
            return phase switch
            {
                ExperiencePhase.Setup =>
                    new TrainingUiVisibility(true, true, false),
                ExperiencePhase.Ready =>
                    new TrainingUiVisibility(true, true, false),
                // The introduction brings its own wide panel, so the tall
                // status board steps aside rather than showing an empty
                // frame beside it.
                ExperiencePhase.Tutorial =>
                    new TrainingUiVisibility(false, false, false),
                ExperiencePhase.Countdown =>
                    new TrainingUiVisibility(false, false, true),
                ExperiencePhase.Training =>
                    new TrainingUiVisibility(false, false, true),
                ExperiencePhase.Transformation =>
                    new TrainingUiVisibility(false, true, false),
                ExperiencePhase.StageTransition =>
                    new TrainingUiVisibility(false, true, false),
                ExperiencePhase.StageEntrance =>
                    new TrainingUiVisibility(false, false, false),
                _ =>
                    new TrainingUiVisibility(false, false, false)
            };
        }
    }

    /// <summary>
    /// Keeps debug panels out of the exercise view without disabling the
    /// persistent system that must restore them after a retry.
    /// </summary>
    public sealed class TrainingUiVisibilityController : MonoBehaviour
    {
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private GameObject _bodyMetricsHudRoot;
        [SerializeField] private GameObject _trainingFlowHudRoot;
        [SerializeField] private GameObject _rhythmLaneRoot;
        [Header("Debug")]
        [SerializeField]
        private bool _showBodyMetricsDebug;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= ApplyPhase;
                _gameFlow.PhaseChanged += ApplyPhase;
                ApplyPhase(_gameFlow.CurrentPhase);
            }
        }

        private void OnDisable()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= ApplyPhase;
            }
        }

        public void ApplyPhase(ExperiencePhase phase)
        {
            var visibility =
                TrainingUiVisibilityPolicy.ForPhase(phase);
            SetActive(
                _bodyMetricsHudRoot,
                visibility.BodyMetrics &&
                _showBodyMetricsDebug);
            SetActive(
                _trainingFlowHudRoot,
                visibility.FlowHud);
            SetActive(
                _rhythmLaneRoot,
                visibility.RhythmLane);
        }

        public void SetBodyMetricsDebugVisible(bool visible)
        {
            _showBodyMetricsDebug = visible;
            if (_gameFlow != null)
            {
                ApplyPhase(_gameFlow.CurrentPhase);
            }
        }

        private void ResolveReferences()
        {
            _gameFlow ??=
                FindFirstObjectByType<GameFlowController>();
        }

        private static void SetActive(
            GameObject target,
            bool active)
        {
            if (target != null &&
                target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }
    }
}
