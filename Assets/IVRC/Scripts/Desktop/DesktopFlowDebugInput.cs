using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

namespace IVRC.Desktop
{
    /// <summary>
    /// Keyboard shortcuts for walking the experience without sitting through
    /// it. Training alone runs over a minute, which is a long time to wait
    /// when the thing being checked is on the stage.
    ///
    /// The component type remains present in every build so the scene never
    /// acquires a Missing Script. Its input code only exists in the Editor and
    /// development builds.
    ///
    ///   N  end the current timed phase and move on
    ///   R  restart the whole experience
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class DesktopFlowDebugInput : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField] private GameFlowController _gameFlow;
        [Tooltip(
            "Off by default on the headset path, where a stray key press " +
            "during a real run would be worse than the time saved.")]
        [SerializeField] private DesktopSimulationManager _manager;
        [SerializeField] private bool _requireDesktopMode = true;
        [SerializeField] private bool _logPresses = true;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            // Desktop mode is only resolved by Start, and silence is the
            // worst failure here: a shortcut that quietly does nothing is
            // indistinguishable from a key that never arrived.
            var armed =
                _gameFlow != null &&
                (!_requireDesktopMode ||
                 (_manager != null && _manager.IsDesktopActive));
            if (armed)
            {
                Debug.Log(
                    "[FlowDebug] Ready. N ends the current phase, R " +
                    "restarts.",
                    this);
                return;
            }

            var reason = _gameFlow == null
                ? "no GameFlowController was found"
                : _manager == null
                    ? "no DesktopSimulationManager was found"
                    : "desktop mode did not resolve (mode is " +
                      $"{_manager.RequestedMode}); set Mode to Force " +
                      "Desktop, or clear Require Desktop Mode";
            Debug.LogWarning(
                $"[FlowDebug] Shortcuts are inactive: {reason}.",
                this);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || _gameFlow == null)
            {
                return;
            }

            if (_requireDesktopMode &&
                (_manager == null || !_manager.IsDesktopActive))
            {
                return;
            }

            if (keyboard.nKey.wasPressedThisFrame)
            {
                var from = _gameFlow.CurrentPhase;
                _gameFlow.DebugEndCurrentPhase();
                Log($"N: ended {from}.");
            }

            if (keyboard.rKey.wasPressedThisFrame)
            {
                _gameFlow.DebugRestartExperience();
                Log(
                    $"R: restarted the experience at " +
                    $"{_gameFlow.CurrentPhase}.");
            }
        }

        private void Log(string message)
        {
            if (_logPresses)
            {
                Debug.Log($"[FlowDebug] {message}", this);
            }
        }

        private void ResolveReferences()
        {
            _gameFlow ??=
                FindFirstObjectByType<GameFlowController>();
            _manager ??=
                FindFirstObjectByType<DesktopSimulationManager>(
                    FindObjectsInactive.Include);
        }
#endif
    }
}
