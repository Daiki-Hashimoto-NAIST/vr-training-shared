using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class PoseTrackingRecoveryTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance |
            BindingFlags.NonPublic;

        [Test]
        public void RecoveryRequiresStableTrackingBeforeReady()
        {
            var state = new PoseTrackingRecoveryState();

            Assert.That(
                state.Observe(false, 0f, 0.35f),
                Is.EqualTo(PoseTrackingReadiness.TrackingLost));
            Assert.That(
                state.Observe(true, 0.20f, 0.35f),
                Is.EqualTo(PoseTrackingReadiness.Settling));
            Assert.That(state.StableSeconds,
                Is.EqualTo(0.20f).Within(0.0001f));
            Assert.That(
                state.Observe(true, 0.15f, 0.35f),
                Is.EqualTo(PoseTrackingReadiness.Settling),
                "The boundary frame stays presentation-only so its full " +
                "delta cannot become hold progress.");
            Assert.That(
                state.Observe(true, 0.01f, 0.35f),
                Is.EqualTo(PoseTrackingReadiness.Ready));
        }

        [Test]
        public void TrackingLossDuringSettlingRestartsTheDelay()
        {
            var state = new PoseTrackingRecoveryState();

            state.Observe(false, 0f, 0.35f);
            state.Observe(true, 0.30f, 0.35f);
            Assert.That(
                state.Observe(false, 0f, 0.35f),
                Is.EqualTo(PoseTrackingReadiness.TrackingLost));
            Assert.That(state.StableSeconds, Is.Zero);
            Assert.That(
                state.Observe(true, 0.10f, 0.35f),
                Is.EqualTo(PoseTrackingReadiness.Settling));
            Assert.That(state.StableSeconds,
                Is.EqualTo(0.10f).Within(0.0001f));
        }

        [TestCase(-1f, 0.10f)]
        [TestCase(0.35f, 0.35f)]
        [TestCase(2f, 0.75f)]
        [TestCase(float.NaN, 0.35f)]
        public void RecoveryDurationIsFiniteAndBounded(
            float configuredSeconds,
            float expectedSeconds)
        {
            Assert.That(
                PoseTrackingRecoverySafety.ClampDuration(
                    configuredSeconds),
                Is.EqualTo(expectedSeconds).Within(0.0001f));
        }

        [Test]
        public void ScorerFreezesHoldWindowAndPoseDuringRecovery()
        {
            var host = new GameObject("Pose Tracking Recovery Test");
            host.SetActive(false);
            var flow = host.AddComponent<GameFlowController>();
            var scorer = host.AddComponent<ThreePointPosingScorer>();
            var profile =
                ScriptableObject.CreateInstance<PosingScoreProfile>();

            try
            {
                SetProperty(
                    flow,
                    nameof(GameFlowController.CurrentPhase),
                    ExperiencePhase.Posing);
                SetField(scorer, "_gameFlow", flow);
                SetField(scorer, "_profiles", new[] { profile });
                SetField(scorer, "_poseWindowSeconds", 10f);
                SetField(scorer, "_trackingRecoverySeconds", 0.35f);
                SetField(scorer, "_calibratedArmLengthMeters", 1.7f);
                scorer.BeginRound();

                var sample = CreateValidSample();
                ProcessSample(scorer, false, default,
                    "LEFT CONTROLLER LOST", 0.1f);
                var windowBefore = scorer.PoseWindowRemaining;
                var holdBefore = scorer.CurrentResult.HeldSeconds;
                var poseBefore = scorer.CurrentPoseIndex;

                ProcessSample(scorer, true, sample, string.Empty, 0.20f);
                ProcessSample(scorer, true, sample, string.Empty, 0.15f);

                Assert.That(scorer.State,
                    Is.EqualTo(PosingRuntimeState.Settling));
                Assert.That(scorer.IsTrackingValid, Is.True);
                Assert.That(scorer.IsReadyForScoring, Is.False);
                Assert.That(scorer.PoseWindowRemaining,
                    Is.EqualTo(windowBefore).Within(0.0001f));
                Assert.That(scorer.CurrentResult.HeldSeconds,
                    Is.EqualTo(holdBefore).Within(0.0001f));
                Assert.That(scorer.CurrentPoseIndex, Is.EqualTo(poseBefore));

                ProcessSample(scorer, true, sample, string.Empty, 0.10f);

                Assert.That(scorer.IsReadyForScoring, Is.True);
                Assert.That(scorer.State,
                    Is.EqualTo(PosingRuntimeState.Countdown));
                Assert.That(scorer.PoseWindowRemaining,
                    Is.EqualTo(windowBefore).Within(0.0001f),
                    "Countdown and pose window both remain paused until " +
                    "normal scoring resumes.");
                Assert.That(scorer.CurrentPoseIndex, Is.EqualTo(poseBefore));
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void GameFlowPausesWholePosingPhaseWhileSettling()
        {
            var host = new GameObject("Pose Recovery Flow Test");
            host.SetActive(false);
            var flow = host.AddComponent<GameFlowController>();
            var scorer = host.AddComponent<ThreePointPosingScorer>();
            var profile =
                ScriptableObject.CreateInstance<PosingScoreProfile>();

            try
            {
                SetField(scorer, "_gameFlow", flow);
                SetField(scorer, "_profiles", new[] { profile });
                SetField(scorer, "_calibratedArmLengthMeters", 1.7f);
                SetField(scorer, "_trackingRecoverySeconds", 0.35f);
                SetField(flow, "_posingScorer", scorer);
                SetField(flow, "_requireReadiness", true);
                SetProperty(
                    flow,
                    nameof(GameFlowController.CurrentPhase),
                    ExperiencePhase.Posing);
                SetProperty(
                    flow,
                    nameof(GameFlowController.CurrentPhaseDuration),
                    10f);
                SetProperty(
                    flow,
                    nameof(GameFlowController.PhaseTimeRemaining),
                    10f);
                scorer.BeginRound();

                var sample = CreateValidSample();
                ProcessSample(scorer, false, default,
                    "RIGHT CONTROLLER LOST", 0.1f);
                ProcessSample(scorer, true, sample, string.Empty, 0.20f);
                Assert.That(scorer.State,
                    Is.EqualTo(PosingRuntimeState.Settling));

                InvokeUpdatePosing(flow, 1f);

                Assert.That(flow.IsPausedForTracking, Is.True);
                Assert.That(flow.PhaseTimeRemaining,
                    Is.EqualTo(10f).Within(0.0001f));

                ProcessSample(scorer, true, sample, string.Empty, 0.15f);
                ProcessSample(scorer, true, sample, string.Empty, 0.01f);
                Assert.That(scorer.IsReadyForScoring, Is.True);

                InvokeUpdatePosing(flow, 1f);

                Assert.That(flow.IsPausedForTracking, Is.False);
                Assert.That(flow.PhaseTimeRemaining,
                    Is.EqualTo(9f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(host);
            }
        }

        private static ThreePointPoseSample CreateValidSample()
        {
            return new ThreePointPoseSample(
                new Pose(new Vector3(0f, 1.7f, 0f), Quaternion.identity),
                new Pose(new Vector3(-0.4f, 1.6f, 0.2f), Quaternion.identity),
                new Pose(new Vector3(0.4f, 1.6f, 0.2f), Quaternion.identity));
        }

        private static void ProcessSample(
            ThreePointPosingScorer scorer,
            bool hasSample,
            ThreePointPoseSample sample,
            string waitingReason,
            float deltaTime)
        {
            var method = typeof(ThreePointPosingScorer).GetMethod(
                "ProcessSample",
                PrivateInstance);
            Assert.That(method, Is.Not.Null);
            method.Invoke(
                scorer,
                new object[]
                {
                    hasSample,
                    sample,
                    waitingReason,
                    deltaTime
                });
        }

        private static void InvokeUpdatePosing(
            GameFlowController flow,
            float deltaTime)
        {
            var method = typeof(GameFlowController).GetMethod(
                "UpdatePosing",
                PrivateInstance);
            Assert.That(method, Is.Not.Null);
            method.Invoke(flow, new object[] { deltaTime });
        }

        private static void SetField<T>(
            object target,
            string fieldName,
            T value)
        {
            var field = target.GetType().GetField(
                fieldName,
                PrivateInstance);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private static void SetProperty<T>(
            object target,
            string propertyName,
            T value)
        {
            var property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null, propertyName);
            property.SetValue(target, value);
        }
    }
}
