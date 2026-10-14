using UnityEngine;
using UnityEngine.InputSystem;

namespace IVRC.Desktop
{
    /// <summary>
    /// Routes keyboard presses into the same public entry points the Quest
    /// buttons call. It adds a second caller; it never replaces OVRInput, and
    /// it does nothing unless desktop mode is active.
    ///
    /// X -> height calibration, B -> play-space alignment, Y -> avatar swap,
    /// A -> start or retry the experience.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DesktopInputBridge : MonoBehaviour
    {
        [SerializeField] private DesktopSimulationManager _manager;

        [Header("Existing systems to drive")]
        [SerializeField] private DesktopHeightProvider _heightProvider;
        [SerializeField] private AvatarCalibrationController
            _alignmentCalibration;
        [SerializeField] private AvatarSwapInput _avatarSwap;
        [SerializeField] private GameFlowController _gameFlow;

        [Header("Debug")]
        [SerializeField] private bool _logPresses = true;

        private bool _warnedMissingKeyboard;

        private bool IsActive =>
            _manager != null && _manager.IsDesktopActive;

        private void Update()
        {
            if (!IsActive)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                if (!_warnedMissingKeyboard)
                {
                    _warnedMissingKeyboard = true;
                    Debug.LogWarning(
                        "[DesktopSim] No keyboard device was found, so " +
                        "X/Y/A/B simulation is unavailable.",
                        this);
                }

                return;
            }

            if (keyboard.xKey.wasPressedThisFrame)
            {
                Press("X (height calibration)");
                if (_heightProvider != null)
                {
                    _heightProvider.ConfirmHeight();
                }
            }

            if (keyboard.bKey.wasPressedThisFrame)
            {
                Press("B (play-space alignment)");
                _alignmentCalibration?.CalibrateNow();
            }

            if (keyboard.yKey.wasPressedThisFrame)
            {
                Press("Y (avatar swap)");
                _avatarSwap?.SwapNext();
            }

            if (keyboard.aKey.wasPressedThisFrame)
            {
                Press("A (start/retry)");
                _gameFlow?.StartOrRestartExperience();
            }
        }

        private void Press(string label)
        {
            if (_logPresses)
            {
                Debug.Log($"[DesktopSim] Key {label}.", this);
            }
        }
    }
}
