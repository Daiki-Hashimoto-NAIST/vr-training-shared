using UnityEngine;

namespace IVRC.Desktop
{
    /// <summary>
    /// Feeds the authored desktop height into the existing calibration
    /// controllers so the normal readiness gate in GameFlowController is
    /// satisfied without a headset.
    ///
    /// The height is only ever applied while desktop mode is active, so a VR
    /// run keeps using the measured HMD eye height.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DesktopHeightProvider : MonoBehaviour
    {
        [SerializeField] private DesktopSimulationManager _manager;

        [Header("Existing calibration systems")]
        [SerializeField] private HeightCalibrationController
            _heightCalibration;
        [SerializeField] private AvatarCalibrationController
            _alignmentCalibration;

        [Header("Desktop height")]
        [Tooltip("Stand-in body height used only in desktop mode.")]
        [SerializeField, Range(1.2f, 2.3f)]
        private float _desktopHeight = 1.70f;
        [SerializeField] private bool _applyDesktopHeightOnPlay = true;
        [Tooltip(
            "When enabled the height is only applied after the X key, " +
            "matching the Quest flow more closely.")]
        [SerializeField] private bool _requireXKeyToConfirmHeight;

        [Header("Debug")]
        [SerializeField] private bool _logApply = true;

        private bool _applied;

        public float DesktopHeight => _desktopHeight;
        public bool HasApplied => _applied;

        private bool IsActive =>
            _manager != null && _manager.IsDesktopActive;

        private void Start()
        {
            if (!IsActive)
            {
                return;
            }

            if (_applyDesktopHeightOnPlay &&
                !_requireXKeyToConfirmHeight)
            {
                ConfirmHeight();
            }
        }

        /// <summary>
        /// Completes height and alignment calibration with the authored value.
        /// Called on Play and by the desktop X key.
        /// </summary>
        public void ConfirmHeight()
        {
            if (!IsActive)
            {
                return;
            }

            if (_heightCalibration == null &&
                _alignmentCalibration == null)
            {
                Debug.LogWarning(
                    "[DesktopSim] No calibration controller is assigned, so " +
                    "the desktop height cannot reach the existing readiness " +
                    "gate.",
                    this);
                return;
            }

            _heightCalibration?.CompleteDesktopCalibration(
                _desktopHeight);
            _alignmentCalibration?.CompleteDesktopCalibration();
            _applied = true;

            if (_logApply)
            {
                Debug.Log(
                    $"[DesktopSim] Applied desktop height " +
                    $"{_desktopHeight:F2} m through the existing " +
                    "calibration path.",
                    this);
            }
        }

        private void OnValidate()
        {
            _desktopHeight = Mathf.Clamp(
                _desktopHeight,
                1.2f,
                2.3f);
        }
    }
}
