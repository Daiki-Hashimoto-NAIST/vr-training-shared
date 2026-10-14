using UnityEngine;
using UnityEngine.InputSystem;

namespace IVRC.Desktop
{
    /// <summary>
    /// Drives the tracked avatar's skeleton from the authored PoseAssets so the
    /// existing detection chain runs without a headset:
    ///
    ///   number key -> PoseAsset -> avatar bones -> BodyPoseMetrics ->
    ///   BodyRepCounter -> TrainingRhythmScorer -> MuscleGrowthController
    ///
    /// This project has no controller-to-avatar IK; body tracking is the only
    /// thing that normally writes these bones. The manager disables the
    /// CharacterRetargeter in desktop mode, so exactly one writer is active.
    ///
    /// The selected pose is held until another key is pressed, which is what
    /// the rep detector needs to see a stable low/high angle.
    /// </summary>
    [DefaultExecutionOrder(20000)]
    [DisallowMultipleComponent]
    public sealed class DesktopPoseDriver : MonoBehaviour
    {
        public enum PoseTransition
        {
            Instant,
            Smooth
        }

        [SerializeField] private DesktopSimulationManager _manager;

        [Header("Skeleton root of the tracked avatar")]
        [Tooltip(
            "Transform the PoseAsset relative paths resolve against, e.g. the " +
            "avatar root that owns 'root/pelvis/...'.")]
        [SerializeField] private Transform _skeletonRoot;
        [Tooltip("Resolve the skeleton root from the active avatar instead.")]
        [SerializeField] private bool _resolveFromActiveAvatar = true;

        [Header("Pose presets (1-4)")]
        [SerializeField] private PoseAsset _armCurlDown;
        [SerializeField] private PoseAsset _armCurlUp;
        [SerializeField] private PoseAsset _sideRaiseDown;
        [SerializeField] private PoseAsset _sideRaiseUp;

        [Header("Switching")]
        [SerializeField] private PoseTransition _transition =
            PoseTransition.Instant;
        [SerializeField, Min(0f)]
        private float _transitionDuration = 0.15f;
        [Tooltip("Apply ArmCurl_Down on Play so the avatar starts settled.")]
        [SerializeField] private bool _applyRestPoseOnPlay = true;

        [Header("Debug")]
        [SerializeField] private bool _logSelection = true;

        private readonly GhostAvatarPoseApplier _applier =
            new GhostAvatarPoseApplier();
        private PoseAsset _selectedPose;
        private bool _usesExercisePreset;
        private BodyRepKind _selectedKind;
        private RepTimingPoint _selectedTimingPoint;
        private AvatarDefinition _selectedAvatar;
        private bool _cacheIsValid;
        private bool _warnedMissingRoot;

        /// <summary>Name of the pose currently held, for debug display.</summary>
        public string SelectedPoseName { get; private set; } = "NONE";

        private bool IsActive =>
            _manager != null && _manager.IsDesktopActive;

        private bool _restPoseApplied;

        private void Update()
        {
            if (!IsActive)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (Pressed(keyboard.digit1Key, keyboard.numpad1Key))
            {
                SelectExercisePose(
                    BodyRepKind.ElbowFlexion,
                    RepTimingPoint.Down,
                    _armCurlDown,
                    "ArmCurl_Down");
            }
            else if (Pressed(keyboard.digit2Key, keyboard.numpad2Key))
            {
                SelectExercisePose(
                    BodyRepKind.ElbowFlexion,
                    RepTimingPoint.Up,
                    _armCurlUp,
                    "ArmCurl_Up");
            }
            else if (Pressed(keyboard.digit3Key, keyboard.numpad3Key))
            {
                SelectExercisePose(
                    BodyRepKind.ArmRaise,
                    RepTimingPoint.Down,
                    _sideRaiseDown,
                    "SideRaise_Down");
            }
            else if (Pressed(keyboard.digit4Key, keyboard.numpad4Key))
            {
                SelectExercisePose(
                    BodyRepKind.ArmRaise,
                    RepTimingPoint.Up,
                    _sideRaiseUp,
                    "SideRaise_Up");
            }
        }

        /// <summary>
        /// Re-applies the held pose after the animator and any other writer has
        /// run for this frame, so the pose survives until the next key press.
        /// </summary>
        private void LateUpdate()
        {
            if (!IsActive)
            {
                return;
            }

            // The manager hands the skeleton over in its own LateUpdate, which
            // runs earlier, so the first pose is applied here rather than in
            // Start where the root is not resolved yet.
            if (!_restPoseApplied &&
                _applyRestPoseOnPlay &&
                _manager.ActiveSkeletonRoot != null)
            {
                _restPoseApplied = true;
                SelectExercisePose(
                    BodyRepKind.ElbowFlexion,
                    RepTimingPoint.Down,
                    _armCurlDown,
                    "ArmCurl_Down");
            }

            if (_usesExercisePreset &&
                ActiveAvatar != _selectedAvatar)
            {
                SelectExercisePose(
                    _selectedKind,
                    _selectedTimingPoint,
                    GetFallbackPose(
                        _selectedKind,
                        _selectedTimingPoint),
                    SelectedPoseName);
            }

            if (_selectedPose == null || !_cacheIsValid)
            {
                return;
            }

            _applier.Apply();
        }

        private static bool Pressed(
            UnityEngine.InputSystem.Controls.KeyControl primary,
            UnityEngine.InputSystem.Controls.KeyControl secondary)
        {
            return (primary != null && primary.wasPressedThisFrame) ||
                   (secondary != null && secondary.wasPressedThisFrame);
        }

        /// <summary>Selects a pose preset. Safe to call from a debug UI.</summary>
        public void Select(PoseAsset pose, string label)
        {
            _usesExercisePreset = false;
            ApplySelection(pose, label);
        }

        private void SelectExercisePose(
            BodyRepKind kind,
            RepTimingPoint timingPoint,
            PoseAsset fallbackPose,
            string label)
        {
            var avatar = ActiveAvatar;
            var pose = avatar == null
                ? null
                : avatar.GetExercisePose(kind, timingPoint);
            pose ??= fallbackPose;
            if (!ApplySelection(pose, label))
            {
                return;
            }

            _usesExercisePreset = true;
            _selectedKind = kind;
            _selectedTimingPoint = timingPoint;
            _selectedAvatar = avatar;
        }

        private bool ApplySelection(PoseAsset pose, string label)
        {
            if (!IsActive || pose == null)
            {
                if (pose == null)
                {
                    Debug.LogWarning(
                        $"[DesktopSim] Pose preset '{label}' is not " +
                        "assigned.",
                        this);
                }

                return false;
            }

            if (!EnsureSkeletonRoot())
            {
                return false;
            }

            _selectedPose = pose;
            SelectedPoseName = string.IsNullOrEmpty(pose.PoseName)
                ? label
                : pose.PoseName;

            // Rebuilds the path cache only when the pose actually changes.
            _applier.Apply(pose);
            _cacheIsValid = true;

            if (_applier.MissingPathCount > 0)
            {
                Debug.LogWarning(
                    $"[DesktopSim] {_applier.MissingPathCount} bone paths " +
                    $"in '{SelectedPoseName}' did not resolve against " +
                    $"'{_skeletonRoot.name}'.",
                    this);
            }

            if (_logSelection)
            {
                Debug.Log(
                    $"[DesktopSim] Pose = {SelectedPoseName}.",
                    this);
            }

            return true;
        }

        private AvatarDefinition ActiveAvatar =>
            _manager == null || _manager.AvatarRegistry == null
                ? null
                : _manager.AvatarRegistry.Active;

        private PoseAsset GetFallbackPose(
            BodyRepKind kind,
            RepTimingPoint timingPoint)
        {
            if (kind == BodyRepKind.ElbowFlexion)
            {
                return timingPoint == RepTimingPoint.Down
                    ? _armCurlDown
                    : _armCurlUp;
            }

            return timingPoint == RepTimingPoint.Down
                ? _sideRaiseDown
                : _sideRaiseUp;
        }

        private bool EnsureSkeletonRoot()
        {
            // PoseAsset paths start at the "root" bone, so they resolve against
            // the Armature rather than the avatar object itself. The manager
            // works that out from the active avatar.
            if (_resolveFromActiveAvatar &&
                _manager != null &&
                _manager.ActiveSkeletonRoot != null &&
                _skeletonRoot != _manager.ActiveSkeletonRoot)
            {
                _skeletonRoot = _manager.ActiveSkeletonRoot;
            }

            if (_skeletonRoot != null)
            {
                if (_applier.SkeletonRoot != _skeletonRoot)
                {
                    _applier.RebuildCache(
                        _skeletonRoot,
                        _applier.Pose);
                    _cacheIsValid = _applier.Pose != null;
                }

                return true;
            }

            if (!_warnedMissingRoot)
            {
                _warnedMissingRoot = true;
                Debug.LogWarning(
                    "[DesktopSim] No skeleton root is available, so poses " +
                    "cannot be applied. Assign Skeleton Root.",
                    this);
            }

            return false;
        }

        private void OnValidate()
        {
            _transitionDuration = Mathf.Max(0f, _transitionDuration);
        }
    }
}
