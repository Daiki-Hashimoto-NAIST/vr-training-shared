using TMPro;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Presents a small, low-frequency body tracking monitor on a world-space panel.
    /// </summary>
    public sealed class BodyMetricsHud : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private BodyPoseMetrics _metrics;
        [SerializeField] private AvatarCalibrationController _calibration;
        [SerializeField] private HeightCalibrationController _heightCalibration;
        [SerializeField] private BodyRepCounter _repCounter;

        [Header("View")]
        [SerializeField] private TMP_Text _text;
        [SerializeField, Min(1f)] private float _refreshRate = 10f;

        private float _nextRefreshTime;

        private void Awake()
        {
            if (_text == null)
            {
                _text = GetComponentInChildren<TMP_Text>(true);
            }
        }

        private void OnEnable()
        {
            _nextRefreshTime = 0f;
            RefreshNow();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            RefreshNow();
        }

        [ContextMenu("Refresh HUD")]
        public void RefreshNow()
        {
            _nextRefreshTime =
                Time.unscaledTime + 1f / Mathf.Max(1f, _refreshRate);

            if (_text == null)
            {
                return;
            }

            if (_metrics == null)
            {
                _text.text =
                    "<b>BODY METRICS</b>\n" +
                    "<color=#FF7070>POSE SOURCE NOT ASSIGNED</color>";
                return;
            }

            var trackingText = _metrics.IsTrackingValid
                ? "<color=#70FFB0>ACTIVE</color>"
                : "<color=#FFD36A>WAITING</color>";

            var calibrationText = _calibration == null
                ? "N/A"
                : _calibration.IsCalibrating
                    ? "<color=#FFD36A>CALIBRATING</color>"
                    : _calibration.HasCalibrated
                        ? "<color=#70FFB0>READY</color>"
                        : "<color=#FFD36A>PRESS B</color>";

            var heightCalibrationText = FormatHeightCalibration();
            var counterStatusText = FormatCounterStatus();
            var leftElbow = FormatAngle(_metrics.LeftElbowFlexion);
            var rightElbow = FormatAngle(_metrics.RightElbowFlexion);
            var leftRaise = FormatAngle(_metrics.LeftArmRaise);
            var rightRaise = FormatAngle(_metrics.RightArmRaise);
            var elbowDifference = FormatAngle(_metrics.ElbowSymmetryDifference);
            var raiseDifference = FormatAngle(_metrics.ArmRaiseSymmetryDifference);

            if (!_metrics.IsTrackingValid)
            {
                leftElbow = rightElbow = leftRaise = rightRaise = "--";
                elbowDifference = raiseDifference = "--";
            }

            _text.text =
                "<b>BODY METRICS</b>\n" +
                $"TRACKING     {trackingText}\n" +
                $"ALIGNMENT    {calibrationText}\n" +
                $"HEIGHT       {heightCalibrationText}\n\n" +
                $"ELBOW FLEX   L {leftElbow,4}  R {rightElbow,4}\n" +
                $"ARM RAISE    L {leftRaise,4}  R {rightRaise,4}\n" +
                $"L/R DIFF     E {elbowDifference,4}  A {raiseDifference,4}\n\n" +
                $"COUNTER      {counterStatusText}\n" +
                $"ELBOW REPS   L {FormatCount(_repCounter?.LeftElbowCount),3}  " +
                $"R {FormatCount(_repCounter?.RightElbowCount),3}\n" +
                $"ARM REPS     L {FormatCount(_repCounter?.LeftArmRaiseCount),3}  " +
                $"R {FormatCount(_repCounter?.RightArmRaiseCount),3}";
        }

        private string FormatHeightCalibration()
        {
            if (_heightCalibration == null)
            {
                return "N/A";
            }

            if (_heightCalibration.IsCalibrating)
            {
                return "<color=#FFD36A>APPLYING...</color>";
            }

            if (!_heightCalibration.HasAttemptedCalibration)
            {
                return _heightCalibration.MeasuresHeightFromHmd
                    ? "<color=#FFD36A>STAND + PRESS X</color>"
                    : $"<color=#FFD36A>PRESS X ({_heightCalibration.HeightMeters:0.00} m)</color>";
            }

            return _heightCalibration.LastCalibrationSucceeded
                ? $"<color=#70FFB0>{_heightCalibration.HeightMeters:0.00} m APPLIED</color>"
                : "<color=#FF7070>FAILED - PRESS X</color>";
        }

        private string FormatCounterStatus()
        {
            if (_repCounter == null)
            {
                return "N/A";
            }

            return _repCounter.IsReady
                ? "<color=#70FFB0>READY</color>"
                : $"<color=#FFD36A>{_repCounter.WaitingReason}</color>";
        }

        private static string FormatCount(int? count)
        {
            return count.HasValue ? count.Value.ToString("00") : "--";
        }

        private static string FormatAngle(float angle)
        {
            return $"{angle:0}\u00B0";
        }
    }
}
