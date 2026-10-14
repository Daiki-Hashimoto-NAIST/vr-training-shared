using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Front-view marker positions used by the lightweight pose guide.
    /// All positions are expressed in the local space of the guide UI.
    /// </summary>
    public readonly struct PoseReferenceLayout
    {
        public PoseReferenceLayout(
            Vector2 headPosition,
            Vector2 leftShoulderPosition,
            Vector2 rightShoulderPosition,
            Vector2 leftHandPosition,
            Vector2 rightHandPosition)
        {
            HeadPosition = headPosition;
            LeftShoulderPosition = leftShoulderPosition;
            RightShoulderPosition = rightShoulderPosition;
            LeftHandPosition = leftHandPosition;
            RightHandPosition = rightHandPosition;
        }

        public Vector2 HeadPosition { get; }
        public Vector2 LeftShoulderPosition { get; }
        public Vector2 RightShoulderPosition { get; }
        public Vector2 LeftHandPosition { get; }
        public Vector2 RightHandPosition { get; }
    }

    /// <summary>
    /// RectTransform values for a centered horizontal UI image used as a line.
    /// </summary>
    public readonly struct PoseReferenceLineLayout
    {
        public PoseReferenceLineLayout(
            Vector2 position,
            Vector2 size,
            float rotationDegrees)
        {
            Position = position;
            Size = size;
            RotationDegrees = rotationDegrees;
        }

        public Vector2 Position { get; }
        public Vector2 Size { get; }
        public float RotationDegrees { get; }
    }

    public enum PoseReferenceDisplayMode
    {
        /// <summary>
        /// Use the flat guide only when the primary 3D guide is unavailable.
        /// </summary>
        AutomaticFallback,

        /// <summary>
        /// Keep the flat guide visible during posing for profile tuning or
        /// an explicitly selected fallback presentation.
        /// </summary>
        AlwaysShow,

        /// <summary>
        /// Never show the flat guide.
        /// </summary>
        Hidden
    }

    public static class PoseReferenceVisibilityPolicy
    {
        public static bool ShouldShow(
            bool isPosing,
            PoseReferenceDisplayMode displayMode,
            bool primaryGuideVisible)
        {
            if (!isPosing ||
                displayMode == PoseReferenceDisplayMode.Hidden)
            {
                return false;
            }

            return displayMode == PoseReferenceDisplayMode.AlwaysShow ||
                   !primaryGuideVisible;
        }
    }

    /// <summary>
    /// Projects the configured three-point pose target into a small front-view
    /// UI guide. The visual uses only RectTransforms, so no extra camera,
    /// RenderTexture, or avatar asset is required.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(10460)]
    public sealed class PoseReferenceGuide : MonoBehaviour
    {
        private const float MinimumLineLength = 0.01f;

        [Header("Target")]
        [Tooltip(
            "The guide reads the pose being scored right now rather than " +
            "holding its own copy, so the picture cannot drift out of step " +
            "with what the scorer is grading.")]
        [SerializeField] private ThreePointPosingScorer _posingScorer;

        [Header("Visibility")]
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private GameObject _visualRoot;
        [Tooltip(
            "The normal player-facing guide. In AutomaticFallback mode the " +
            "flat guide stays hidden while this 3D guide is visible.")]
        [SerializeField] private PoseGhostGuideController _primaryGuide;
        [SerializeField] private PoseReferenceDisplayMode _displayMode =
            PoseReferenceDisplayMode.AutomaticFallback;
        [SerializeField] private RectTransform _contentRect;
        [SerializeField, Min(1f)]
        private float _posingContentWidth = 430f;
        [SerializeField, Min(1f)]
        private float _otherContentWidth = 696f;

        [Header("Markers")]
        [SerializeField] private RectTransform _headMarker;
        [SerializeField] private RectTransform _leftHandMarker;
        [SerializeField] private RectTransform _rightHandMarker;

        [Header("Arm Lines")]
        [Tooltip(
            "A centered UI image whose unrotated direction points right.")]
        [SerializeField] private RectTransform _leftArmLine;
        [Tooltip(
            "A centered UI image whose unrotated direction points right.")]
        [SerializeField] private RectTransform _rightArmLine;

        [Header("Front View Layout")]
        [SerializeField] private Vector2 _headPosition =
            new Vector2(0f, 30f);
        [SerializeField, Min(0f)]
        private float _pixelsPerNormalizedUnit = 200f;
        [SerializeField, Min(0f)]
        private float _shoulderHalfWidthNormalized = 0.12f;
        [SerializeField, Min(0f)]
        private float _shoulderDropNormalized = 0.13f;
        [SerializeField, Min(0.1f)]
        private float _armLineThickness = 7f;

        [Header("Forearm Lines")]
        [Tooltip(
            "Optional. When assigned, each arm is drawn as two segments " +
            "bent at a solved elbow instead of one straight line, so the " +
            "figure reads as a bent arm rather than a pointer.")]
        [SerializeField] private RectTransform _leftForearmLine;
        [SerializeField] private RectTransform _rightForearmLine;
        [SerializeField] private RectTransform _sideLeftForearmLine;
        [SerializeField] private RectTransform _sideRightForearmLine;

        [Header("Arm Proportions")]
        [SerializeField, Min(0f)]
        private float _upperArmLengthNormalized = 0.186f;
        [SerializeField, Min(0f)]
        private float _forearmLengthNormalized = 0.146f;
        [Tooltip(
            "Which way each elbow breaks. The front view reads best with " +
            "the elbows swinging away from the body; the profile view " +
            "breaks them backwards.")]
        [SerializeField] private bool _frontElbowsPointOutward = true;
        [SerializeField] private bool _sideElbowsPointBackward = true;

        [Header("Side View Markers")]
        [Tooltip(
            "Optional. A profile view drawn beside the front view so the " +
            "depth of the target hands is readable. Front view alone cannot " +
            "show how far forward or back an arm belongs.")]
        [SerializeField] private RectTransform _sideHeadMarker;
        [SerializeField] private RectTransform _sideLeftHandMarker;
        [SerializeField] private RectTransform _sideRightHandMarker;
        [SerializeField] private RectTransform _sideLeftArmLine;
        [SerializeField] private RectTransform _sideRightArmLine;

        [Header("Side View Layout")]
        [SerializeField] private Vector2 _sideHeadPosition =
            new Vector2(0f, 30f);
        [SerializeField]
        private float _sideShoulderDepthNormalized;

        public PosingScoreProfile Profile =>
            _posingScorer == null
                ? null
                : _posingScorer.CurrentProfile;

        [ContextMenu("Refresh Pose Reference Guide")]
        public void RefreshNow()
        {
            var profile = Profile;
            if (profile == null)
            {
                return;
            }

            var layout = CalculateLayout(
                profile.ToSettings().Target,
                _headPosition,
                _pixelsPerNormalizedUnit,
                _shoulderHalfWidthNormalized,
                _shoulderDropNormalized);
            var elbowHints = profile.GuideElbows;
            var leftFrontPole =
                layout.LeftShoulderPosition +
                new Vector2(
                    elbowHints
                        .LeftPoleFromShoulderNormalized.x,
                    elbowHints
                        .LeftPoleFromShoulderNormalized.y) *
                _pixelsPerNormalizedUnit;
            var rightFrontPole =
                layout.RightShoulderPosition +
                new Vector2(
                    elbowHints
                        .RightPoleFromShoulderNormalized.x,
                    elbowHints
                        .RightPoleFromShoulderNormalized.y) *
                _pixelsPerNormalizedUnit;

            ApplyMarker(_headMarker, layout.HeadPosition);
            ApplyMarker(
                _leftHandMarker,
                layout.LeftHandPosition);
            ApplyMarker(
                _rightHandMarker,
                layout.RightHandPosition);
            ApplyArm(
                _leftArmLine,
                _leftForearmLine,
                layout.LeftShoulderPosition,
                layout.LeftHandPosition,
                leftFrontPole,
                _frontElbowsPointOutward);
            ApplyArm(
                _rightArmLine,
                _rightForearmLine,
                layout.RightShoulderPosition,
                layout.RightHandPosition,
                rightFrontPole,
                !_frontElbowsPointOutward);

            var sideLayout = CalculateSideLayout(
                profile.ToSettings().Target,
                _sideHeadPosition,
                _pixelsPerNormalizedUnit,
                _sideShoulderDepthNormalized,
                _shoulderDropNormalized);
            var leftSidePole =
                sideLayout.LeftShoulderPosition +
                new Vector2(
                    elbowHints
                        .LeftPoleFromShoulderNormalized.z,
                    elbowHints
                        .LeftPoleFromShoulderNormalized.y) *
                _pixelsPerNormalizedUnit;
            var rightSidePole =
                sideLayout.RightShoulderPosition +
                new Vector2(
                    elbowHints
                        .RightPoleFromShoulderNormalized.z,
                    elbowHints
                        .RightPoleFromShoulderNormalized.y) *
                _pixelsPerNormalizedUnit;

            ApplyMarker(
                _sideHeadMarker,
                sideLayout.HeadPosition);
            ApplyMarker(
                _sideLeftHandMarker,
                sideLayout.LeftHandPosition);
            ApplyMarker(
                _sideRightHandMarker,
                sideLayout.RightHandPosition);
            ApplyArm(
                _sideLeftArmLine,
                _sideLeftForearmLine,
                sideLayout.LeftShoulderPosition,
                sideLayout.LeftHandPosition,
                leftSidePole,
                _sideElbowsPointBackward);
            ApplyArm(
                _sideRightArmLine,
                _sideRightForearmLine,
                sideLayout.RightShoulderPosition,
                sideLayout.RightHandPosition,
                rightSidePole,
                _sideElbowsPointBackward);
        }

        /// <summary>
        /// Converts the target's normalized X/Y offsets into a front-view UI
        /// layout. Z is intentionally ignored because the guide is a silhouette,
        /// while depth remains part of the actual pose score.
        /// </summary>
        public static PoseReferenceLayout CalculateLayout(
            in PosingScoreTarget target,
            Vector2 headPosition,
            float pixelsPerNormalizedUnit,
            float shoulderHalfWidthNormalized,
            float shoulderDropNormalized)
        {
            var safeHead = Sanitize(headPosition);
            var safeScale = SanitizeNonNegative(
                pixelsPerNormalizedUnit);
            var shoulderHalfWidth =
                SanitizeNonNegative(
                    shoulderHalfWidthNormalized) *
                safeScale;
            var shoulderDrop =
                SanitizeNonNegative(
                    shoulderDropNormalized) *
                safeScale;
            var leftHandOffset = Sanitize(
                new Vector2(
                    target
                        .LeftHandPositionFromHeadNormalized
                        .x,
                    target
                        .LeftHandPositionFromHeadNormalized
                        .y));
            var rightHandOffset = Sanitize(
                new Vector2(
                    target
                        .RightHandPositionFromHeadNormalized
                        .x,
                    target
                        .RightHandPositionFromHeadNormalized
                        .y));

            return new PoseReferenceLayout(
                safeHead,
                safeHead + new Vector2(
                    -shoulderHalfWidth,
                    -shoulderDrop),
                safeHead + new Vector2(
                    shoulderHalfWidth,
                    -shoulderDrop),
                safeHead + leftHandOffset * safeScale,
                safeHead + rightHandOffset * safeScale);
        }

        /// <summary>
        /// Converts the target's normalized Z/Y offsets into a profile view,
        /// drawn as if standing to the performer's right so that forward runs
        /// to the right of the panel. This is the half of the target the front
        /// view cannot show: whether a hand belongs in front of the chest or
        /// behind it. Both shoulders collapse onto one point because they sit
        /// at the same depth.
        /// </summary>
        public static PoseReferenceLayout CalculateSideLayout(
            in PosingScoreTarget target,
            Vector2 headPosition,
            float pixelsPerNormalizedUnit,
            float shoulderDepthNormalized,
            float shoulderDropNormalized)
        {
            var safeHead = Sanitize(headPosition);
            var safeScale = SanitizeNonNegative(
                pixelsPerNormalizedUnit);
            var shoulderDepth =
                Sanitize(shoulderDepthNormalized) * safeScale;
            var shoulderDrop =
                SanitizeNonNegative(
                    shoulderDropNormalized) *
                safeScale;
            var shoulder = safeHead + new Vector2(
                shoulderDepth,
                -shoulderDrop);
            var leftHandOffset = Sanitize(
                new Vector2(
                    target
                        .LeftHandPositionFromHeadNormalized
                        .z,
                    target
                        .LeftHandPositionFromHeadNormalized
                        .y));
            var rightHandOffset = Sanitize(
                new Vector2(
                    target
                        .RightHandPositionFromHeadNormalized
                        .z,
                    target
                        .RightHandPositionFromHeadNormalized
                        .y));

            return new PoseReferenceLayout(
                safeHead,
                shoulder,
                shoulder,
                safeHead + leftHandOffset * safeScale,
                safeHead + rightHandOffset * safeScale);
        }

        /// <summary>
        /// Solves the elbow of a two bone arm in the plane of the guide.
        /// Only the head and the two hands are tracked, so the elbow is
        /// inferred here just as the runtime IK infers it. When the hand is
        /// further away than the arm can reach the arm straightens instead
        /// of tearing, and the elbow lands on the line between the two.
        /// </summary>
        public static Vector2 CalculateElbow(
            Vector2 shoulder,
            Vector2 hand,
            float upperArmLength,
            float forearmLength,
            bool bendPositive)
        {
            var safeShoulder = Sanitize(shoulder);
            var safeHand = Sanitize(hand);
            var upper = SanitizeNonNegative(upperArmLength);
            var fore = SanitizeNonNegative(forearmLength);

            var delta = safeHand - safeShoulder;
            var reach = delta.magnitude;
            if (reach <= MinimumLineLength ||
                upper <= MinimumLineLength ||
                fore <= MinimumLineLength)
            {
                return Vector2.Lerp(safeShoulder, safeHand, 0.5f);
            }

            var direction = delta / reach;
            var clampedReach = Mathf.Clamp(
                reach,
                Mathf.Abs(upper - fore) + MinimumLineLength,
                upper + fore);
            var alongAxis =
                (upper * upper -
                 fore * fore +
                 clampedReach * clampedReach) /
                (2f * clampedReach);
            var offAxis = Mathf.Sqrt(
                Mathf.Max(
                    0f,
                    upper * upper - alongAxis * alongAxis));
            var perpendicular = bendPositive
                ? new Vector2(-direction.y, direction.x)
                : new Vector2(direction.y, -direction.x);

            return safeShoulder +
                direction * alongAxis +
                perpendicular * offAxis;
        }

        /// <summary>
        /// Chooses the two-bone elbow solution closest to an authored pole.
        /// A fixed left/right bend flag cannot describe both hands-above-head
        /// and hands-below-chest poses, because the same flag changes visual
        /// meaning as the shoulder-to-hand direction rotates.
        /// </summary>
        public static Vector2 CalculateElbowTowardPole(
            Vector2 shoulder,
            Vector2 hand,
            float upperArmLength,
            float forearmLength,
            Vector2 elbowPole)
        {
            var positive = CalculateElbow(
                shoulder,
                hand,
                upperArmLength,
                forearmLength,
                true);
            var negative = CalculateElbow(
                shoulder,
                hand,
                upperArmLength,
                forearmLength,
                false);
            var safePole = Sanitize(elbowPole);
            return
                (positive - safePole).sqrMagnitude <=
                (negative - safePole).sqrMagnitude
                    ? positive
                    : negative;
        }

        /// <summary>
        /// Calculates the centered RectTransform values for a UI line between
        /// two points. Invalid inputs are sanitized to deterministic values.
        /// </summary>
        public static PoseReferenceLineLayout CalculateLineLayout(
            Vector2 from,
            Vector2 to,
            float thickness)
        {
            var safeFrom = Sanitize(from);
            var safeTo = Sanitize(to);
            var delta = safeTo - safeFrom;
            var distance = delta.magnitude;
            var rotation = distance > Mathf.Epsilon
                ? Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg
                : 0f;

            return new PoseReferenceLineLayout(
                (safeFrom + safeTo) * 0.5f,
                new Vector2(
                    Mathf.Max(MinimumLineLength, distance),
                    SanitizePositive(thickness, 0.1f)),
                rotation);
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            RefreshNow();
            RefreshVisibility();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void LateUpdate()
        {
            if (Application.isPlaying &&
                _displayMode ==
                    PoseReferenceDisplayMode.AutomaticFallback &&
                (_gameFlow == null ||
                 _gameFlow.CurrentPhase == ExperiencePhase.Posing))
            {
                // The 3D guide is created and can become unavailable at
                // runtime. Re-evaluate after its LateUpdate so the fallback
                // changes without presenting both guides at once.
                RefreshVisibility();
            }
        }

        private void OnValidate()
        {
            ResolveReferences();
            _pixelsPerNormalizedUnit =
                SanitizeNonNegative(
                    _pixelsPerNormalizedUnit);
            _shoulderHalfWidthNormalized =
                SanitizeNonNegative(
                    _shoulderHalfWidthNormalized);
            _shoulderDropNormalized =
                SanitizeNonNegative(
                    _shoulderDropNormalized);
            _armLineThickness =
                SanitizePositive(
                    _armLineThickness,
                    0.1f);
            _posingContentWidth =
                SanitizePositive(
                    _posingContentWidth,
                    1f);
            _otherContentWidth =
                SanitizePositive(
                    _otherContentWidth,
                    1f);
            RefreshNow();
            RefreshVisibility();
        }

        private void HandlePhaseChanged(ExperiencePhase phase)
        {
            RefreshVisibility();
        }

        private void RefreshVisibility()
        {
            if (_visualRoot == null)
            {
                return;
            }

            var isPosing =
                _gameFlow == null ||
                _gameFlow.CurrentPhase ==
                    ExperiencePhase.Posing;
            var shouldShow =
                PoseReferenceVisibilityPolicy.ShouldShow(
                    isPosing,
                    _displayMode,
                    _primaryGuide != null &&
                    _primaryGuide.IsGuideVisible);
            if (_visualRoot.activeSelf != shouldShow)
            {
                _visualRoot.SetActive(shouldShow);
            }

            if (_contentRect != null)
            {
                var size = _contentRect.sizeDelta;
                size.x = shouldShow
                    ? _posingContentWidth
                    : _otherContentWidth;
                _contentRect.sizeDelta = size;
            }
        }

        private void ResolveReferences()
        {
            _gameFlow ??=
                FindFirstObjectByType<GameFlowController>();
            _posingScorer ??=
                FindFirstObjectByType<ThreePointPosingScorer>(
                    FindObjectsInactive.Include);
            _primaryGuide ??=
                FindFirstObjectByType<PoseGhostGuideController>(
                    FindObjectsInactive.Include);
        }

        private void HandlePoseChanged(PosingScoreProfile profile)
        {
            RefreshNow();
        }

        private void Subscribe()
        {
            if (_gameFlow == null)
            {
                return;
            }

            _gameFlow.PhaseChanged -= HandlePhaseChanged;
            _gameFlow.PhaseChanged += HandlePhaseChanged;

            if (_posingScorer != null)
            {
                _posingScorer.PoseChanged -= HandlePoseChanged;
                _posingScorer.PoseChanged += HandlePoseChanged;
            }
        }

        private void Unsubscribe()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= HandlePhaseChanged;
            }

            if (_posingScorer != null)
            {
                _posingScorer.PoseChanged -= HandlePoseChanged;
            }
        }

        /// <summary>
        /// Draws one arm. With a forearm line assigned the arm bends at the
        /// solved elbow; without one it falls back to the original single
        /// straight segment so a half-built scene still shows something.
        /// </summary>
        private void ApplyArm(
            RectTransform upperArmLine,
            RectTransform forearmLine,
            Vector2 shoulder,
            Vector2 hand,
            Vector2 elbowPole,
            bool fallbackBendPositive)
        {
            if (forearmLine == null)
            {
                ApplyLine(
                    upperArmLine,
                    CalculateLineLayout(
                        shoulder,
                        hand,
                        _armLineThickness));
                return;
            }

            var upperLength =
                _upperArmLengthNormalized *
                _pixelsPerNormalizedUnit;
            var forearmLength =
                _forearmLengthNormalized *
                _pixelsPerNormalizedUnit;
            var elbow =
                (elbowPole - shoulder).sqrMagnitude >
                MinimumLineLength * MinimumLineLength
                    ? CalculateElbowTowardPole(
                        shoulder,
                        hand,
                        upperLength,
                        forearmLength,
                        elbowPole)
                    : CalculateElbow(
                        shoulder,
                        hand,
                        upperLength,
                        forearmLength,
                        fallbackBendPositive);
            ApplyLine(
                upperArmLine,
                CalculateLineLayout(
                    shoulder,
                    elbow,
                    _armLineThickness));
            ApplyLine(
                forearmLine,
                CalculateLineLayout(
                    elbow,
                    hand,
                    _armLineThickness));
        }

        private static void ApplyMarker(
            RectTransform marker,
            Vector2 position)
        {
            if (marker == null)
            {
                return;
            }

            SetCenteredAnchor(marker);
            marker.anchoredPosition = position;
        }

        private static void ApplyLine(
            RectTransform line,
            in PoseReferenceLineLayout layout)
        {
            if (line == null)
            {
                return;
            }

            SetCenteredAnchor(line);
            line.anchoredPosition = layout.Position;
            line.sizeDelta = layout.Size;
            line.localRotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    layout.RotationDegrees);
        }

        private static void SetCenteredAnchor(
            RectTransform target)
        {
            target.anchorMin = new Vector2(0.5f, 0.5f);
            target.anchorMax = new Vector2(0.5f, 0.5f);
            target.pivot = new Vector2(0.5f, 0.5f);
        }

        private static Vector2 Sanitize(Vector2 value)
        {
            return new Vector2(
                IsFinite(value.x) ? value.x : 0f,
                IsFinite(value.y) ? value.y : 0f);
        }

        private static float Sanitize(float value)
        {
            return IsFinite(value) ? value : 0f;
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

        private static bool IsFinite(float value)
        {
            return
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }
    }
}
