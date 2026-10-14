using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using IVRC.Desktop;

namespace IVRC.Tests
{
    public sealed class StageTransitionControllerTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance |
            BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic =
            BindingFlags.Static |
            BindingFlags.NonPublic;
        private const float PositionTolerance = 0.001f;
        private const float AngleTolerance = 0.05f;

        private GameObject _testRoot;
        private OVRCameraRig _cameraRig;
        private StageTransitionController _transition;
        private Transform _trainingArrival;
        private Transform _stageArrival;
        private Transform _judgeArrival;

        [SetUp]
        public void SetUp()
        {
            _testRoot = new GameObject(
                "StageTransitionControllerTests_Root");
            _testRoot.SetActive(false);

            var rigObject = new GameObject("CameraRig");
            rigObject.transform.SetParent(
                _testRoot.transform,
                false);
            _cameraRig = rigObject.AddComponent<OVRCameraRig>();
            _cameraRig.EnsureGameObjectIntegrity();

            _cameraRig.trackingSpace.SetPositionAndRotation(
                new Vector3(1.5f, 0.25f, -2.25f),
                Quaternion.Euler(0f, 22f, 0f));
            _cameraRig.centerEyeAnchor.localPosition =
                new Vector3(0.18f, 1.62f, -0.12f);
            _cameraRig.centerEyeAnchor.localRotation =
                Quaternion.Euler(0f, 17f, 0f);

            _trainingArrival = CreateArrivalPoint(
                "TrainingArrival",
                _cameraRig.centerEyeAnchor.position,
                HorizontalYaw(
                    _cameraRig.centerEyeAnchor.forward));
            _stageArrival = CreateArrivalPoint(
                "StageArrival",
                new Vector3(-3.5f, 0f, 4.75f),
                -68f);
            _judgeArrival = CreateArrivalPoint(
                "JudgeArrival",
                new Vector3(0f, 0f, 20.8f),
                180f);

            var transitionObject =
                new GameObject("StageTransition");
            transitionObject.transform.SetParent(
                _testRoot.transform,
                false);
            _transition =
                transitionObject.AddComponent<
                    StageTransitionController>();
            SetField(
                "_headTrackingValiditySource",
                new FakeHeadTrackingValiditySource(true));
            SetField("_cameraRig", _cameraRig);
            SetField(
                "_trainingArrivalPoint",
                _trainingArrival);
            SetField("_stageArrivalPoint", _stageArrival);
            SetField("_judgeArrivalPoint", _judgeArrival);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_testRoot);
        }

        [Test]
        public void RepeatedRoundTripsDoNotAccumulatePoseOffset()
        {
            var initialEyePosition =
                _cameraRig.centerEyeAnchor.position;
            var initialEyeForward =
                Horizontal(
                    _cameraRig.centerEyeAnchor.forward);
            var initialTrackingHeight =
                _cameraRig.trackingSpace.position.y;
            var initialRigPosition =
                _cameraRig.transform.position;
            var initialRigRotation =
                _cameraRig.transform.rotation;

            for (var round = 0; round < 2; round++)
            {
                Assert.That(
                    _transition.MoveToStageImmediately(),
                    Is.True,
                    $"Stage move failed on round {round + 1}.");
                AssertEyeAlignedTo(
                    _stageArrival,
                    initialEyePosition.y);
                Assert.That(
                    _cameraRig.trackingSpace.position.y,
                    Is.EqualTo(initialTrackingHeight)
                        .Within(PositionTolerance));

                _transition.RestoreTrainingPose();

                Assert.That(
                    _cameraRig.centerEyeAnchor.position.x,
                    Is.EqualTo(initialEyePosition.x)
                        .Within(PositionTolerance));
                Assert.That(
                    _cameraRig.centerEyeAnchor.position.y,
                    Is.EqualTo(initialEyePosition.y)
                        .Within(PositionTolerance));
                Assert.That(
                    _cameraRig.centerEyeAnchor.position.z,
                    Is.EqualTo(initialEyePosition.z)
                        .Within(PositionTolerance));
                Assert.That(
                    Vector3.Angle(
                        Horizontal(
                            _cameraRig.centerEyeAnchor.forward),
                        initialEyeForward),
                    Is.LessThanOrEqualTo(AngleTolerance));
                Assert.That(
                    _cameraRig.trackingSpace.position.y,
                    Is.EqualTo(initialTrackingHeight)
                        .Within(PositionTolerance));
            }

            Assert.That(
                _cameraRig.transform.position,
                Is.EqualTo(initialRigPosition));
            Assert.That(
                Quaternion.Angle(
                    _cameraRig.transform.rotation,
                    initialRigRotation),
                Is.LessThanOrEqualTo(AngleTolerance));
        }

        [Test]
        public void DestinationValuesRemainBackwardCompatible()
        {
            Assert.That(
                (int)StageTransitionDestination.Stage,
                Is.Zero);
            Assert.That(
                (int)StageTransitionDestination.Training,
                Is.EqualTo(1));
            Assert.That(
                (int)StageTransitionDestination.Judges,
                Is.EqualTo(2));
        }

        [Test]
        public void JudgesDestinationUsesJudgeArrivalAndLogName()
        {
            var getArrivalPoint =
                typeof(StageTransitionController).GetMethod(
                    "GetArrivalPoint",
                    PrivateInstance);
            Assert.That(getArrivalPoint, Is.Not.Null);
            Assert.That(
                getArrivalPoint.Invoke(
                    _transition,
                    new object[]
                    {
                        StageTransitionDestination.Judges
                    }),
                Is.SameAs(_judgeArrival));
            Assert.That(
                _transition.JudgeArrivalPoint,
                Is.SameAs(_judgeArrival));

            var getDestinationName =
                typeof(StageTransitionController).GetMethod(
                    "GetDestinationName",
                    PrivateStatic);
            Assert.That(getDestinationName, Is.Not.Null);
            Assert.That(
                getDestinationName.Invoke(
                    null,
                    new object[]
                    {
                        StageTransitionDestination.Judges
                    }),
                Is.EqualTo("judges"));
        }

        [Test]
        public void JudgesDestinationRequiresAssignedArrivalPoint()
        {
            SetField("_judgeArrivalPoint", null);
            var canBeginTransition =
                typeof(StageTransitionController).GetMethod(
                    "CanBeginTransition",
                    PrivateInstance);
            Assert.That(canBeginTransition, Is.Not.Null);
            LogAssert.Expect(
                LogType.Error,
                "[StageTransition] The requested arrival point " +
                "is missing.");

            Assert.That(
                canBeginTransition.Invoke(
                    _transition,
                    new object[]
                    {
                        StageTransitionDestination.Judges
                    }),
                Is.False);
        }

        [Test]
        public void InvalidHeadTrackingRefusesRelocationWithoutPoseDrift()
        {
            SetField(
                "_headTrackingValiditySource",
                new FakeHeadTrackingValiditySource(false));
            var initialTrackingPosition =
                _cameraRig.trackingSpace.position;
            var initialTrackingRotation =
                _cameraRig.trackingSpace.rotation;
            var initialEyePosition =
                _cameraRig.centerEyeAnchor.position;
            LogAssert.Expect(
                LogType.Error,
                "[StageTransition] HMD position and rotation " +
                "tracking are not valid. Relocation was refused.");
            LogAssert.Expect(
                LogType.Error,
                "[StageTransition] Fade completed, but the play space " +
                "could not be aligned to stage.");

            Assert.That(
                _transition.MoveToStageImmediately(),
                Is.False);
            Assert.That(_transition.HasRelocated, Is.False);
            Assert.That(
                _cameraRig.trackingSpace.position,
                Is.EqualTo(initialTrackingPosition));
            Assert.That(
                Quaternion.Angle(
                    _cameraRig.trackingSpace.rotation,
                    initialTrackingRotation),
                Is.LessThanOrEqualTo(AngleTolerance));
            Assert.That(
                _cameraRig.centerEyeAnchor.position,
                Is.EqualTo(initialEyePosition));
        }

        [Test]
        public void DesktopSimulationAllowsAuthoredPoseWithoutXrTracking()
        {
            SetField(
                "_headTrackingValiditySource",
                new FakeHeadTrackingValiditySource(false));
            var desktopObject = new GameObject("DesktopSimulation");
            desktopObject.transform.SetParent(
                _testRoot.transform,
                false);
            var desktop = desktopObject.AddComponent<
                DesktopSimulationManager>();
            typeof(DesktopSimulationManager)
                .GetProperty(
                    nameof(DesktopSimulationManager.IsDesktopActive))
                ?.SetValue(desktop, true);
            SetField("_desktopSimulation", desktop);

            Assert.That(
                _transition.MoveToStageImmediately(),
                Is.True);
            Assert.That(_transition.HasRelocated, Is.True);
            AssertEyeAlignedTo(
                _stageArrival,
                _cameraRig.centerEyeAnchor.position.y);
        }

        private Transform CreateArrivalPoint(
            string name,
            Vector3 position,
            float yaw)
        {
            var arrival = new GameObject(name).transform;
            arrival.SetParent(_testRoot.transform, false);
            arrival.SetPositionAndRotation(
                position,
                Quaternion.Euler(0f, yaw, 0f));
            return arrival;
        }

        private void AssertEyeAlignedTo(
            Transform arrival,
            float expectedHeight)
        {
            var eye = _cameraRig.centerEyeAnchor;
            Assert.That(
                eye.position.x,
                Is.EqualTo(arrival.position.x)
                    .Within(PositionTolerance));
            Assert.That(
                eye.position.y,
                Is.EqualTo(expectedHeight)
                    .Within(PositionTolerance));
            Assert.That(
                eye.position.z,
                Is.EqualTo(arrival.position.z)
                    .Within(PositionTolerance));
            Assert.That(
                Vector3.Angle(
                    Horizontal(eye.forward),
                    Horizontal(arrival.forward)),
                Is.LessThanOrEqualTo(AngleTolerance));
        }

        private static Vector3 Horizontal(Vector3 direction)
        {
            var horizontal = Vector3.ProjectOnPlane(
                direction,
                Vector3.up);
            return horizontal.normalized;
        }

        private static float HorizontalYaw(Vector3 direction)
        {
            var horizontal = Horizontal(direction);
            return Mathf.Atan2(
                       horizontal.x,
                       horizontal.z) *
                   Mathf.Rad2Deg;
        }

        private void SetField(string fieldName, object value)
        {
            var field = typeof(StageTransitionController)
                .GetField(fieldName, PrivateInstance);
            Assert.That(
                field,
                Is.Not.Null,
                $"Missing field {fieldName}.");
            field.SetValue(_transition, value);
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
