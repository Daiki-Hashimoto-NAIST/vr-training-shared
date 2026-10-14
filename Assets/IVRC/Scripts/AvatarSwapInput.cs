using UnityEngine;
using UnityEngine.InputSystem;

namespace IVRC
{
    /// <summary>
    /// Cycles registered avatars with left-controller Y. Swapping is limited
    /// to setup phases so a running attempt cannot change skeleton midway.
    /// </summary>
    public sealed class AvatarSwapInput : MonoBehaviour
    {
        [SerializeField] private AvatarRegistry _registry;
        [SerializeField] private GameFlowController _gameFlow;

        [Header("Input")]
        [SerializeField] private bool _enableControllerInput = true;
        [SerializeField] private OVRInput.RawButton _cycleButton =
            OVRInput.RawButton.Y;
        [SerializeField] private Key _editorKey = Key.Tab;

        public bool InputEnabled { get; private set; } = true;

        public void SetInputEnabled(bool enabled)
        {
            InputEnabled = enabled;
        }

        private void Update()
        {
            if (!CanSwap())
            {
                return;
            }

            var keyboard = Keyboard.current;
            var keyboardPressed =
                keyboard != null &&
                keyboard[_editorKey].wasPressedThisFrame;
            var controllerPressed =
                _enableControllerInput &&
                OVRInput.GetDown(_cycleButton);
            if (keyboardPressed || controllerPressed)
            {
                _registry.SwapNext();
            }
        }

        public void SwapNext()
        {
            if (CanSwap())
            {
                _registry.SwapNext();
            }
        }

        public void SwapToIndex(int index)
        {
            if (CanSwap())
            {
                _registry.SwapToIndex(index);
            }
        }

        public static bool AllowsSwapping(
            ExperiencePhase phase)
        {
            return
                phase == ExperiencePhase.Setup ||
                phase == ExperiencePhase.Ready;
        }

        private bool CanSwap()
        {
            return
                InputEnabled &&
                _registry != null &&
                (_gameFlow == null ||
                 AllowsSwapping(_gameFlow.CurrentPhase));
        }
    }
}
