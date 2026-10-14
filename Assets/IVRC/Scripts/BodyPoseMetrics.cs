using Meta.XR.Movement.Retargeting;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Calculates avatar-independent body metrics from a retargeted Humanoid Animator.
    /// The values remain reusable when the temporary avatar is replaced by the final FBX.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class BodyPoseMetrics : MonoBehaviour
    {
        private const float MinimumSegmentLengthSquared = 0.000001f;

        [Header("Pose Source")]
        [SerializeField] private Animator _animator;
        [SerializeField] private CharacterRetargeter _retargeter;

        [Header("Filtering")]
        [SerializeField, Min(0f)] private float _smoothingSpeed = 12f;

        private Transform _hips;
        private Transform _leftUpperArm;
        private Transform _leftLowerArm;

        /// <summary>
        /// Shoulder and elbow of each arm, for anything that needs to record
        /// where the solver put them. Null until the rig has resolved.
        /// </summary>
        public Transform LeftShoulder => _leftUpperArm;
        public Transform LeftElbow => _leftLowerArm;
        private Transform _leftHand;
        private Transform _rightUpperArm;
        private Transform _rightLowerArm;

        public Transform RightShoulder => _rightUpperArm;
        public Transform RightElbow => _rightLowerArm;
        private Transform _rightHand;
        private bool _hasSmoothedSample;

        public bool HasRequiredBones { get; private set; }
        public bool HasPose { get; private set; }
        public bool IsTrackingValid =>
            HasPose &&
            (_retargeter == null || (_retargeter.isActiveAndEnabled && _retargeter.IsValid));

        /// <summary>Elbow flexion where a straight arm is 0 degrees.</summary>
        public float LeftElbowFlexion { get; private set; }

        /// <summary>Elbow flexion where a straight arm is 0 degrees.</summary>
        public float RightElbowFlexion { get; private set; }

        /// <summary>Arm elevation where arm-down is 0, T-pose is 90, and overhead is 180 degrees.</summary>
        public float LeftArmRaise { get; private set; }

        /// <summary>Arm elevation where arm-down is 0, T-pose is 90, and overhead is 180 degrees.</summary>
        public float RightArmRaise { get; private set; }

        public float ElbowSymmetryDifference =>
            Mathf.Abs(LeftElbowFlexion - RightElbowFlexion);

        public float ArmRaiseSymmetryDifference =>
            Mathf.Abs(LeftArmRaise - RightArmRaise);

        private void Reset()
        {
            ResolveReferences();
            CacheBones();
        }

        private void Awake()
        {
            ResolveReferences();
            CacheBones();
        }

        private void OnEnable()
        {
            _hasSmoothedSample = false;
            HasPose = false;
        }

        private void OnValidate()
        {
            _smoothingSpeed = Mathf.Max(0f, _smoothingSpeed);
            ResolveReferences();
            CacheBones();
        }

        private void LateUpdate()
        {
            if (!HasRequiredBones)
            {
                ResolveReferences();
                CacheBones();
            }

            if (!TryCalculateRawMetrics(
                    out var leftElbow,
                    out var rightElbow,
                    out var leftRaise,
                    out var rightRaise))
            {
                HasPose = false;
                return;
            }

            HasPose = true;

            if (!_hasSmoothedSample || _smoothingSpeed <= 0f)
            {
                LeftElbowFlexion = leftElbow;
                RightElbowFlexion = rightElbow;
                LeftArmRaise = leftRaise;
                RightArmRaise = rightRaise;
                _hasSmoothedSample = true;
                return;
            }

            var blend = 1f - Mathf.Exp(-_smoothingSpeed * Time.unscaledDeltaTime);
            LeftElbowFlexion = Mathf.Lerp(LeftElbowFlexion, leftElbow, blend);
            RightElbowFlexion = Mathf.Lerp(RightElbowFlexion, rightElbow, blend);
            LeftArmRaise = Mathf.Lerp(LeftArmRaise, leftRaise, blend);
            RightArmRaise = Mathf.Lerp(RightArmRaise, rightRaise, blend);
        }

        /// <summary>
        /// Rebinds pose sampling to the active Humanoid and refreshes every
        /// cached bone Transform.
        /// </summary>
        public void BindAvatar(
            Animator animator,
            CharacterRetargeter retargeter)
        {
            _animator = animator;
            _retargeter = retargeter;
            _hasSmoothedSample = false;
            HasPose = false;
            CacheBones();
        }

        private void ResolveReferences()
        {
            if (_animator == null)
            {
                _animator = GetComponent<Animator>();
            }

            if (_retargeter == null)
            {
                _retargeter = GetComponent<CharacterRetargeter>();
            }
        }

        private void CacheBones()
        {
            HasRequiredBones = false;

            if (_animator == null || !_animator.isHuman || _animator.avatar == null)
            {
                return;
            }

            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            _leftUpperArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _leftLowerArm = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _rightUpperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _rightLowerArm = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);

            HasRequiredBones =
                _hips != null &&
                _leftUpperArm != null &&
                _leftLowerArm != null &&
                _leftHand != null &&
                _rightUpperArm != null &&
                _rightLowerArm != null &&
                _rightHand != null;
        }

        private bool TryCalculateRawMetrics(
            out float leftElbow,
            out float rightElbow,
            out float leftRaise,
            out float rightRaise)
        {
            leftElbow = 0f;
            rightElbow = 0f;
            leftRaise = 0f;
            rightRaise = 0f;

            if (!HasRequiredBones)
            {
                return false;
            }

            var shoulderCenter = (_leftUpperArm.position + _rightUpperArm.position) * 0.5f;
            var torsoDown = _hips.position - shoulderCenter;
            var leftUpperArm = _leftLowerArm.position - _leftUpperArm.position;
            var rightUpperArm = _rightLowerArm.position - _rightUpperArm.position;
            var leftForearmFromElbow = _leftHand.position - _leftLowerArm.position;
            var rightForearmFromElbow = _rightHand.position - _rightLowerArm.position;
            var leftUpperArmFromElbow = _leftUpperArm.position - _leftLowerArm.position;
            var rightUpperArmFromElbow = _rightUpperArm.position - _rightLowerArm.position;

            if (!HasLength(torsoDown) ||
                !HasLength(leftUpperArm) ||
                !HasLength(rightUpperArm) ||
                !HasLength(leftForearmFromElbow) ||
                !HasLength(rightForearmFromElbow))
            {
                return false;
            }

            leftElbow = CalculateElbowFlexion(leftUpperArmFromElbow, leftForearmFromElbow);
            rightElbow = CalculateElbowFlexion(rightUpperArmFromElbow, rightForearmFromElbow);
            leftRaise = CalculateArmRaise(torsoDown, leftUpperArm);
            rightRaise = CalculateArmRaise(torsoDown, rightUpperArm);
            return true;
        }

        public static float CalculateElbowFlexion(
            Vector3 upperArmDirectionFromElbow,
            Vector3 forearmDirectionFromElbow)
        {
            return Mathf.Clamp(
                180f - Vector3.Angle(upperArmDirectionFromElbow, forearmDirectionFromElbow),
                0f,
                180f);
        }

        public static float CalculateArmRaise(Vector3 torsoDown, Vector3 upperArmFromShoulder)
        {
            return Vector3.Angle(torsoDown, upperArmFromShoulder);
        }

        private static bool HasLength(Vector3 value)
        {
            return value.sqrMagnitude > MinimumSegmentLengthSquared;
        }
    }
}
