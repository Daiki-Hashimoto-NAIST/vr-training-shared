using UnityEngine;

namespace IVRC.Desktop
{
    /// <summary>
    /// Places three transforms where the pose currently being asked for
    /// wants them, so the posing routine can be driven and watched without a
    /// headset. Without this the desktop run stalls in Posing on
    /// TrackingLost, because there are no controllers to sample.
    ///
    /// Desktop simulation only. It never touches the scorer unless desktop
    /// mode actually resolved, so the headset path is untouched.
    /// </summary>
    [DefaultExecutionOrder(10200)]
    [DisallowMultipleComponent]
    public sealed class DesktopPosingDriver : MonoBehaviour
    {
        [Header("Systems")]
        [SerializeField] private DesktopSimulationManager _manager;
        [SerializeField] private ThreePointPosingScorer _scorer;

        [Header("Driven Points")]
        [SerializeField] private Transform _headPoint;
        [SerializeField] private Transform _leftHandPoint;
        [SerializeField] private Transform _rightHandPoint;

        [Header("Simulated Performance")]
        [Tooltip(
            "1 puts the hands exactly on target so every pose clears and " +
            "the routine walks all the way through. Lower it to watch a " +
            "pose time out instead.")]
        [SerializeField, Range(0f, 1f)]
        private float _accuracy = 1f;
        [Tooltip(
            "How far off target a fully inaccurate run sits, in metres.")]
        [SerializeField, Min(0f)]
        private float _missDistanceMeters = 0.5f;
        [Tooltip(
            "Used until the scorer reports the calibrated body scale.")]
        [SerializeField, Min(0.1f)]
        private float _fallbackBodyScaleMeters = 1.66f;

        private bool _isDriving;

        public bool IsDriving => _isDriving;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            // DesktopSimulationManager resolves Auto in its own Start, so the
            // decision is only reliable from here on.
            TryBeginDriving();
        }

        private void LateUpdate()
        {
            if (!_isDriving)
            {
                TryBeginDriving();
                if (!_isDriving)
                {
                    return;
                }
            }

            var profile = _scorer == null
                ? null
                : _scorer.CurrentProfile;
            if (profile == null ||
                _headPoint == null ||
                _leftHandPoint == null ||
                _rightHandPoint == null)
            {
                return;
            }

            var target = profile.ToSettings().Target;
            var bodyScale = _scorer.LastBodyScaleMeters > 0.01f
                ? _scorer.LastBodyScaleMeters
                : _fallbackBodyScaleMeters;
            var stage = Quaternion.Euler(
                0f,
                _scorer.StageYawDegrees,
                0f);

            // The scorer measures hands relative to the head in stage space,
            // so the head may sit anywhere as long as the offsets hold.
            var headPosition = _headPoint.position;
            _headPoint.rotation =
                stage *
                Quaternion.Euler(
                    target.HeadEulerDegreesInStageFrame);

            var miss = Mathf.Clamp01(1f - _accuracy) *
                Mathf.Max(0f, _missDistanceMeters);
            _leftHandPoint.SetPositionAndRotation(
                headPosition +
                stage *
                (target.LeftHandPositionFromHeadNormalized *
                 bodyScale) +
                Vector3.up * miss,
                stage *
                Quaternion.Euler(
                    target.LeftHandEulerDegreesInStageFrame));
            _rightHandPoint.SetPositionAndRotation(
                headPosition +
                stage *
                (target.RightHandPositionFromHeadNormalized *
                 bodyScale) +
                Vector3.up * miss,
                stage *
                Quaternion.Euler(
                    target.RightHandEulerDegreesInStageFrame));
        }

        private void TryBeginDriving()
        {
            ResolveReferences();
            if (_manager == null ||
                !_manager.IsDesktopActive ||
                _scorer == null ||
                _headPoint == null ||
                _leftHandPoint == null ||
                _rightHandPoint == null)
            {
                return;
            }

            _scorer.UsePoseOverrides(
                _headPoint,
                _leftHandPoint,
                _rightHandPoint);
            _isDriving = true;
            Debug.Log(
                "[DesktopPosing] Driving the posing routine from " +
                "simulated points.",
                this);
        }

        private void ResolveReferences()
        {
            _manager ??=
                FindFirstObjectByType<DesktopSimulationManager>(
                    FindObjectsInactive.Include);
            _scorer ??=
                FindFirstObjectByType<ThreePointPosingScorer>(
                    FindObjectsInactive.Include);
        }
    }
}
