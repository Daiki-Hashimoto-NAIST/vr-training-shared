using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Safe placeholder proving that the growth signal reaches an output.
    /// It changes no avatar transforms or meshes; the current value and MOCK
    /// status are surfaced by ExperienceFlowHud.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MockMuscleGrowthOutput :
        MonoBehaviour,
        IMuscleGrowthOutput
    {
        [SerializeField] private bool _logMilestones;
        [SerializeField, Range(0.1f, 1f)]
        private float _milestoneStep = 0.25f;

        private int _lastLoggedMilestone;

        public string OutputLabel => "MOCK";
        public bool IsAvailable => isActiveAndEnabled;
        public bool IsMock => true;
        public float LastAppliedGrowthNormalized { get; private set; }

        public void ApplyGrowth(float normalizedGrowth)
        {
            LastAppliedGrowthNormalized =
                MuscleGrowthMath.SanitizeNormalized(
                    normalizedGrowth);
            if (!_logMilestones)
            {
                return;
            }

            var milestone = Mathf.FloorToInt(
                LastAppliedGrowthNormalized /
                Mathf.Max(0.1f, _milestoneStep));
            if (milestone <= _lastLoggedMilestone)
            {
                return;
            }

            _lastLoggedMilestone = milestone;
            Debug.Log(
                $"[MuscleGrowthMock] Applied " +
                $"{LastAppliedGrowthNormalized:P0}.",
                this);
        }

        public void ResetGrowth()
        {
            LastAppliedGrowthNormalized = 0f;
            _lastLoggedMilestone = 0;
        }

        private void OnValidate()
        {
            _milestoneStep =
                float.IsNaN(_milestoneStep) ||
                float.IsInfinity(_milestoneStep)
                    ? 0.25f
                    : Mathf.Clamp(
                        _milestoneStep,
                        0.1f,
                        1f);
        }
    }
}
