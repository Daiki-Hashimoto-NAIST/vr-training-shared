using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class PosingScoreCalculatorTests
    {
        private static readonly PosingScoreTarget Target =
            new PosingScoreTarget
            {
                LeftHandPositionFromHeadNormalized =
                    new Vector3(-0.3f, 0.2f, 0.1f),
                RightHandPositionFromHeadNormalized =
                    new Vector3(0.3f, 0.2f, 0.1f),
                HeadEulerDegreesInStageFrame =
                    new Vector3(5f, 0f, 0f),
                LeftHandEulerDegreesInStageFrame =
                    new Vector3(0f, 0f, 15f),
                RightHandEulerDegreesInStageFrame =
                    new Vector3(0f, 0f, -15f)
            };

        [Test]
        public void CorrectPoseScoresOneHundredAndWrongPoseScoresLow()
        {
            var settings = CreateSettings(requiredHoldSeconds: 0f);
            var correct = CreateTargetSample(
                1.8f,
                new Vector3(2f, 1f, -3f),
                20f);

            var correctResult = Evaluate(
                correct,
                20f,
                new PosingBodyScales(1.8f, 0f),
                settings,
                default,
                0f,
                out _);

            Assert.That(correctResult.IsValid, Is.True);
            Assert.That(correctResult.PositionScore, Is.EqualTo(100f));
            Assert.That(correctResult.RotationScore, Is.EqualTo(100f));
            Assert.That(correctResult.SymmetryScore, Is.EqualTo(100f));
            Assert.That(correctResult.HoldScore, Is.EqualTo(100f));
            Assert.That(correctResult.TotalScore, Is.EqualTo(100f));

            var yaw = Quaternion.Euler(0f, 20f, 0f);
            var wrong = new ThreePointPoseSample(
                correct.Head,
                new Pose(
                    correct.LeftHand.position,
                    yaw * Quaternion.Euler(0f, 90f, 0f)),
                new Pose(
                    correct.RightHand.position +
                    yaw * (Vector3.up * 1.8f),
                    yaw * Quaternion.Euler(0f, -90f, 0f)));
            var wrongResult = Evaluate(
                wrong,
                20f,
                new PosingBodyScales(1.8f, 0f),
                settings,
                default,
                0f,
                out _);

            Assert.That(wrongResult.IsValid, Is.True);
            Assert.That(wrongResult.TotalScore, Is.LessThan(35f));
            AssertScoresAreBounded(wrongResult);
        }

        [Test]
        public void ZeroSecondHoldStillRequiresSpatialThreshold()
        {
            var settings = CreateSettings(requiredHoldSeconds: 0f);
            var target = CreateTargetSample(
                1.8f,
                Vector3.zero,
                0f);
            var wrong = new ThreePointPoseSample(
                target.Head,
                new Pose(
                    target.LeftHand.position + Vector3.down * 1.8f,
                    target.LeftHand.rotation),
                new Pose(
                    target.RightHand.position + Vector3.down * 1.8f,
                    target.RightHand.rotation));

            var result = Evaluate(
                wrong,
                0f,
                new PosingBodyScales(1.8f, 0f),
                settings,
                default,
                0f,
                out _);

            Assert.That(result.HoldScore, Is.EqualTo(100f));
            Assert.That(result.IsHolding, Is.False);
        }

        [Test]
        public void ZeroSpatialWeightsCannotQualifyForHold()
        {
            var settings = new PosingScoreSettings(
                PoseScaleBasis.HmdHeight,
                0.25f,
                Target,
                new PosingScoreTolerances
                {
                    PositionPerfect = 0f,
                    PositionMiss = 0.25f,
                    RotationPerfectDegrees = 0f,
                    RotationMissDegrees = 45f,
                    SymmetryPerfect = 0f,
                    SymmetryMiss = 0.25f
                },
                new PosingScoreWeights
                {
                    Position = 0f,
                    Rotation = 0f,
                    Symmetry = 0f,
                    Hold = 1f
                },
                new PosingHoldSettings
                {
                    RequiredSeconds = 0f,
                    MinimumSpatialScore = 0f,
                    BreakGraceSeconds = 0f
                });
            var sample = CreateTargetSample(
                1.8f,
                Vector3.zero,
                0f);

            var result = Evaluate(
                sample,
                0f,
                new PosingBodyScales(1.8f, 0f),
                settings,
                default,
                0f,
                out _);

            Assert.That(result.SpatialScore, Is.EqualTo(0f));
            Assert.That(result.IsHolding, Is.False);
        }

        [Test]
        public void ConfiguredProfileRequiresSomeRotationAccuracy()
        {
            var profile = AssetDatabase.LoadAssetAtPath<
                PosingScoreProfile>(
                "Assets/IVRC/Config/" +
                "FrontDoubleBicepsProfile.asset");
            Assert.That(profile, Is.Not.Null);

            var settings = profile.ToSettings();
            const float bodyScale = 1.8f;
            var target = settings.Target;
            var wrongRotation =
                Quaternion.Euler(0f, 180f, 0f);
            var sample = new ThreePointPoseSample(
                new Pose(Vector3.zero, wrongRotation),
                new Pose(
                    target.LeftHandPositionFromHeadNormalized *
                    bodyScale,
                    wrongRotation),
                new Pose(
                    target.RightHandPositionFromHeadNormalized *
                    bodyScale,
                    wrongRotation));

            var result = Evaluate(
                sample,
                0f,
                new PosingBodyScales(bodyScale, 0f),
                settings,
                default,
                0f,
                out _);

            Assert.That(result.PositionScore, Is.EqualTo(100f));
            Assert.That(result.SymmetryScore, Is.EqualTo(100f));
            Assert.That(result.RotationScore, Is.EqualTo(0f));
            Assert.That(result.IsHolding, Is.False);
        }

        [Test]
        public void HandsInTheRightPlaceTurnedTheWrongWayNowLoseMarks()
        {
            var profile = AssetDatabase.LoadAssetAtPath<
                PosingScoreProfile>(
                "Assets/IVRC/Config/" +
                "FrontDoubleBicepsProfile.asset");
            Assert.That(profile, Is.Not.Null);

            var settings = profile.ToSettings();
            const float bodyScale = 1.8f;
            var target = settings.Target;
            var headRotation =
                Quaternion.Euler(
                    target.HeadEulerDegreesInStageFrame);
            var backwardRotation =
                Quaternion.Euler(0f, 180f, 0f);
            var sample = new ThreePointPoseSample(
                new Pose(Vector3.zero, headRotation),
                new Pose(
                    target.LeftHandPositionFromHeadNormalized *
                    bodyScale,
                    backwardRotation),
                new Pose(
                    target.RightHandPositionFromHeadNormalized *
                    bodyScale,
                    backwardRotation));

            var result = Evaluate(
                sample,
                0f,
                new PosingBodyScales(bodyScale, 0f),
                settings,
                default,
                0f,
                out _);

            // The hands are exactly where the pose asks and turned right
            // around. Before the targets were captured from a body this
            // scored full marks, because the rotation targets were identity
            // and grading them would have demanded unrotated controllers.
            // Now that a real wrist angle is recorded, the wrong one costs.
            Assert.That(result.PositionScore, Is.EqualTo(100f));
            Assert.That(result.SymmetryScore, Is.EqualTo(100f));
            Assert.That(
                result.RotationScore,
                Is.LessThan(50f),
                "A wrist turned right around should not pass.");
        }

        [Test]
        public void HeadRotationIsAlwaysGraded()
        {
            // Facing away from the audience must still fail, even while the
            // hand targets are unset: the head target is authored.
            var profile = AssetDatabase.LoadAssetAtPath<
                PosingScoreProfile>(
                "Assets/IVRC/Config/" +
                "FrontDoubleBicepsProfile.asset");
            var settings = profile.ToSettings();
            const float bodyScale = 1.8f;
            var target = settings.Target;
            var turnedAway = Quaternion.Euler(0f, 180f, 0f);
            var sample = new ThreePointPoseSample(
                new Pose(Vector3.zero, turnedAway),
                new Pose(
                    target.LeftHandPositionFromHeadNormalized *
                    bodyScale,
                    Quaternion.identity),
                new Pose(
                    target.RightHandPositionFromHeadNormalized *
                    bodyScale,
                    Quaternion.identity));

            var result = Evaluate(
                sample,
                0f,
                new PosingBodyScales(bodyScale, 0f),
                settings,
                default,
                0f,
                out _);

            Assert.That(result.RotationScore, Is.LessThan(25f));
            Assert.That(result.IsHolding, Is.False);
        }

        [Test]
        public void CommonWorldTranslationDoesNotChangeScore()
        {
            var settings = CreateSettings(requiredHoldSeconds: 0f);
            var first = CreateTargetSample(
                1.7f,
                Vector3.zero,
                0f);
            var second = CreateTargetSample(
                1.7f,
                new Vector3(12f, -4f, 7f),
                0f);

            var firstResult = Evaluate(
                first,
                0f,
                new PosingBodyScales(1.7f, 0f),
                settings,
                default,
                0f,
                out _);
            var secondResult = Evaluate(
                second,
                0f,
                new PosingBodyScales(1.7f, 0f),
                settings,
                default,
                0f,
                out _);

            Assert.That(
                secondResult.TotalScore,
                Is.EqualTo(firstResult.TotalScore).Within(0.0001f));
        }

        [Test]
        public void RotatingSampleAndStageYawTogetherDoesNotChangeScore()
        {
            var settings = CreateSettings(requiredHoldSeconds: 0f);
            var zeroYaw = CreateTargetSample(
                1.8f,
                new Vector3(1f, 2f, 3f),
                0f);
            var turned = CreateTargetSample(
                1.8f,
                new Vector3(1f, 2f, 3f),
                73f);

            var zeroResult = Evaluate(
                zeroYaw,
                0f,
                new PosingBodyScales(1.8f, 0f),
                settings,
                default,
                0f,
                out _);
            var turnedResult = Evaluate(
                turned,
                73f,
                new PosingBodyScales(1.8f, 0f),
                settings,
                default,
                0f,
                out _);

            Assert.That(
                turnedResult.PositionScore,
                Is.EqualTo(zeroResult.PositionScore).Within(0.0001f));
            Assert.That(
                turnedResult.RotationScore,
                Is.EqualTo(zeroResult.RotationScore).Within(0.0001f));
            Assert.That(
                turnedResult.TotalScore,
                Is.EqualTo(zeroResult.TotalScore).Within(0.0001f));
        }

        [Test]
        public void ScalingOffsetsAndSelectedBodyScaleTogetherIsInvariant()
        {
            var hmdSettings = CreateSettings(
                requiredHoldSeconds: 0f,
                scaleBasis: PoseScaleBasis.HmdHeight);
            var armSettings = CreateSettings(
                requiredHoldSeconds: 0f,
                scaleBasis: PoseScaleBasis.ArmLength);
            var small = CreateTargetSample(
                1.25f,
                Vector3.zero,
                0f);
            var large = CreateTargetSample(
                2.1f,
                Vector3.zero,
                0f);

            var smallResult = Evaluate(
                small,
                0f,
                new PosingBodyScales(1.25f, 9f),
                hmdSettings,
                default,
                0f,
                out _);
            var largeResult = Evaluate(
                large,
                0f,
                new PosingBodyScales(9f, 2.1f),
                armSettings,
                default,
                0f,
                out _);

            Assert.That(smallResult.TotalScore, Is.EqualTo(100f));
            Assert.That(largeResult.TotalScore, Is.EqualTo(100f));
            Assert.That(
                largeResult.AppliedScaleBasis,
                Is.EqualTo(PoseScaleBasis.ArmLength));
        }

        [Test]
        public void MissingPreferredScaleFallsBackToOtherBodyScale()
        {
            var settings = CreateSettings(
                requiredHoldSeconds: 0f,
                scaleBasis: PoseScaleBasis.ArmLength);
            var sample = CreateTargetSample(
                1.75f,
                Vector3.zero,
                0f);

            var result = Evaluate(
                sample,
                0f,
                new PosingBodyScales(1.75f, 0f),
                settings,
                default,
                0f,
                out _);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.TotalScore, Is.EqualTo(100f));
            Assert.That(
                result.AppliedScaleBasis,
                Is.EqualTo(PoseScaleBasis.HmdHeight));
        }

        [Test]
        public void BreakingLeftRightRelationReducesSymmetryScore()
        {
            var settings = CreateSettings(requiredHoldSeconds: 0f);
            var correct = CreateTargetSample(
                2f,
                Vector3.zero,
                0f);
            var asymmetric = new ThreePointPoseSample(
                correct.Head,
                correct.LeftHand,
                new Pose(
                    correct.RightHand.position + Vector3.up,
                    correct.RightHand.rotation));

            var correctResult = Evaluate(
                correct,
                0f,
                new PosingBodyScales(2f, 0f),
                settings,
                default,
                0f,
                out _);
            var asymmetricResult = Evaluate(
                asymmetric,
                0f,
                new PosingBodyScales(2f, 0f),
                settings,
                default,
                0f,
                out _);

            Assert.That(correctResult.SymmetryScore, Is.EqualTo(100f));
            Assert.That(
                asymmetricResult.SymmetryScore,
                Is.LessThan(correctResult.SymmetryScore));
        }

        [Test]
        public void QualifyingPoseAccumulatesHoldToOneHundred()
        {
            var settings = CreateSettings(
                requiredHoldSeconds: 1f,
                breakGraceSeconds: 0.25f);
            var sample = CreateTargetSample(
                1.8f,
                Vector3.zero,
                0f);

            var first = Evaluate(
                sample,
                0f,
                new PosingBodyScales(1.8f, 0f),
                settings,
                default,
                0.4f,
                out var firstState);
            var second = Evaluate(
                sample,
                0f,
                new PosingBodyScales(1.8f, 0f),
                settings,
                firstState,
                0.6f,
                out var secondState);

            Assert.That(first.HoldScore, Is.EqualTo(40f).Within(0.001f));
            Assert.That(firstState.HeldSeconds, Is.EqualTo(0.4f));
            Assert.That(second.HoldScore, Is.EqualTo(100f));
            Assert.That(secondState.HeldSeconds, Is.EqualTo(1f));
        }

        [Test]
        public void WrongPosePreservesHoldInsideGraceThenResetsIt()
        {
            var settings = CreateSettings(
                requiredHoldSeconds: 1f,
                breakGraceSeconds: 0.25f);
            var correct = CreateTargetSample(
                1.8f,
                Vector3.zero,
                0f);
            var wrong = OffsetRightHand(
                correct,
                Vector3.up * 2f);

            Evaluate(
                correct,
                0f,
                new PosingBodyScales(1.8f, 0f),
                settings,
                default,
                0.5f,
                out var heldState);
            var insideGrace = Evaluate(
                wrong,
                0f,
                new PosingBodyScales(1.8f, 0f),
                settings,
                heldState,
                0.2f,
                out var graceState);
            var afterGrace = Evaluate(
                wrong,
                0f,
                new PosingBodyScales(1.8f, 0f),
                settings,
                graceState,
                0.06f,
                out var resetState);

            Assert.That(insideGrace.IsHolding, Is.False);
            Assert.That(insideGrace.HoldScore, Is.EqualTo(50f));
            Assert.That(graceState.HeldSeconds, Is.EqualTo(0.5f));
            Assert.That(afterGrace.HoldScore, Is.EqualTo(0f));
            Assert.That(resetState.HeldSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void TrackingLossDoesNotAccumulateAndResetsAfterGrace()
        {
            var settings = CreateSettings(
                requiredHoldSeconds: 1f,
                breakGraceSeconds: 0.25f);
            var tracked = CreateTargetSample(
                1.8f,
                Vector3.zero,
                0f);
            var untracked = new ThreePointPoseSample(
                tracked.Head,
                tracked.LeftHand,
                tracked.RightHand,
                true,
                false,
                true);

            Evaluate(
                tracked,
                0f,
                new PosingBodyScales(1.8f, 0f),
                settings,
                default,
                0.4f,
                out var heldState);
            var insideGrace = Evaluate(
                untracked,
                0f,
                new PosingBodyScales(1.8f, 0f),
                settings,
                heldState,
                0.1f,
                out var graceState);
            var afterGrace = Evaluate(
                untracked,
                0f,
                new PosingBodyScales(1.8f, 0f),
                settings,
                graceState,
                0.16f,
                out var resetState);

            Assert.That(insideGrace.IsValid, Is.False);
            Assert.That(insideGrace.TotalScore, Is.EqualTo(0f));
            Assert.That(graceState.HeldSeconds, Is.EqualTo(0.4f));
            Assert.That(afterGrace.HoldScore, Is.EqualTo(0f));
            Assert.That(resetState.HeldSeconds, Is.EqualTo(0f));
            AssertScoresAreBounded(afterGrace);
        }

        private static PoseScoreResult Evaluate(
            ThreePointPoseSample sample,
            float stageYawDegrees,
            PosingBodyScales bodyScales,
            PosingScoreSettings settings,
            PoseHoldState previousHold,
            float deltaTime,
            out PoseHoldState nextHold)
        {
            return PosingScoreCalculator.Evaluate(
                sample,
                stageYawDegrees,
                bodyScales,
                settings,
                previousHold,
                deltaTime,
                out nextHold);
        }

        /// <summary>
        /// The point of capturing a reference from a real body: whatever was
        /// standing there when it was recorded has to score as correct
        /// afterwards. The shipped targets are typed-in round numbers, so a
        /// participant holding the pose properly was being marked down against
        /// a pose nobody had ever actually stood in.
        /// </summary>
        [Test]
        public void APoseCapturedFromLifeScoresPerfectlyWhenRepeated()
        {
            const float bodyScale = 1.72f;
            const float stageYaw = 34f;
            var headPosition = new Vector3(-1.4f, 1.6f, 2.9f);

            // Something a person is actually standing in, unrelated to the
            // round numbers in Target.
            var performed = new ThreePointPoseSample(
                new Pose(
                    headPosition,
                    Quaternion.Euler(4f, stageYaw + 7f, -2f)),
                new Pose(
                    headPosition + new Vector3(-0.37f, 0.11f, 0.19f),
                    Quaternion.Euler(12f, stageYaw - 40f, 63f)),
                new Pose(
                    headPosition + new Vector3(0.41f, 0.08f, 0.14f),
                    Quaternion.Euler(9f, stageYaw + 44f, -58f)),
                true,
                true,
                true);

            var captured = PosingScoreCalculator.BuildTarget(
                performed,
                stageYaw,
                bodyScale);

            var settings = new PosingScoreSettings(
                PoseScaleBasis.HmdHeight,
                0.25f,
                captured,
                new PosingScoreTolerances
                {
                    PositionPerfect = 0.001f,
                    PositionMiss = 0.25f,
                    RotationPerfectDegrees = 0.1f,
                    RotationMissDegrees = 45f,
                    SymmetryPerfect = 0.001f,
                    SymmetryMiss = 0.25f
                },
                new PosingScoreWeights
                {
                    Position = 1f,
                    Rotation = 1f,
                    Symmetry = 0f,
                    Hold = 0f
                },
                new PosingHoldSettings
                {
                    RequiredSeconds = 0f,
                    MinimumSpatialScore = 80f,
                    BreakGraceSeconds = 0.25f
                },
                true);

            var result = Evaluate(
                performed,
                stageYaw,
                new PosingBodyScales(bodyScale, 0f),
                settings,
                default,
                0f,
                out _);

            Assert.That(result.IsValid, Is.True);
            Assert.That(
                result.PositionScore,
                Is.EqualTo(100f).Within(0.01f));
            Assert.That(
                result.RotationScore,
                Is.EqualTo(100f).Within(0.01f),
                "Hand rotation is scored here, so a captured pose has to " +
                "reproduce those angles too.");
        }

        /// <summary>
        /// Capture has to be independent of where the participant was standing
        /// and which way the stage faces, or a reference recorded once would
        /// only work from that spot.
        /// </summary>
        [Test]
        public void CaptureIgnoresWhereThePersonStood()
        {
            var offsets = new Vector3(-0.3f, 0.15f, 0.2f);
            PosingScoreTarget? first = null;

            foreach (var (head, yaw) in new[]
            {
                (new Vector3(0f, 1.6f, 0f), 0f),
                (new Vector3(5f, 1.6f, -3f), 90f),
                (new Vector3(-2f, 1.6f, 8f), 213f)
            })
            {
                var stage = Quaternion.Euler(0f, yaw, 0f);
                var sample = new ThreePointPoseSample(
                    new Pose(head, stage),
                    new Pose(head + stage * offsets, stage),
                    new Pose(
                        head + stage * new Vector3(
                            -offsets.x,
                            offsets.y,
                            offsets.z),
                        stage),
                    true,
                    true,
                    true);

                var captured = PosingScoreCalculator.BuildTarget(
                    sample,
                    yaw,
                    1.8f);
                if (first == null)
                {
                    first = captured;
                    continue;
                }

                Assert.That(
                    Vector3.Distance(
                        captured.LeftHandPositionFromHeadNormalized,
                        first.Value.LeftHandPositionFromHeadNormalized),
                    Is.LessThan(0.0001f),
                    $"Standing at {head} facing {yaw} recorded a " +
                    "different pose.");
            }
        }

        /// <summary>
        /// The guide places an elbow from a hint; capture has to produce the
        /// hint that puts it back where the body had it. Two implementations
        /// of that arithmetic would be free to drift, and a drifted elbow is
        /// what makes the guide show a pose nobody asked for.
        /// </summary>
        [Test]
        public void ACapturedElbowPutsTheGuidesElbowBackWhereItWas()
        {
            const float bodyScale = 1.68f;
            var stageRotation = Quaternion.Euler(0f, 47f, 0f);
            var shoulder = new Vector3(0.2f, 1.4f, -0.6f);
            var elbow = new Vector3(0.55f, 1.31f, -0.42f);

            var hint = PoseGhostGuideController.BuildElbowHint(
                shoulder,
                elbow,
                stageRotation,
                bodyScale);
            var restored =
                PoseGhostGuideController.CalculateElbowPoleFromHint(
                    shoulder,
                    stageRotation,
                    hint,
                    bodyScale);

            Assert.That(
                Vector3.Distance(restored, elbow),
                Is.LessThan(0.0001f));
        }

        [Test]
        public void AnElbowHintIsIndependentOfWhereThePersonStood()
        {
            const float bodyScale = 1.7f;
            var offset = new Vector3(0.3f, -0.1f, 0.12f);
            Vector3? first = null;

            foreach (var (shoulder, yaw) in new[]
            {
                (new Vector3(0f, 1.4f, 0f), 0f),
                (new Vector3(4f, 1.4f, -2f), 120f),
                (new Vector3(-3f, 1.4f, 6f), 300f)
            })
            {
                var stage = Quaternion.Euler(0f, yaw, 0f);
                var hint = PoseGhostGuideController.BuildElbowHint(
                    shoulder,
                    shoulder + stage * offset,
                    stage,
                    bodyScale);
                if (first == null)
                {
                    first = hint;
                    continue;
                }

                Assert.That(
                    Vector3.Distance(hint, first.Value),
                    Is.LessThan(0.0001f),
                    $"Standing at {shoulder} facing {yaw} recorded a " +
                    "different elbow.");
            }
        }

        [Test]
        public void AnImpossibleBodyScaleProducesNoElbowHint()
        {
            Assert.That(
                PoseGhostGuideController.BuildElbowHint(
                    Vector3.zero,
                    Vector3.one,
                    Quaternion.identity,
                    0f),
                Is.EqualTo(Vector3.zero));
        }

        private static PosingScoreSettings CreateSettings(
            float requiredHoldSeconds,
            float breakGraceSeconds = 0.25f,
            PoseScaleBasis scaleBasis = PoseScaleBasis.HmdHeight)
        {
            return new PosingScoreSettings(
                scaleBasis,
                0.25f,
                Target,
                new PosingScoreTolerances
                {
                    PositionPerfect = 0.001f,
                    PositionMiss = 0.25f,
                    RotationPerfectDegrees = 0.1f,
                    RotationMissDegrees = 45f,
                    SymmetryPerfect = 0.001f,
                    SymmetryMiss = 0.25f
                },
                new PosingScoreWeights
                {
                    Position = 1f,
                    Rotation = 1f,
                    Symmetry = 1f,
                    Hold = 1f
                },
                new PosingHoldSettings
                {
                    RequiredSeconds = requiredHoldSeconds,
                    MinimumSpatialScore = 80f,
                    BreakGraceSeconds = breakGraceSeconds
                });
        }

        private static ThreePointPoseSample CreateTargetSample(
            float bodyScale,
            Vector3 headPosition,
            float stageYawDegrees)
        {
            var stageRotation =
                Quaternion.Euler(0f, stageYawDegrees, 0f);
            return new ThreePointPoseSample(
                new Pose(
                    headPosition,
                    stageRotation *
                    Quaternion.Euler(
                        Target.HeadEulerDegreesInStageFrame)),
                new Pose(
                    headPosition +
                    stageRotation *
                    (Target.LeftHandPositionFromHeadNormalized *
                     bodyScale),
                    stageRotation *
                    Quaternion.Euler(
                        Target.LeftHandEulerDegreesInStageFrame)),
                new Pose(
                    headPosition +
                    stageRotation *
                    (Target.RightHandPositionFromHeadNormalized *
                     bodyScale),
                    stageRotation *
                    Quaternion.Euler(
                        Target.RightHandEulerDegreesInStageFrame)));
        }

        private static ThreePointPoseSample OffsetRightHand(
            ThreePointPoseSample sample,
            Vector3 offset)
        {
            return new ThreePointPoseSample(
                sample.Head,
                sample.LeftHand,
                new Pose(
                    sample.RightHand.position + offset,
                    sample.RightHand.rotation),
                sample.HeadTracked,
                sample.LeftHandTracked,
                sample.RightHandTracked);
        }

        private static void AssertScoresAreBounded(
            PoseScoreResult result)
        {
            Assert.That(result.PositionScore, Is.InRange(0f, 100f));
            Assert.That(result.RotationScore, Is.InRange(0f, 100f));
            Assert.That(result.SymmetryScore, Is.InRange(0f, 100f));
            Assert.That(result.HoldScore, Is.InRange(0f, 100f));
            Assert.That(result.SpatialScore, Is.InRange(0f, 100f));
            Assert.That(result.TotalScore, Is.InRange(0f, 100f));
        }
    }
}
