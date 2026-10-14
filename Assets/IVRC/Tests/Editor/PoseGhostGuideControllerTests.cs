using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace IVRC.Tests
{
    public sealed class PoseGhostGuideControllerTests
    {
        [Test]
        public void WorldTargetsRemainSymmetricInRotatedStageFrame()
        {
            var head = new Vector3(1.2f, 1.72f, -0.8f);
            var stageRotation =
                Quaternion.Euler(0f, 37f, 0f);

            Assert.That(
                PoseGhostGuideController.TryCalculateWorldTarget(
                    head,
                    new Vector3(-0.3f, -0.02f, 0.05f),
                    stageRotation,
                    1.8f,
                    out var left),
                Is.True);
            Assert.That(
                PoseGhostGuideController.TryCalculateWorldTarget(
                    head,
                    new Vector3(0.3f, -0.02f, 0.05f),
                    stageRotation,
                    1.8f,
                    out var right),
                Is.True);

            AssertVector3(
                PoseGhostGuideController
                    .MirrorAcrossStageSagittalPlane(
                        left,
                        head,
                        stageRotation),
                right,
                0.0001f);

            var localLeft =
                Quaternion.Inverse(stageRotation) *
                (left - head);
            Assert.That(
                localLeft.x,
                Is.EqualTo(-0.54f).Within(0.0001f));
            Assert.That(
                localLeft.y,
                Is.EqualTo(-0.036f).Within(0.0001f));
            Assert.That(
                localLeft.z,
                Is.EqualTo(0.09f).Within(0.0001f));
        }

        [Test]
        public void InvalidWorldTargetInputsReturnFiniteFallback()
        {
            var success =
                PoseGhostGuideController.TryCalculateWorldTarget(
                    new Vector3(float.NaN, 1.6f, 2f),
                    new Vector3(
                        0.3f,
                        float.PositiveInfinity,
                        0f),
                    new Quaternion(
                        0f,
                        float.NaN,
                        0f,
                        1f),
                    float.NaN,
                    out var target);

            Assert.That(success, Is.False);
            AssertVector3(
                target,
                new Vector3(0f, 1.6f, 2f),
                0f);
            AssertFinite(target);
        }

        [Test]
        public void ElbowOutPolesAreSymmetricAroundStage()
        {
            var stageOrigin =
                new Vector3(-0.4f, 1.3f, 2.1f);
            var stageRotation =
                Quaternion.Euler(0f, -28f, 0f);
            var stageRight =
                stageRotation * Vector3.right;
            var stageForward =
                stageRotation * Vector3.forward;
            var stageUp =
                stageRotation * Vector3.up;
            var leftShoulder =
                stageOrigin - stageRight * 0.2f;
            var rightShoulder =
                stageOrigin + stageRight * 0.2f;

            var leftPole =
                PoseGhostGuideController.CalculateElbowPole(
                    leftShoulder,
                    stageRight,
                    stageForward,
                    stageUp,
                    -1f,
                    0.8f,
                    -0.1f,
                    0.05f);
            var rightPole =
                PoseGhostGuideController.CalculateElbowPole(
                    rightShoulder,
                    stageRight,
                    stageForward,
                    stageUp,
                    1f,
                    0.8f,
                    -0.1f,
                    0.05f);

            AssertVector3(
                PoseGhostGuideController
                    .MirrorAcrossStageSagittalPlane(
                        leftPole,
                        stageOrigin,
                        stageRotation),
                rightPole,
                0.0001f);
        }

        [Test]
        public void ProfileElbowHintUsesTheStageFrame()
        {
            var shoulder = new Vector3(1f, 2f, 3f);
            var stageRotation = Quaternion.Euler(0f, 90f, 0f);

            var pole =
                PoseGhostGuideController
                    .CalculateElbowPoleFromHint(
                        shoulder,
                        stageRotation,
                        new Vector3(0.2f, 0.1f, 0.05f),
                        2f);

            AssertVector3(
                pole,
                shoulder +
                stageRotation *
                new Vector3(0.4f, 0.2f, 0.1f),
                0.0001f);
        }

        [Test]
        public void UnscoredHandRotationPreservesHumanoidLocalRotation()
        {
            var parent = new GameObject("HandParent");
            var hand = new GameObject("Hand");
            hand.transform.SetParent(parent.transform, false);
            hand.transform.localRotation =
                Quaternion.Euler(12f, 34f, 56f);
            var expected = hand.transform.localRotation;

            try
            {
                PoseGhostGuideController.ApplyAuthoredHandRotation(
                    hand.transform,
                    Quaternion.Euler(80f, 20f, 10f),
                    false);
                Assert.That(
                    Quaternion.Angle(
                        expected,
                        hand.transform.localRotation),
                    Is.LessThan(0.001f));

                var authored = Quaternion.Euler(80f, 20f, 10f);
                PoseGhostGuideController.ApplyAuthoredHandRotation(
                    hand.transform,
                    authored,
                    true);
                Assert.That(
                    Quaternion.Angle(
                        authored,
                        hand.transform.rotation),
                    Is.LessThan(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void CalculateHeadWorldRotationPreservesRigNeutralAxes()
        {
            var stageRotation = Quaternion.Euler(0f, 35f, 0f);
            var neutralInGhost = Quaternion.Euler(271f, 0f, 180f);

            var result =
                PoseGhostGuideController.CalculateHeadWorldRotation(
                    stageRotation,
                    Quaternion.identity,
                    neutralInGhost);

            Assert.That(
                Quaternion.Angle(
                    stageRotation * neutralInGhost,
                    result),
                Is.LessThan(0.001f));
        }

        [Test]
        public void CalculateHeadWorldRotationAppliesAuthoredDeltaBeforeRigAxes()
        {
            var stageRotation = Quaternion.Euler(0f, 20f, 0f);
            var authoredRotation = Quaternion.Euler(-12f, 8f, 0f);
            var neutralInGhost = Quaternion.Euler(271f, 0f, 180f);

            var result =
                PoseGhostGuideController.CalculateHeadWorldRotation(
                    stageRotation,
                    authoredRotation,
                    neutralInGhost);

            Assert.That(
                Quaternion.Angle(
                    stageRotation *
                    authoredRotation *
                    neutralInGhost,
                    result),
                Is.LessThan(0.001f));
        }

        [Test]
        public void ScorerWithoutCurrentPoseDoesNotShowLegacyFallback()
        {
            var host = new GameObject("PoseGhostProfileSelection");
            var fallback =
                ScriptableObject.CreateInstance<PosingScoreProfile>();
            try
            {
                var scorer =
                    host.AddComponent<ThreePointPosingScorer>();
                var guide =
                    host.AddComponent<PoseGhostGuideController>();
                var serialized = new SerializedObject(guide);
                serialized.FindProperty("_posingScorer")
                    .objectReferenceValue = scorer;
                serialized.FindProperty("_profile")
                    .objectReferenceValue = fallback;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(guide.ActiveProfile, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(fallback);
            }
        }

        [Test]
        public void ReachableIkPreservesSegmentsAndHitsTarget()
        {
            var shoulder = new Vector3(0f, 1.4f, 0f);
            var target = new Vector3(-0.42f, 1.72f, 0.08f);

            Assert.That(
                PoseGhostGuideController.TrySolveTwoBoneIk(
                    shoulder,
                    0.38f,
                    0.34f,
                    target,
                    new Vector3(-1f, 1.4f, -0.2f),
                    out var solution),
                Is.True);

            Assert.That(solution.WasClamped, Is.False);
            Assert.That(
                Vector3.Distance(
                    solution.Shoulder,
                    solution.Elbow),
                Is.EqualTo(0.38f).Within(0.0001f));
            Assert.That(
                Vector3.Distance(
                    solution.Elbow,
                    solution.Hand),
                Is.EqualTo(0.34f).Within(0.0001f));
            AssertVector3(solution.Hand, target, 0.0001f);
        }

        [Test]
        public void UnreachableIkClampsReachAndPreservesSegments()
        {
            var shoulder = new Vector3(0f, 1f, 0f);

            Assert.That(
                PoseGhostGuideController.TrySolveTwoBoneIk(
                    shoulder,
                    0.4f,
                    0.35f,
                    new Vector3(10f, 1f, 0f),
                    new Vector3(0f, 2f, 1f),
                    out var solution),
                Is.True);

            Assert.That(solution.WasClamped, Is.True);
            Assert.That(
                solution.SolvedDistance,
                Is.LessThan(0.75f));
            Assert.That(
                Vector3.Distance(
                    solution.Shoulder,
                    solution.Elbow),
                Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(
                Vector3.Distance(
                    solution.Elbow,
                    solution.Hand),
                Is.EqualTo(0.35f).Within(0.0001f));
            Assert.That(
                Vector3.Distance(
                    solution.Shoulder,
                    solution.Hand),
                Is.EqualTo(solution.SolvedDistance)
                    .Within(0.0001f));
        }

        [Test]
        public void SymmetricIkInputsProduceSymmetricElbowsAndHands()
        {
            var origin = new Vector3(0f, 1.4f, 0f);
            var leftShoulder =
                new Vector3(-0.2f, 1.4f, 0f);
            var rightShoulder =
                new Vector3(0.2f, 1.4f, 0f);

            Assert.That(
                PoseGhostGuideController.TrySolveTwoBoneIk(
                    leftShoulder,
                    0.35f,
                    0.32f,
                    new Vector3(-0.52f, 1.7f, 0.05f),
                    new Vector3(-1f, 1.5f, -0.1f),
                    out var left),
                Is.True);
            Assert.That(
                PoseGhostGuideController.TrySolveTwoBoneIk(
                    rightShoulder,
                    0.35f,
                    0.32f,
                    new Vector3(0.52f, 1.7f, 0.05f),
                    new Vector3(1f, 1.5f, -0.1f),
                    out var right),
                Is.True);

            AssertVector3(
                PoseGhostGuideController
                    .MirrorAcrossStageSagittalPlane(
                        left.Elbow,
                        origin,
                        Quaternion.identity),
                right.Elbow,
                0.0001f);
            AssertVector3(
                PoseGhostGuideController
                    .MirrorAcrossStageSagittalPlane(
                        left.Hand,
                        origin,
                        Quaternion.identity),
                right.Hand,
                0.0001f);
        }

        [Test]
        public void InvalidIkInputsNeverProduceNonFiniteGeometry()
        {
            var success =
                PoseGhostGuideController.TrySolveTwoBoneIk(
                    new Vector3(float.NaN, 1f, 0f),
                    float.PositiveInfinity,
                    0.3f,
                    new Vector3(1f, float.NaN, 0f),
                    Vector3.zero,
                    out var solution);

            Assert.That(success, Is.False);
            AssertFinite(solution.Shoulder);
            AssertFinite(solution.Elbow);
            AssertFinite(solution.Hand);
            Assert.That(
                float.IsNaN(solution.SolvedDistance) ||
                float.IsInfinity(solution.SolvedDistance),
                Is.False);
        }

        [Test]
        public void UniformScaleUsesHeightAndSanitizesInvalidValues()
        {
            Assert.That(
                PoseGhostGuideController.CalculateUniformScale(
                    1.8f,
                    1.5f),
                Is.EqualTo(1.2f).Within(0.0001f));
            Assert.That(
                PoseGhostGuideController.CalculateUniformScale(
                    float.NaN,
                    1.5f),
                Is.EqualTo(1f));
            Assert.That(
                PoseGhostGuideController.CalculateUniformScale(
                    20f,
                    1f,
                    0.5f,
                    1.4f),
                Is.EqualTo(1.4f));
            Assert.That(
                PoseGhostGuideController.CalculateUniformScale(
                    0.1f,
                    1f,
                    0.5f,
                    1.4f),
                Is.EqualTo(0.5f));
        }

        [Test]
        public void RendererConfigurationUsesOneMaterialAndNoLighting()
        {
            var root = new GameObject(
                "PoseGhostRendererTest");
            var renderer =
                root.AddComponent<MeshRenderer>();
            var shader =
                Shader.Find(
                    "Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Standard");
            Assert.That(shader, Is.Not.Null);
            var originalA = new Material(shader);
            var originalB = new Material(shader);
            var ghost = new Material(shader);

            try
            {
                renderer.sharedMaterials =
                    new[] { originalA, originalB };

                Assert.That(
                    PoseGhostGuideController
                        .ConfigureGhostRenderer(
                            renderer,
                            ghost),
                    Is.True);

                Assert.That(
                    renderer.sharedMaterials,
                    Has.Length.EqualTo(2));
                Assert.That(
                    renderer.sharedMaterials[0],
                    Is.SameAs(ghost));
                Assert.That(
                    renderer.sharedMaterials[1],
                    Is.SameAs(ghost));
                Assert.That(
                    renderer.shadowCastingMode,
                    Is.EqualTo(ShadowCastingMode.Off));
                Assert.That(renderer.receiveShadows, Is.False);
                Assert.That(
                    renderer.lightProbeUsage,
                    Is.EqualTo(LightProbeUsage.Off));
                Assert.That(
                    renderer.reflectionProbeUsage,
                    Is.EqualTo(ReflectionProbeUsage.Off));
                Assert.That(
                    renderer.motionVectorGenerationMode,
                    Is.EqualTo(
                        MotionVectorGenerationMode
                            .ForceNoMotion));
                Assert.That(
                    renderer.allowOcclusionWhenDynamic,
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(originalA);
                Object.DestroyImmediate(originalB);
                Object.DestroyImmediate(ghost);
            }
        }

        [Test]
        public void LayerAssignmentIncludesEveryDescendant()
        {
            var root = new GameObject("PoseGhostLayerRoot");
            var child = new GameObject("Child");
            var grandchild = new GameObject("Grandchild");

            try
            {
                child.transform.SetParent(root.transform, false);
                grandchild.transform.SetParent(
                    child.transform,
                    false);

                Assert.That(
                    PoseGhostGuideController
                        .SetLayerRecursively(root, 8),
                    Is.True);
                Assert.That(root.layer, Is.EqualTo(8));
                Assert.That(child.layer, Is.EqualTo(8));
                Assert.That(grandchild.layer, Is.EqualTo(8));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [TestCase(
            "Assets/IVRC/Avatar/2_UniversalBaseCharacters.fbx")]
        [TestCase(
            "Assets/IVRC/Avatar/UniversalBaseCharacters_famale.fbx")]
        public void CinematicModelsContainNoMetaTrackingComponents(
            string assetPath)
        {
            var model =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    assetPath);

            Assert.That(model, Is.Not.Null);
            Assert.That(
                PoseGhostGuideController
                    .ContainsForbiddenTrackingComponents(
                        model),
                Is.False);
        }

        private static void AssertFinite(Vector3 value)
        {
            Assert.That(
                float.IsNaN(value.x) ||
                float.IsInfinity(value.x) ||
                float.IsNaN(value.y) ||
                float.IsInfinity(value.y) ||
                float.IsNaN(value.z) ||
                float.IsInfinity(value.z),
                Is.False);
        }

        private static void AssertVector3(
            Vector3 actual,
            Vector3 expected,
            float tolerance)
        {
            Assert.That(
                actual.x,
                Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(
                actual.y,
                Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(
                actual.z,
                Is.EqualTo(expected.z).Within(tolerance));
        }
    }
}
