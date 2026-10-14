using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Rebinds avatar-dependent gameplay systems after an avatar swap and
    /// invalidates calibration that belongs to the previous skeleton.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class AvatarSwapCoordinator : MonoBehaviour
    {
        [SerializeField] private AvatarRegistry _registry;

        [Header("Systems to rebind")]
        [SerializeField] private BodyPoseMetrics
            _bodyPoseMetrics;
        [SerializeField] private HeightCalibrationController
            _heightCalibration;
        [SerializeField] private AvatarCalibrationController
            _avatarCalibration;
        [SerializeField] private BodyRepCounter _repCounter;
        [SerializeField] private MuscleGrowthController
            _muscleGrowth;

        private AvatarDefinition _boundAvatar;

        private void OnEnable()
        {
            if (_registry != null)
            {
                _registry.OnAvatarChanged += HandleAvatarChanged;
            }
        }

        private void OnDisable()
        {
            if (_registry != null)
            {
                _registry.OnAvatarChanged -= HandleAvatarChanged;
            }

            _boundAvatar = null;
        }

        private void HandleAvatarChanged(
            AvatarDefinition avatar)
        {
            if (avatar == null || avatar == _boundAvatar)
            {
                return;
            }

            _boundAvatar = avatar;
            _bodyPoseMetrics?.BindAvatar(
                avatar.Animator,
                avatar.Retargeter);
            _heightCalibration?.SetRetargeters(
                avatar.Retargeter);
            _avatarCalibration?.SetRetargeters(
                avatar.Retargeter);
            _repCounter?.ResetCounts();

            avatar.GrowthOutput?.RefreshBindings();
            _muscleGrowth?.RefreshOutputs();

            Debug.Log(
                $"[AvatarSwap] Active avatar: " +
                $"{avatar.DisplayName}. " +
                "Press B and X to recalibrate.",
                avatar);
        }
    }
}
