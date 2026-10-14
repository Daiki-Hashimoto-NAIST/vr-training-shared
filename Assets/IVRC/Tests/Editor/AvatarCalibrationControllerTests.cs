using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IVRC.Tests
{
    public sealed class AvatarCalibrationControllerTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance |
            BindingFlags.NonPublic;

        private GameObject _root;
        private OVRCameraRig _cameraRig;
        private AvatarCalibrationController _controller;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject(
                "AvatarCalibrationControllerTests_Root");
            _root.SetActive(false);

            var rigObject = new GameObject("CameraRig");
            rigObject.transform.SetParent(_root.transform, false);
            _cameraRig = rigObject.AddComponent<OVRCameraRig>();
            _cameraRig.EnsureGameObjectIntegrity();
            _cameraRig.trackingSpace.SetPositionAndRotation(
                new Vector3(1.2f, 0.15f, -2.1f),
                Quaternion.Euler(0f, 27f, 0f));
            _cameraRig.centerEyeAnchor.localPosition =
                new Vector3(0.12f, 1.68f, -0.08f);
            _cameraRig.centerEyeAnchor.localRotation =
                Quaternion.Euler(0f, 11f, 0f);

            var point = new GameObject("CalibrationPoint");
            point.transform.SetParent(_root.transform, false);
            point.transform.SetPositionAndRotation(
                new Vector3(-2f, 0f, 3f),
                Quaternion.Euler(0f, -40f, 0f));

            var controllerObject =
                new GameObject("AvatarCalibration");
            controllerObject.transform.SetParent(
                _root.transform,
                false);
            _controller = controllerObject.AddComponent<
                AvatarCalibrationController>();
            SetField("_cameraRig", _cameraRig);
            SetField("_calibrationPoint", point.transform);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void InvalidHeadTrackingRefusesCalibrationWithoutPoseDrift()
        {
            SetField(
                "_headTrackingValiditySource",
                new FakeHeadTrackingValiditySource(false));
            var initialPosition =
                _cameraRig.trackingSpace.position;
            var initialRotation =
                _cameraRig.trackingSpace.rotation;
            LogAssert.Expect(
                LogType.Error,
                "[AvatarCalibration] HMD position and rotation " +
                "tracking are not valid. Calibration was refused.");

            var method = typeof(AvatarCalibrationController)
                .GetMethod("TryAlignTrackingSpace", PrivateInstance);
            Assert.That(method, Is.Not.Null);
            var arguments = new object[]
            {
                0f,
                Vector3.zero,
                Vector3.zero
            };
            var succeeded = (bool)method.Invoke(
                _controller,
                arguments);

            Assert.That(succeeded, Is.False);
            Assert.That(
                _cameraRig.trackingSpace.position,
                Is.EqualTo(initialPosition));
            Assert.That(
                Quaternion.Angle(
                    _cameraRig.trackingSpace.rotation,
                    initialRotation),
                Is.LessThanOrEqualTo(0.001f));
        }

        private void SetField(string fieldName, object value)
        {
            var field = typeof(AvatarCalibrationController)
                .GetField(fieldName, PrivateInstance);
            Assert.That(
                field,
                Is.Not.Null,
                $"Missing field {fieldName}.");
            field.SetValue(_controller, value);
        }

        private sealed class FakeHeadTrackingValiditySource :
            IHeadTrackingValiditySource
        {
            private readonly bool _isValid;

            public FakeHeadTrackingValiditySource(bool isValid)
            {
                _isValid = isValid;
            }

            public bool IsHeadTrackingValid()
            {
                return _isValid;
            }
        }
    }
}
