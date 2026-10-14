using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace IVRC
{
    public enum GhostGuideOffsetSpace
    {
        Local,
        World
    }

    /// <summary>
    /// Immutable visual values calculated once by the rhythm guide controller
    /// and distributed to every active ghost in the same LateUpdate.
    /// </summary>
    public readonly struct RhythmPoseGuideVisualState
    {
        public RhythmPoseGuideVisualState(
            PoseAsset pose,
            float scaleMultiplier,
            float beatProgress,
            bool isVisible,
            bool isBeatFlash)
            : this(
                pose,
                scaleMultiplier,
                beatProgress,
                isVisible,
                isBeatFlash,
                0f)
        {
        }

        public RhythmPoseGuideVisualState(
            PoseAsset pose,
            float scaleMultiplier,
            float beatProgress,
            bool isVisible,
            bool isBeatFlash,
            float contourOffset)
        {
            Pose = pose;
            ScaleMultiplier = Mathf.Max(
                0f,
                scaleMultiplier);
            BeatProgress = Mathf.Clamp01(beatProgress);
            IsVisible = isVisible;
            IsBeatFlash = isBeatFlash;
            ContourOffset = Mathf.Max(0f, contourOffset);
        }

        public PoseAsset Pose { get; }
        public float ScaleMultiplier { get; }
        public float BeatProgress { get; }
        public bool IsVisible { get; }
        public bool IsBeatFlash { get; }

        /// <summary>
        /// Line art only. How far outside the goal outline the approaching
        /// contour currently sits, in metres, where 0 means the two have met.
        /// </summary>
        public float ContourOffset { get; }
    }

    /// <summary>
    /// Per-destination appearance. It is a value type so selecting the common
    /// or separate Player/Mirror values does not allocate every frame.
    /// </summary>
    public readonly struct GhostGuideVisualStyle
    {
        public GhostGuideVisualStyle(
            Color baseColor,
            float normalAlpha,
            float beatAlpha,
            float beatIntensity)
            : this(
                baseColor,
                normalAlpha,
                beatAlpha,
                1f,
                beatIntensity)
        {
        }

        public GhostGuideVisualStyle(
            Color baseColor,
            float normalAlpha,
            float beatAlpha,
            float normalIntensity,
            float beatIntensity)
        {
            BaseColor = baseColor;
            NormalAlpha = Mathf.Clamp01(normalAlpha);
            BeatAlpha = Mathf.Clamp01(beatAlpha);
            NormalIntensity = Mathf.Max(
                0f,
                normalIntensity);
            BeatIntensity = Mathf.Max(0f, beatIntensity);
        }

        public Color BaseColor { get; }
        public float NormalAlpha { get; }
        public float BeatAlpha { get; }
        public float NormalIntensity { get; }
        public float BeatIntensity { get; }

        public Color GetColor(bool beatFlash)
        {
            var color = BaseColor;
            color.a = beatFlash
                ? BeatAlpha
                : NormalAlpha;
            return color;
        }

        public float GetIntensity(bool beatFlash)
        {
            return beatFlash
                ? BeatIntensity
                : NormalIntensity;
        }
    }

    /// <summary>
    /// One persistent visual destination for a rhythm pose. The target avatar
    /// root,
    /// renderer list, pose paths, signed base scale, and property blocks are
    /// cached. No hierarchy search or material creation occurs per frame.
    /// </summary>
    [Serializable]
    public sealed class GhostGuideInstance
    {
        [SerializeField] private string _label = "Ghost Guide";

        [Header("Scene References")]
        [SerializeField] private GameObject _guideRoot;
        [SerializeField] private GameObject _visualRoot;
        [SerializeField] private Transform _skeletonRoot;
        [FormerlySerializedAs("_targetHips")]
        [SerializeField] private Transform _targetRoot;
        [Tooltip(
            "Optional runtime motion root. Movement SDK writes tracked world " +
            "position and rotation to Armature/root while the avatar GameObject " +
            "root remains stationary. The initial yaw difference from Target " +
            "Root is removed so authored skeleton rotation is not applied twice.")]
        [SerializeField] private Transform _motionRoot;
        [SerializeField] private Transform _scaleSource;

        [Header("Follow")]
        [SerializeField] private bool _followPosition = true;
        [SerializeField] private bool _followYaw = true;
        [SerializeField] private GhostGuideOffsetSpace _offsetSpace =
            GhostGuideOffsetSpace.Local;
        [SerializeField] private Vector3 _positionOffset;
        [SerializeField] private float _yawOffset;

        [Header("Base Scale")]
        [SerializeField] private bool _autoCaptureBaseScale = true;
        [SerializeField] private Vector3 _baseScale = Vector3.one;

        [NonSerialized] private GhostAvatarMaterialController
            _materialController;
        [NonSerialized] private GhostAvatarPoseApplier
            _poseApplier;
        [NonSerialized] private UnityEngine.Object _logContext;
        [NonSerialized] private PoseAsset _lastPose;
        [NonSerialized] private Vector3 _cachedBaseScale =
            Vector3.one;
        [NonSerialized] private Quaternion _lastYaw =
            Quaternion.identity;
        [NonSerialized] private Quaternion _motionYawOffset =
            Quaternion.identity;
        [NonSerialized] private bool _hasCachedBaseScale;
        [NonSerialized] private bool _hasLastYaw;
        [NonSerialized] private bool _hasMotionYawOffset;
        [NonSerialized] private bool _isInitialized;
        [NonSerialized] private bool _warnedMissingTarget;

        public string Label =>
            string.IsNullOrWhiteSpace(_label)
                ? "Ghost Guide"
                : _label;
        public GameObject GuideRoot => _guideRoot;
        public GameObject VisualRoot => _visualRoot;
        public Transform SkeletonRoot => _skeletonRoot;
        public Transform TargetRoot => _targetRoot;
        public Transform MotionRoot => _motionRoot;
        [Obsolete("Use TargetRoot.")]
        public Transform TargetHips => _targetRoot;
        public Transform ScaleSource => _scaleSource;
        public Vector3 PositionOffset => _positionOffset;
        public float YawOffset => _yawOffset;
        public Vector3 BaseScale =>
            _hasCachedBaseScale
                ? _cachedBaseScale
                : _baseScale;
        public bool FollowPosition => _followPosition;
        public bool FollowYaw => _followYaw;
        public GhostGuideOffsetSpace OffsetSpace =>
            _offsetSpace;
        public GhostAvatarMaterialController MaterialController =>
            _materialController;
        public GhostAvatarPoseApplier PoseApplier => _poseApplier;
        public bool IsConfigured =>
            _guideRoot != null &&
            _visualRoot != null &&
            _skeletonRoot != null;
        public bool HasFollowRoot => _targetRoot != null;
        [Obsolete("Use HasFollowRoot.")]
        public bool HasFollowTarget => HasFollowRoot;
        public bool IsUsable => IsConfigured && HasFollowRoot;

        public void SetRuntimeReferences(
            string label,
            GameObject guideRoot,
            GameObject visualRoot,
            Transform skeletonRoot,
            Transform targetRoot,
            Transform scaleSource,
            Transform motionRoot = null)
        {
            _label = label;
            _guideRoot = guideRoot;
            _visualRoot = visualRoot;
            _skeletonRoot = skeletonRoot;
            _targetRoot = targetRoot;
            _scaleSource = scaleSource;
            _motionRoot = motionRoot;
            ResetRuntimeCache();
        }

        public void ConfigureFollow(
            Vector3 positionOffset,
            float yawOffset,
            bool followPosition = true,
            bool followYaw = true,
            GhostGuideOffsetSpace offsetSpace =
                GhostGuideOffsetSpace.Local)
        {
            _positionOffset = positionOffset;
            _yawOffset = yawOffset;
            _followPosition = followPosition;
            _followYaw = followYaw;
            _offsetSpace = offsetSpace;
        }

        public void SetFollowRoot(
            Transform targetRoot,
            Transform scaleSource,
            Transform motionRoot = null)
        {
            _targetRoot = targetRoot;
            _scaleSource = scaleSource;
            _motionRoot = motionRoot;
            _warnedMissingTarget = false;
            _hasCachedBaseScale = false;
            _hasLastYaw = false;
            _hasMotionYawOffset = false;
        }

        [Obsolete("Use SetFollowRoot.")]
        public void SetFollowTarget(
            Transform targetHips,
            Transform scaleSource)
        {
            SetFollowRoot(
                targetHips,
                scaleSource);
        }

        public void ConfigureBaseScale(
            Vector3 baseScale,
            bool autoCapture)
        {
            _baseScale = baseScale;
            _autoCaptureBaseScale = autoCapture;
            _hasCachedBaseScale = false;
        }

        public void Initialize(
            Material ghostMaterial,
            UnityEngine.Object logContext)
        {
            if (_isInitialized)
            {
                return;
            }

            _logContext = logContext;
            if (!IsConfigured)
            {
                return;
            }

            _guideRoot.SetActive(true);
            _visualRoot.SetActive(true);
            foreach (var behaviour in
                     _visualRoot.GetComponentsInChildren<
                         Behaviour>(true))
            {
                // The ghost is posed from the outside, so everything that
                // could animate it is switched off. The dumbbell grip is
                // presentation rather than animation and has to keep running,
                // otherwise the guide is the one body holding nothing.
                if (behaviour is DumbbellGripVisualController)
                {
                    continue;
                }

                behaviour.enabled = false;
            }

            _materialController =
                new GhostAvatarMaterialController();
            _materialController.Initialize(
                _visualRoot,
                ghostMaterial);
            _materialController.SetVisible(false);

            _poseApplier = new GhostAvatarPoseApplier
            {
                // The same pose rotations are shared between body variants,
                // while each FBX keeps its own authored bone lengths.
                ApplyLocalPositions = false
            };
            _poseApplier.SetSkeletonRoot(_skeletonRoot);
            RefreshBaseScale();
            CaptureMotionYawOffset();
            _isInitialized = true;
        }

        public void RefreshBaseScale()
        {
            if (_autoCaptureBaseScale)
            {
                var source = _scaleSource != null
                    ? _scaleSource
                    : _targetRoot;
                if (source != null &&
                    IsFinite(source.lossyScale))
                {
                    _cachedBaseScale = source.lossyScale;
                    _hasCachedBaseScale = true;
                    return;
                }
            }

            _cachedBaseScale = IsFinite(_baseScale)
                ? _baseScale
                : Vector3.one;
            _hasCachedBaseScale = true;
        }

        public int RebuildMuscleState(
            BlendShapeMuscleGrowthOutput source,
            SkinnedMeshRenderer authoritativeRenderer,
            MuscleBlendShapeStateSynchronizer synchronizer)
        {
            if (!_isInitialized ||
                _materialController == null ||
                source == null ||
                authoritativeRenderer == null ||
                synchronizer == null)
            {
                return 0;
            }

            // A rhythm ghost is a presentation copy of the tracked body.
            // Copy every same-named shape, not only the channels driven by
            // earned growth.  The female FBX, for example, has a fixed
            // "Muscle" weight that defines its base body and is deliberately
            // not a growth binding.
            synchronizer.RebuildAllSharedBlendShapes(
                authoritativeRenderer,
                _materialController.Renderers);
            return synchronizer.Apply();
        }

        public int ApplyPose(PoseAsset pose)
        {
            if (!_isInitialized ||
                _poseApplier == null ||
                pose == null ||
                _lastPose == pose)
            {
                return 0;
            }

            _lastPose = pose;
            return _poseApplier.Apply(pose);
        }

        public bool ApplyState(
            RhythmPoseGuideVisualState state,
            GhostGuideVisualStyle style)
        {
            if (!_isInitialized || !IsConfigured)
            {
                return false;
            }

            if (_targetRoot == null)
            {
                WarnMissingTargetOnce();
                SetVisible(false);
                return false;
            }

            if (!_targetRoot.gameObject.activeInHierarchy)
            {
                SetVisible(false);
                return false;
            }

            ApplyPose(state.Pose);
            FollowRoot(state.ScaleMultiplier);
            if (!state.IsVisible)
            {
                SetVisible(false);
                return true;
            }

            if (_materialController != null)
            {
                _materialController.SetVisual(
                    style.GetColor(state.IsBeatFlash),
                    style.GetIntensity(state.IsBeatFlash),
                    state.ContourOffset);
                _materialController.SetVisible(true);
            }

            return true;
        }

        public void SetVisible(bool visible)
        {
            _materialController?.SetVisible(visible);
        }

        private void FollowRoot(float scaleMultiplier)
        {
            var yaw = ResolveYaw();
            if (_followPosition)
            {
                var offset =
                    _offsetSpace ==
                    GhostGuideOffsetSpace.Local
                        ? yaw * _positionOffset
                        : _positionOffset;
                var positionSource =
                    _motionRoot != null
                        ? _motionRoot
                        : _targetRoot;
                _guideRoot.transform.position =
                    positionSource.position + offset;
            }

            if (_followYaw)
            {
                _guideRoot.transform.rotation =
                    yaw *
                    Quaternion.AngleAxis(
                        _yawOffset,
                        Vector3.up);
            }

            if (!_hasCachedBaseScale)
            {
                RefreshBaseScale();
            }

            var desiredWorldScale =
                RhythmPoseGuideMath.ApplySignedMultiplier(
                    _cachedBaseScale,
                    scaleMultiplier);
            _guideRoot.transform.localScale =
                ConvertWorldToLocalScale(
                    desiredWorldScale,
                    _guideRoot.transform.parent);
        }

        private Quaternion ResolveYaw()
        {
            var yawSource =
                _motionRoot != null
                    ? _motionRoot
                    : _targetRoot;
            var forward = yawSource.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.000001f &&
                _scaleSource != null)
            {
                forward = _scaleSource.forward;
                forward.y = 0f;
            }

            if (forward.sqrMagnitude > 0.000001f)
            {
                var sourceYaw = Quaternion.LookRotation(
                    forward.normalized,
                    Vector3.up);
                if (_motionRoot != null)
                {
                    if (!_hasMotionYawOffset)
                    {
                        CaptureMotionYawOffset();
                    }

                    sourceYaw *= _motionYawOffset;
                }

                _lastYaw = sourceYaw;
                _hasLastYaw = true;
            }

            return _hasLastYaw
                ? _lastYaw
                : Quaternion.identity;
        }

        private void CaptureMotionYawOffset()
        {
            _hasMotionYawOffset = false;
            _motionYawOffset = Quaternion.identity;
            if (_motionRoot == null ||
                _targetRoot == null ||
                !TryGetHorizontalYaw(
                    _motionRoot,
                    out var motionYaw) ||
                !TryGetHorizontalYaw(
                    _targetRoot,
                    out var targetYaw))
            {
                return;
            }

            _motionYawOffset =
                Quaternion.Inverse(motionYaw) *
                targetYaw;
            _hasMotionYawOffset = true;
        }

        private static bool TryGetHorizontalYaw(
            Transform source,
            out Quaternion yaw)
        {
            yaw = Quaternion.identity;
            if (source == null)
            {
                return false;
            }

            var forward = source.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            yaw = Quaternion.LookRotation(
                forward.normalized,
                Vector3.up);
            return true;
        }

        private void WarnMissingTargetOnce()
        {
            if (_warnedMissingTarget)
            {
                return;
            }

            _warnedMissingTarget = true;
            Debug.LogWarning(
                $"[RhythmPoseGuide] {Label} has no avatar Root " +
                "follow target. Only the other configured guide " +
                "will be displayed.",
                _logContext);
        }

        private void ResetRuntimeCache()
        {
            _materialController = null;
            _poseApplier = null;
            _logContext = null;
            _lastPose = null;
            _cachedBaseScale = Vector3.one;
            _lastYaw = Quaternion.identity;
            _motionYawOffset = Quaternion.identity;
            _hasCachedBaseScale = false;
            _hasLastYaw = false;
            _hasMotionYawOffset = false;
            _isInitialized = false;
            _warnedMissingTarget = false;
        }

        private static Vector3 ConvertWorldToLocalScale(
            Vector3 desiredWorldScale,
            Transform parent)
        {
            if (parent == null)
            {
                return desiredWorldScale;
            }

            var parentScale = parent.lossyScale;
            return new Vector3(
                SafeDivide(
                    desiredWorldScale.x,
                    parentScale.x),
                SafeDivide(
                    desiredWorldScale.y,
                    parentScale.y),
                SafeDivide(
                    desiredWorldScale.z,
                    parentScale.z));
        }

        private static float SafeDivide(
            float value,
            float divisor)
        {
            return Mathf.Abs(divisor) <= 0.000001f
                ? value
                : value / divisor;
        }

        private static bool IsFinite(Vector3 value)
        {
            return
                IsFinite(value.x) &&
                IsFinite(value.y) &&
                IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }
    }
}
