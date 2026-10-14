using Meta.XR.Movement.Retargeting;
using System.Collections;
using IVRC.Desktop;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Aligns the current HMD pose to a fixed play-space marker, then refreshes
    /// Movement SDK retargeting. Only OVRCameraRig.TrackingSpace is moved;
    /// the tracked avatar must remain under a world-identity hierarchy.
    /// </summary>
    public sealed class AvatarCalibrationController : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private OVRInput.Button _calibrationButton = OVRInput.Button.Two;
        [SerializeField] private OVRInput.Controller _controller = OVRInput.Controller.RTouch;

        [Header("Play Space")]
        [SerializeField] private OVRCameraRig _cameraRig;
        [SerializeField] private DesktopSimulationManager
            _desktopSimulation;
        [SerializeField] private Transform _calibrationPoint;

        [Header("Movement SDK")]
        [SerializeField] private CharacterRetargeter[] _characterRetargeters;

        private IHeadTrackingValiditySource
            _headTrackingValiditySource;

        public bool HasCalibrated { get; private set; }
        public bool HasAttemptedCalibration { get; private set; }
        public bool IsCalibrating { get; private set; }
        public bool InputEnabled { get; private set; } = true;

        private void Update()
        {
            if (!InputEnabled)
            {
                return;
            }

            if (OVRInput.GetDown(_calibrationButton, _controller))
            {
                CalibrateNow();
            }
        }

        public void SetInputEnabled(bool enabled)
        {
            InputEnabled = enabled;
        }

        public void SetRetargeters(
            params CharacterRetargeter[] retargeters)
        {
            _characterRetargeters = retargeters;
            InvalidateCalibration();
        }

        public void InvalidateCalibration()
        {
            StopAllCoroutines();
            HasCalibrated = false;
            HasAttemptedCalibration = false;
            IsCalibrating = false;
        }

        /// <summary>
        /// Marks alignment as complete for desktop simulation. The VR pass in
        /// <see cref="CalibrateNow"/> requires a valid tracked retargeter,
        /// which is unavailable without a headset. The VR path is unchanged.
        /// </summary>
        public void CompleteDesktopCalibration()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            StopAllCoroutines();
            IsCalibrating = false;
            HasAttemptedCalibration = true;
            HasCalibrated = true;
        }

        /// <summary>
        /// Starts one placement and body-retargeting calibration pass.
        /// Safe to connect directly to a UI Button.
        /// </summary>
        [ContextMenu("Calibrate Now")]
        public void CalibrateNow()
        {
            if (!InputEnabled ||
                IsCalibrating ||
                !isActiveAndEnabled)
            {
                return;
            }

            StartCoroutine(CalibrateRoutine());
        }

        private IEnumerator CalibrateRoutine()
        {
            IsCalibrating = true;
            HasAttemptedCalibration = true;
            HasCalibrated = false;

            if (!TryAlignTrackingSpace(out var yawCorrection, out var eyeBefore, out var eyeAfter))
            {
                HasCalibrated = false;
                IsCalibrating = false;
                yield break;
            }

            // Movement SDK samples TrackingSpace in Update. Let it observe the new
            // world transform before fixing the retargeter's T-pose reference.
            yield return null;

            var retargeterCount = CalibrateRetargeters();
            HasCalibrated = retargeterCount > 0;
            IsCalibrating = false;

            Debug.Log(
                $"[AvatarCalibration] Play space aligned. " +
                $"Eye XZ ({eyeBefore.x:F3}, {eyeBefore.z:F3}) -> " +
                $"({eyeAfter.x:F3}, {eyeAfter.z:F3}), " +
                $"yaw correction {yawCorrection:F1} deg. " +
                $"Body retargeters calibrated: {retargeterCount}.",
                this);
        }

        private bool TryAlignTrackingSpace(
            out float yawCorrection,
            out Vector3 eyeBefore,
            out Vector3 eyeAfter)
        {
            yawCorrection = 0f;
            eyeBefore = Vector3.zero;
            eyeAfter = Vector3.zero;

            if (_cameraRig == null || _cameraRig.trackingSpace == null || _cameraRig.centerEyeAnchor == null)
            {
                Debug.LogError("[AvatarCalibration] Camera Rig or its tracking anchors are not assigned.", this);
                return false;
            }

            if (_calibrationPoint == null)
            {
                Debug.LogError("[AvatarCalibration] Calibration Point is not assigned.", this);
                return false;
            }

            if (!IsHeadTrackingValid())
            {
                Debug.LogError(
                    "[AvatarCalibration] HMD position and rotation " +
                    "tracking are not valid. Calibration was refused.",
                    this);
                return false;
            }

            var trackingSpace = _cameraRig.trackingSpace;
            var centerEye = _cameraRig.centerEyeAnchor;
            var currentForward = Vector3.ProjectOnPlane(centerEye.forward, Vector3.up);
            var targetForward = Vector3.ProjectOnPlane(_calibrationPoint.forward, Vector3.up);

            if (currentForward.sqrMagnitude < 0.0001f || targetForward.sqrMagnitude < 0.0001f)
            {
                Debug.LogError("[AvatarCalibration] A horizontal facing direction could not be calculated.", this);
                return false;
            }

            currentForward.Normalize();
            targetForward.Normalize();
            eyeBefore = centerEye.position;
            yawCorrection = Vector3.SignedAngle(currentForward, targetForward, Vector3.up);

            // Rotate around the user's head so yaw does not introduce a position offset.
            trackingSpace.RotateAround(eyeBefore, Vector3.up, yawCorrection);

            // FloorLevel owns height. Calibration only places the user on the XZ plane.
            var translation = _calibrationPoint.position - centerEye.position;
            translation.y = 0f;
            trackingSpace.position += translation;
            eyeAfter = centerEye.position;
            return true;
        }

        private bool IsHeadTrackingValid()
        {
            _desktopSimulation ??=
                FindFirstObjectByType<DesktopSimulationManager>(
                    FindObjectsInactive.Include);
            if (_desktopSimulation != null &&
                _desktopSimulation.IsDesktopActive)
            {
                return true;
            }

            _headTrackingValiditySource ??=
                new UnityXrHeadTrackingValiditySource();
            return _headTrackingValiditySource
                .IsHeadTrackingValid();
        }

        private int CalibrateRetargeters()
        {
            var calibratedCount = 0;

            if (_characterRetargeters != null)
            {
                foreach (var retargeter in _characterRetargeters)
                {
                    if (retargeter == null || !retargeter.isActiveAndEnabled)
                    {
                        continue;
                    }

                    if (!retargeter.IsValid)
                    {
                        Debug.LogWarning(
                            $"[AvatarCalibration] Body tracking is not valid for {retargeter.name}. " +
                            "Face forward, stand still, and press A again.",
                            retargeter);
                        continue;
                    }

                    retargeter.Calibrate();
                    calibratedCount++;
                }
            }

            if (calibratedCount == 0)
            {
                Debug.LogWarning(
                    "[AvatarCalibration] Play space was aligned, but no valid CharacterRetargeter " +
                    "was calibrated. Face forward, stand still, and press A again.",
                    this);
            }

            return calibratedCount;
        }

        private void OnDisable()
        {
            InvalidateCalibration();
        }
    }
}
