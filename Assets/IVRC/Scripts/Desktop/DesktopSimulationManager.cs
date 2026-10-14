using System.Collections;
using UnityEngine;

namespace IVRC.Desktop
{
    /// <summary>
    /// Single owner of the desktop-simulation decision. Resolves the mode once
    /// at startup, then enables the desktop-only components or leaves the whole
    /// subsystem inert so the shipping VR path is untouched.
    ///
    /// Nothing here writes to VR transforms. In desktop mode the tracked
    /// avatar's CharacterRetargeter is disabled instead, so body-tracking and
    /// the desktop pose driver can never write the same bones in one frame.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class DesktopSimulationManager : MonoBehaviour
    {
        private const float AutoResolveTimeoutSeconds = 5f;
        private const float XrResolutionGraceSeconds = 1f;

        [Header("Mode")]
        [SerializeField] private DesktopSimulationMode _mode =
            DesktopSimulationMode.Auto;
        [SerializeField] private XrStartupController _xrStartup;

        [Header("Existing systems (read-only use)")]
        [SerializeField] private AvatarRegistry _avatarRegistry;
        [SerializeField] private BodyPoseMetrics _bodyPoseMetrics;
        [SerializeField] private Camera _vrCamera;

        [Header("Desktop-only children")]
        [Tooltip(
            "Root holding the desktop camera and pose driver. Kept inactive " +
            "in VR mode.")]
        [SerializeField] private GameObject _desktopRoot;

        [Header("Debug")]
        [SerializeField] private bool _logDecision = true;

        private bool _resolved;
        private bool _pendingEnforce;
        private bool _askedRigToReleaseCameras;

        /// <summary>
        /// Skeleton root the desktop pose driver should write to, resolved from
        /// the active avatar. Null until the registry has an active avatar.
        /// </summary>
        public Transform ActiveSkeletonRoot { get; private set; }

        /// <summary>True when desktop simulation drives this run.</summary>
        public bool IsDesktopActive { get; private set; }

        /// <summary>Read-only view of the resolved mode for the Inspector.</summary>
        public string ActiveModeLabel { get; private set; } = "UNRESOLVED";

        /// <summary>What the scene asks for. Always Auto for the exhibition.</summary>
        public DesktopSimulationMode RequestedMode => _mode;

        /// <summary>
        /// What this run actually uses. In the Editor a per-machine override
        /// can outrank the scene, so development can force desktop without
        /// touching a value the exhibition depends on. In a build there is no
        /// override and this is the scene's own setting.
        /// </summary>
        public DesktopSimulationMode EffectiveMode =>
            DesktopModeOverride.Resolve(_mode, out _);
        public AvatarRegistry AvatarRegistry => _avatarRegistry;

        private void Awake()
        {
            _xrStartup ??= GetComponent<XrStartupController>();

            // Force modes are deterministic and can be resolved immediately.
            // Auto waits until the XR loader has had a chance to expose the HMD
            // before choosing a camera/input path.
            if (EffectiveMode != DesktopSimulationMode.Auto)
            {
                Resolve();
            }
        }

        private void Resolve()
        {
            if (_resolved)
            {
                return;
            }

            _resolved = true;
            var mode = DesktopModeOverride.Resolve(
                _mode,
                out var overridden);
            string reason;
            if (mode == DesktopSimulationMode.Auto &&
                _xrStartup != null &&
                _xrStartup.HasResolved)
            {
                IsDesktopActive =
                    _xrStartup.Resolution ==
                    XrStartupResolution.Desktop;
                reason =
                    $"XR startup resolved to " +
                    $"{_xrStartup.Resolution}: " +
                    _xrStartup.StatusReason;
            }
            else
            {
                IsDesktopActive =
                    SimulationModeResolver.ShouldUseDesktop(
                        mode,
                        out reason);
            }
            ActiveModeLabel = IsDesktopActive
                ? "DESKTOP"
                : "VR";

            if (overridden)
            {
                // A warning, not a log. The Inspector shows the scene's mode
                // while something else runs, and this has twice been mistaken
                // for the build ignoring a setting. It survives crashes and
                // reinstates itself if the Editor is killed before its prefs
                // are flushed, so it has to announce itself in a colour that
                // gets noticed.
                Debug.LogWarning(
                    $"[DesktopSim] {mode} is forced for this machine and " +
                    $"overrides the scene's {_mode}. Clear it from " +
                    "IVRC / Desktop Mode / Follow Scene. " + reason,
                    this);
            }
            else if (mode == DesktopSimulationMode.ForceVR &&
                     !SimulationModeResolver.IsVrRuntimeActive())
            {
                Debug.LogWarning(
                    "[DesktopSim] " + reason,
                    this);
            }
            else if (_logDecision)
            {
                Debug.Log(
                    $"[DesktopSim] Mode={ActiveModeLabel}. {reason}",
                    this);
            }

            if (_desktopRoot != null)
            {
                _desktopRoot.SetActive(IsDesktopActive);
            }

            if (!IsDesktopActive)
            {
                // VR run: leave every existing reference exactly as authored.
                return;
            }

            if (_avatarRegistry != null)
            {
                _avatarRegistry.OnAvatarChanged += HandleAvatarChanged;
            }
        }

        private void OnDestroy()
        {
            if (_avatarRegistry != null)
            {
                _avatarRegistry.OnAvatarChanged -= HandleAvatarChanged;
            }
        }

        private IEnumerator Start()
        {
            if (!_resolved)
            {
                if (_xrStartup != null)
                {
                    var startedAt = Time.realtimeSinceStartup;
                    var timeout =
                        _xrStartup.InitializationTimeoutSeconds +
                        XrResolutionGraceSeconds;
                    while (!_xrStartup.HasResolved &&
                        Time.realtimeSinceStartup - startedAt < timeout)
                    {
                        yield return null;
                    }

                    if (!_xrStartup.HasResolved)
                    {
                        _xrStartup
                            .CancelPendingStartupForDesktopFallback(
                                "XR startup did not report a result; " +
                                "using desktop mode.");
                    }
                }
                else
                {
                    var startedAt = Time.realtimeSinceStartup;
                    while (!SimulationModeResolver.IsVrRuntimeActive() &&
                        Time.realtimeSinceStartup - startedAt <
                        AutoResolveTimeoutSeconds)
                    {
                        yield return null;
                    }
                }

                Resolve();
            }

            // AvatarRegistry resolves its active avatar in its own Awake, which
            // runs after this component, so the hand-over waits until Start.
            _pendingEnforce = IsDesktopActive;
        }

        private void HandleAvatarChanged(AvatarDefinition avatar)
        {
            _pendingEnforce = true;
        }

        private void LateUpdate()
        {
            if (!IsDesktopActive)
            {
                return;
            }

            // AvatarSwapCoordinator re-binds the metrics to the tracked
            // retargeter whenever the registry raises its change event, so the
            // desktop hand-over is re-applied after that ran this frame.
            if (_pendingEnforce)
            {
                _pendingEnforce = false;
                ConfigureForDesktop();
            }

            KeepVrCameraDisabled();
        }

        /// <summary>
        /// Hands the avatar over to the desktop pose driver and makes the
        /// existing metrics accept poses that no longer come from body
        /// tracking. Only runs when desktop mode is active.
        /// </summary>
        private void ConfigureForDesktop()
        {
            var avatar = _avatarRegistry == null
                ? null
                : _avatarRegistry.Active;
            if (avatar == null)
            {
                Debug.LogWarning(
                    "[DesktopSim] No active avatar was available, so the " +
                    "desktop pose driver has nothing to drive. Assign " +
                    "Avatar Registry.",
                    this);
            }
            else
            {
                // Stop body-tracking retargeting from fighting our bone writes.
                // Left enabled it collapses the skeleton to the origin when no
                // headset is reporting poses, which hides the whole avatar.
                if (avatar.Retargeter != null)
                {
                    avatar.Retargeter.enabled = false;
                }

                // Re-bind through the existing public API with no retargeter,
                // so IsTrackingValid depends only on the pose we apply.
                if (_bodyPoseMetrics != null)
                {
                    _bodyPoseMetrics.BindAvatar(
                        avatar.Animator,
                        null);
                }

                // CharacterRetargeter drives the avatar's root scale from the
                // tracked body size. With no headset it resolves to zero and
                // collapses the whole avatar to a point, so a degenerate scale
                // left behind by that pass is restored here.
                RestoreDegenerateScale(avatar.transform);

                ActiveSkeletonRoot =
                    ResolveSkeletonRoot(avatar.transform);
            }

            KeepVrCameraDisabled();
        }

        /// <summary>
        /// OVRCameraRig re-enables its eye cameras every frame, so this is
        /// re-applied rather than set once. Desktop mode only.
        /// </summary>
        private void KeepVrCameraDisabled()
        {
            if (_vrCamera == null)
            {
                return;
            }

            // OVRCameraRig owns the eye cameras and re-enables them in its own
            // LateUpdate, so ask the rig to stop driving them instead of
            // fighting it every frame.
            if (!_askedRigToReleaseCameras)
            {
                var rig =
                    _vrCamera.GetComponentInParent<OVRCameraRig>();
                if (rig != null)
                {
                    rig.disableEyeAnchorCameras = true;
                    _askedRigToReleaseCameras = true;
                }
            }

            if (_vrCamera.enabled)
            {
                _vrCamera.enabled = false;
            }

            var listener = _vrCamera.GetComponent<AudioListener>();
            if (listener != null && listener.enabled)
            {
                listener.enabled = false;
            }
        }

        /// <summary>
        /// Resets a collapsed root scale back to one. Only a near-zero scale is
        /// touched, so an intentionally scaled avatar is left alone.
        /// </summary>
        private static void RestoreDegenerateScale(Transform avatarRoot)
        {
            if (avatarRoot == null)
            {
                return;
            }

            var scale = avatarRoot.localScale;
            const float minimumScale = 0.001f;
            if (Mathf.Abs(scale.x) >= minimumScale &&
                Mathf.Abs(scale.y) >= minimumScale &&
                Mathf.Abs(scale.z) >= minimumScale)
            {
                return;
            }

            avatarRoot.localScale = Vector3.one;
        }

        /// <summary>
        /// Finds the transform the PoseAsset relative paths are authored
        /// against: the parent that owns the skeleton's "root" bone, which is
        /// the Armature on these avatars.
        /// </summary>
        private static Transform ResolveSkeletonRoot(Transform avatarRoot)
        {
            if (avatarRoot == null)
            {
                return null;
            }

            if (avatarRoot.Find("root") != null)
            {
                return avatarRoot;
            }

            foreach (Transform child in avatarRoot)
            {
                if (child.Find("root") != null)
                {
                    return child;
                }
            }

            return avatarRoot;
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                return;
            }

            // Shows the effective mode, so the Inspector cannot claim Auto
            // while this machine is set to force something else.
            var mode = DesktopModeOverride.Resolve(
                _mode,
                out var overridden);
            var suffix = overridden
                ? " (this machine)"
                : " (forced)";
            ActiveModeLabel = mode == DesktopSimulationMode.ForceDesktop
                ? "DESKTOP" + suffix
                : mode == DesktopSimulationMode.ForceVR
                    ? "VR" + suffix
                    : "AUTO (resolved on Play)";
        }
    }
}
