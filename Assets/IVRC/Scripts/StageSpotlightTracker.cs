using System;
using UnityEngine;

namespace IVRC
{
    public static class StageSpotlightTrackingMath
    {
        public static Vector3 ResolveAimPoint(
            Vector3 targetPosition,
            float heightOffset)
        {
            if (!IsFinite(targetPosition) ||
                !IsFinite(heightOffset))
            {
                return targetPosition;
            }

            return targetPosition +
                Vector3.up * heightOffset;
        }

        /// <summary>
        /// Frame rate independent share of the remaining angle to
        /// close this frame. A follow spot sweeps, it never snaps.
        /// </summary>
        public static float EvaluateTurnBlend(
            float turnResponse,
            float deltaTime)
        {
            if (!IsFinite(turnResponse) ||
                !IsFinite(deltaTime) ||
                deltaTime <= 0f)
            {
                return 0f;
            }

            if (turnResponse <= 0f)
            {
                return 1f;
            }

            return 1f - Mathf.Exp(-turnResponse * deltaTime);
        }

        public static Quaternion EvaluateAimRotation(
            Vector3 lightPosition,
            Vector3 aimPoint,
            Quaternion currentRotation,
            float turnResponse,
            float deltaTime)
        {
            if (!IsFinite(lightPosition) ||
                !IsFinite(aimPoint))
            {
                return currentRotation;
            }

            var direction = aimPoint - lightPosition;
            if (direction.sqrMagnitude <= 0.000001f)
            {
                return currentRotation;
            }

            var up = Mathf.Abs(
                Vector3.Dot(
                    direction.normalized,
                    Vector3.up)) > 0.999f
                ? Vector3.forward
                : Vector3.up;
            var desired = Quaternion.LookRotation(
                direction.normalized,
                up);
            var blend = EvaluateTurnBlend(
                turnResponse,
                deltaTime);
            return Quaternion.Slerp(
                currentRotation,
                desired,
                Mathf.Clamp01(blend));
        }

        private static bool IsFinite(float value)
        {
            return
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return
                IsFinite(value.x) &&
                IsFinite(value.y) &&
                IsFinite(value.z);
        }
    }

    /// <summary>
    /// Keeps the stage dark while a fixed pair of spotlights follows
    /// the performer. Only the rotation of each light is driven, so
    /// the rigged position of every lamp is preserved. Intensity and
    /// colour stay owned by <see cref="PoseStageFeedbackController"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageSpotlightTracker : MonoBehaviour
    {
        [Header("Lights")]
        [SerializeField] private Light[] _spotlights =
            Array.Empty<Light>();

        [Header("Targets")]
        [SerializeField] private Transform _performerTarget;
        [SerializeField] private StageEntranceCinematicController
            _stageEntrance;

        [Header("Aim")]
        [SerializeField] private float _performerAimHeightOffset =
            -0.4f;
        [SerializeField] private float _entranceAimHeightOffset =
            1.2f;
        [SerializeField, Min(0f)] private float _turnResponse =
            7f;

        private Vector3[] _restPositions =
            Array.Empty<Vector3>();

        public Transform CurrentTarget { get; private set; }
        public Vector3 CurrentAimPoint { get; private set; }

        private void Awake()
        {
            ResolveReferences();
            CaptureRestPositions();
        }

        private void OnEnable()
        {
            ResolveReferences();
            CaptureRestPositions();
            UpdateAim(0f);
        }

        private void LateUpdate()
        {
            UpdateAim(Time.deltaTime);
        }

        private void UpdateAim(float deltaTime)
        {
            var target = ResolveTarget(out var heightOffset);
            CurrentTarget = target;
            if (target == null)
            {
                return;
            }

            var aimPoint =
                StageSpotlightTrackingMath.ResolveAimPoint(
                    target.position,
                    heightOffset);
            CurrentAimPoint = aimPoint;

            for (var index = 0;
                 index < _spotlights.Length;
                 index++)
            {
                var spotlight = _spotlights[index];
                if (spotlight == null)
                {
                    continue;
                }

                // The lamp is rigged in place; only the beam moves.
                RestorePosition(index, spotlight);
                spotlight.transform.rotation =
                    StageSpotlightTrackingMath
                        .EvaluateAimRotation(
                            spotlight.transform.position,
                            aimPoint,
                            spotlight.transform.rotation,
                            _turnResponse,
                            deltaTime);
            }
        }

        private Transform ResolveTarget(out float heightOffset)
        {
            var entranceAvatar =
                _stageEntrance != null &&
                _stageEntrance.IsRunning &&
                _stageEntrance.RuntimeAvatar != null
                    ? _stageEntrance.RuntimeAvatar.transform
                    : null;
            if (entranceAvatar != null)
            {
                heightOffset = _entranceAimHeightOffset;
                return entranceAvatar;
            }

            heightOffset = _performerAimHeightOffset;
            return _performerTarget;
        }

        private void CaptureRestPositions()
        {
            _spotlights ??= Array.Empty<Light>();
            _restPositions = new Vector3[_spotlights.Length];
            for (var index = 0;
                 index < _spotlights.Length;
                 index++)
            {
                var spotlight = _spotlights[index];
                _restPositions[index] = spotlight == null
                    ? Vector3.zero
                    : spotlight.transform.position;
            }
        }

        private void RestorePosition(
            int index,
            Light spotlight)
        {
            if (index < 0 ||
                index >= _restPositions.Length)
            {
                return;
            }

            spotlight.transform.position =
                _restPositions[index];
        }

        private void ResolveReferences()
        {
            _spotlights ??= Array.Empty<Light>();
            _stageEntrance ??=
                FindFirstObjectByType<
                    StageEntranceCinematicController>(
                    FindObjectsInactive.Include);
        }
    }
}
