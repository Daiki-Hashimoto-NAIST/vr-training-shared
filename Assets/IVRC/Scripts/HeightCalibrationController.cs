using Meta.XR.Movement.Retargeting;
using System.Collections;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Applies Meta body-tracking height calibration from the left controller X button.
    /// This is intentionally separate from play-space position and yaw alignment.
    /// </summary>
    public sealed class HeightCalibrationController : MonoBehaviour
    {
        private const float RetargeterRefreshTimeoutSeconds = 1f;

        [Header("Input")]
        [SerializeField] private OVRInput.RawButton _calibrationButton = OVRInput.RawButton.X;

        [Header("Body Height")]
        [SerializeField] private bool _measureHeightFromHmd = true;
        [SerializeField, Range(0.05f, 0.20f)]
        private float _eyeToTopOfHeadMeters = 0.11f;
        [SerializeField, Min(0.5f)] private float _heightMeters = 1.8f;

        [Header("Tracking")]
        [SerializeField] private OVRCameraRig _cameraRig;

        [Header("Movement SDK")]
        [SerializeField] private CharacterRetargeter[] _characterRetargeters;

        public bool MeasuresHeightFromHmd => _measureHeightFromHmd;
        public float HeightMeters =>
            LastResolvedHeightMeters > 0f ? LastResolvedHeightMeters : _heightMeters;
        public float LastResolvedHeightMeters { get; private set; }
        public float LastMeasuredEyeHeightMeters { get; private set; }
        public bool HasAttemptedCalibration { get; private set; }
        public bool LastCalibrationSucceeded { get; private set; }
        public bool IsCalibrating { get; private set; }
        public int LastCalibratedRetargeterCount { get; private set; }
        public bool InputEnabled { get; private set; } = true;

        private void Update()
        {
            if (!InputEnabled)
            {
                return;
            }

            if (OVRInput.GetDown(_calibrationButton))
            {
                ApplyHeightCalibration();
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
            LastResolvedHeightMeters = 0f;
            LastMeasuredEyeHeightMeters = 0f;
            HasAttemptedCalibration = false;
            LastCalibrationSucceeded = false;
            IsCalibrating = false;
            LastCalibratedRetargeterCount = 0;
        }

        /// <summary>
        /// Completes calibration from an authored height for desktop
        /// simulation. The Meta runtime call in
        /// <see cref="ApplyHeightCalibration"/> needs a connected headset, so
        /// it is skipped here. The VR path is unchanged.
        /// </summary>
        public void CompleteDesktopCalibration(float heightMeters)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            StopAllCoroutines();
            IsCalibrating = false;
            HasAttemptedCalibration = true;
            LastResolvedHeightMeters =
                Mathf.Clamp(heightMeters, 1.2f, 2.3f);
            LastMeasuredEyeHeightMeters = 0f;
            LastCalibratedRetargeterCount = 0;
            LastCalibrationSucceeded = true;
        }

        [ContextMenu("Apply Height Calibration")]
        public void ApplyHeightCalibration()
        {
            if (!InputEnabled ||
                IsCalibrating ||
                !isActiveAndEnabled)
            {
                return;
            }

            StartCoroutine(ApplyHeightCalibrationRoutine());
        }

        private IEnumerator ApplyHeightCalibrationRoutine()
        {
            IsCalibrating = true;
            HasAttemptedCalibration = true;
            LastCalibrationSucceeded = false;
            LastCalibratedRetargeterCount = 0;

            if (!TryResolveBodyHeight(
                    out var resolvedHeightMeters,
                    out var measuredEyeHeightMeters))
            {
                IsCalibrating = false;
                yield break;
            }

            LastResolvedHeightMeters = resolvedHeightMeters;
            LastMeasuredEyeHeightMeters = measuredEyeHeightMeters;

            if (!OVRBody.SuggestBodyTrackingCalibrationOverride(resolvedHeightMeters))
            {
                Debug.LogWarning(
                    $"[HeightCalibration] Meta runtime rejected body height " +
                    $"{resolvedHeightMeters:F2} m. " +
                    "Confirm that body tracking is active, then press left controller X again.",
                    this);
                IsCalibrating = false;
                yield break;
            }

            // The runtime publishes the recalibrated source skeleton asynchronously.
            // Our A-button alignment intentionally locks CharacterRetargeter to its last
            // source T-pose, so wait for the new source T-pose before locking it again.
            var refreshDeadline =
                Time.realtimeSinceStartup + RetargeterRefreshTimeoutSeconds;
            while (!IsRetargeterTPoseReady() &&
                   Time.realtimeSinceStartup < refreshDeadline)
            {
                yield return null;
            }

            if (_characterRetargeters != null)
            {
                foreach (var retargeter in _characterRetargeters)
                {
                    if (retargeter == null ||
                        !retargeter.isActiveAndEnabled ||
                        !retargeter.IsValid)
                    {
                        continue;
                    }

                    retargeter.Calibrate();
                    LastCalibratedRetargeterCount++;
                }
            }

            LastCalibrationSucceeded = LastCalibratedRetargeterCount > 0;
            IsCalibrating = false;

            if (LastCalibrationSucceeded)
            {
                var measurementText = _measureHeightFromHmd
                    ? $" from HMD eye height {measuredEyeHeightMeters:F2} m"
                    : string.Empty;
                Debug.Log(
                    $"[HeightCalibration] Applied body height {resolvedHeightMeters:F2} m" +
                    $"{measurementText} and refreshed " +
                    $"{LastCalibratedRetargeterCount} retargeter(s) from left controller X.",
                    this);
            }
            else
            {
                Debug.LogWarning(
                    $"[HeightCalibration] Meta accepted body height {resolvedHeightMeters:F2} m, " +
                    "but no valid CharacterRetargeter was refreshed. " +
                    "Face forward, wait for body tracking, then press left controller X again.",
                    this);
            }
        }

        private bool TryResolveBodyHeight(
            out float bodyHeightMeters,
            out float measuredEyeHeightMeters)
        {
            measuredEyeHeightMeters = 0f;
            bodyHeightMeters = _heightMeters;

            if (!_measureHeightFromHmd)
            {
                return true;
            }

            if (_cameraRig == null)
            {
                _cameraRig = FindFirstObjectByType<OVRCameraRig>();
            }

            if (_cameraRig == null ||
                _cameraRig.trackingSpace == null ||
                _cameraRig.centerEyeAnchor == null)
            {
                Debug.LogWarning(
                    "[HeightCalibration] Camera Rig is not ready. " +
                    "Wait for the headset to begin tracking, then press left controller X again.",
                    this);
                return false;
            }

            // TrackingSpace uses FloorLevel in this scene, and A-button alignment never
            // modifies its Y position. The eye anchor's local Y is therefore the user's
            // current eye height above the physical floor.
            measuredEyeHeightMeters = _cameraRig.centerEyeAnchor.localPosition.y;
            if (float.IsNaN(measuredEyeHeightMeters) ||
                float.IsInfinity(measuredEyeHeightMeters) ||
                measuredEyeHeightMeters < 1.0f ||
                measuredEyeHeightMeters > 2.15f)
            {
                Debug.LogWarning(
                    $"[HeightCalibration] HMD eye height {measuredEyeHeightMeters:F2} m " +
                    "is not a valid standing measurement. Stand upright and press " +
                    "left controller X again.",
                    this);
                return false;
            }

            bodyHeightMeters = Mathf.Clamp(
                measuredEyeHeightMeters + _eyeToTopOfHeadMeters,
                1.2f,
                2.3f);
            return true;
        }

        private void OnDisable()
        {
            InvalidateCalibration();
        }

        private bool IsRetargeterTPoseReady()
        {
            if (_characterRetargeters == null)
            {
                return false;
            }

            var hasValidRetargeter = false;
            foreach (var retargeter in _characterRetargeters)
            {
                if (retargeter == null ||
                    !retargeter.isActiveAndEnabled ||
                    !retargeter.IsValid ||
                    retargeter.DataProvider == null)
                {
                    continue;
                }

                hasValidRetargeter = true;
                if (!retargeter.DataProvider.IsNewTPoseAvailable())
                {
                    return false;
                }
            }

            return hasValidRetargeter;
        }

        private void OnValidate()
        {
            _heightMeters = Mathf.Max(0.5f, _heightMeters);
            _eyeToTopOfHeadMeters = Mathf.Clamp(
                _eyeToTopOfHeadMeters,
                0.05f,
                0.20f);
        }
    }
}
