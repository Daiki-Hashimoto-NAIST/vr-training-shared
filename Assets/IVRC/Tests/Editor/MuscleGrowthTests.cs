using NUnit.Framework;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class MuscleGrowthTests
    {
        [TestCase(ExperiencePhase.Setup, 0f)]
        [TestCase(ExperiencePhase.Ready, 0f)]
        [TestCase(ExperiencePhase.Countdown, 0f)]
        [TestCase(ExperiencePhase.Training, 0.25f)]
        [TestCase(ExperiencePhase.Transformation, 0.75f)]
        [TestCase(ExperiencePhase.StageTransition, 0.75f)]
        [TestCase(ExperiencePhase.StageEntrance, 0.75f)]
        [TestCase(ExperiencePhase.Posing, 0.75f)]
        [TestCase(ExperiencePhase.Results, 0.75f)]
        public void PhaseSelectsLiveOrFrozenGrowth(
            ExperiencePhase phase,
            float expected)
        {
            Assert.That(
                MuscleGrowthMath.SelectTarget(
                    phase,
                    0.25f,
                    0.75f),
                Is.EqualTo(expected));
        }

        [TestCase(-1f, 0f)]
        [TestCase(0.4f, 0.4f)]
        [TestCase(2f, 1f)]
        public void NormalizedGrowthIsClamped(
            float input,
            float expected)
        {
            Assert.That(
                MuscleGrowthMath.SanitizeNormalized(input),
                Is.EqualTo(expected));
        }

        [Test]
        public void InvalidNumbersFailClosedToZero()
        {
            Assert.That(
                MuscleGrowthMath.SanitizeNormalized(float.NaN),
                Is.EqualTo(0f));
            Assert.That(
                MuscleGrowthMath.SanitizeNormalized(
                    float.PositiveInfinity),
                Is.EqualTo(0f));
            Assert.That(
                MuscleGrowthMath.SanitizeNormalized(
                    float.NegativeInfinity),
                Is.EqualTo(0f));
        }

        [Test]
        public void GrowthSnapshotSanitizesEveryChannel()
        {
            var snapshot = new MuscleGrowthSnapshot(
                new MuscleGrowthChannels(
                    0.75f,
                    -1f,
                    2f,
                    float.NaN,
                    0.4f,
                    float.PositiveInfinity,
                    0.9f));

            Assert.That(snapshot.Overall, Is.EqualTo(0.75f));
            Assert.That(
                snapshot.Get(
                    MuscleGrowthChannel.ElbowFlexionLeft),
                Is.Zero);
            Assert.That(
                snapshot.Get(
                    MuscleGrowthChannel.ElbowFlexionRight),
                Is.EqualTo(1f));
            Assert.That(
                snapshot.Get(
                    MuscleGrowthChannel.ArmRaiseLeft),
                Is.Zero);
            Assert.That(
                snapshot.Get(
                    MuscleGrowthChannel.ArmRaiseRight),
                Is.EqualTo(0.4f));
            Assert.That(
                snapshot.Get(
                    MuscleGrowthChannel.ChestLeft),
                Is.Zero);
            Assert.That(
                snapshot.Get(
                    MuscleGrowthChannel.ChestRight),
                Is.EqualTo(0.9f));
        }

        [Test]
        public void ScorerSnapshotIsDetachedFromLaterLiveChanges()
        {
            var host = new GameObject(
                "TrainingGrowthSnapshotTest");
            try
            {
                var scorer =
                    host.AddComponent<TrainingRhythmScorer>();
                SetScorerGrowth(
                    scorer,
                    0.7f,
                    0.2f,
                    0.4f,
                    0.6f,
                    0.8f);

                var snapshot =
                    scorer.CaptureGrowthSnapshot();
                SetScorerGrowth(
                    scorer,
                    0.1f,
                    1f,
                    1f,
                    1f,
                    1f);

                Assert.That(snapshot.Overall, Is.EqualTo(0.7f));
                Assert.That(
                    snapshot.Get(
                        MuscleGrowthChannel.ElbowFlexionLeft),
                    Is.EqualTo(0.2f));
                Assert.That(
                    snapshot.Get(
                        MuscleGrowthChannel.ArmRaiseRight),
                    Is.EqualTo(0.8f));
                Assert.That(
                    snapshot.Get(
                        MuscleGrowthChannel.ChestLeft),
                    Is.EqualTo(0.4f).Within(0.0001f));
                Assert.That(
                    snapshot.Get(
                        MuscleGrowthChannel.ChestRight),
                    Is.EqualTo(0.6f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void MuscleSurfaceResponseIsBoundedAndMonotonic()
        {
            var restingNormal =
                MuscleSurfacePresentationMath
                    .EvaluateNormalScale(
                        0f,
                        0.72f,
                        1.08f);
            var middleNormal =
                MuscleSurfacePresentationMath
                    .EvaluateNormalScale(
                        0.5f,
                        0.72f,
                        1.08f);
            var trainedNormal =
                MuscleSurfacePresentationMath
                    .EvaluateNormalScale(
                        1f,
                        0.72f,
                        1.08f);
            Assert.That(restingNormal, Is.EqualTo(0.72f));
            Assert.That(middleNormal,
                Is.GreaterThan(restingNormal));
            Assert.That(trainedNormal,
                Is.GreaterThan(middleNormal));
            Assert.That(trainedNormal, Is.EqualTo(1.08f));

            Assert.That(
                MuscleSurfacePresentationMath
                    .EvaluateSmoothness(
                        float.NaN,
                        float.NaN,
                        float.PositiveInfinity),
                Is.InRange(0f, 1f));
            Assert.That(
                MuscleSurfacePresentationMath
                    .EvaluateNormalScale(
                        float.PositiveInfinity,
                        float.NegativeInfinity,
                        float.NaN),
                Is.InRange(0f, 2f));
        }

        [Test]
        public void MuscleSurfaceStateUsesPropertyBlockAndRestoresIt()
        {
            var gameObject =
                new GameObject("MuscleSurfacePropertyBlockTest");
            var material = new Material(
                Shader.Find(
                    "Universal Render Pipeline/Lit"));
            var normal = new Texture2D(2, 2);
            try
            {
                var renderer =
                    gameObject.AddComponent<
                        SkinnedMeshRenderer>();
                renderer.sharedMesh = CreateMuscleMesh(
                    "MuscleSurfacePropertyBlockMesh");
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
                material.SetFloat("_BumpScale", 1f);
                material.SetFloat("_Smoothness", 0.15f);
                renderer.sharedMaterial = material;

                var original = new MaterialPropertyBlock();
                original.SetFloat("_Metallic", 0.23f);
                renderer.SetPropertyBlock(original);

                var synchronizer =
                    new MuscleSurfaceStateSynchronizer();
                Assert.That(
                    synchronizer.Rebuild(
                        new[] { renderer }),
                    Is.EqualTo(1),
                    synchronizer.ValidationError);
                Assert.That(synchronizer.IsComplete, Is.True);
                Assert.That(
                    synchronizer.Apply(
                        new MuscleSurfaceSnapshot(
                            1f,
                            0.72f,
                            1.08f,
                            0.14f,
                            0.25f)),
                    Is.EqualTo(1));

                var applied = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(applied);
                Assert.That(
                    applied.GetFloat("_BumpScale"),
                    Is.EqualTo(1.08f).Within(0.0001f));
                Assert.That(
                    applied.GetFloat("_Smoothness"),
                    Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(
                    applied.GetFloat("_Metallic"),
                    Is.EqualTo(0.23f).Within(0.0001f));
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(material));

                synchronizer.Clear();
                var restored = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(restored);
                Assert.That(
                    restored.GetFloat("_Metallic"),
                    Is.EqualTo(0.23f).Within(0.0001f));
                Assert.That(
                    restored.GetFloat("_BumpScale"),
                    Is.Zero);
                Assert.That(
                    restored.GetFloat("_Smoothness"),
                    Is.Zero);
            }
            finally
            {
                var renderer = gameObject.GetComponent<
                    SkinnedMeshRenderer>();
                if (renderer != null)
                {
                    Object.DestroyImmediate(renderer.sharedMesh);
                }
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(normal);
            }
        }

        [Test]
        public void MuscleSurfaceFailureIsCachedUntilRefresh()
        {
            var gameObject =
                new GameObject("MuscleSurfaceFailureCacheTest");
            var mesh = CreateMuscleMesh(
                "MuscleSurfaceFailureCacheMesh");
            var material = new Material(
                Shader.Find("Universal Render Pipeline/Lit"));
            var normal = new Texture2D(2, 2);
            try
            {
                var renderer = gameObject.AddComponent<
                    SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.sharedMaterial = material;
                var output = gameObject.AddComponent<
                    BlendShapeMuscleGrowthOutput>();
                ConfigureSingleBinding(output, renderer);

                output.ApplyGrowth(1f);
                Assert.That(output.SurfaceReady, Is.False);
                Assert.That(output.SurfaceTargetCount, Is.Zero);

                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
                output.ApplyGrowth(1f);
                Assert.That(
                    output.SurfaceTargetCount,
                    Is.Zero,
                    "A failed surface setup must not allocate and " +
                    "revalidate every LateUpdate.");

                output.RefreshBindings();
                Assert.That(output.SurfaceReady, Is.True);
                Assert.That(output.SurfaceTargetCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(normal);
            }
        }

        [Test]
        public void MuscleSurfaceDisableRestoresAndEnableRebuilds()
        {
            var gameObject =
                new GameObject("MuscleSurfaceLifecycleTest");
            var mesh = CreateMuscleMesh(
                "MuscleSurfaceLifecycleMesh");
            var material = new Material(
                Shader.Find("Universal Render Pipeline/Lit"));
            var normal = new Texture2D(2, 2);
            try
            {
                var renderer = gameObject.AddComponent<
                    SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
                renderer.sharedMaterial = material;
                var original = new MaterialPropertyBlock();
                original.SetFloat("_Metallic", 0.31f);
                renderer.SetPropertyBlock(original);

                var output = gameObject.AddComponent<
                    BlendShapeMuscleGrowthOutput>();
                ConfigureSingleBinding(output, renderer);
                output.RefreshBindings();
                output.ApplyGrowth(1f);
                Assert.That(output.SurfaceTargetCount, Is.EqualTo(1));

                InvokePrivate(output, "OnDisable");
                var restored = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(restored);
                Assert.That(output.SurfaceTargetCount, Is.Zero);
                Assert.That(restored.GetFloat("_Metallic"),
                    Is.EqualTo(0.31f).Within(0.0001f));
                Assert.That(restored.GetFloat("_BumpScale"), Is.Zero);

                InvokePrivate(output, "OnEnable");
                output.ApplyGrowth(1f);
                var reapplied = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(reapplied);
                Assert.That(output.SurfaceReady, Is.True);
                Assert.That(output.SurfaceTargetCount, Is.EqualTo(1));
                Assert.That(reapplied.GetFloat("_BumpScale"),
                    Is.EqualTo(1.08f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(normal);
            }
        }

        [Test]
        public void MockOutputStoresSanitizedValueAndResets()
        {
            var gameObject =
                new GameObject("MockMuscleGrowthOutputTest");
            try
            {
                var output =
                    gameObject.AddComponent<
                        MockMuscleGrowthOutput>();
                output.ApplyGrowth(1.5f);

                Assert.That(output.IsAvailable, Is.True);
                Assert.That(output.IsMock, Is.True);
                Assert.That(
                    output.LastAppliedGrowthNormalized,
                    Is.EqualTo(1f));

                output.ResetGrowth();
                Assert.That(
                    output.LastAppliedGrowthNormalized,
                    Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void BlendShapeOutputUsesExplicitNameAndRestoresBaseline()
        {
            var gameObject =
                new GameObject("BlendShapeMuscleGrowthOutputTest");
            var mesh = new Mesh
            {
                name = "MuscleGrowthTestMesh",
                vertices = new[]
                {
                    Vector3.zero,
                    Vector3.right,
                    Vector3.up
                },
                triangles = new[] { 0, 1, 2 }
            };
            mesh.AddBlendShapeFrame(
                "UpperArm.L",
                100f,
                new[]
                {
                    Vector3.forward,
                    Vector3.forward,
                    Vector3.forward
                },
                new Vector3[3],
                new Vector3[3]);

            try
            {
                var renderer =
                    gameObject.AddComponent<
                        SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.SetBlendShapeWeight(0, 20f);
                var output =
                    gameObject.AddComponent<
                        BlendShapeMuscleGrowthOutput>();
                var serializedOutput =
                    new SerializedObject(output);
                var bindings =
                    serializedOutput.FindProperty("_bindings");
                bindings.arraySize = 1;
                var binding =
                    bindings.GetArrayElementAtIndex(0);
                binding.FindPropertyRelative("_renderer")
                    .objectReferenceValue = renderer;
                binding.FindPropertyRelative("_blendShapeName")
                    .stringValue = "UpperArm.L";
                binding.FindPropertyRelative(
                        "_useCurrentWeightAsBaseline")
                    .boolValue = true;
                binding.FindPropertyRelative("_targetWeight")
                    .floatValue = float.NaN;
                serializedOutput
                    .ApplyModifiedPropertiesWithoutUndo();

                output.ApplyGrowth(1f);

                Assert.That(output.IsAvailable, Is.True);
                Assert.That(output.IsMock, Is.False);
                Assert.That(
                    renderer.GetBlendShapeWeight(0),
                    Is.EqualTo(100f).Within(0.001f));

                output.RefreshBindings();
                output.ResetGrowth();
                Assert.That(
                    renderer.GetBlendShapeWeight(0),
                    Is.EqualTo(20f).Within(0.001f));

                output.ApplyGrowth(1f);
                InvokePrivate(output, "OnEnable");
                output.ResetGrowth();
                Assert.That(
                    renderer.GetBlendShapeWeight(0),
                    Is.EqualTo(20f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void RemovingBindingRestoresPreviousRendererBaseline()
        {
            var gameObject =
                new GameObject("RemovedMuscleBindingTest");
            var mesh = CreateMuscleMesh(
                "RemovedMuscleBindingMesh");
            try
            {
                var renderer =
                    gameObject.AddComponent<
                        SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.SetBlendShapeWeight(0, 20f);
                var output =
                    gameObject.AddComponent<
                        BlendShapeMuscleGrowthOutput>();
                ConfigureSingleBinding(
                    output,
                    renderer);

                output.ApplyGrowth(1f);
                Assert.That(
                    renderer.GetBlendShapeWeight(0),
                    Is.EqualTo(100f).Within(0.001f));

                var serializedOutput =
                    new SerializedObject(output);
                serializedOutput
                    .FindProperty("_bindings")
                    .arraySize = 0;
                serializedOutput
                    .ApplyModifiedPropertiesWithoutUndo();
                InvokePrivate(output, "OnValidate");

                Assert.That(
                    renderer.GetBlendShapeWeight(0),
                    Is.EqualTo(20f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SharedMeshSwapUsesSafeBaselineAndRemembersOldMesh()
        {
            var gameObject =
                new GameObject("MuscleSharedMeshSwapTest");
            var originalMesh =
                CreateMuscleMesh("OriginalMuscleMesh");
            var replacementMesh =
                CreateMuscleMesh("ReplacementMuscleMesh");
            try
            {
                var renderer =
                    gameObject.AddComponent<
                        SkinnedMeshRenderer>();
                renderer.sharedMesh = originalMesh;
                renderer.SetBlendShapeWeight(0, 20f);
                var output =
                    gameObject.AddComponent<
                        BlendShapeMuscleGrowthOutput>();
                ConfigureSingleBinding(
                    output,
                    renderer);

                output.ApplyGrowth(1f);
                renderer.sharedMesh = replacementMesh;
                renderer.SetBlendShapeWeight(0, 100f);
                output.ResetGrowth();
                Assert.That(
                    renderer.GetBlendShapeWeight(0),
                    Is.EqualTo(0f).Within(0.001f));

                output.ApplyGrowth(1f);
                renderer.sharedMesh = originalMesh;
                renderer.SetBlendShapeWeight(0, 100f);
                output.ResetGrowth();
                Assert.That(
                    renderer.GetBlendShapeWeight(0),
                    Is.EqualTo(20f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(originalMesh);
                Object.DestroyImmediate(replacementMesh);
            }
        }

        [Test]
        public void PresentationSyncCopiesOnlyTrackedConfiguredShapes()
        {
            var sourceObject =
                new GameObject("MuscleSyncTrackedSource");
            var mirrorObject =
                new GameObject("MuscleSyncMirrorSource");
            var targetObject =
                new GameObject("MuscleSyncTarget");
            var mesh = CreatePresentationSyncMesh();
            try
            {
                var sourceRenderer =
                    sourceObject.AddComponent<
                        SkinnedMeshRenderer>();
                var mirrorRenderer =
                    mirrorObject.AddComponent<
                        SkinnedMeshRenderer>();
                var targetRenderer =
                    targetObject.AddComponent<
                        SkinnedMeshRenderer>();
                sourceRenderer.sharedMesh = mesh;
                mirrorRenderer.sharedMesh = mesh;
                targetRenderer.sharedMesh = mesh;

                var output =
                    sourceObject.AddComponent<
                        BlendShapeMuscleGrowthOutput>();
                var serializedOutput =
                    new SerializedObject(output);
                var bindings =
                    serializedOutput.FindProperty("_bindings");
                bindings.arraySize = 5;
                ConfigureBinding(
                    bindings.GetArrayElementAtIndex(0),
                    sourceRenderer,
                    "Shoulder.L");
                ConfigureBinding(
                    bindings.GetArrayElementAtIndex(1),
                    sourceRenderer,
                    "UpperArm.L");
                ConfigureBinding(
                    bindings.GetArrayElementAtIndex(2),
                    sourceRenderer,
                    "Shoulder.L");
                ConfigureBinding(
                    bindings.GetArrayElementAtIndex(3),
                    mirrorRenderer,
                    "Shoulder.L");
                ConfigureBinding(
                    bindings.GetArrayElementAtIndex(4),
                    mirrorRenderer,
                    "UpperArm.L");
                serializedOutput
                    .ApplyModifiedPropertiesWithoutUndo();

                sourceRenderer.SetBlendShapeWeight(0, 72f);
                sourceRenderer.SetBlendShapeWeight(1, 41f);
                sourceRenderer.SetBlendShapeWeight(2, 9f);
                mirrorRenderer.SetBlendShapeWeight(0, 4f);
                mirrorRenderer.SetBlendShapeWeight(1, 5f);
                targetRenderer.SetBlendShapeWeight(0, 0f);
                targetRenderer.SetBlendShapeWeight(1, 0f);
                targetRenderer.SetBlendShapeWeight(2, 37f);

                var synchronizer =
                    new MuscleBlendShapeStateSynchronizer();
                Assert.That(
                    synchronizer.Rebuild(
                        output,
                        sourceRenderer,
                        new[]
                        {
                            targetRenderer,
                            targetRenderer
                        }),
                    Is.EqualTo(2));
                Assert.That(
                    synchronizer.ChannelCount,
                    Is.EqualTo(2));
                Assert.That(
                    synchronizer.TargetCount,
                    Is.EqualTo(2));
                Assert.That(synchronizer.IsComplete, Is.True);
                Assert.That(
                    synchronizer.ValidationError,
                    Is.Empty);
                Assert.That(
                    synchronizer.Apply(),
                    Is.EqualTo(2));
                Assert.That(
                    targetRenderer.GetBlendShapeWeight(0),
                    Is.EqualTo(72f).Within(0.001f));
                Assert.That(
                    targetRenderer.GetBlendShapeWeight(1),
                    Is.EqualTo(41f).Within(0.001f));
                Assert.That(
                    targetRenderer.GetBlendShapeWeight(2),
                    Is.EqualTo(37f).Within(0.001f));

                sourceRenderer.SetBlendShapeWeight(0, 25f);
                mirrorRenderer.SetBlendShapeWeight(0, 99f);
                Assert.That(
                    synchronizer.Apply(),
                    Is.EqualTo(2));
                Assert.That(
                    targetRenderer.GetBlendShapeWeight(0),
                    Is.EqualTo(25f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(mirrorObject);
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void FullPresentationSyncCopiesFixedAndConfiguredShapes()
        {
            var sourceObject =
                new GameObject("FullBlendShapeSyncSource");
            var targetObject =
                new GameObject("FullBlendShapeSyncTarget");
            var mesh = CreatePresentationSyncMesh();
            try
            {
                var sourceRenderer =
                    sourceObject.AddComponent<SkinnedMeshRenderer>();
                var targetRenderer =
                    targetObject.AddComponent<SkinnedMeshRenderer>();
                sourceRenderer.sharedMesh = mesh;
                targetRenderer.sharedMesh = mesh;
                sourceRenderer.SetBlendShapeWeight(0, 72f);
                sourceRenderer.SetBlendShapeWeight(1, 41f);
                sourceRenderer.SetBlendShapeWeight(2, -15f);

                var synchronizer =
                    new MuscleBlendShapeStateSynchronizer();
                Assert.That(
                    synchronizer.RebuildAllSharedBlendShapes(
                        sourceRenderer,
                        new[] { targetRenderer }),
                    Is.EqualTo(3));
                Assert.That(synchronizer.IsComplete, Is.True);
                Assert.That(synchronizer.Apply(), Is.EqualTo(3));
                Assert.That(
                    targetRenderer.GetBlendShapeWeight(0),
                    Is.EqualTo(72f).Within(0.001f));
                Assert.That(
                    targetRenderer.GetBlendShapeWeight(1),
                    Is.EqualTo(41f).Within(0.001f));
                Assert.That(
                    targetRenderer.GetBlendShapeWeight(2),
                    Is.EqualTo(-15f).Within(0.001f),
                    "A fixed presentation shape must be copied exactly " +
                    "rather than clamped as a growth channel.");
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void PresentationSyncRejectsMissingConfiguredMuscleShape()
        {
            var sourceObject =
                new GameObject("IncompleteMuscleSyncSource");
            var targetObject =
                new GameObject("IncompleteMuscleSyncTarget");
            var sourceMesh = CreatePresentationSyncMesh();
            var targetMesh = CreateMuscleMesh(
                "IncompleteMuscleSyncTargetMesh");
            try
            {
                var sourceRenderer =
                    sourceObject.AddComponent<
                        SkinnedMeshRenderer>();
                var targetRenderer =
                    targetObject.AddComponent<
                        SkinnedMeshRenderer>();
                sourceRenderer.sharedMesh = sourceMesh;
                targetRenderer.sharedMesh = targetMesh;

                var output =
                    sourceObject.AddComponent<
                        BlendShapeMuscleGrowthOutput>();
                var serializedOutput =
                    new SerializedObject(output);
                var bindings =
                    serializedOutput.FindProperty("_bindings");
                bindings.arraySize = 2;
                ConfigureBinding(
                    bindings.GetArrayElementAtIndex(0),
                    sourceRenderer,
                    "UpperArm.L");
                ConfigureBinding(
                    bindings.GetArrayElementAtIndex(1),
                    sourceRenderer,
                    "Shoulder.L");
                serializedOutput
                    .ApplyModifiedPropertiesWithoutUndo();

                var synchronizer =
                    new MuscleBlendShapeStateSynchronizer();
                Assert.That(
                    synchronizer.Rebuild(
                        output,
                        sourceRenderer,
                        new[] { targetRenderer }),
                    Is.Zero,
                    "A partial body must not silently receive only some " +
                    "of the earned muscle state.");
                Assert.That(synchronizer.IsComplete, Is.False);
                Assert.That(
                    synchronizer.ValidationError,
                    Does.Contain("Shoulder.L"));
                Assert.That(synchronizer.ChannelCount, Is.Zero);
                Assert.That(synchronizer.TargetCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(sourceMesh);
                Object.DestroyImmediate(targetMesh);
            }
        }

        [Test]
        public void ControllerResponseSettingRejectsNaN()
        {
            var gameObject =
                new GameObject("MuscleGrowthControllerNaNTest");
            try
            {
                var controller =
                    gameObject.AddComponent<
                        MuscleGrowthController>();
                var serializedController =
                    new SerializedObject(controller);
                serializedController
                    .FindProperty("_responseSeconds")
                    .floatValue = float.NaN;
                serializedController
                    .ApplyModifiedPropertiesWithoutUndo();
                InvokePrivate(controller, "OnValidate");
                serializedController.Update();

                Assert.That(
                    serializedController
                        .FindProperty("_responseSeconds")
                        .floatValue,
                    Is.EqualTo(0.12f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void RefreshOutputsResetsRemovedOutput()
        {
            var controllerObject =
                new GameObject("MuscleGrowthRefreshTest");
            var outputObject =
                new GameObject("RemovedMuscleOutputTest");
            try
            {
                var controller =
                    controllerObject.AddComponent<
                        MuscleGrowthController>();
                var output =
                    outputObject.AddComponent<
                        MockMuscleGrowthOutput>();
                var serializedController =
                    new SerializedObject(controller);
                var outputs = serializedController
                    .FindProperty("_outputBehaviours");
                outputs.arraySize = 1;
                outputs.GetArrayElementAtIndex(0)
                    .objectReferenceValue = output;
                serializedController
                    .ApplyModifiedPropertiesWithoutUndo();
                controller.RefreshOutputs();
                output.ApplyGrowth(0.8f);

                outputs = serializedController
                    .FindProperty("_outputBehaviours");
                outputs.arraySize = 0;
                serializedController
                    .ApplyModifiedPropertiesWithoutUndo();
                controller.RefreshOutputs();

                Assert.That(
                    output.LastAppliedGrowthNormalized,
                    Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(outputObject);
            }
        }

        private static void InvokePrivate(
            object target,
            string methodName)
        {
            var method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance |
                BindingFlags.NonPublic);
            Assert.That(
                method,
                Is.Not.Null,
                $"Missing private method {methodName}.");
            method.Invoke(target, null);
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
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);
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

        private static Mesh CreateMuscleMesh(
            string name)
        {
            var mesh = new Mesh
            {
                name = name,
                vertices = new[]
                {
                    Vector3.zero,
                    Vector3.right,
                    Vector3.up
                },
                triangles = new[] { 0, 1, 2 }
            };
            mesh.AddBlendShapeFrame(
                "UpperArm.L",
                100f,
                new[]
                {
                    Vector3.forward,
                    Vector3.forward,
                    Vector3.forward
                },
                new Vector3[3],
                new Vector3[3]);
            return mesh;
        }

        private static Mesh CreatePresentationSyncMesh()
        {
            var mesh = new Mesh
            {
                name = "MusclePresentationSyncMesh",
                vertices = new[]
                {
                    Vector3.zero,
                    Vector3.right,
                    Vector3.up
                },
                triangles = new[] { 0, 1, 2 }
            };
            var deltas = new[]
            {
                Vector3.forward,
                Vector3.forward,
                Vector3.forward
            };
            var empty = new Vector3[3];
            mesh.AddBlendShapeFrame(
                "Shoulder.L",
                100f,
                deltas,
                empty,
                empty);
            mesh.AddBlendShapeFrame(
                "UpperArm.L",
                100f,
                deltas,
                empty,
                empty);
            mesh.AddBlendShapeFrame(
                "FaceSmile",
                100f,
                deltas,
                empty,
                empty);
            return mesh;
        }

        private static void ConfigureBinding(
            SerializedProperty binding,
            SkinnedMeshRenderer renderer,
            string blendShapeName)
        {
            binding.FindPropertyRelative("_renderer")
                .objectReferenceValue = renderer;
            binding.FindPropertyRelative("_blendShapeName")
                .stringValue = blendShapeName;
            binding.FindPropertyRelative(
                    "_useCurrentWeightAsBaseline")
                .boolValue = false;
            binding.FindPropertyRelative(
                    "_configuredBaselineWeight")
                .floatValue = 100f;
            binding.FindPropertyRelative("_targetWeight")
                .floatValue = 0f;
        }

        private static void ConfigureSingleBinding(
            BlendShapeMuscleGrowthOutput output,
            SkinnedMeshRenderer renderer)
        {
            var serializedOutput =
                new SerializedObject(output);
            var bindings =
                serializedOutput.FindProperty("_bindings");
            bindings.arraySize = 1;
            var binding =
                bindings.GetArrayElementAtIndex(0);
            binding.FindPropertyRelative("_renderer")
                .objectReferenceValue = renderer;
            binding.FindPropertyRelative("_blendShapeName")
                .stringValue = "UpperArm.L";
            binding.FindPropertyRelative(
                    "_useCurrentWeightAsBaseline")
                .boolValue = true;
            binding.FindPropertyRelative("_targetWeight")
                .floatValue = 100f;
            serializedOutput
                .ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
