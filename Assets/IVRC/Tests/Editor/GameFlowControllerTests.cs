using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IVRC.Tests
{
    public sealed class GameFlowControllerTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance |
            BindingFlags.NonPublic;

        private GameObject _host;
        private GameObject _trainingRoot;
        private GameObject _posingRoot;
        private GameObject _stageEntranceRoot;
        private GameFlowController _flow;
        private FakeStageTransitionDriver _stageTransition;
        private AvatarCalibrationController
            _alignmentCalibration;
        private HeightCalibrationController
            _heightCalibration;
        private AvatarSwapInput _avatarSwapInput;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject(
                "GameFlowControllerTests_Host");
            _host.SetActive(false);
            _flow = _host.AddComponent<GameFlowController>();
            _alignmentCalibration =
                _host.AddComponent<
                    AvatarCalibrationController>();
            _heightCalibration =
                _host.AddComponent<
                    HeightCalibrationController>();
            _avatarSwapInput =
                _host.AddComponent<AvatarSwapInput>();

            _trainingRoot = new GameObject(
                "GameFlowControllerTests_Training");
            _posingRoot = new GameObject(
                "GameFlowControllerTests_Posing");
            _stageEntranceRoot = new GameObject(
                "GameFlowControllerTests_StageEntrance");
            _stageTransition =
                new FakeStageTransitionDriver();

            SetField("_rhythmClock", null);
            SetField("_rhythmScorer", null);
            SetField("_metrics", null);
            SetField("_repCounter", null);
            SetField(
                "_alignmentCalibration",
                _alignmentCalibration);
            SetField(
                "_heightCalibration",
                _heightCalibration);
            SetField(
                "_avatarSwapInput",
                _avatarSwapInput);
            SetField("_stageTransition", null);
            SetField("_stageEntrance", null);
            SetField("_posingScorer", null);
            SetField(
                "_stageTransitionDriverOverride",
                _stageTransition);
            SetField("_trainingRoot", _trainingRoot);
            SetField("_posingRoot", _posingRoot);
            SetField(
                "_stageEntranceRoot",
                _stageEntranceRoot);
            SetField(
                "_stageEntranceFallbackSeconds",
                0.1f);
            SetField("_hideTrainingDuringPosing", true);
            SetField("_requireReadiness", false);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_host);
            UnityEngine.Object.DestroyImmediate(_trainingRoot);
            UnityEngine.Object.DestroyImmediate(_posingRoot);
            UnityEngine.Object.DestroyImmediate(
                _stageEntranceRoot);
        }

        [Test]
        public void FullTimedLoopReturnsToReadyWithoutDuplicatePhases()
        {
            var phases = new List<ExperiencePhase>();
            _flow.PhaseChanged += phases.Add;

            _flow.ResetExperience();
            AssertPhase(
                ExperiencePhase.Setup,
                "PREPARING",
                true,
                false);

            Advance(0f, false);
            AssertPhase(
                ExperiencePhase.Ready,
                "PRESS A TO START",
                true,
                false);

            Advance(0f, true);
            AssertPhase(
                ExperiencePhase.Countdown,
                _flow.CountdownNumber.ToString(),
                true,
                false);

            Advance(100f, false);
            AssertPhase(
                ExperiencePhase.Training,
                "MOVE TO THE BEAT",
                true,
                false);

            Advance(100f, false);
            Assert.That(
                _flow.CurrentPhase,
                Is.EqualTo(ExperiencePhase.Training),
                "Training keeps one final LateUpdate frame " +
                "before snapshotting.");
            Advance(0f, false);
            AssertPhase(
                ExperiencePhase.Transformation,
                "MUSCLE UP!",
                true,
                false);

            Advance(100f, false);
            AssertPhase(
                ExperiencePhase.StageTransition,
                "MOVING TO STAGE",
                false,
                true);
            Assert.That(
                _stageTransition.Destinations,
                Is.EqualTo(new[]
                {
                    StageTransitionDestination.Judges
                }));

            Advance(100f, false);
            AssertPhase(
                ExperiencePhase.StageEntrance,
                "ENTERING THE STAGE",
                false,
                true);

            Advance(100f, false);
            AssertPhase(
                ExperiencePhase.StageTransition,
                "MOVING TO STAGE",
                false,
                true);
            Assert.That(
                _stageTransition.Destinations,
                Is.EqualTo(new[]
                {
                    StageTransitionDestination.Judges,
                    StageTransitionDestination.Stage
                }));

            Advance(100f, false);
            AssertPhase(
                ExperiencePhase.Posing,
                "HOLD YOUR POSE",
                false,
                true);

            Advance(100f, false);
            Assert.That(
                _flow.CurrentPhase,
                Is.EqualTo(ExperiencePhase.Posing),
                "Posing keeps one final LateUpdate frame " +
                "before snapshotting.");
            Advance(0f, false);
            AssertPhase(
                ExperiencePhase.Results,
                "PRESS A TO RETRY",
                false,
                true);

            Advance(0f, true);
            AssertPhase(
                ExperiencePhase.StageTransition,
                "BACK TO MIRROR",
                true,
                false);
            Assert.That(
                _stageTransition.Destinations,
                Is.EqualTo(new[]
                {
                    StageTransitionDestination.Judges,
                    StageTransitionDestination.Stage,
                    StageTransitionDestination.Training
                }));

            Advance(100f, false);
            AssertPhase(
                ExperiencePhase.Setup,
                "PREPARING",
                true,
                false);
            Advance(0f, false);
            AssertPhase(
                ExperiencePhase.Ready,
                "PRESS A TO START",
                true,
                false);

            Assert.That(
                phases,
                Is.EqualTo(new[]
                {
                    ExperiencePhase.Setup,
                    ExperiencePhase.Ready,
                    ExperiencePhase.Countdown,
                    ExperiencePhase.Training,
                    ExperiencePhase.Transformation,
                    ExperiencePhase.StageTransition,
                    ExperiencePhase.StageEntrance,
                    ExperiencePhase.StageTransition,
                    ExperiencePhase.Posing,
                    ExperiencePhase.Results,
                    ExperiencePhase.StageTransition,
                    ExperiencePhase.Setup,
                    ExperiencePhase.Ready
                }));
            Assert.That(
                _flow.CompletedElbowReps,
                Is.Zero);
            Assert.That(
                _flow.CompletedArmRaiseReps,
                Is.Zero);
            Assert.That(
                _flow.CompletedGrowthNormalized,
                Is.Zero);
            Assert.That(
                _flow.CompletedPoseSuccessCount,
                Is.Zero);
            Assert.That(
                _flow.CompletedPoseSucceeded,
                Is.False);
        }

        [Test]
        public void StartInputIsIgnoredDuringActivePhases()
        {
            var phases = new List<ExperiencePhase>();
            _flow.PhaseChanged += phases.Add;

            _flow.ResetExperience();
            Advance(0f, false);
            Advance(0f, true);
            Assert.That(
                _flow.CurrentPhase,
                Is.EqualTo(ExperiencePhase.Countdown));

            var phaseCountBeforeSecondPress =
                phases.Count;
            Advance(0f, true);

            Assert.That(
                _flow.CurrentPhase,
                Is.EqualTo(ExperiencePhase.Countdown));
            Assert.That(
                phases,
                Has.Count.EqualTo(
                    phaseCountBeforeSecondPress));
            Assert.That(
                _stageTransition.Destinations,
                Is.Empty);
        }

        [Test]
        public void DebugRestartReturnsAnActivePhaseToCountdown()
        {
            _flow.ResetExperience();
            Advance(0f, false);
            Advance(0f, true);
            Advance(100f, false);
            Assert.That(
                _flow.CurrentPhase,
                Is.EqualTo(ExperiencePhase.Training));

            _flow.DebugRestartExperience();

            Assert.That(
                _flow.CurrentPhase,
                Is.EqualTo(ExperiencePhase.Countdown));
            Assert.That(_flow.CompletedElbowReps, Is.Zero);
            Assert.That(_flow.CompletedArmRaiseReps, Is.Zero);
            Assert.That(_flow.CompletedPoseSuccessCount, Is.Zero);
        }

        [Test]
        public void TrainingBoundaryFreezesEveryMuscleChannel()
        {
            var scorer =
                _host.AddComponent<TrainingRhythmScorer>();
            SetScorerGrowth(
                scorer,
                0.72f,
                0.2f,
                0.4f,
                0.6f,
                0.8f);
            SetField("_rhythmScorer", scorer);

            var capture = typeof(GameFlowController)
                .GetMethod(
                    "CaptureTrainingResults",
                    PrivateInstance);
            Assert.That(capture, Is.Not.Null);
            capture.Invoke(_flow, null);
            var frozen = _flow.CompletedMuscleGrowth;

            SetScorerGrowth(
                scorer,
                0.1f,
                1f,
                1f,
                1f,
                1f);

            Assert.That(
                _flow.CompletedGrowthNormalized,
                Is.EqualTo(0.72f));
            Assert.That(frozen.Overall, Is.EqualTo(0.72f));
            Assert.That(
                frozen.Get(
                    MuscleGrowthChannel.ElbowFlexionLeft),
                Is.EqualTo(0.2f));
            Assert.That(
                frozen.Get(
                    MuscleGrowthChannel.ArmRaiseRight),
                Is.EqualTo(0.8f));
            Assert.That(
                frozen.Get(MuscleGrowthChannel.ChestLeft),
                Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(
                frozen.Get(MuscleGrowthChannel.ChestRight),
                Is.EqualTo(0.6f).Within(0.0001f));

            _flow.ResetExperience();
            Assert.That(
                _flow.CompletedMuscleGrowth.Overall,
                Is.Zero);
            Assert.That(
                _flow.CompletedMuscleGrowth.Get(
                    MuscleGrowthChannel.ArmRaiseRight),
                Is.Zero);
        }

        [Test]
        public void InvalidDeltaTimeCannotAdvanceCountdown()
        {
            _flow.ResetExperience();
            Advance(0f, false);
            Advance(0f, true);
            var remaining = _flow.PhaseTimeRemaining;

            Advance(float.NaN, false);
            Advance(float.PositiveInfinity, false);
            Advance(-10f, false);

            Assert.That(
                _flow.CurrentPhase,
                Is.EqualTo(ExperiencePhase.Countdown));
            Assert.That(
                _flow.PhaseTimeRemaining,
                Is.EqualTo(remaining));
        }

        [Test]
        public void FailedStageTransitionReturnsSafelyToSetup()
        {
            _flow.ResetExperience();
            Advance(0f, false);
            Advance(0f, true);
            Advance(100f, false);
            Advance(100f, false);
            Advance(0f, false);
            Assert.That(
                _flow.CurrentPhase,
                Is.EqualTo(
                    ExperiencePhase.Transformation));

            _stageTransition.RejectNextBegin = true;
            LogAssert.Expect(
                LogType.Error,
                "[GameFlow] Stage transition could not " +
                "be started.");
            Advance(100f, false);

            AssertPhase(
                ExperiencePhase.Setup,
                "PREPARING",
                true,
                false);
            Assert.That(
                _stageTransition.CancelCount,
                Is.EqualTo(1));
        }

        [Test]
        public void StageEntranceDoesNotWaitForBodyTracking()
        {
            _flow.ResetExperience();
            Advance(0f, false);
            Advance(0f, true);
            Advance(100f, false);
            Advance(100f, false);
            Advance(0f, false);
            Advance(100f, false);
            Advance(100f, false);
            Assert.That(
                _flow.CurrentPhase,
                Is.EqualTo(ExperiencePhase.StageEntrance));

            SetField("_requireReadiness", true);
            Advance(100f, false);

            Assert.That(
                _flow.CurrentPhase,
                Is.EqualTo(
                    ExperiencePhase.StageTransition));
            Assert.That(_flow.IsPausedForTracking, Is.False);

            Advance(100f, false);

            Assert.That(
                _flow.CurrentPhase,
                Is.EqualTo(ExperiencePhase.Posing));
            Assert.That(_flow.IsPausedForTracking, Is.False);
        }

        [Test]
        public void JudgeRelocationShowsStageBeforeFadeCompletes()
        {
            _flow.ResetExperience();
            Advance(0f, false);
            Advance(0f, true);
            Advance(100f, false);
            Advance(100f, false);
            Advance(0f, false);

            _stageTransition.CompleteImmediately = false;
            Advance(100f, false);

            Assert.That(
                _flow.CurrentPhase,
                Is.EqualTo(
                    ExperiencePhase.StageTransition));
            Assert.That(_trainingRoot.activeSelf, Is.True);
            Assert.That(_posingRoot.activeSelf, Is.False);
            Assert.That(
                _stageEntranceRoot.activeSelf,
                Is.False);

            _stageTransition.RelocateWithoutCompleting();
            Advance(0f, false);

            Assert.That(
                _flow.CurrentPhase,
                Is.EqualTo(
                    ExperiencePhase.StageTransition));
            Assert.That(_trainingRoot.activeSelf, Is.False);
            Assert.That(_posingRoot.activeSelf, Is.True);
            Assert.That(
                _stageEntranceRoot.activeSelf,
                Is.False);

            _stageTransition.CompleteSuccessfulTransition();
            Advance(100f, false);

            Assert.That(
                _flow.CurrentPhase,
                Is.EqualTo(
                    ExperiencePhase.StageEntrance));
        }

        [Test]
        public void OutgoingStageTransitionKeepsEntranceRootUntilPosing()
        {
            _flow.ResetExperience();
            Advance(0f, false);
            Advance(0f, true);
            Advance(100f, false);
            Advance(100f, false);
            Advance(0f, false);
            Advance(100f, false);
            Advance(100f, false);
            Assert.That(
                _flow.CurrentPhase,
                Is.EqualTo(
                    ExperiencePhase.StageEntrance));

            _stageTransition.CompleteImmediately = false;
            Advance(100f, false);

            Assert.That(
                _flow.CurrentPhase,
                Is.EqualTo(
                    ExperiencePhase.StageTransition));
            Assert.That(_posingRoot.activeSelf, Is.True);
            Assert.That(
                _stageEntranceRoot.activeSelf,
                Is.True);

            _stageTransition.RelocateWithoutCompleting();
            Advance(0f, false);

            Assert.That(
                _stageEntranceRoot.activeSelf,
                Is.True);

            _stageTransition.CompleteSuccessfulTransition();
            Advance(100f, false);

            Assert.That(
                _flow.CurrentPhase,
                Is.EqualTo(ExperiencePhase.Posing));
            Assert.That(
                _stageEntranceRoot.activeSelf,
                Is.False);
        }

        private void AssertPhase(
            ExperiencePhase expectedPhase,
            string expectedInstruction,
            bool trainingVisible,
            bool posingVisible)
        {
            Assert.That(
                _flow.CurrentPhase,
                Is.EqualTo(expectedPhase));
            Assert.That(
                _flow.InstructionText,
                Is.EqualTo(expectedInstruction));
            Assert.That(
                _trainingRoot.activeSelf,
                Is.EqualTo(trainingVisible));
            Assert.That(
                _posingRoot.activeSelf,
                Is.EqualTo(posingVisible));
            Assert.That(
                _stageEntranceRoot.activeSelf,
                Is.EqualTo(
                    expectedPhase ==
                    ExperiencePhase.StageEntrance ||
                    (expectedPhase ==
                     ExperiencePhase.StageTransition &&
                     _stageTransition.LastDestination ==
                     StageTransitionDestination.Stage)));
            var calibrationInputExpected =
                expectedPhase == ExperiencePhase.Setup ||
                expectedPhase == ExperiencePhase.Ready;
            Assert.That(
                _alignmentCalibration.InputEnabled,
                Is.EqualTo(calibrationInputExpected));
            Assert.That(
                _heightCalibration.InputEnabled,
                Is.EqualTo(calibrationInputExpected));
            Assert.That(
                _avatarSwapInput.InputEnabled,
                Is.EqualTo(calibrationInputExpected));
        }

        private void Advance(
            float deltaTime,
            bool startPressed)
        {
            var advance = typeof(GameFlowController)
                .GetMethod("Advance", PrivateInstance);
            Assert.That(advance, Is.Not.Null);
            advance.Invoke(
                _flow,
                new object[]
                {
                    deltaTime,
                    startPressed
                });
        }

        private void SetField(
            string fieldName,
            object value)
        {
            var field = typeof(GameFlowController)
                .GetField(fieldName, PrivateInstance);
            Assert.That(
                field,
                Is.Not.Null,
                $"Missing test field {fieldName}.");
            field.SetValue(_flow, value);
        }

        private static void SetScorerGrowth(
            TrainingRhythmScorer scorer,
            float overall,
            float elbowLeft,
            float elbowRight,
            float raiseLeft,
            float raiseRight)
        {
            var overallProperty =
                typeof(TrainingRhythmScorer).GetProperty(
                    nameof(
                        TrainingRhythmScorer.GrowthNormalized),
                    BindingFlags.Instance |
                    BindingFlags.Public);
            Assert.That(overallProperty, Is.Not.Null);
            overallProperty.SetValue(scorer, overall);

            var channelsField =
                typeof(TrainingRhythmScorer).GetField(
                    "_channelGrowth",
                    PrivateInstance);
            Assert.That(channelsField, Is.Not.Null);
            var channels =
                channelsField.GetValue(scorer) as float[];
            Assert.That(channels, Is.Not.Null);
            channels[(int)MuscleGrowthChannel.ElbowFlexionLeft] =
                elbowLeft;
            channels[(int)MuscleGrowthChannel.ElbowFlexionRight] =
                elbowRight;
            channels[(int)MuscleGrowthChannel.ArmRaiseLeft] =
                raiseLeft;
            channels[(int)MuscleGrowthChannel.ArmRaiseRight] =
                raiseRight;
        }

        private sealed class FakeStageTransitionDriver :
            IStageTransitionDriver
        {
            public bool IsTransitioning { get; private set; }
            public bool HasCompleted { get; private set; }
            public bool LastTransitionSucceeded
            {
                get;
                private set;
            }

            public bool IsAtStage { get; private set; }
            public bool HasRelocated { get; private set; }
            public float TotalDuration => 0.1f;
            public List<StageTransitionDestination>
                Destinations { get; } = new();
            public StageTransitionDestination LastDestination =>
                Destinations.Count <= 0
                    ? StageTransitionDestination.Training
                    : Destinations[Destinations.Count - 1];
            public bool RejectNextBegin { get; set; }
            public bool CompleteImmediately { get; set; } = true;
            public int CancelCount { get; private set; }

            private StageTransitionDestination
                _activeDestination;

            public bool BeginTransition(
                StageTransitionDestination destination)
            {
                Destinations.Add(destination);
                _activeDestination = destination;
                if (RejectNextBegin)
                {
                    RejectNextBegin = false;
                    IsTransitioning = false;
                    HasCompleted = false;
                    LastTransitionSucceeded = false;
                    HasRelocated = false;
                    return false;
                }

                IsTransitioning = true;
                HasCompleted = false;
                LastTransitionSucceeded = false;
                HasRelocated = false;
                if (CompleteImmediately)
                {
                    RelocateWithoutCompleting();
                    CompleteSuccessfulTransition();
                }

                return true;
            }

            public void RelocateWithoutCompleting()
            {
                IsAtStage =
                    _activeDestination !=
                    StageTransitionDestination.Training;
                HasRelocated = true;
            }

            public void CompleteSuccessfulTransition()
            {
                if (!HasRelocated)
                {
                    RelocateWithoutCompleting();
                }

                IsTransitioning = false;
                HasCompleted = true;
                LastTransitionSucceeded = true;
            }

            public bool BeginSafeRecoveryToTraining()
            {
                _activeDestination =
                    StageTransitionDestination.Training;
                IsAtStage = false;
                IsTransitioning = false;
                HasCompleted = true;
                LastTransitionSucceeded = true;
                HasRelocated = true;
                return true;
            }

            public void CancelAtCurrentLocation()
            {
                CancelCount++;
                IsTransitioning = false;
                HasCompleted = true;
                LastTransitionSucceeded = false;
                HasRelocated = false;
            }

            public void RestoreTrainingPose()
            {
                IsAtStage = false;
                IsTransitioning = false;
                HasCompleted = false;
                LastTransitionSucceeded = false;
                HasRelocated = false;
            }
        }
    }
}
