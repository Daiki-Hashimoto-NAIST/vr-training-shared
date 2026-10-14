using System;
using Meta.XR.Movement.Retargeting;
using UnityEngine;
using UnityEngine.Rendering;

namespace IVRC
{
    /// <summary>
    /// Deterministic result of an analytic two-bone IK solve.
    /// </summary>
    public readonly struct PoseGhostIkSolution
    {
        public PoseGhostIkSolution(
            Vector3 shoulder,
            Vector3 elbow,
            Vector3 hand,
            float targetDistance,
            float solvedDistance,
            bool wasClamped)
        {
            Shoulder = shoulder;
            Elbow = elbow;
            Hand = hand;
            TargetDistance = targetDistance;
            SolvedDistance = solvedDistance;
            WasClamped = wasClamped;
        }

        public Vector3 Shoulder { get; }
        public Vector3 Elbow { get; }
        public Vector3 Hand { get; }
        public float TargetDistance { get; }
        public float SolvedDistance { get; }
        public bool WasClamped { get; }
    }

    /// <summary>
    /// Creates a render-only Humanoid guide for the posing phase. The live
    /// avatar, TrackingSpace, HMD, controller samples, and posing scorer are
    /// never modified.
    /// </summary>
    [DefaultExecutionOrder(10450)]
    public sealed class PoseGhostGuideController : MonoBehaviour
    {
        private const string DefaultPoseGuideLayer = "PoseGuide";
        private const float MinimumSegmentLength = 0.0001f;
        private const float MinimumDirectionMagnitudeSquared = 0.000001f;

        [Header("Flow")]
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private AvatarRegistry _avatarRegistry;
        [Tooltip(
            "The guide follows the pose the scorer is currently asking " +
            "for. Read live rather than cached, so it keeps up as the " +
            "routine advances.")]
        [SerializeField] private ThreePointPosingScorer _posingScorer;
        [Tooltip(
            "Fallback for scenes with no scorer. Ignored whenever one is " +
            "available.")]
        [SerializeField] private PosingScoreProfile _profile;

        [Header("Tracking Frame")]
        [SerializeField] private OVRCameraRig _cameraRig;
        [SerializeField] private HeightCalibrationController
            _heightCalibration;
        [SerializeField] private Transform _stageFrame;

        [Header("Presentation")]
        [SerializeField] private Transform _ghostParent;
        [SerializeField] private Material _ghostMaterial;
        [SerializeField] private string _poseGuideLayerName =
            DefaultPoseGuideLayer;

        [Header("Display Board")]
        [Tooltip(
            "When set, the guide stands here at a fixed size instead of " +
            "being overlaid on the player. Point it at somewhere the " +
            "jumbotron camera can see to get a small posed reference " +
            "beside the live view.")]
        [SerializeField] private Transform _displayAnchor;
        [Tooltip(
            "Size of the guide on the display board, as a fraction of the " +
            "source avatar.")]
        [SerializeField, Min(0.01f)]
        private float _displayUniformScale = 0.25f;

        [Header("Adaptive Guidance")]
        [SerializeField, Range(1f, 1.5f)]
        private float _adaptiveHintScaleMultiplier = 1.12f;
        [SerializeField, Range(1f, 3f)]
        private float _adaptiveHintAlphaMultiplier = 1.8f;

        [Header("Scale")]
        [SerializeField, Min(0.01f)]
        private float _minimumUniformScale = 0.5f;
        [SerializeField, Min(0.01f)]
        private float _maximumUniformScale = 2f;

        [Header("Elbow-out IK")]
        [SerializeField, Min(0f)]
        private float _elbowPoleLateralNormalized = 0.45f;
        [SerializeField]
        private float _elbowPoleForwardNormalized = -0.05f;
        [SerializeField]
        private float _elbowPoleUpNormalized;

        private GameObject _runtimeGhost;
        private Transform _head;
        private ArmBinding _leftArm;
        private ArmBinding _rightArm;
        private Quaternion _headBaseLocalRotation;
        private Quaternion _headBaseRotationInGhostSpace;
        private Vector3 _baseLocalScale;
        private float _referenceHeightMeters;
        private bool _creationAttempted;
        private bool _adaptiveHintActive;
        private Renderer[] _ghostRenderers =
            Array.Empty<Renderer>();
        private MaterialPropertyBlock
            _adaptiveHintPropertyBlock;

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");

        private readonly MuscleBlendShapeStateSynchronizer
            _muscleState =
                new MuscleBlendShapeStateSynchronizer();

        /// <summary>
        /// True while the guide stands on a board rather than on the player.
        /// The tracked headset is then irrelevant to it.
        /// </summary>
        public bool UsesDisplayAnchor => _displayAnchor != null;

        /// <summary>
        /// The pose being asked for right now. Caching this was what froze
        /// the guide on the first pose of a routine while the flat guide
        /// kept switching.
        /// </summary>
        public PosingScoreProfile ActiveProfile =>
            _posingScorer != null
                ? _posingScorer.CurrentProfile
                : _profile;

        public GameObject RuntimeGhost => _runtimeGhost;
        public bool IsGuideVisible =>
            _runtimeGhost != null &&
            _runtimeGhost.activeInHierarchy;
        public bool IsAdaptiveHintActive => _adaptiveHintActive;

        public void SetAdaptiveHintActive(bool active)
        {
            if (_adaptiveHintActive == active)
            {
                return;
            }

            _adaptiveHintActive = active;
            ApplyAdaptiveHintPresentation();
        }

        private void Awake()
        {
            _adaptiveHintPropertyBlock =
                new MaterialPropertyBlock();
            ResolveReferences();
            ValidateSettings();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ResolveReferences();
            Subscribe();
            RefreshForCurrentPhase();
        }

        private void Start()
        {
            if (Application.isPlaying)
            {
                RefreshForCurrentPhase();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            _adaptiveHintActive = false;
            DestroyRuntimeGhost();
            _creationAttempted = false;
        }

        private void OnValidate()
        {
            ValidateSettings();
        }

        private void LateUpdate()
        {
            if (_gameFlow == null ||
                _gameFlow.CurrentPhase != ExperiencePhase.Posing)
            {
                if (_runtimeGhost != null)
                {
                    DestroyRuntimeGhost();
                }

                return;
            }

            if (_runtimeGhost == null)
            {
                if (!_creationAttempted)
                {
                    TryCreateRuntimeGhost();
                }

                return;
            }

            UpdateGhostPose();
            SynchronizeMuscleBlendShape();
        }

        private void HandlePhaseChanged(ExperiencePhase phase)
        {
            if (phase != ExperiencePhase.Posing)
            {
                DestroyRuntimeGhost();
                _creationAttempted = false;
                return;
            }

            _creationAttempted = false;
            TryCreateRuntimeGhost();
        }

        private void HandleAvatarChanged(AvatarDefinition avatar)
        {
            DestroyRuntimeGhost();
            _creationAttempted = false;
            if (_gameFlow != null &&
                _gameFlow.CurrentPhase == ExperiencePhase.Posing)
            {
                TryCreateRuntimeGhost();
            }
        }

        private void RefreshForCurrentPhase()
        {
            if (_gameFlow == null ||
                _gameFlow.CurrentPhase != ExperiencePhase.Posing)
            {
                DestroyRuntimeGhost();
                _creationAttempted = false;
                return;
            }

            if (_runtimeGhost == null && !_creationAttempted)
            {
                TryCreateRuntimeGhost();
            }
        }

        private bool TryCreateRuntimeGhost()
        {
            _creationAttempted = true;
            ResolveReferences();

            var definition =
                _avatarRegistry == null
                    ? null
                    : _avatarRegistry.Active;
            if (definition == null)
            {
                Debug.LogWarning(
                    "[PoseGhostGuide] No active avatar is available.",
                    this);
                return false;
            }

            if (_ghostMaterial == null)
            {
                Debug.LogWarning(
                    "[PoseGhostGuide] Ghost material is not assigned.",
                    this);
                return false;
            }

            var layer = LayerMask.NameToLayer(
                _poseGuideLayerName);
            if (layer < 0)
            {
                Debug.LogWarning(
                    $"[PoseGhostGuide] Layer " +
                    $"'{_poseGuideLayerName}' does not exist.",
                    this);
                return false;
            }

            if (!definition.ValidateCinematicPrefab(
                    out var validationError))
            {
                Debug.LogWarning(
                    $"[PoseGhostGuide] {definition.DisplayName}: " +
                    validationError,
                    definition);
                return false;
            }

            var parent =
                _ghostParent == null
                    ? transform
                    : _ghostParent;
            var ghost = Instantiate(
                definition.CinematicPrefab,
                parent,
                false);
            ghost.name =
                $"PoseGhost_{definition.DisplayName}";

            if (ContainsForbiddenTrackingComponents(ghost))
            {
                Debug.LogWarning(
                    "[PoseGhostGuide] The clean presentation prefab " +
                    "contains a Meta tracking component. The guide was " +
                    "discarded without touching the live avatar.",
                    definition);
                DestroyUnityObject(ghost);
                return false;
            }

            var animator =
                ghost.GetComponentInChildren<Animator>(true);
            if (animator == null ||
                animator.avatar == null ||
                !animator.isHuman ||
                !animator.avatar.isValid)
            {
                Debug.LogWarning(
                    "[PoseGhostGuide] A valid Humanoid Animator is " +
                    "required.",
                    definition);
                DestroyUnityObject(ghost);
                return false;
            }

            animator.applyRootMotion = false;
            animator.cullingMode =
                AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = true;
            animator.Rebind();
            animator.Update(0f);
            animator.enabled = false;

            if (!TryCreateBindings(animator, out var head,
                    out var leftArm, out var rightArm))
            {
                Debug.LogWarning(
                    "[PoseGhostGuide] Humanoid head and complete arm " +
                    "chains are required.",
                    definition);
                DestroyUnityObject(ghost);
                return false;
            }

            var renderers =
                ghost.GetComponentsInChildren<Renderer>(true);
            if (!TryCalculateRendererHeight(
                    renderers,
                    out var referenceHeight))
            {
                Debug.LogWarning(
                    "[PoseGhostGuide] The presentation prefab has no " +
                    "finite renderer height.",
                    definition);
                DestroyUnityObject(ghost);
                return false;
            }

            SetLayerRecursively(ghost, layer);
            foreach (var renderer in renderers)
            {
                ConfigureGhostRenderer(
                    renderer,
                    _ghostMaterial);
            }

            _runtimeGhost = ghost;
            _ghostRenderers = renderers;
            _head = head;
            _leftArm = leftArm;
            _rightArm = rightArm;
            _headBaseLocalRotation = head.localRotation;
            _headBaseRotationInGhostSpace =
                Quaternion.Inverse(ghost.transform.rotation) *
                head.rotation;
            _baseLocalScale = ghost.transform.localScale;
            _referenceHeightMeters = referenceHeight;
            CacheMuscleBlendShapes(definition);

            var fistController =
                ghost.GetComponent<DumbbellGripVisualController>();
            if (fistController == null)
            {
                fistController =
                    ghost.AddComponent<DumbbellGripVisualController>();
            }

            fistController.ConfigureFistsOnly(animator);

            ApplyAdaptiveHintPresentation();

            UpdateGhostPose();
            SynchronizeMuscleBlendShape();
            return true;
        }

        private void UpdateGhostPose()
        {
            var onBoard = UsesDisplayAnchor;
            var profile = ActiveProfile;
            if (_runtimeGhost == null ||
                _head == null ||
                profile == null ||
                (!onBoard &&
                 (_cameraRig == null ||
                  _cameraRig.centerEyeAnchor == null)))
            {
                if (_runtimeGhost != null)
                {
                    _runtimeGhost.SetActive(false);
                }

                return;
            }

            // On a board the guide is a fixed exhibit, so the anchor
            // supplies both the origin its arms are measured from and the
            // direction it faces.
            var trackedHeadPosition = onBoard
                ? _displayAnchor.position
                : _cameraRig.centerEyeAnchor.position;
            if (!IsFinite(trackedHeadPosition))
            {
                _runtimeGhost.SetActive(false);
                return;
            }

            if (!_runtimeGhost.activeSelf)
            {
                _runtimeGhost.SetActive(true);
            }

            var calibratedHeight =
                ResolveCalibratedHeight();
            var uniformScale = onBoard
                ? Mathf.Max(0.01f, _displayUniformScale)
                : CalculateUniformScale(
                    calibratedHeight,
                    _referenceHeightMeters,
                    _minimumUniformScale,
                    _maximumUniformScale);
            if (_adaptiveHintActive)
            {
                uniformScale *= _adaptiveHintScaleMultiplier;
            }
            _runtimeGhost.transform.localScale =
                _baseLocalScale * uniformScale;

            // Arm reach has to shrink with the body, or a scaled down guide
            // would reach for targets sized to a full grown performer.
            var poseHeight = onBoard
                ? Mathf.Max(
                    0.01f,
                    _referenceHeightMeters * uniformScale)
                : calibratedHeight;

            var stageRotation = onBoard
                ? _displayAnchor.rotation
                : GetStageYawRotation();
            var stageOrigin = onBoard
                ? _displayAnchor.position
                : _stageFrame == null
                    ? Vector3.zero
                    : _stageFrame.position;
            _runtimeGhost.transform.SetPositionAndRotation(
                stageOrigin,
                stageRotation);

            ResetGuideBoneRotations();
            _runtimeGhost.transform.position +=
                trackedHeadPosition - _head.position;

            var target = profile.ToSettings().Target;
            if (!TryCalculateWorldTarget(
                    trackedHeadPosition,
                    target.LeftHandPositionFromHeadNormalized,
                    stageRotation,
                    poseHeight,
                    out var leftHandTarget) ||
                !TryCalculateWorldTarget(
                    trackedHeadPosition,
                    target.RightHandPositionFromHeadNormalized,
                    stageRotation,
                    poseHeight,
                    out var rightHandTarget))
            {
                _runtimeGhost.SetActive(false);
                return;
            }

            var stageRight = stageRotation * Vector3.right;
            var stageForward = stageRotation * Vector3.forward;
            var stageUp = stageRotation * Vector3.up;
            var lateralDistance =
                poseHeight *
                _elbowPoleLateralNormalized;
            var forwardDistance =
                poseHeight *
                _elbowPoleForwardNormalized;
            var upDistance =
                poseHeight *
                _elbowPoleUpNormalized;

            var elbowHints = profile.GuideElbows;
            var leftHint =
                elbowHints.LeftPoleFromShoulderNormalized;
            var rightHint =
                elbowHints.RightPoleFromShoulderNormalized;
            var leftPole = HasElbowHint(leftHint)
                ? CalculateElbowPoleFromHint(
                    _leftArm.Upper.position,
                    stageRotation,
                    leftHint,
                    poseHeight)
                : CalculateElbowPole(
                    _leftArm.Upper.position,
                    stageRight,
                    stageForward,
                    stageUp,
                    -1f,
                    lateralDistance,
                    forwardDistance,
                    upDistance);
            var rightPole = HasElbowHint(rightHint)
                ? CalculateElbowPoleFromHint(
                    _rightArm.Upper.position,
                    stageRotation,
                    rightHint,
                    poseHeight)
                : CalculateElbowPole(
                    _rightArm.Upper.position,
                    stageRight,
                    stageForward,
                    stageUp,
                    1f,
                    lateralDistance,
                    forwardDistance,
                    upDistance);

            ApplyArmIk(
                _leftArm,
                leftHandTarget,
                leftPole,
                stageRotation *
                Quaternion.Euler(
                    target.LeftHandEulerDegreesInStageFrame),
                profile.ScoresHandRotation);
            ApplyArmIk(
                _rightArm,
                rightHandTarget,
                rightPole,
                stageRotation *
                Quaternion.Euler(
                    target.RightHandEulerDegreesInStageFrame),
                profile.ScoresHandRotation);

            _head.rotation = CalculateHeadWorldRotation(
                stageRotation,
                Quaternion.Euler(
                    target.HeadEulerDegreesInStageFrame),
                _headBaseRotationInGhostSpace);
        }

        private void ResetGuideBoneRotations()
        {
            if (_head != null)
            {
                _head.localRotation =
                    _headBaseLocalRotation;
            }

            _leftArm.ResetRotations();
            _rightArm.ResetRotations();
        }

        private static void ApplyArmIk(
            in ArmBinding arm,
            Vector3 handTarget,
            Vector3 elbowPole,
            Quaternion handRotation,
            bool applyHandRotation)
        {
            if (!arm.IsValid)
            {
                return;
            }

            var upperLength = Vector3.Distance(
                arm.Upper.position,
                arm.Lower.position);
            var lowerLength = Vector3.Distance(
                arm.Lower.position,
                arm.Hand.position);
            if (!TrySolveTwoBoneIk(
                    arm.Upper.position,
                    upperLength,
                    lowerLength,
                    handTarget,
                    elbowPole,
                    out var solution))
            {
                return;
            }

            RotateBoneToward(
                arm.Upper,
                arm.Lower.position,
                solution.Elbow);
            RotateBoneToward(
                arm.Lower,
                arm.Hand.position,
                solution.Hand);

            ApplyAuthoredHandRotation(
                arm.Hand,
                handRotation,
                applyHandRotation);
        }

        /// <summary>
        /// Applies a wrist target only when the profile actually scores one.
        /// Otherwise the hand keeps its Humanoid base local rotation and
        /// naturally follows the solved forearm.
        /// </summary>
        public static void ApplyAuthoredHandRotation(
            Transform hand,
            Quaternion handRotation,
            bool applyHandRotation)
        {
            if (hand == null ||
                !applyHandRotation ||
                !IsFinite(handRotation))
            {
                return;
            }

            hand.rotation = handRotation;
        }

        /// <summary>
        /// Converts the captured HMD rotation into a model-head rotation.
        /// Humanoid head bones do not use the HMD's world axes, and the male
        /// and female FBXs also have different rest axes. Keeping the model's
        /// neutral head rotation as the final offset makes an identity target
        /// face forward instead of forcing the bone itself to world identity.
        /// </summary>
        public static Quaternion CalculateHeadWorldRotation(
            Quaternion stageRotation,
            Quaternion authoredHeadRotation,
            Quaternion neutralHeadRotationInGhostSpace)
        {
            stageRotation = IsFinite(stageRotation)
                ? NormalizeQuaternion(stageRotation)
                : Quaternion.identity;
            authoredHeadRotation = IsFinite(authoredHeadRotation)
                ? NormalizeQuaternion(authoredHeadRotation)
                : Quaternion.identity;
            neutralHeadRotationInGhostSpace =
                IsFinite(neutralHeadRotationInGhostSpace)
                    ? NormalizeQuaternion(
                        neutralHeadRotationInGhostSpace)
                    : Quaternion.identity;
            return NormalizeQuaternion(
                stageRotation *
                authoredHeadRotation *
                neutralHeadRotationInGhostSpace);
        }

        private static void RotateBoneToward(
            Transform bone,
            Vector3 currentChildPosition,
            Vector3 desiredChildPosition)
        {
            if (bone == null)
            {
                return;
            }

            var currentDirection =
                currentChildPosition - bone.position;
            var desiredDirection =
                desiredChildPosition - bone.position;
            if (!TryNormalize(
                    currentDirection,
                    out currentDirection) ||
                !TryNormalize(
                    desiredDirection,
                    out desiredDirection))
            {
                return;
            }

            bone.rotation =
                Quaternion.FromToRotation(
                    currentDirection,
                    desiredDirection) *
                bone.rotation;
        }

        private void CacheMuscleBlendShapes(
            AvatarDefinition definition)
        {
            // The posing guide is a display copy of the active avatar.
            // Include fixed character-shape values as well as the channels
            // controlled by earned growth; the female avatar's Muscle shape
            // is a fixed base value and must remain at the source weight.
            _muscleState.RebuildAllSharedBlendShapes(
                definition.TrackedBodyRenderer,
                _runtimeGhost.GetComponentsInChildren<
                    SkinnedMeshRenderer>(true));
        }

        private void SynchronizeMuscleBlendShape()
        {
            _muscleState.Apply();
        }

        private float ResolveCalibratedHeight()
        {
            var height =
                _heightCalibration == null
                    ? 0f
                    : _heightCalibration.HeightMeters;
            if (IsFinite(height) && height >= 0.25f)
            {
                return height;
            }

            if (_cameraRig != null &&
                _cameraRig.trackingSpace != null &&
                _cameraRig.centerEyeAnchor != null)
            {
                var eyeHeight =
                    _cameraRig.trackingSpace
                        .InverseTransformPoint(
                            _cameraRig.centerEyeAnchor.position)
                        .y;
                if (IsFinite(eyeHeight) && eyeHeight >= 0.25f)
                {
                    return eyeHeight + 0.11f;
                }
            }

            return 1.8f;
        }

        private Quaternion GetStageYawRotation()
        {
            if (_stageFrame == null)
            {
                return Quaternion.identity;
            }

            var yaw = _stageFrame.eulerAngles.y;
            return IsFinite(yaw)
                ? Quaternion.Euler(0f, yaw, 0f)
                : Quaternion.identity;
        }

        private void DestroyRuntimeGhost()
        {
            var ghost = _runtimeGhost;
            _runtimeGhost = null;
            _head = null;
            _leftArm = default;
            _rightArm = default;
            _referenceHeightMeters = 0f;
            _ghostRenderers = Array.Empty<Renderer>();
            _muscleState.Clear();

            if (ghost != null)
            {
                ghost.SetActive(false);
                DestroyUnityObject(ghost);
            }
        }

        private void ResolveReferences()
        {
            _gameFlow ??=
                FindFirstObjectByType<GameFlowController>();
            _avatarRegistry ??=
                FindFirstObjectByType<AvatarRegistry>();
            _cameraRig ??=
                FindFirstObjectByType<OVRCameraRig>();
            _heightCalibration ??=
                FindFirstObjectByType<
                    HeightCalibrationController>();

            _posingScorer ??=
                FindFirstObjectByType<ThreePointPosingScorer>(
                    FindObjectsInactive.Include);

            if (_stageFrame == null)
            {
                var transition =
                    FindFirstObjectByType<
                        StageTransitionController>();
                _stageFrame =
                    transition == null
                        ? null
                        : transition.StageArrivalPoint;
            }
        }

        private void Subscribe()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= HandlePhaseChanged;
                _gameFlow.PhaseChanged += HandlePhaseChanged;
            }

            if (_avatarRegistry != null)
            {
                _avatarRegistry.OnAvatarChanged -=
                    HandleAvatarChanged;
                _avatarRegistry.OnAvatarChanged +=
                    HandleAvatarChanged;
            }
        }

        private void Unsubscribe()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= HandlePhaseChanged;
            }

            if (_avatarRegistry != null)
            {
                _avatarRegistry.OnAvatarChanged -=
                    HandleAvatarChanged;
            }
        }

        private void ValidateSettings()
        {
            _poseGuideLayerName =
                string.IsNullOrWhiteSpace(
                    _poseGuideLayerName)
                    ? DefaultPoseGuideLayer
                    : _poseGuideLayerName.Trim();
            _minimumUniformScale =
                SanitizePositive(
                    _minimumUniformScale,
                    0.5f);
            _maximumUniformScale =
                SanitizePositive(
                    _maximumUniformScale,
                    2f);
            _adaptiveHintScaleMultiplier = Mathf.Clamp(
                _adaptiveHintScaleMultiplier,
                1f,
                1.5f);
            _adaptiveHintAlphaMultiplier = Mathf.Clamp(
                _adaptiveHintAlphaMultiplier,
                1f,
                3f);
            if (_minimumUniformScale >
                _maximumUniformScale)
            {
                (_minimumUniformScale,
                 _maximumUniformScale) =
                    (_maximumUniformScale,
                     _minimumUniformScale);
            }

            _elbowPoleLateralNormalized =
                SanitizeNonNegative(
                    _elbowPoleLateralNormalized);
            _elbowPoleForwardNormalized =
                SanitizeFinite(
                    _elbowPoleForwardNormalized);
            _elbowPoleUpNormalized =
                SanitizeFinite(
                    _elbowPoleUpNormalized);
        }

        private void ApplyAdaptiveHintPresentation()
        {
            if (_ghostRenderers == null ||
                _ghostRenderers.Length == 0)
            {
                return;
            }

            _adaptiveHintPropertyBlock ??=
                new MaterialPropertyBlock();

            var baseColor = _ghostMaterial != null &&
                            _ghostMaterial.HasProperty(BaseColorId)
                ? _ghostMaterial.GetColor(BaseColorId)
                : Color.cyan;
            var emphasizedColor = baseColor;
            emphasizedColor.a = Mathf.Clamp01(
                baseColor.a *
                (_adaptiveHintActive
                    ? _adaptiveHintAlphaMultiplier
                    : 1f));

            foreach (var renderer in _ghostRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(
                    _adaptiveHintPropertyBlock);
                _adaptiveHintPropertyBlock.SetColor(
                    BaseColorId,
                    emphasizedColor);
                _adaptiveHintPropertyBlock.SetColor(
                    ColorId,
                    emphasizedColor);
                renderer.SetPropertyBlock(
                    _adaptiveHintPropertyBlock);
                _adaptiveHintPropertyBlock.Clear();
            }
        }

        public static bool TryCalculateWorldTarget(
            Vector3 headWorldPosition,
            Vector3 normalizedOffset,
            Quaternion stageRotation,
            float bodyScaleMeters,
            out Vector3 worldTarget)
        {
            var safeHead =
                SanitizeVector(headWorldPosition);
            worldTarget = safeHead;
            if (!IsFinite(headWorldPosition) ||
                !IsFinite(normalizedOffset) ||
                !IsFinite(stageRotation) ||
                !IsFinite(bodyScaleMeters) ||
                bodyScaleMeters <= 0f)
            {
                return false;
            }

            stageRotation =
                NormalizeQuaternion(stageRotation);
            worldTarget =
                headWorldPosition +
                stageRotation *
                (normalizedOffset * bodyScaleMeters);
            if (IsFinite(worldTarget))
            {
                return true;
            }

            worldTarget = safeHead;
            return false;
        }

        public static Vector3 CalculateElbowPole(
            Vector3 shoulder,
            Vector3 stageRight,
            Vector3 stageForward,
            Vector3 stageUp,
            float sideSign,
            float lateralDistance,
            float forwardDistance,
            float upDistance)
        {
            shoulder = SanitizeVector(shoulder);
            stageRight =
                NormalizeOrFallback(
                    stageRight,
                    Vector3.right);
            stageForward =
                NormalizeOrFallback(
                    stageForward,
                    Vector3.forward);
            stageUp =
                NormalizeOrFallback(
                    stageUp,
                    Vector3.up);
            sideSign =
                !IsFinite(sideSign) ||
                Mathf.Approximately(sideSign, 0f)
                    ? 1f
                    : Mathf.Sign(sideSign);
            lateralDistance =
                SanitizeNonNegative(lateralDistance);
            forwardDistance =
                SanitizeFinite(forwardDistance);
            upDistance =
                SanitizeFinite(upDistance);

            return shoulder +
                   stageRight *
                   (sideSign * lateralDistance) +
                   stageForward * forwardDistance +
                   stageUp * upDistance;
        }

        /// <summary>
        /// Converts a profile-specific shoulder-relative elbow pole into the
        /// stage frame. This lets each pose choose a believable arm plane.
        /// </summary>
        public static Vector3 CalculateElbowPoleFromHint(
            Vector3 shoulder,
            Quaternion stageRotation,
            Vector3 normalizedHint,
            float bodyScaleMeters)
        {
            shoulder = SanitizeVector(shoulder);
            stageRotation = IsFinite(stageRotation)
                ? NormalizeQuaternion(stageRotation)
                : Quaternion.identity;
            normalizedHint = SanitizeVector(normalizedHint);
            bodyScaleMeters = SanitizeNonNegative(
                bodyScaleMeters);
            return shoulder +
                stageRotation *
                (normalizedHint * bodyScaleMeters);
        }

        /// <summary>
        /// The inverse of <see cref="CalculateElbowPoleFromHint"/>: turns a
        /// real elbow into the hint that would put the guide's elbow there.
        ///
        /// Kept beside its inverse so the two cannot drift. Capturing a target
        /// without capturing this leaves the guide bending the arms towards
        /// wherever the hand-authored hint pointed, which no longer relates to
        /// the hands that were just recorded — the guide then shows a pose
        /// nobody asked for.
        ///
        /// The elbow is inferred by the tracking rather than measured, which
        /// is why it is not scored. It is still exactly the right thing to
        /// show: it is where the solver put the elbow when a real body held
        /// the pose, so the guide asks for something reachable.
        /// </summary>
        public static Vector3 BuildElbowHint(
            Vector3 shoulder,
            Vector3 elbow,
            Quaternion stageRotation,
            float bodyScaleMeters)
        {
            shoulder = SanitizeVector(shoulder);
            elbow = SanitizeVector(elbow);
            stageRotation = IsFinite(stageRotation)
                ? NormalizeQuaternion(stageRotation)
                : Quaternion.identity;
            bodyScaleMeters = SanitizeNonNegative(bodyScaleMeters);
            if (bodyScaleMeters <= 0.01f)
            {
                return Vector3.zero;
            }

            return Quaternion.Inverse(stageRotation) *
                   (elbow - shoulder) /
                   bodyScaleMeters;
        }

        private static bool HasElbowHint(Vector3 hint)
        {
            return IsFinite(hint) &&
                hint.sqrMagnitude >=
                MinimumDirectionMagnitudeSquared;
        }

        public static Vector3 MirrorAcrossStageSagittalPlane(
            Vector3 point,
            Vector3 stageOrigin,
            Quaternion stageRotation)
        {
            point = SanitizeVector(point);
            stageOrigin = SanitizeVector(stageOrigin);
            stageRotation =
                IsFinite(stageRotation)
                    ? NormalizeQuaternion(stageRotation)
                    : Quaternion.identity;
            var local =
                Quaternion.Inverse(stageRotation) *
                (point - stageOrigin);
            local.x = -local.x;
            return stageOrigin + stageRotation * local;
        }

        public static bool TrySolveTwoBoneIk(
            Vector3 shoulder,
            float upperLength,
            float lowerLength,
            Vector3 handTarget,
            Vector3 elbowPole,
            out PoseGhostIkSolution solution)
        {
            var safeShoulder = SanitizeVector(shoulder);
            solution = new PoseGhostIkSolution(
                safeShoulder,
                safeShoulder,
                safeShoulder,
                0f,
                0f,
                false);

            if (!IsFinite(shoulder) ||
                !IsFinite(handTarget) ||
                !IsFinite(elbowPole) ||
                !IsFinite(upperLength) ||
                !IsFinite(lowerLength) ||
                upperLength < MinimumSegmentLength ||
                lowerLength < MinimumSegmentLength)
            {
                return false;
            }

            var toTarget = handTarget - shoulder;
            var targetDistance = toTarget.magnitude;
            if (!IsFinite(targetDistance))
            {
                return false;
            }

            var direction =
                NormalizeOrFallback(
                    toTarget,
                    Vector3.up);

            var bendEpsilon = Mathf.Min(
                0.0001f,
                Mathf.Min(
                    upperLength,
                    lowerLength) *
                0.001f);
            var minimumReach =
                Mathf.Abs(
                    upperLength - lowerLength) +
                bendEpsilon;
            var maximumReach =
                upperLength +
                lowerLength -
                bendEpsilon;
            if (minimumReach > maximumReach)
            {
                var midpoint =
                    (minimumReach + maximumReach) *
                    0.5f;
                minimumReach = midpoint;
                maximumReach = midpoint;
            }

            var solvedDistance = Mathf.Clamp(
                targetDistance,
                minimumReach,
                maximumReach);
            var poleDirection =
                Vector3.ProjectOnPlane(
                    elbowPole - shoulder,
                    direction);
            if (!TryNormalize(
                    poleDirection,
                    out poleDirection))
            {
                var perpendicularFallback =
                    Vector3.ProjectOnPlane(
                        Vector3.up,
                        direction);
                if (!TryNormalize(
                        perpendicularFallback,
                        out poleDirection))
                {
                    perpendicularFallback =
                        Vector3.ProjectOnPlane(
                            Vector3.right,
                            direction);
                    if (!TryNormalize(
                            perpendicularFallback,
                            out poleDirection))
                    {
                        perpendicularFallback =
                            Vector3.ProjectOnPlane(
                                Vector3.forward,
                                direction);
                        if (!TryNormalize(
                                perpendicularFallback,
                                out poleDirection))
                        {
                            return false;
                        }
                    }
                }
            }

            var denominator =
                2f * upperLength * solvedDistance;
            if (denominator <= Mathf.Epsilon)
            {
                return false;
            }

            var shoulderCosine = Mathf.Clamp(
                (upperLength * upperLength +
                 solvedDistance * solvedDistance -
                 lowerLength * lowerLength) /
                denominator,
                -1f,
                1f);
            var alongDistance =
                upperLength * shoulderCosine;
            var perpendicularDistance = Mathf.Sqrt(
                Mathf.Max(
                    0f,
                    upperLength * upperLength -
                    alongDistance * alongDistance));

            var solvedHand =
                shoulder +
                direction * solvedDistance;
            var solvedElbow =
                shoulder +
                direction * alongDistance +
                poleDirection * perpendicularDistance;
            if (!IsFinite(solvedHand) ||
                !IsFinite(solvedElbow))
            {
                return false;
            }

            solution = new PoseGhostIkSolution(
                shoulder,
                solvedElbow,
                solvedHand,
                targetDistance,
                solvedDistance,
                !Mathf.Approximately(
                    targetDistance,
                    solvedDistance));
            return true;
        }

        public static float CalculateUniformScale(
            float calibratedHeightMeters,
            float referenceHeightMeters,
            float minimumScale = 0.5f,
            float maximumScale = 2f)
        {
            minimumScale =
                SanitizePositive(
                    minimumScale,
                    0.5f);
            maximumScale =
                SanitizePositive(
                    maximumScale,
                    2f);
            if (minimumScale > maximumScale)
            {
                (minimumScale, maximumScale) =
                    (maximumScale, minimumScale);
            }

            if (!IsFinite(calibratedHeightMeters) ||
                !IsFinite(referenceHeightMeters) ||
                calibratedHeightMeters <= Mathf.Epsilon ||
                referenceHeightMeters <= Mathf.Epsilon)
            {
                return Mathf.Clamp(
                    1f,
                    minimumScale,
                    maximumScale);
            }

            return Mathf.Clamp(
                calibratedHeightMeters /
                referenceHeightMeters,
                minimumScale,
                maximumScale);
        }

        public static bool ConfigureGhostRenderer(
            Renderer renderer,
            Material material)
        {
            if (renderer == null || material == null)
            {
                return false;
            }

            var slotCount =
                Mathf.Max(
                    1,
                    renderer.sharedMaterials.Length);
            var materials =
                new Material[slotCount];
            for (var index = 0;
                 index < materials.Length;
                 index++)
            {
                materials[index] = material;
            }

            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode =
                ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage =
                LightProbeUsage.Off;
            renderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                skinned.updateWhenOffscreen = true;
            }

            return true;
        }

        public static bool SetLayerRecursively(
            GameObject root,
            int layer)
        {
            if (root == null || layer < 0 || layer > 31)
            {
                return false;
            }

            SetLayer(root.transform, layer);
            return true;
        }

        public static bool ContainsForbiddenTrackingComponents(
            GameObject root)
        {
            if (root == null)
            {
                return false;
            }

            foreach (var component in
                     root.GetComponentsInChildren<
                         Component>(true))
            {
                if (component == null)
                {
                    continue;
                }

                if (component is CharacterRetargeter ||
                    component is MetaSourceDataProvider)
                {
                    return true;
                }

                var type = component.GetType();
                var typeNamespace = type.Namespace ?? string.Empty;
                var typeName = type.Name;
                if (typeNamespace.StartsWith(
                        "Meta.XR.Movement",
                        StringComparison.Ordinal) ||
                    typeName.StartsWith(
                        "OVRBody",
                        StringComparison.Ordinal) ||
                    typeName.StartsWith(
                        "OVRSkeleton",
                        StringComparison.Ordinal) ||
                    typeName.StartsWith(
                        "OVRFace",
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryCreateBindings(
            Animator animator,
            out Transform head,
            out ArmBinding leftArm,
            out ArmBinding rightArm)
        {
            head =
                animator.GetBoneTransform(
                    HumanBodyBones.Head);
            leftArm = new ArmBinding(
                animator.GetBoneTransform(
                    HumanBodyBones.LeftUpperArm),
                animator.GetBoneTransform(
                    HumanBodyBones.LeftLowerArm),
                animator.GetBoneTransform(
                    HumanBodyBones.LeftHand));
            rightArm = new ArmBinding(
                animator.GetBoneTransform(
                    HumanBodyBones.RightUpperArm),
                animator.GetBoneTransform(
                    HumanBodyBones.RightLowerArm),
                animator.GetBoneTransform(
                    HumanBodyBones.RightHand));
            return
                head != null &&
                leftArm.IsValid &&
                rightArm.IsValid;
        }

        private static bool TryCalculateRendererHeight(
            Renderer[] renderers,
            out float height)
        {
            height = 0f;
            var hasBounds = false;
            var bounds = default(Bounds);
            if (renderers == null)
            {
                return false;
            }

            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                var rendererBounds = renderer.bounds;
                if (!IsFinite(rendererBounds.min) ||
                    !IsFinite(rendererBounds.max))
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = rendererBounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(rendererBounds);
                }
            }

            if (!hasBounds ||
                !IsFinite(bounds.size.y) ||
                bounds.size.y <= Mathf.Epsilon)
            {
                return false;
            }

            height = bounds.size.y;
            return true;
        }

        private static void SetLayer(
            Transform current,
            int layer)
        {
            current.gameObject.layer = layer;
            for (var index = 0;
                 index < current.childCount;
                 index++)
            {
                SetLayer(
                    current.GetChild(index),
                    layer);
            }
        }

        private static void DestroyUnityObject(
            UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private static Vector3 NormalizeOrFallback(
            Vector3 value,
            Vector3 fallback)
        {
            if (TryNormalize(value, out var normalized))
            {
                return normalized;
            }

            return TryNormalize(fallback, out normalized)
                ? normalized
                : Vector3.up;
        }

        private static bool TryNormalize(
            Vector3 value,
            out Vector3 normalized)
        {
            if (!IsFinite(value) ||
                value.sqrMagnitude <
                MinimumDirectionMagnitudeSquared)
            {
                normalized = Vector3.zero;
                return false;
            }

            normalized = value.normalized;
            return IsFinite(normalized);
        }

        private static Quaternion NormalizeQuaternion(
            Quaternion value)
        {
            var magnitudeSquared =
                value.x * value.x +
                value.y * value.y +
                value.z * value.z +
                value.w * value.w;
            if (!IsFinite(magnitudeSquared) ||
                magnitudeSquared <
                MinimumDirectionMagnitudeSquared)
            {
                return Quaternion.identity;
            }

            var inverseMagnitude =
                1f / Mathf.Sqrt(magnitudeSquared);
            return new Quaternion(
                value.x * inverseMagnitude,
                value.y * inverseMagnitude,
                value.z * inverseMagnitude,
                value.w * inverseMagnitude);
        }

        private static Vector3 SanitizeVector(Vector3 value)
        {
            return new Vector3(
                SanitizeFinite(value.x),
                SanitizeFinite(value.y),
                SanitizeFinite(value.z));
        }

        private static float SanitizeNonNegative(float value)
        {
            return IsFinite(value)
                ? Mathf.Max(0f, value)
                : 0f;
        }

        private static float SanitizePositive(
            float value,
            float fallback)
        {
            return IsFinite(value) && value > 0f
                ? value
                : fallback;
        }

        private static float SanitizeFinite(float value)
        {
            return IsFinite(value) ? value : 0f;
        }

        private static bool IsFinite(Vector3 value)
        {
            return
                IsFinite(value.x) &&
                IsFinite(value.y) &&
                IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return
                IsFinite(value.x) &&
                IsFinite(value.y) &&
                IsFinite(value.z) &&
                IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }

        private readonly struct ArmBinding
        {
            public ArmBinding(
                Transform upper,
                Transform lower,
                Transform hand)
            {
                Upper = upper;
                Lower = lower;
                Hand = hand;
                UpperBaseLocalRotation =
                    upper == null
                        ? Quaternion.identity
                        : upper.localRotation;
                LowerBaseLocalRotation =
                    lower == null
                        ? Quaternion.identity
                        : lower.localRotation;
                HandBaseLocalRotation =
                    hand == null
                        ? Quaternion.identity
                        : hand.localRotation;
            }

            public Transform Upper { get; }
            public Transform Lower { get; }
            public Transform Hand { get; }
            private Quaternion UpperBaseLocalRotation { get; }
            private Quaternion LowerBaseLocalRotation { get; }
            private Quaternion HandBaseLocalRotation { get; }
            public bool IsValid =>
                Upper != null &&
                Lower != null &&
                Hand != null;

            public void ResetRotations()
            {
                if (!IsValid)
                {
                    return;
                }

                Upper.localRotation =
                    UpperBaseLocalRotation;
                Lower.localRotation =
                    LowerBaseLocalRotation;
                Hand.localRotation =
                    HandBaseLocalRotation;
            }
        }
    }
}
