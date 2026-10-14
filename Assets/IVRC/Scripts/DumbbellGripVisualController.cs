using System;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Gives one avatar a dumbbell in each hand and keeps its fingers closed
    /// around them.
    ///
    /// Everything hangs off the Humanoid hand bones, so this component never
    /// has to know what is driving the arms. Meta body tracking in VR, the
    /// desktop pose driver in the editor, MirrorTransforms on the mirror body
    /// and the ghost pose applier on the guides all move the same hand bone,
    /// and a dumbbell parented to that bone follows for free.
    ///
    /// Finger rotations are written in LateUpdate behind every existing writer
    /// in the project (RhythmPoseGuideController at 20100 is currently the
    /// latest). Anything earlier would be straightened out again on the same
    /// frame, because the authored pose assets carry all 40 finger bones.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(ExecutionOrder)]
    public sealed class DumbbellGripVisualController : MonoBehaviour
    {
        /// <summary>
        /// Behind every other bone writer in the project. See the class summary.
        /// </summary>
        public const int ExecutionOrder = 20200;

        private const int FingerCount = 5;
        private const int JointCount = 3;
        private const int ThumbSlot = 0;

        /// <summary>Where one dumbbell sits relative to its hand bone.</summary>
        [Serializable]
        public sealed class DumbbellPlacement
        {
            [Tooltip(
                "Offset from the hand bone, in the hand bone's own space " +
                "(metres). Use 'Auto-Fit Dumbbells To Hands' for a starting " +
                "value, then nudge it until the grip sits in the palm.")]
            public Vector3 PositionOffset;

            [Tooltip("Rotation relative to the hand bone, in degrees.")]
            public Vector3 RotationOffset;

            [Tooltip("Scale of the dumbbell instance.")]
            public Vector3 Scale = Vector3.one;
        }

        /// <summary>
        /// How far each finger of one hand closes. 0 leaves the finger exactly
        /// where the rig put it, 1 applies the full angles configured under
        /// Curl Shaping.
        /// </summary>
        [Serializable]
        public sealed class GripPose
        {
            [Range(0f, 1f)] public float ThumbCurl = 0.5f;
            [Range(0f, 1f)] public float IndexCurl = 0.65f;
            [Range(0f, 1f)] public float MiddleCurl = 0.7f;
            [Range(0f, 1f)] public float RingCurl = 0.7f;
            [Range(0f, 1f)] public float LittleCurl = 0.65f;

            public float Get(int fingerSlot)
            {
                switch (fingerSlot)
                {
                    case 0: return ThumbCurl;
                    case 1: return IndexCurl;
                    case 2: return MiddleCurl;
                    case 3: return RingCurl;
                    default: return LittleCurl;
                }
            }
        }

        [Header("Source")]
        [Tooltip(
            "Humanoid animator this avatar's hand and finger bones come from. " +
            "Left empty it is taken from this GameObject.")]
        [SerializeField] private Animator _animator;

        [Tooltip(
            "Model placed in both hands. Its local +X must run along the bar " +
            "and its origin must sit at the centre of the grip.")]
        [SerializeField] private GameObject _dumbbellPrefab;

        [Header("Dumbbell / Left")]
        [SerializeField] private DumbbellPlacement _leftDumbbell =
            new DumbbellPlacement();

        [Header("Dumbbell / Right")]
        [SerializeField] private DumbbellPlacement _rightDumbbell =
            new DumbbellPlacement();

        [Header("Grip Pose / Left Hand")]
        [SerializeField] private GripPose _leftGrip = new GripPose();

        [Header("Grip Pose / Right Hand")]
        [SerializeField] private GripPose _rightGrip = new GripPose();

        [Header("Curl Shaping")]
        [Tooltip(
            "Degrees applied at curl = 1 to the proximal (x), intermediate " +
            "(y) and distal (z) joint of the four fingers.")]
        [SerializeField] private Vector3 _fingerMaxCurlDegrees =
            new Vector3(60f, 80f, 45f);

        [Tooltip(
            "Same three joints for the thumb, which closes much less than the " +
            "fingers do.")]
        [SerializeField] private Vector3 _thumbMaxCurlDegrees =
            new Vector3(28f, 34f, 26f);

        [Tooltip(
            "Swings the whole thumb up towards the fingers, around the bar, " +
            "before it curls. Curl alone only folds the thumb into the palm " +
            "beside the grip; this is what brings it over the top of the bar. " +
            "Negative swings it away from the fingers.")]
        [Range(-90f, 90f)]
        [SerializeField] private float _thumbSwingDegrees = 55f;

        [Header("Presentation")]
        [Tooltip(
            "Renderer the dumbbells copy their visibility from, so a hidden " +
            "ghost never leaves two dumbbells floating in the air. Left empty " +
            "it is taken from the body mesh of this avatar.")]
        [SerializeField] private SkinnedMeshRenderer _visibilitySource;

        [Tooltip(
            "Also copy the source renderer's material and property block. Turn " +
            "this on for the ghost guides so the dumbbells fade with them, and " +
            "off for solid avatars that keep the metal material.")]
        [SerializeField] private bool _matchSourceMaterial;

        [Header("Editor")]
        [Tooltip(
            "Hold the grip in edit mode too, for tuning the curls in the scene " +
            "view. Off by default because posing the fingers marks the shared " +
            "scene dirty; the grip always applies in play mode. Turning it on " +
            "is safe - the rotations are restored before a save, so a preview " +
            "never reaches the scene file.")]
        [SerializeField] private bool _applyGripInEditMode;

        private readonly HandBinding _left = new HandBinding();
        private readonly HandBinding _right = new HandBinding();
        private MaterialPropertyBlock _propertyBlock;
        private GameFlowController _gameFlow;
        private bool _bound;
        private bool _warnedNoAnimator;
        private bool _warnedNoBones;
        private bool _warnedNoPrefab;

        private sealed class BoneGrip
        {
            public Transform Bone;
            public Vector3 CurlAxis = Vector3.right;
            public Vector3 SwingAxis = Vector3.up;
            public Quaternion Rest = Quaternion.identity;
            public bool HasRest;
        }

        private sealed class HandBinding
        {
            public bool IsValid;
            public Transform Hand;
            public Vector3 PalmDirection = Vector3.right;
            public Vector3 BarDirection = Vector3.up;
            public Vector3 FingerDirection = Vector3.forward;
            public Vector3 KnuckleMid;
            public readonly BoneGrip[] Bones =
                new BoneGrip[FingerCount * JointCount];
            public Transform Dumbbell;
            public Vector3 AppliedPosition;
            public Quaternion AppliedRotation = Quaternion.identity;
            public Vector3 AppliedScale;
            public bool HasAppliedPlacement;
        }

        private void OnEnable()
        {
            Bind();
        }

        private void OnDisable()
        {
            // Put the hands back the way the rig had them so a disabled
            // component leaves nothing of itself behind, and drop the preview
            // dumbbells that only exist while this is running.
            ReleaseGrip();
            DestroyDumbbells();
            _bound = false;
        }

        private void LateUpdate()
        {
            if (!_bound)
            {
                Bind();
            }

            var applyGrip = Application.isPlaying || _applyGripInEditMode;
            var showDumbbells = ShouldShowDumbbells();
            ApplyHand(
                _left,
                _leftDumbbell,
                _leftGrip,
                showDumbbells,
                applyGrip);
            ApplyHand(
                _right,
                _rightDumbbell,
                _rightGrip,
                showDumbbells,
                applyGrip);
        }

        /// <summary>
        /// Restores the captured rest rotations. The editor calls this around
        /// scene saves so an edit-mode preview never reaches the scene file.
        /// </summary>
        public void ReleaseGrip()
        {
            ReleaseHand(_left);
            ReleaseHand(_right);
        }

        /// <summary>
        /// Opens the hands, then reads the pose the grip is built from again.
        /// Only needed after a domain reload in the middle of play mode, which
        /// leaves the fingers already closed when the component rebinds.
        /// </summary>
        [ContextMenu("Recapture Rest Pose")]
        public void RecaptureRestPose()
        {
            ReleaseGrip();
            Bind();
        }

        /// <summary>
        /// Configures a runtime-only avatar, such as the posing reference
        /// guide, to show closed fists without spawning dumbbells. The curl
        /// shape matches the hand pose already tuned on the tracked avatar,
        /// while Bind still derives its axes from the supplied Humanoid rig so
        /// male and female finger bones close in their own anatomical frame.
        /// </summary>
        public void ConfigureFistsOnly(Animator animator)
        {
            ReleaseGrip();
            DestroyDumbbells();

            _animator = animator;
            _dumbbellPrefab = null;
            _leftGrip ??= new GripPose();
            _rightGrip ??= new GripPose();
            ConfigureClosedFist(_leftGrip);
            ConfigureClosedFist(_rightGrip);
            _fingerMaxCurlDegrees =
                new Vector3(96.9f, 62.9f, 86.5f);
            _thumbMaxCurlDegrees =
                new Vector3(86.5f, 56.2f, 26f);
            _thumbSwingDegrees = 49f;
            _applyGripInEditMode = false;
            _gameFlow = null;
            _bound = false;
            Bind();
        }

        private static void ConfigureClosedFist(GripPose grip)
        {
            grip.ThumbCurl = 0.403f;
            grip.IndexCurl = 0.758f;
            grip.MiddleCurl = 0.826f;
            grip.RingCurl = 0.805f;
            grip.LittleCurl = 0.839f;
        }

        private void Bind()
        {
            _bound = true;
            _left.IsValid = false;
            _right.IsValid = false;

            var animator = ResolveAnimator();
            if (animator == null)
            {
                WarnOnce(
                    ref _warnedNoAnimator,
                    "has no valid Humanoid Animator, so no dumbbells or grip " +
                    "are applied.");
                return;
            }

            if (_visibilitySource == null)
            {
                _visibilitySource = FindBodyRenderer();
            }

            var leftBound = BindHand(_left, animator, true);
            var rightBound = BindHand(_right, animator, false);
            if (!leftBound || !rightBound)
            {
                WarnOnce(
                    ref _warnedNoBones,
                    "is missing hand or finger bones in its Humanoid avatar, " +
                    "so that hand keeps no dumbbell or grip.");
            }
        }

        private Animator ResolveAnimator()
        {
            if (_animator == null)
            {
                _animator = GetComponent<Animator>();
            }

            if (_animator == null ||
                !_animator.isHuman ||
                _animator.avatar == null ||
                !_animator.avatar.isValid)
            {
                return null;
            }

            return _animator;
        }

        private static bool BindHand(
            HandBinding binding,
            Animator animator,
            bool isLeft)
        {
            binding.IsValid = false;
            binding.Hand = animator.GetBoneTransform(
                isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (binding.Hand == null)
            {
                return false;
            }

            var index = animator.GetBoneTransform(
                isLeft
                    ? HumanBodyBones.LeftIndexProximal
                    : HumanBodyBones.RightIndexProximal);
            var little = animator.GetBoneTransform(
                isLeft
                    ? HumanBodyBones.LeftLittleProximal
                    : HumanBodyBones.RightLittleProximal);
            var thumb = animator.GetBoneTransform(
                isLeft
                    ? HumanBodyBones.LeftThumbProximal
                    : HumanBodyBones.RightThumbProximal);
            if (index == null || little == null || thumb == null)
            {
                return false;
            }

            // The hand frame is built from the knuckles in the hand bone's own
            // space. Local positions are unaffected by the negative scale the
            // mirror rig carries, and they do not move when the fingers curl,
            // so the same frame stays correct on all four bodies.
            if (!TryBuildHandFrame(binding, index, little, thumb))
            {
                return false;
            }

            for (var finger = 0; finger < FingerCount; finger++)
            {
                for (var joint = 0; joint < JointCount; joint++)
                {
                    var bone = animator.GetBoneTransform(
                        FingerBone(isLeft, finger, joint));
                    if (bone == null)
                    {
                        return false;
                    }

                    var slot = finger * JointCount + joint;
                    if (binding.Bones[slot] == null)
                    {
                        binding.Bones[slot] = new BoneGrip();
                    }

                    binding.Bones[slot].Bone = bone;
                    binding.Bones[slot].HasRest = false;
                }
            }

            binding.IsValid = true;

            // The rest pose is read here, in OnEnable, and then left alone. It
            // is the open hand the rig was authored with, because nothing has
            // posed the avatar yet this frame, and taking it once is what
            // makes the grip absolute: the same closed hand on the player, the
            // mirror and both ghosts no matter what the pose assets, the
            // retargeter or MirrorTransforms write later.
            for (var finger = 0; finger < FingerCount; finger++)
            {
                CaptureFinger(binding, finger);
            }

            return true;
        }

        private static bool TryBuildHandFrame(
            HandBinding binding,
            Transform index,
            Transform little,
            Transform thumb)
        {
            var indexLocal = index.localPosition;
            var littleLocal = little.localPosition;
            binding.KnuckleMid = (indexLocal + littleLocal) * 0.5f;

            var bar = littleLocal - indexLocal;
            if (bar.sqrMagnitude < 1e-10f ||
                binding.KnuckleMid.sqrMagnitude < 1e-10f)
            {
                return false;
            }

            binding.BarDirection = bar.normalized;
            binding.FingerDirection = binding.KnuckleMid.normalized;
            var perpendicular = Vector3.Cross(
                binding.BarDirection,
                binding.FingerDirection);
            if (perpendicular.sqrMagnitude < 1e-10f)
            {
                return false;
            }

            perpendicular.Normalize();

            // Which side of the palm plane the thumb sits on is what tells the
            // two hands apart, whatever axis conventions the rig uses.
            var thumbSide = Vector3.Dot(
                thumb.localPosition - binding.KnuckleMid,
                perpendicular);
            binding.PalmDirection = thumbSide >= 0f
                ? perpendicular
                : -perpendicular;
            return true;
        }

        private void ApplyHand(
            HandBinding binding,
            DumbbellPlacement placement,
            GripPose grip,
            bool showDumbbell,
            bool applyGrip)
        {
            if (!binding.IsValid)
            {
                return;
            }

            if (showDumbbell)
            {
                UpdateDumbbell(binding, placement);
            }
            else
            {
                DestroyDumbbell(binding);
            }

            if (applyGrip)
            {
                ApplyGrip(binding, grip);
            }
        }

        private bool ShouldShowDumbbells()
        {
            if (!Application.isPlaying)
            {
                return true;
            }

            if (_gameFlow == null)
            {
                _gameFlow = FindFirstObjectByType<GameFlowController>();
            }

            if (_gameFlow == null)
            {
                // A standalone avatar without the game flow retains the
                // controller's original behavior.
                return true;
            }

            return _gameFlow.CurrentPhase != ExperiencePhase.Posing &&
                   _gameFlow.CurrentPhase != ExperiencePhase.Results;
        }

        private void ApplyGrip(HandBinding binding, GripPose grip)
        {
            for (var finger = 0; finger < FingerCount; finger++)
            {
                var curl = Mathf.Clamp01(grip.Get(finger));
                var maxima = finger == ThumbSlot
                    ? _thumbMaxCurlDegrees
                    : _fingerMaxCurlDegrees;

                for (var joint = 0; joint < JointCount; joint++)
                {
                    var bone = binding.Bones[finger * JointCount + joint];
                    if (bone == null || bone.Bone == null || !bone.HasRest)
                    {
                        continue;
                    }

                    var target = bone.Rest;

                    // The swing has to happen before the curl so the curl axis
                    // is carried round with the thumb, the way the joint does.
                    if (finger == ThumbSlot &&
                        joint == 0 &&
                        Mathf.Abs(_thumbSwingDegrees) > 0.001f)
                    {
                        target *= Quaternion.AngleAxis(
                            _thumbSwingDegrees,
                            bone.SwingAxis);
                    }

                    target *= Quaternion.AngleAxis(
                        curl * JointDegrees(maxima, joint),
                        bone.CurlAxis);

                    // Writing an identical rotation would dirty the scene on
                    // every editor tick, so only real changes are pushed. On
                    // the mirror the bones do change every frame, because
                    // MirrorTransforms copies the player's fingers first.
                    if (!NearlyEqual(bone.Bone.localRotation, target))
                    {
                        bone.Bone.localRotation = target;
                    }
                }
            }
        }

        private static void CaptureFinger(HandBinding binding, int finger)
        {
            // Rotation carrying a direction from the current bone's space up
            // into the hand's space, accumulated down the chain.
            var boneToHand = Quaternion.identity;

            for (var joint = 0; joint < JointCount; joint++)
            {
                var grip = binding.Bones[finger * JointCount + joint];
                if (grip == null || grip.Bone == null)
                {
                    continue;
                }

                grip.Rest = grip.Bone.localRotation;
                boneToHand *= grip.Rest;
                var handToBone = Quaternion.Inverse(boneToHand);
                var palmAxis = handToBone * binding.PalmDirection;

                // Built from the palm and finger directions rather than from
                // the bar, because both of those are signed by anatomy - the
                // palm from which side the thumb is on - so the axis flips by
                // itself between the two hands. The bar direction does not:
                // index-to-little points the same way in both local frames,
                // which swung the right thumb backwards.
                grip.SwingAxis = handToBone * Vector3.Cross(
                    binding.PalmDirection,
                    binding.FingerDirection);

                var forward = ResolveForward(binding, finger, joint, grip.Bone);
                var axis = Vector3.Cross(forward, palmAxis);
                grip.CurlAxis = axis.sqrMagnitude < 1e-8f
                    ? Vector3.right
                    : axis.normalized;
                grip.HasRest = true;
            }
        }

        /// <summary>
        /// The direction the bone points, taken from the next joint down so a
        /// rig that rolls its finger bones differently still curls correctly.
        /// </summary>
        private static Vector3 ResolveForward(
            HandBinding binding,
            int finger,
            int joint,
            Transform bone)
        {
            Transform next = null;
            if (joint + 1 < JointCount)
            {
                var deeper = binding.Bones[finger * JointCount + joint + 1];
                if (deeper != null)
                {
                    next = deeper.Bone;
                }
            }
            else if (bone.childCount > 0)
            {
                next = bone.GetChild(0);
            }

            if (next == null || next.localPosition.sqrMagnitude < 1e-10f)
            {
                return Vector3.forward;
            }

            return next.localPosition.normalized;
        }

        private void UpdateDumbbell(
            HandBinding binding,
            DumbbellPlacement placement)
        {
            if (binding.Dumbbell == null)
            {
                if (!TrySpawnDumbbell(binding))
                {
                    return;
                }

                binding.HasAppliedPlacement = false;
            }

            var rotation = Quaternion.Euler(placement.RotationOffset);
            if (!binding.HasAppliedPlacement ||
                binding.AppliedPosition != placement.PositionOffset ||
                binding.AppliedScale != placement.Scale ||
                !NearlyEqual(binding.AppliedRotation, rotation))
            {
                binding.Dumbbell.localPosition = placement.PositionOffset;
                binding.Dumbbell.localRotation = rotation;
                binding.Dumbbell.localScale = placement.Scale;
                binding.AppliedPosition = placement.PositionOffset;
                binding.AppliedRotation = rotation;
                binding.AppliedScale = placement.Scale;
                binding.HasAppliedPlacement = true;
            }

            SyncPresentation(binding);
        }

        private bool TrySpawnDumbbell(HandBinding binding)
        {
            if (_dumbbellPrefab == null)
            {
                WarnOnce(
                    ref _warnedNoPrefab,
                    "has no dumbbell prefab assigned, so only the grip pose is " +
                    "applied.");
                return false;
            }

            var instance = Instantiate(_dumbbellPrefab, binding.Hand);
            instance.name = _dumbbellPrefab.name;

            // Presentation only: nothing here should ever collide, be grabbed
            // or be simulated. The lights in this project are routed by layer,
            // so the dumbbell also has to sit on the same layer as the hand
            // carrying it or it would be lit by the wrong rig.
            StripPhysics(instance);
            SetLayerRecursively(instance, binding.Hand.gameObject.layer);

            if (!Application.isPlaying)
            {
                foreach (var child in
                         instance.GetComponentsInChildren<Transform>(true))
                {
                    child.gameObject.hideFlags = HideFlags.HideAndDontSave;
                }
            }

            binding.Dumbbell = instance.transform;
            return true;
        }

        private void SyncPresentation(HandBinding binding)
        {
            if (_visibilitySource == null || binding.Dumbbell == null)
            {
                return;
            }

            var visible = _visibilitySource.enabled &&
                          _visibilitySource.gameObject.activeInHierarchy;
            var material = _visibilitySource.sharedMaterial;

            foreach (var renderer in
                     binding.Dumbbell.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = visible;
                if (!_matchSourceMaterial || material == null)
                {
                    continue;
                }

                if (renderer.sharedMaterial != material)
                {
                    renderer.sharedMaterial = material;
                }

                if (_propertyBlock == null)
                {
                    _propertyBlock = new MaterialPropertyBlock();
                }

                _visibilitySource.GetPropertyBlock(_propertyBlock);
                renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        private SkinnedMeshRenderer FindBodyRenderer()
        {
            SkinnedMeshRenderer best = null;
            var bestCount = -1;
            foreach (var renderer in
                     GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var count = renderer.sharedMesh == null
                    ? 0
                    : renderer.sharedMesh.blendShapeCount;
                if (count <= bestCount)
                {
                    continue;
                }

                best = renderer;
                bestCount = count;
            }

            return best;
        }

        private static void ReleaseHand(HandBinding binding)
        {
            if (!binding.IsValid)
            {
                return;
            }

            foreach (var bone in binding.Bones)
            {
                if (bone == null || bone.Bone == null || !bone.HasRest)
                {
                    continue;
                }

                if (!NearlyEqual(bone.Bone.localRotation, bone.Rest))
                {
                    bone.Bone.localRotation = bone.Rest;
                }
            }
        }

        private void DestroyDumbbells()
        {
            DestroyDumbbell(_left);
            DestroyDumbbell(_right);
        }

        private static void DestroyDumbbell(HandBinding binding)
        {
            if (binding.Dumbbell == null)
            {
                return;
            }

            var doomed = binding.Dumbbell.gameObject;
            binding.Dumbbell = null;
            binding.HasAppliedPlacement = false;
            if (Application.isPlaying)
            {
                Destroy(doomed);
            }
            else
            {
                DestroyImmediate(doomed);
            }
        }

        private static void StripPhysics(GameObject instance)
        {
            foreach (var collider in
                     instance.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }

            foreach (var body in
                     instance.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private static float JointDegrees(Vector3 maxima, int joint)
        {
            switch (joint)
            {
                case 0: return maxima.x;
                case 1: return maxima.y;
                default: return maxima.z;
            }
        }

        private static bool NearlyEqual(Quaternion a, Quaternion b)
        {
            return Mathf.Abs(Quaternion.Dot(a, b)) > 0.9999995f;
        }

        private void WarnOnce(ref bool alreadyWarned, string message)
        {
            if (alreadyWarned)
            {
                return;
            }

            alreadyWarned = true;
            Debug.LogWarning(
                $"[DumbbellGripVisualController] '{name}' {message}",
                this);
        }

        private static HumanBodyBones FingerBone(
            bool isLeft,
            int finger,
            int joint)
        {
            return isLeft
                ? LeftFingerBones[finger * JointCount + joint]
                : RightFingerBones[finger * JointCount + joint];
        }

        private static readonly HumanBodyBones[] LeftFingerBones =
        {
            HumanBodyBones.LeftThumbProximal,
            HumanBodyBones.LeftThumbIntermediate,
            HumanBodyBones.LeftThumbDistal,
            HumanBodyBones.LeftIndexProximal,
            HumanBodyBones.LeftIndexIntermediate,
            HumanBodyBones.LeftIndexDistal,
            HumanBodyBones.LeftMiddleProximal,
            HumanBodyBones.LeftMiddleIntermediate,
            HumanBodyBones.LeftMiddleDistal,
            HumanBodyBones.LeftRingProximal,
            HumanBodyBones.LeftRingIntermediate,
            HumanBodyBones.LeftRingDistal,
            HumanBodyBones.LeftLittleProximal,
            HumanBodyBones.LeftLittleIntermediate,
            HumanBodyBones.LeftLittleDistal
        };

        private static readonly HumanBodyBones[] RightFingerBones =
        {
            HumanBodyBones.RightThumbProximal,
            HumanBodyBones.RightThumbIntermediate,
            HumanBodyBones.RightThumbDistal,
            HumanBodyBones.RightIndexProximal,
            HumanBodyBones.RightIndexIntermediate,
            HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleProximal,
            HumanBodyBones.RightMiddleIntermediate,
            HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightRingProximal,
            HumanBodyBones.RightRingIntermediate,
            HumanBodyBones.RightRingDistal,
            HumanBodyBones.RightLittleProximal,
            HumanBodyBones.RightLittleIntermediate,
            HumanBodyBones.RightLittleDistal
        };

        /// <summary>
        /// Places both dumbbells by measuring the closed hand: the bar is laid
        /// along the knuckle line and dropped into the middle of the tunnel the
        /// curled fingers make against the palm. Because it measures the grip
        /// rather than guessing at it, running this again after changing the
        /// curls moves the dumbbells to match. A good starting point, not a
        /// replacement for looking at the result.
        /// </summary>
        [ContextMenu("Auto-Fit Dumbbells To Hands")]
        public void AutoFitDumbbells()
        {
            Bind();

            // The hand has to actually be closed to be measured.
            var wasOpen = !Application.isPlaying && !_applyGripInEditMode;
            ApplyGrip(_left, _leftGrip);
            ApplyGrip(_right, _rightGrip);

            AutoFitHand(_left, _leftDumbbell);
            AutoFitHand(_right, _rightDumbbell);

            if (wasOpen)
            {
                ReleaseGrip();
            }
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        private static void AutoFitHand(
            HandBinding binding,
            DumbbellPlacement placement)
        {
            if (!binding.IsValid)
            {
                return;
            }

            var knuckles = Vector3.zero;
            var tips = Vector3.zero;
            var counted = 0;

            // The thumb is deliberately left out: it closes over the bar from
            // the side and would drag the centre out of the palm.
            for (var finger = ThumbSlot + 1; finger < FingerCount; finger++)
            {
                var proximal = binding.Bones[finger * JointCount];
                var distal =
                    binding.Bones[finger * JointCount + JointCount - 1];
                if (proximal == null ||
                    proximal.Bone == null ||
                    distal == null ||
                    distal.Bone == null)
                {
                    continue;
                }

                var tip = distal.Bone.childCount > 0
                    ? distal.Bone.GetChild(0)
                    : distal.Bone;
                knuckles += binding.Hand.InverseTransformPoint(
                    proximal.Bone.position);
                tips += binding.Hand.InverseTransformPoint(tip.position);
                counted++;
            }

            if (counted == 0)
            {
                return;
            }

            placement.PositionOffset = (knuckles + tips) / (counted * 2f);
            placement.RotationOffset = Quaternion.LookRotation(
                    Vector3.Cross(binding.BarDirection, binding.PalmDirection),
                    binding.PalmDirection)
                .eulerAngles;
        }
    }
}
