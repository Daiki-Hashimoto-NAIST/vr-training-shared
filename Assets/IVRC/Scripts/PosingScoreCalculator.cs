using System;
using UnityEngine;

namespace IVRC
{
    [Serializable]
    public struct PosingScoreTarget
    {
        public Vector3 LeftHandPositionFromHeadNormalized;
        public Vector3 RightHandPositionFromHeadNormalized;
        public Vector3 HeadEulerDegreesInStageFrame;
        public Vector3 LeftHandEulerDegreesInStageFrame;
        public Vector3 RightHandEulerDegreesInStageFrame;
    }

    [Serializable]
    public struct PosingScoreTolerances
    {
        [Min(0f)] public float PositionPerfect;
        [Min(0f)] public float PositionMiss;
        [Range(0f, 180f)] public float RotationPerfectDegrees;
        [Range(0f, 180f)] public float RotationMissDegrees;
        [Min(0f)] public float SymmetryPerfect;
        [Min(0f)] public float SymmetryMiss;
    }

    [Serializable]
    public struct PosingScoreWeights
    {
        [Min(0f)] public float Position;
        [Min(0f)] public float Rotation;
        [Min(0f)] public float Symmetry;
        [Min(0f)] public float Hold;
    }

    [Serializable]
    public struct PosingHoldSettings
    {
        [Min(0f)] public float RequiredSeconds;
        [Range(0f, 100f)] public float MinimumSpatialScore;
        [Min(0f)] public float BreakGraceSeconds;
    }

    public readonly struct PosingScoreSettings
    {
        public PosingScoreSettings(
            PoseScaleBasis scaleBasis,
            float minimumBodyScaleMeters,
            PosingScoreTarget target,
            PosingScoreTolerances tolerances,
            PosingScoreWeights weights,
            PosingHoldSettings hold)
            : this(
                scaleBasis,
                minimumBodyScaleMeters,
                target,
                tolerances,
                weights,
                hold,
                true)
        {
        }

        public PosingScoreSettings(
            PoseScaleBasis scaleBasis,
            float minimumBodyScaleMeters,
            PosingScoreTarget target,
            PosingScoreTolerances tolerances,
            PosingScoreWeights weights,
            PosingHoldSettings hold,
            bool scoreHandRotation)
        {
            ScaleBasis = scaleBasis;
            MinimumBodyScaleMeters = minimumBodyScaleMeters;
            Target = target;
            Tolerances = tolerances;
            Weights = weights;
            Hold = hold;
            ScoreHandRotation = scoreHandRotation;
        }

        public PoseScaleBasis ScaleBasis { get; }
        public float MinimumBodyScaleMeters { get; }
        public PosingScoreTarget Target { get; }
        public PosingScoreTolerances Tolerances { get; }
        public PosingScoreWeights Weights { get; }
        public PosingHoldSettings Hold { get; }

        /// <summary>
        /// Whether the hand rotation targets are real. Head rotation is
        /// always graded: its target means "face the audience", which is
        /// authored and meaningful. The hand targets default to identity,
        /// and grading against identity would demand unrotated controllers,
        /// which no pose uses.
        /// </summary>
        public bool ScoreHandRotation { get; }
    }

    public readonly struct ThreePointPoseSample
    {
        public ThreePointPoseSample(
            Pose head,
            Pose leftHand,
            Pose rightHand,
            bool headTracked = true,
            bool leftHandTracked = true,
            bool rightHandTracked = true)
        {
            Head = head;
            LeftHand = leftHand;
            RightHand = rightHand;
            HeadTracked = headTracked;
            LeftHandTracked = leftHandTracked;
            RightHandTracked = rightHandTracked;
        }

        public Pose Head { get; }
        public Pose LeftHand { get; }
        public Pose RightHand { get; }
        public bool HeadTracked { get; }
        public bool LeftHandTracked { get; }
        public bool RightHandTracked { get; }
        public bool IsFullyTracked =>
            HeadTracked && LeftHandTracked && RightHandTracked;
    }

    public readonly struct PosingBodyScales
    {
        public PosingBodyScales(
            float hmdHeightMeters,
            float armLengthMeters)
        {
            HmdHeightMeters = hmdHeightMeters;
            ArmLengthMeters = armLengthMeters;
        }

        public float HmdHeightMeters { get; }
        public float ArmLengthMeters { get; }
    }

    public readonly struct PoseHoldState
    {
        public PoseHoldState(
            float heldSeconds,
            float brokenSeconds)
        {
            HeldSeconds = heldSeconds;
            BrokenSeconds = brokenSeconds;
        }

        public float HeldSeconds { get; }
        public float BrokenSeconds { get; }
    }

    public readonly struct PoseScoreResult
    {
        public PoseScoreResult(
            bool isValid,
            PoseScaleBasis appliedScaleBasis,
            float bodyScaleMeters,
            float positionScore,
            float rotationScore,
            float symmetryScore,
            float holdScore,
            float spatialScore,
            float totalScore,
            float heldSeconds,
            bool isHolding)
        {
            IsValid = isValid;
            AppliedScaleBasis = appliedScaleBasis;
            BodyScaleMeters = bodyScaleMeters;
            PositionScore = positionScore;
            RotationScore = rotationScore;
            SymmetryScore = symmetryScore;
            HoldScore = holdScore;
            SpatialScore = spatialScore;
            TotalScore = totalScore;
            HeldSeconds = heldSeconds;
            IsHolding = isHolding;
        }

        public bool IsValid { get; }
        public PoseScaleBasis AppliedScaleBasis { get; }
        public float BodyScaleMeters { get; }
        public float PositionScore { get; }
        public float RotationScore { get; }
        public float SymmetryScore { get; }
        public float HoldScore { get; }
        public float SpatialScore { get; }
        public float TotalScore { get; }
        public float HeldSeconds { get; }
        public bool IsHolding { get; }
    }

    /// <summary>
    /// Stateless three-point pose scoring. All temporal state and elapsed time
    /// are explicit arguments so the calculation remains deterministic in tests.
    /// </summary>
    public static class PosingScoreCalculator
    {
        private const float MinimumQuaternionMagnitudeSquared = 0.000001f;
        private const float MinimumToleranceSeparation = 0.000001f;

        /// <summary>
        /// Turns one observed sample into the form a target is written in:
        /// hands relative to the head, divided by body scale, with the stage
        /// yaw taken out, and rotations expressed in that same stage frame.
        ///
        /// Scoring reads a target through this exact transform, so capturing
        /// one through it too is what makes "stand in the pose and record it"
        /// mean the same thing as "score well". Any second implementation of
        /// this arithmetic would be free to drift from the first, which is the
        /// whole failure being fixed: the targets in the profiles are typed-in
        /// round numbers, and a real body holding the pose correctly does not
        /// report them.
        /// </summary>
        public static PosingScoreTarget BuildTarget(
            in ThreePointPoseSample sample,
            float stageYawDegrees,
            float bodyScale)
        {
            bodyScale = IsFinite(bodyScale) && bodyScale > 0.01f
                ? bodyScale
                : 1f;
            var inverseStageRotation = Quaternion.Inverse(
                Quaternion.Euler(
                    0f,
                    IsFinite(stageYawDegrees) ? stageYawDegrees : 0f,
                    0f));

            return new PosingScoreTarget
            {
                LeftHandPositionFromHeadNormalized =
                    inverseStageRotation *
                    (sample.LeftHand.position - sample.Head.position) /
                    bodyScale,
                RightHandPositionFromHeadNormalized =
                    inverseStageRotation *
                    (sample.RightHand.position - sample.Head.position) /
                    bodyScale,
                HeadEulerDegreesInStageFrame =
                    (inverseStageRotation * sample.Head.rotation)
                    .eulerAngles,
                LeftHandEulerDegreesInStageFrame =
                    (inverseStageRotation * sample.LeftHand.rotation)
                    .eulerAngles,
                RightHandEulerDegreesInStageFrame =
                    (inverseStageRotation * sample.RightHand.rotation)
                    .eulerAngles
            };
        }

        public static PoseScoreResult Evaluate(
            in ThreePointPoseSample sample,
            float stageYawDegrees,
            in PosingBodyScales bodyScales,
            in PosingScoreSettings settings,
            in PoseHoldState previousHold,
            float deltaTime,
            out PoseHoldState nextHold)
        {
            var safeDeltaTime = IsFinite(deltaTime)
                ? Mathf.Max(0f, deltaTime)
                : 0f;
            var minimumScale = IsFinite(settings.MinimumBodyScaleMeters)
                ? Mathf.Max(0.01f, settings.MinimumBodyScaleMeters)
                : 0.01f;

            if (!sample.IsFullyTracked ||
                !IsFinite(stageYawDegrees) ||
                !IsValidPose(sample.Head) ||
                !IsValidPose(sample.LeftHand) ||
                !IsValidPose(sample.RightHand) ||
                !TryResolveBodyScale(
                    settings.ScaleBasis,
                    bodyScales,
                    minimumScale,
                    out var bodyScale,
                    out var appliedScaleBasis))
            {
                nextHold = AdvanceHold(
                    previousHold,
                    false,
                    safeDeltaTime,
                    settings.Hold);
                var invalidHoldScore = CalculateHoldScore(
                    nextHold.HeldSeconds,
                    settings.Hold.RequiredSeconds);
                return new PoseScoreResult(
                    false,
                    settings.ScaleBasis,
                    0f,
                    0f,
                    0f,
                    0f,
                    invalidHoldScore,
                    0f,
                    0f,
                    nextHold.HeldSeconds,
                    false);
            }

            var stageRotation =
                Quaternion.Euler(0f, stageYawDegrees, 0f);
            var inverseStageRotation =
                Quaternion.Inverse(stageRotation);
            var leftPosition =
                inverseStageRotation *
                (sample.LeftHand.position - sample.Head.position) /
                bodyScale;
            var rightPosition =
                inverseStageRotation *
                (sample.RightHand.position - sample.Head.position) /
                bodyScale;

            var target = settings.Target;
            var positionError =
                (Vector3.Distance(
                     leftPosition,
                     target.LeftHandPositionFromHeadNormalized) +
                 Vector3.Distance(
                     rightPosition,
                     target.RightHandPositionFromHeadNormalized)) *
                0.5f;

            var headRotation = NormalizeQuaternion(
                inverseStageRotation * sample.Head.rotation);
            var leftRotation = NormalizeQuaternion(
                inverseStageRotation * sample.LeftHand.rotation);
            var rightRotation = NormalizeQuaternion(
                inverseStageRotation * sample.RightHand.rotation);
            var headRotationError = Quaternion.Angle(
                headRotation,
                Quaternion.Euler(
                    target.HeadEulerDegreesInStageFrame));
            var rotationError = settings.ScoreHandRotation
                ? (headRotationError +
                   Quaternion.Angle(
                       leftRotation,
                       Quaternion.Euler(
                           target
                               .LeftHandEulerDegreesInStageFrame)) +
                   Quaternion.Angle(
                       rightRotation,
                       Quaternion.Euler(
                           target
                               .RightHandEulerDegreesInStageFrame))) /
                  3f
                : headRotationError;

            var currentSymmetryRelation =
                rightPosition - MirrorAcrossSagittalPlane(leftPosition);
            var targetSymmetryRelation =
                target.RightHandPositionFromHeadNormalized -
                MirrorAcrossSagittalPlane(
                    target.LeftHandPositionFromHeadNormalized);
            var symmetryError = Vector3.Distance(
                currentSymmetryRelation,
                targetSymmetryRelation);

            var tolerances = settings.Tolerances;
            var positionScore = ScoreError(
                positionError,
                tolerances.PositionPerfect,
                tolerances.PositionMiss);
            var rotationScore = ScoreError(
                rotationError,
                tolerances.RotationPerfectDegrees,
                tolerances.RotationMissDegrees);
            var symmetryScore = ScoreError(
                symmetryError,
                tolerances.SymmetryPerfect,
                tolerances.SymmetryMiss);

            var weights = settings.Weights;
            var spatialScore = WeightedAverage(
                positionScore,
                weights.Position,
                rotationScore,
                weights.Rotation,
                symmetryScore,
                weights.Symmetry);
            var holdThreshold = IsFinite(
                settings.Hold.MinimumSpatialScore)
                ? Mathf.Clamp(
                    settings.Hold.MinimumSpatialScore,
                    0f,
                    100f)
                : 100f;
            var hasSpatialWeight =
                SanitizeWeight(weights.Position) +
                SanitizeWeight(weights.Rotation) +
                SanitizeWeight(weights.Symmetry) >
                Mathf.Epsilon;
            var isHolding =
                hasSpatialWeight &&
                spatialScore >= holdThreshold;
            nextHold = AdvanceHold(
                previousHold,
                isHolding,
                safeDeltaTime,
                settings.Hold);
            var holdScore = CalculateHoldScore(
                nextHold.HeldSeconds,
                settings.Hold.RequiredSeconds);
            var totalScore = WeightedAverage(
                positionScore,
                weights.Position,
                rotationScore,
                weights.Rotation,
                symmetryScore,
                weights.Symmetry,
                holdScore,
                weights.Hold);

            return new PoseScoreResult(
                true,
                appliedScaleBasis,
                bodyScale,
                positionScore,
                rotationScore,
                symmetryScore,
                holdScore,
                spatialScore,
                totalScore,
                nextHold.HeldSeconds,
                isHolding);
        }

        /// <summary>
        /// The body scale a capture should divide by, chosen the same way
        /// scoring chooses it. A reference recorded against a different scale
        /// than it is later scored against would be wrong by that ratio for
        /// everyone.
        /// </summary>
        public static bool TryResolveCaptureScale(
            PosingScoreProfile profile,
            in PosingBodyScales bodyScales,
            out float scale)
        {
            if (profile == null)
            {
                scale = 0f;
                return false;
            }

            var settings = profile.ToSettings();
            var minimumScale =
                IsFinite(settings.MinimumBodyScaleMeters)
                    ? Mathf.Max(0.01f, settings.MinimumBodyScaleMeters)
                    : 0.01f;
            return TryResolveBodyScale(
                settings.ScaleBasis,
                bodyScales,
                minimumScale,
                out scale,
                out _);
        }

        private static bool TryResolveBodyScale(
            PoseScaleBasis preferredBasis,
            in PosingBodyScales bodyScales,
            float minimumScale,
            out float scale,
            out PoseScaleBasis appliedBasis)
        {
            var preferredScale =
                preferredBasis == PoseScaleBasis.ArmLength
                    ? bodyScales.ArmLengthMeters
                    : bodyScales.HmdHeightMeters;
            if (IsValidScale(preferredScale, minimumScale))
            {
                scale = preferredScale;
                appliedBasis = preferredBasis;
                return true;
            }

            var fallbackBasis =
                preferredBasis == PoseScaleBasis.ArmLength
                    ? PoseScaleBasis.HmdHeight
                    : PoseScaleBasis.ArmLength;
            var fallbackScale =
                fallbackBasis == PoseScaleBasis.ArmLength
                    ? bodyScales.ArmLengthMeters
                    : bodyScales.HmdHeightMeters;
            if (IsValidScale(fallbackScale, minimumScale))
            {
                scale = fallbackScale;
                appliedBasis = fallbackBasis;
                return true;
            }

            scale = 0f;
            appliedBasis = preferredBasis;
            return false;
        }

        private static PoseHoldState AdvanceHold(
            in PoseHoldState previous,
            bool qualifies,
            float deltaTime,
            in PosingHoldSettings settings)
        {
            var heldSeconds = IsFinite(previous.HeldSeconds)
                ? Mathf.Max(0f, previous.HeldSeconds)
                : 0f;
            var brokenSeconds = IsFinite(previous.BrokenSeconds)
                ? Mathf.Max(0f, previous.BrokenSeconds)
                : 0f;
            var requiredSeconds = IsFinite(settings.RequiredSeconds)
                ? Mathf.Max(0f, settings.RequiredSeconds)
                : 0f;

            if (qualifies)
            {
                heldSeconds += deltaTime;
                if (requiredSeconds > Mathf.Epsilon)
                {
                    heldSeconds = Mathf.Min(
                        heldSeconds,
                        requiredSeconds);
                }

                return new PoseHoldState(heldSeconds, 0f);
            }

            var graceSeconds = IsFinite(settings.BreakGraceSeconds)
                ? Mathf.Max(0f, settings.BreakGraceSeconds)
                : 0f;
            brokenSeconds += deltaTime;
            if (graceSeconds <= Mathf.Epsilon ||
                brokenSeconds >= graceSeconds)
            {
                heldSeconds = 0f;
            }

            return new PoseHoldState(
                heldSeconds,
                brokenSeconds);
        }

        private static float CalculateHoldScore(
            float heldSeconds,
            float requiredSeconds)
        {
            if (!IsFinite(requiredSeconds) ||
                requiredSeconds <= Mathf.Epsilon)
            {
                return 100f;
            }

            return ClampScore(
                heldSeconds / requiredSeconds * 100f);
        }

        private static float ScoreError(
            float error,
            float perfectTolerance,
            float missTolerance)
        {
            if (!IsFinite(error))
            {
                return 0f;
            }

            perfectTolerance = IsFinite(perfectTolerance)
                ? Mathf.Max(0f, perfectTolerance)
                : 0f;
            missTolerance = IsFinite(missTolerance)
                ? Mathf.Max(
                    perfectTolerance + MinimumToleranceSeparation,
                    missTolerance)
                : perfectTolerance + MinimumToleranceSeparation;
            return ClampScore(
                (1f - Mathf.InverseLerp(
                    perfectTolerance,
                    missTolerance,
                    Mathf.Max(0f, error))) *
                100f);
        }

        private static float WeightedAverage(
            float firstScore,
            float firstWeight,
            float secondScore,
            float secondWeight,
            float thirdScore,
            float thirdWeight)
        {
            firstWeight = SanitizeWeight(firstWeight);
            secondWeight = SanitizeWeight(secondWeight);
            thirdWeight = SanitizeWeight(thirdWeight);
            var totalWeight =
                firstWeight + secondWeight + thirdWeight;
            if (totalWeight <= Mathf.Epsilon)
            {
                return 0f;
            }

            return ClampScore(
                (firstScore * firstWeight +
                 secondScore * secondWeight +
                 thirdScore * thirdWeight) /
                totalWeight);
        }

        private static float WeightedAverage(
            float firstScore,
            float firstWeight,
            float secondScore,
            float secondWeight,
            float thirdScore,
            float thirdWeight,
            float fourthScore,
            float fourthWeight)
        {
            firstWeight = SanitizeWeight(firstWeight);
            secondWeight = SanitizeWeight(secondWeight);
            thirdWeight = SanitizeWeight(thirdWeight);
            fourthWeight = SanitizeWeight(fourthWeight);
            var totalWeight =
                firstWeight +
                secondWeight +
                thirdWeight +
                fourthWeight;
            if (totalWeight <= Mathf.Epsilon)
            {
                return 0f;
            }

            return ClampScore(
                (firstScore * firstWeight +
                 secondScore * secondWeight +
                 thirdScore * thirdWeight +
                 fourthScore * fourthWeight) /
                totalWeight);
        }

        private static Vector3 MirrorAcrossSagittalPlane(
            Vector3 value)
        {
            return new Vector3(-value.x, value.y, value.z);
        }

        private static bool IsValidPose(Pose pose)
        {
            return IsFinite(pose.position) &&
                   IsFinite(pose.rotation) &&
                   QuaternionMagnitudeSquared(pose.rotation) >=
                   MinimumQuaternionMagnitudeSquared;
        }

        private static Quaternion NormalizeQuaternion(
            Quaternion value)
        {
            var inverseMagnitude = 1f / Mathf.Sqrt(
                QuaternionMagnitudeSquared(value));
            return new Quaternion(
                value.x * inverseMagnitude,
                value.y * inverseMagnitude,
                value.z * inverseMagnitude,
                value.w * inverseMagnitude);
        }

        private static float QuaternionMagnitudeSquared(
            Quaternion value)
        {
            return value.x * value.x +
                   value.y * value.y +
                   value.z * value.z +
                   value.w * value.w;
        }

        private static bool IsValidScale(
            float value,
            float minimumScale)
        {
            return IsFinite(value) && value >= minimumScale;
        }

        private static float SanitizeWeight(float value)
        {
            return IsFinite(value) ? Mathf.Max(0f, value) : 0f;
        }

        private static float ClampScore(float value)
        {
            return IsFinite(value)
                ? Mathf.Clamp(value, 0f, 100f)
                : 0f;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
