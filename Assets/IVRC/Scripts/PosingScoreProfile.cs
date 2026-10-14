using System;
using UnityEngine;

namespace IVRC
{
    public enum PoseScaleBasis
    {
        HmdHeight,
        ArmLength
    }

    /// <summary>
    /// Presentation-only elbow poles for the inferred arms. The headset and
    /// hands cannot measure elbows, so these hints make the reference avatar
    /// read as the intended bodybuilding pose without changing what is scored.
    /// Values are offsets from each shoulder in the stage frame, normalized by
    /// body height.
    /// </summary>
    [Serializable]
    public struct PoseGuideElbowHints
    {
        public Vector3 LeftPoleFromShoulderNormalized;
        public Vector3 RightPoleFromShoulderNormalized;
    }

    [CreateAssetMenu(
        fileName = "PosingScoreProfile",
        menuName = "IVRC/Posing Score Profile")]
    public sealed class PosingScoreProfile : ScriptableObject
    {
        [Header("Presentation")]
        [SerializeField] private string _displayName =
            "FRONT DOUBLE BICEPS";
        [SerializeField] private string _instruction =
            "HANDS BESIDE HEAD - ELBOWS WIDE";
        [SerializeField, Min(0f)] private float _countdownSeconds = 3f;

        [Header("Target Pose")]
        [SerializeField] private PosingScoreTarget _target =
            new PosingScoreTarget
            {
                LeftHandPositionFromHeadNormalized =
                    new Vector3(-0.3f, -0.02f, 0.05f),
                RightHandPositionFromHeadNormalized =
                    new Vector3(0.3f, -0.02f, 0.05f),
                HeadEulerDegreesInStageFrame = Vector3.zero,
                LeftHandEulerDegreesInStageFrame = Vector3.zero,
                RightHandEulerDegreesInStageFrame = Vector3.zero
            };

        [Header("Reference Avatar Elbows")]
        [Tooltip(
            "Presentation only. These poles choose how the inferred elbows " +
            "bend; scoring still uses only the headset and controllers.")]
        [SerializeField] private PoseGuideElbowHints _guideElbows =
            new PoseGuideElbowHints
            {
                LeftPoleFromShoulderNormalized =
                    new Vector3(-0.3f, 0f, -0.05f),
                RightPoleFromShoulderNormalized =
                    new Vector3(0.3f, 0f, -0.05f)
            };

        [Header("Body Scale")]
        [SerializeField] private PoseScaleBasis _scaleBasis =
            PoseScaleBasis.HmdHeight;
        [SerializeField, Min(0.01f)]
        private float _minimumBodyScaleMeters = 0.25f;

        [Header("Perfect / Miss Tolerances")]
        [SerializeField] private PosingScoreTolerances _tolerances =
            new PosingScoreTolerances
            {
                PositionPerfect = 0.025f,
                PositionMiss = 0.3f,
                RotationPerfectDegrees = 5f,
                RotationMissDegrees = 60f,
                SymmetryPerfect = 0.025f,
                SymmetryMiss = 0.3f
            };

        [Header("Hand Rotation")]
        [Tooltip(
            "Off until real controller rotations have been captured on the " +
            "headset. The rotation targets below default to identity, and " +
            "scoring against identity asks the player to hold the " +
            "controllers unrotated, which no bodybuilding pose does. While " +
            "this is off the rotation weight is dropped and its share goes " +
            "to position and symmetry.")]
        [SerializeField] private bool _scoreHandRotation;

        [Header("Score Weights")]
        [SerializeField] private PosingScoreWeights _weights =
            new PosingScoreWeights
            {
                Position = 1f,
                Rotation = 1f,
                Symmetry = 1f,
                Hold = 1f
            };

        [Header("Hold")]
        [SerializeField] private PosingHoldSettings _hold =
            new PosingHoldSettings
            {
                RequiredSeconds = 2f,
                MinimumSpatialScore = 70f,
                BreakGraceSeconds = 0.2f
            };

        public string DisplayName =>
            string.IsNullOrWhiteSpace(_displayName)
                ? name.ToUpperInvariant()
                : _displayName;

        public string Instruction =>
            string.IsNullOrWhiteSpace(_instruction)
                ? "MATCH THE TARGET"
                : _instruction;

        public float CountdownSeconds =>
            Mathf.Max(0f, _countdownSeconds);

        public bool ScoresHandRotation => _scoreHandRotation;

        public PoseGuideElbowHints GuideElbows => _guideElbows;

        public PosingScoreSettings ToSettings()
        {
            // The rotation weight is kept: head rotation is still graded,
            // because "face the audience" is an authored target. Only the
            // unset hand targets are dropped.
            return new PosingScoreSettings(
                _scaleBasis,
                _minimumBodyScaleMeters,
                _target,
                _tolerances,
                _weights,
                _hold,
                _scoreHandRotation);
        }

        private void OnValidate()
        {
            _displayName = _displayName?.Trim();
            _instruction = _instruction?.Trim();
            _countdownSeconds = Mathf.Max(
                0f,
                _countdownSeconds);
            _minimumBodyScaleMeters = Mathf.Max(
                0.01f,
                _minimumBodyScaleMeters);
            _guideElbows.LeftPoleFromShoulderNormalized =
                SanitizeVector(
                    _guideElbows
                        .LeftPoleFromShoulderNormalized);
            _guideElbows.RightPoleFromShoulderNormalized =
                SanitizeVector(
                    _guideElbows
                        .RightPoleFromShoulderNormalized);

            _tolerances.PositionPerfect = Mathf.Max(
                0f,
                _tolerances.PositionPerfect);
            _tolerances.PositionMiss = Mathf.Max(
                _tolerances.PositionPerfect + 0.0001f,
                _tolerances.PositionMiss);
            _tolerances.RotationPerfectDegrees = Mathf.Clamp(
                _tolerances.RotationPerfectDegrees,
                0f,
                180f);
            _tolerances.RotationMissDegrees = Mathf.Clamp(
                Mathf.Max(
                    _tolerances.RotationPerfectDegrees + 0.01f,
                    _tolerances.RotationMissDegrees),
                0.01f,
                180f);
            _tolerances.SymmetryPerfect = Mathf.Max(
                0f,
                _tolerances.SymmetryPerfect);
            _tolerances.SymmetryMiss = Mathf.Max(
                _tolerances.SymmetryPerfect + 0.0001f,
                _tolerances.SymmetryMiss);

            _weights.Position = Mathf.Max(0f, _weights.Position);
            _weights.Rotation = Mathf.Max(0f, _weights.Rotation);
            _weights.Symmetry = Mathf.Max(0f, _weights.Symmetry);
            _weights.Hold = Mathf.Max(0f, _weights.Hold);
            if (_weights.Position +
                _weights.Rotation +
                _weights.Symmetry <= Mathf.Epsilon)
            {
                _weights.Position = 1f;
            }

            _hold.RequiredSeconds = Mathf.Max(
                0f,
                _hold.RequiredSeconds);
            _hold.MinimumSpatialScore = Mathf.Clamp(
                _hold.MinimumSpatialScore,
                0f,
                100f);
            _hold.BreakGraceSeconds = Mathf.Max(
                0f,
                _hold.BreakGraceSeconds);
        }

        private static Vector3 SanitizeVector(Vector3 value)
        {
            return new Vector3(
                Sanitize(value.x),
                Sanitize(value.y),
                Sanitize(value.z));
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : value;
        }
    }
}
