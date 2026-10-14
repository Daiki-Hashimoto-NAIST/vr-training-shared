using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace IVRC.Tests
{
    public sealed class RhythmPoseGuideTests
    {
        [Test]
        public void AvatarExercisePoseSetSelectsAllFourAuthoredPoses()
        {
            var host = new GameObject(
                "RhythmPoseGuideTests_Avatar");
            var avatar = host.AddComponent<AvatarDefinition>();
            var armCurlDown = ScriptableObject.CreateInstance<PoseAsset>();
            var armCurlUp = ScriptableObject.CreateInstance<PoseAsset>();
            var sideRaiseDown = ScriptableObject.CreateInstance<PoseAsset>();
            var sideRaiseUp = ScriptableObject.CreateInstance<PoseAsset>();

            try
            {
                var serialized = new SerializedObject(avatar);
                var poses = serialized.FindProperty("_exercisePoses");
                poses.FindPropertyRelative("_armCurlDown")
                    .objectReferenceValue = armCurlDown;
                poses.FindPropertyRelative("_armCurlUp")
                    .objectReferenceValue = armCurlUp;
                poses.FindPropertyRelative("_sideRaiseDown")
                    .objectReferenceValue = sideRaiseDown;
                poses.FindPropertyRelative("_sideRaiseUp")
                    .objectReferenceValue = sideRaiseUp;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(
                    avatar.GetExercisePose(
                        BodyRepKind.ElbowFlexion,
                        RepTimingPoint.Down),
                    Is.SameAs(armCurlDown));
                Assert.That(
                    avatar.GetExercisePose(
                        BodyRepKind.ElbowFlexion,
                        RepTimingPoint.Up),
                    Is.SameAs(armCurlUp));
                Assert.That(
                    avatar.GetExercisePose(
                        BodyRepKind.ArmRaise,
                        RepTimingPoint.Down),
                    Is.SameAs(sideRaiseDown));
                Assert.That(
                    avatar.GetExercisePose(
                        BodyRepKind.ArmRaise,
                        RepTimingPoint.Up),
                    Is.SameAs(sideRaiseUp));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(armCurlDown);
                Object.DestroyImmediate(armCurlUp);
                Object.DestroyImmediate(sideRaiseDown);
                Object.DestroyImmediate(sideRaiseUp);
            }
        }

        [Test]
        public void RhythmControllerPrefersTheActiveAvatarsPoseSet()
        {
            var host = new GameObject(
                "RhythmPoseGuideTests_Controller");
            var avatarHost = new GameObject(
                "RhythmPoseGuideTests_Avatar");
            var controller =
                host.AddComponent<RhythmPoseGuideController>();
            var avatar =
                avatarHost.AddComponent<AvatarDefinition>();
            var avatarPose =
                ScriptableObject.CreateInstance<PoseAsset>();
            var fallbackPose =
                ScriptableObject.CreateInstance<PoseAsset>();

            try
            {
                var avatarSerialized = new SerializedObject(avatar);
                avatarSerialized.FindProperty("_exercisePoses")
                    .FindPropertyRelative("_armCurlDown")
                    .objectReferenceValue = avatarPose;
                avatarSerialized.ApplyModifiedPropertiesWithoutUndo();

                var controllerSerialized =
                    new SerializedObject(controller);
                controllerSerialized.FindProperty("_armCurlDown")
                    .objectReferenceValue = fallbackPose;
                controllerSerialized.ApplyModifiedPropertiesWithoutUndo();

                var variant = new GhostAvatarVariantBinding();
                variant.SetRuntimeReferences(
                    avatar,
                    (GhostGuideInstance)null,
                    null,
                    null);
                typeof(RhythmPoseGuideController)
                    .GetField(
                        "_activeVariant",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)
                    .SetValue(controller, variant);
                var selected = (PoseAsset)typeof(
                        RhythmPoseGuideController)
                    .GetMethod(
                        "GetPose",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)
                    .Invoke(
                        controller,
                        new object[]
                        {
                            BodyRepKind.ElbowFlexion,
                            RepTimingPoint.Down
                        });

                Assert.That(selected, Is.SameAs(avatarPose));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(avatarHost);
                Object.DestroyImmediate(avatarPose);
                Object.DestroyImmediate(fallbackPose);
            }
        }

        [Test]
        public void SchedulePreviewsDownUpDownBeforeEachScoredBlock()
        {
            var notes = new List<TrainingRhythmNote>
            {
                NewNote(
                    0,
                    0,
                    BodyRepKind.ElbowFlexion,
                    4,
                    6),
                NewNote(
                    1,
                    0,
                    BodyRepKind.ElbowFlexion,
                    8,
                    10),
                NewNote(
                    2,
                    1,
                    BodyRepKind.ArmRaise,
                    14,
                    16),
                NewNote(
                    3,
                    1,
                    BodyRepKind.ArmRaise,
                    18,
                    20)
            };
            var targets =
                new List<RhythmPoseGuideTarget>();

            RhythmPoseGuideSchedule.Build(
                notes,
                targets);

            Assert.That(targets, Has.Count.EqualTo(14));
            AssertTarget(
                targets[0],
                1,
                BodyRepKind.ElbowFlexion,
                RepTimingPoint.Down);
            AssertTarget(
                targets[1],
                2,
                BodyRepKind.ElbowFlexion,
                RepTimingPoint.Up);
            AssertTarget(
                targets[2],
                3,
                BodyRepKind.ElbowFlexion,
                RepTimingPoint.Down);
            AssertTarget(
                targets[3],
                4,
                BodyRepKind.ElbowFlexion,
                RepTimingPoint.Up);
            AssertTarget(
                targets[4],
                6,
                BodyRepKind.ElbowFlexion,
                RepTimingPoint.Down);
            AssertTarget(
                targets[7],
                11,
                BodyRepKind.ArmRaise,
                RepTimingPoint.Down);
            AssertTarget(
                targets[8],
                12,
                BodyRepKind.ArmRaise,
                RepTimingPoint.Up);
            AssertTarget(
                targets[9],
                13,
                BodyRepKind.ArmRaise,
                RepTimingPoint.Down);
            AssertTarget(
                targets[10],
                14,
                BodyRepKind.ArmRaise,
                RepTimingPoint.Up);
        }

        [Test]
        public void ScheduleFallsBackWhenPreviewDoesNotFit()
        {
            var notes = new List<TrainingRhythmNote>
            {
                NewNote(
                    0,
                    0,
                    BodyRepKind.ElbowFlexion,
                    2,
                    4)
            };
            var targets =
                new List<RhythmPoseGuideTarget>();

            RhythmPoseGuideSchedule.Build(
                notes,
                targets);

            Assert.That(targets, Has.Count.EqualTo(3));
            AssertTarget(
                targets[0],
                0,
                BodyRepKind.ElbowFlexion,
                RepTimingPoint.Down);
            AssertTarget(
                targets[1],
                2,
                BodyRepKind.ElbowFlexion,
                RepTimingPoint.Up);
            AssertTarget(
                targets[2],
                4,
                BodyRepKind.ElbowFlexion,
                RepTimingPoint.Down);
        }

        [Test]
        public void ScheduleCanDisablePreparationPreview()
        {
            var notes = new List<TrainingRhythmNote>
            {
                NewNote(
                    0,
                    0,
                    BodyRepKind.ElbowFlexion,
                    4,
                    6)
            };
            var targets =
                new List<RhythmPoseGuideTarget>();

            RhythmPoseGuideSchedule.Build(
                notes,
                targets,
                false);

            Assert.That(targets, Has.Count.EqualTo(3));
            AssertTarget(
                targets[0],
                2,
                BodyRepKind.ElbowFlexion,
                RepTimingPoint.Down);
            AssertTarget(
                targets[1],
                4,
                BodyRepKind.ElbowFlexion,
                RepTimingPoint.Up);
            AssertTarget(
                targets[2],
                6,
                BodyRepKind.ElbowFlexion,
                RepTimingPoint.Down);
        }

        [Test]
        public void ScaleMovesFromOnePointTwoThroughOnePointOneToOne()
        {
            var curve =
                AnimationCurve.Linear(
                    0f,
                    0f,
                    1f,
                    1f);

            Assert.That(
                RhythmPoseGuideMath
                    .EvaluateScaleMultiplier(
                        1f,
                        1f,
                        1.2f,
                        1f,
                        curve),
                Is.EqualTo(1.2f).Within(0.0001f));
            Assert.That(
                RhythmPoseGuideMath
                    .EvaluateScaleMultiplier(
                        0.5f,
                        1f,
                        1.2f,
                        1f,
                        curve),
                Is.EqualTo(1.1f).Within(0.0001f));
            Assert.That(
                RhythmPoseGuideMath
                    .EvaluateScaleMultiplier(
                        0f,
                        1f,
                        1.2f,
                        1f,
                        curve),
                Is.EqualTo(1f).Within(0.0001f));

            var signed =
                RhythmPoseGuideMath.ApplySignedMultiplier(
                    new Vector3(-1f, 1f, 1f),
                    1.2f);
            Assert.That(signed.x, Is.EqualTo(-1.2f));
            Assert.That(signed.y, Is.EqualTo(1.2f));
        }

        [Test]
        public void LeadTimeIsCappedBelowShortTargetInterval()
        {
            Assert.That(
                RhythmPoseGuideMath.GetEffectiveLeadTime(
                    1f,
                    0.5f),
                Is.EqualTo(0.45f).Within(0.0001f));
            Assert.That(
                RhythmPoseGuideMath.GetEffectiveLeadTime(
                    0.25f,
                    1f),
                Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void MuscleInitializationChangesOnlyConfiguredBodyShape()
        {
            var host = new GameObject(
                "RhythmPoseGuideTests_Muscle");
            var mesh = new Mesh
            {
                name = "RhythmPoseGuideTests_Mesh",
                vertices = new[]
                {
                    Vector3.zero,
                    Vector3.right,
                    Vector3.up
                },
                triangles = new[] { 0, 1, 2 }
            };

            try
            {
                var deltas = new Vector3[3];
                mesh.AddBlendShapeFrame(
                    "UpperArm.L",
                    100f,
                    deltas,
                    deltas,
                    deltas);
                mesh.AddBlendShapeFrame(
                    "Muscle",
                    100f,
                    deltas,
                    deltas,
                    deltas);
                mesh.AddBlendShapeFrame(
                    "FaceSmile",
                    100f,
                    deltas,
                    deltas,
                    deltas);

                var renderer =
                    host.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.SetBlendShapeWeight(0, 100f);
                renderer.SetBlendShapeWeight(1, 42f);
                renderer.SetBlendShapeWeight(2, 37f);

                var output =
                    host.AddComponent<
                        BlendShapeMuscleGrowthOutput>();
                var serialized = new SerializedObject(output);
                var bindings =
                    serialized.FindProperty("_bindings");
                bindings.arraySize = 1;
                var binding =
                    bindings.GetArrayElementAtIndex(0);
                binding.FindPropertyRelative("_renderer")
                    .objectReferenceValue = renderer;
                binding.FindPropertyRelative(
                        "_blendShapeName")
                    .stringValue = "UpperArm.L";
                binding.FindPropertyRelative(
                        "_useCurrentWeightAsBaseline")
                    .boolValue = false;
                binding.FindPropertyRelative(
                        "_configuredBaselineWeight")
                    .floatValue = 100f;
                binding.FindPropertyRelative("_targetWeight")
                    .floatValue = 0f;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var applied =
                    GhostAvatarMuscleInitializer
                        .ApplyConfiguredTargets(
                            new[] { renderer },
                            output);

                Assert.That(applied, Is.EqualTo(1));
                Assert.That(
                    renderer.GetBlendShapeWeight(0),
                    Is.EqualTo(0f));
                Assert.That(
                    renderer.GetBlendShapeWeight(1),
                    Is.EqualTo(42f));
                Assert.That(
                    renderer.GetBlendShapeWeight(2),
                    Is.EqualTo(37f));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void PoseApplierCachesPathsAndSkipsMissingBone()
        {
            var root = new GameObject(
                "RhythmPoseGuideTests_Skeleton");
            var arm = new GameObject("Arm");
            arm.transform.SetParent(root.transform, false);
            var pose = ScriptableObject.CreateInstance<PoseAsset>();

            try
            {
                var armRotation =
                    Quaternion.Euler(0f, 0f, 45f);
                pose.ReplacePoseData(
                    "Test",
                    BodyRepKind.ElbowFlexion,
                    RepTimingPoint.Up,
                    new[]
                    {
                        new PoseBoneData(
                            "Arm",
                            new Vector3(1f, 2f, 3f),
                            armRotation,
                            false,
                            Vector3.one),
                        new PoseBoneData(
                            "Missing",
                            Vector3.zero,
                            Quaternion.identity,
                            false,
                            Vector3.one)
                    });
                var applier =
                    new GhostAvatarPoseApplier(
                        root.transform,
                        pose);

                var missing = applier.Apply();

                Assert.That(missing, Is.EqualTo(1));
                Assert.That(
                    applier.MissingPathCount,
                    Is.EqualTo(1));
                Assert.That(
                    arm.transform.localPosition,
                    Is.EqualTo(
                        new Vector3(1f, 2f, 3f)));
                Assert.That(
                    Quaternion.Angle(
                        arm.transform.localRotation,
                        armRotation),
                    Is.LessThan(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(pose);
            }
        }

        [Test]
        public void GhostVariantPreservesItsOwnBonePositions()
        {
            var mirror = new GameObject(
                "RhythmPoseGuideTests_Mirror");
            var ghost = new GameObject(
                "RhythmPoseGuideTests_Ghost");
            var skeleton = new GameObject("Armature");
            skeleton.transform.SetParent(
                ghost.transform,
                false);
            var arm = new GameObject("Arm");
            arm.transform.SetParent(
                skeleton.transform,
                false);
            var originalPosition =
                new Vector3(0.25f, 0.5f, 0.75f);
            arm.transform.localPosition =
                originalPosition;
            var pose = ScriptableObject.CreateInstance<PoseAsset>();

            try
            {
                var armRotation =
                    Quaternion.Euler(0f, 0f, 45f);
                pose.ReplacePoseData(
                    "Test",
                    BodyRepKind.ElbowFlexion,
                    RepTimingPoint.Up,
                    new[]
                    {
                        new PoseBoneData(
                            "Arm",
                            new Vector3(7f, 8f, 9f),
                            armRotation,
                            false,
                            Vector3.one)
                    });
                var variant =
                    new GhostAvatarVariantBinding();
                variant.SetRuntimeReferences(
                    null,
                    mirror.transform,
                    ghost,
                    skeleton.transform,
                    null);
                variant.Initialize(null);

                var applied =
                    variant.PoseApplier.Apply(pose);

                Assert.That(applied, Is.EqualTo(1));
                Assert.That(
                    variant.PoseApplier.ApplyLocalPositions,
                    Is.False);
                Assert.That(
                    arm.transform.localPosition,
                    Is.EqualTo(originalPosition));
                Assert.That(
                    Quaternion.Angle(
                        arm.transform.localRotation,
                        armRotation),
                    Is.LessThan(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(mirror);
                Object.DestroyImmediate(ghost);
                Object.DestroyImmediate(pose);
            }
        }

        [Test]
        public void PrototypeSceneHasWiredLightweightGhostVariants()
        {
            const string scenePath =
                "Assets/Scenes/VRTrainingPrototype.unity";
            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedForTest =
                !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    scenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                var controller =
                    FindSingleInScene<
                        RhythmPoseGuideController>(scene);
                var serialized =
                    new SerializedObject(controller);
                AssertAssigned(
                    serialized,
                    "_rhythmClock",
                    "_rhythmChart",
                    "_gameFlow",
                    "_ringGuide",
                    "_avatarRegistry",
                    "_ghostMaterial",
                    "_lineArtMaterial",
                    "_armCurlDown",
                    "_armCurlUp",
                    "_sideRaiseDown",
                    "_sideRaiseUp");
                Assert.That(
                    serialized.FindProperty("_displayMode")
                        .intValue,
                    Is.EqualTo(
                        (int)RhythmGuideDisplayMode
                            .LineArt));
                Assert.That(
                    serialized
                        .FindProperty("_showPreparationPreview")
                        .boolValue,
                    Is.True,
                    "Each unscored block lead-in should demonstrate " +
                    "DOWN -> UP -> DOWN before the first scored UP.");

                var ringGuide =
                    (TrainingRhythmLaneHud)serialized
                        .FindProperty("_ringGuide")
                        .objectReferenceValue;
                Assert.That(
                    new SerializedObject(ringGuide)
                        .FindProperty("_ringVisualsEnabled")
                        .boolValue,
                    Is.False);

                var ghostMaterial =
                    (Material)serialized
                        .FindProperty("_ghostMaterial")
                        .objectReferenceValue;
                var configuredPoses = new[]
                {
                    (PoseAsset)serialized
                        .FindProperty("_armCurlDown")
                        .objectReferenceValue,
                    (PoseAsset)serialized
                        .FindProperty("_armCurlUp")
                        .objectReferenceValue,
                    (PoseAsset)serialized
                        .FindProperty("_sideRaiseDown")
                        .objectReferenceValue,
                    (PoseAsset)serialized
                        .FindProperty("_sideRaiseUp")
                        .objectReferenceValue
                };
                foreach (var pose in configuredPoses)
                {
                    Assert.That(pose, Is.Not.Null);
                    Assert.That(
                        pose.Bones.Count,
                        Is.GreaterThan(0),
                        $"{pose.name} must contain a captured pose.");
                }

                var variants =
                    serialized.FindProperty("_variants");
                Assert.That(
                    variants.arraySize,
                    Is.EqualTo(2));

                for (var index = 0;
                     index < variants.arraySize;
                     index++)
                {
                    var variant =
                        variants.GetArrayElementAtIndex(
                            index);
                    var source =
                        (AvatarDefinition)variant
                            .FindPropertyRelative(
                                "_sourceAvatar")
                            .objectReferenceValue;
                    var muscleSource =
                        (BlendShapeMuscleGrowthOutput)
                        variant.FindPropertyRelative(
                                "_muscleConfigSource")
                            .objectReferenceValue;

                    Assert.That(source, Is.Not.Null);
                    Assert.That(
                        muscleSource,
                        Is.Not.Null);
                    var avatarPoses = GetConfiguredAvatarPoses(source);
                    var playerGuide = AssertGuideBinding(
                        variant.FindPropertyRelative(
                            "_playerGuide"),
                        source,
                        true,
                        avatarPoses,
                        ghostMaterial,
                        muscleSource);
                    var mirrorGuide = AssertGuideBinding(
                        variant.FindPropertyRelative(
                            "_mirrorGuide"),
                        source,
                        false,
                        avatarPoses,
                        ghostMaterial,
                        muscleSource);
                    Assert.That(
                        playerGuide,
                        Is.Not.SameAs(mirrorGuide));
                    Assert.That(
                        playerGuide.transform.parent,
                        Is.SameAs(controller.transform));
                    Assert.That(
                        mirrorGuide.transform.parent,
                        Is.SameAs(controller.transform));
                }

                Assert.That(
                    controller.transform.parent.name,
                    Is.EqualTo("Phase_Training"));
            }
            finally
            {
                if (openedForTest &&
                    scene.IsValid() &&
                    scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(
                        scene,
                        true);
                }
            }
        }

        private static PoseAsset[] GetConfiguredAvatarPoses(
            AvatarDefinition avatar)
        {
            var serialized = new SerializedObject(avatar);
            var poses = serialized.FindProperty("_exercisePoses");
            Assert.That(
                poses,
                Is.Not.Null,
                $"{avatar.name} must own its rig-specific exercise poses.");

            var result = new[]
            {
                (PoseAsset)poses.FindPropertyRelative("_armCurlDown")
                    .objectReferenceValue,
                (PoseAsset)poses.FindPropertyRelative("_armCurlUp")
                    .objectReferenceValue,
                (PoseAsset)poses.FindPropertyRelative("_sideRaiseDown")
                    .objectReferenceValue,
                (PoseAsset)poses.FindPropertyRelative("_sideRaiseUp")
                    .objectReferenceValue
            };
            foreach (var pose in result)
            {
                Assert.That(
                    pose,
                    Is.Not.Null,
                    $"{avatar.name} has an unassigned exercise pose.");
                Assert.That(
                    pose.Bones.Count,
                    Is.GreaterThan(0),
                    $"{pose.name} must contain a captured pose.");
            }

            return result;
        }

        private static GameObject AssertGuideBinding(
            SerializedProperty guide,
            AvatarDefinition source,
            bool isPlayer,
            IReadOnlyList<PoseAsset> configuredPoses,
            Material ghostMaterial,
            BlendShapeMuscleGrowthOutput muscleSource)
        {
            Assert.That(guide, Is.Not.Null);
            var guideRoot =
                (GameObject)guide
                    .FindPropertyRelative("_guideRoot")
                    .objectReferenceValue;
            var visualRoot =
                (GameObject)guide
                    .FindPropertyRelative("_visualRoot")
                    .objectReferenceValue;
            var skeleton =
                (Transform)guide
                    .FindPropertyRelative("_skeletonRoot")
                    .objectReferenceValue;
            var targetRoot =
                (Transform)guide
                    .FindPropertyRelative("_targetRoot")
                    .objectReferenceValue;
            var motionRoot =
                (Transform)guide
                    .FindPropertyRelative("_motionRoot")
                    .objectReferenceValue;
            var scaleSource =
                (Transform)guide
                    .FindPropertyRelative("_scaleSource")
                    .objectReferenceValue;

            Assert.That(guideRoot, Is.Not.Null);
            Assert.That(visualRoot, Is.Not.Null);
            Assert.That(skeleton, Is.Not.Null);
            Assert.That(targetRoot, Is.Not.Null);
            Assert.That(motionRoot, Is.Not.Null);
            Assert.That(scaleSource, Is.SameAs(targetRoot));
            Assert.That(
                guideRoot.name,
                Is.EqualTo(
                    isPlayer
                        ? "PlayerGhostGuide"
                        : "MirrorGhostGuide"));
            Assert.That(
                visualRoot.transform.IsChildOf(
                    guideRoot.transform),
                Is.True);
            Assert.That(
                guideRoot.transform.IsChildOf(targetRoot),
                Is.False);
            Assert.That(
                guideRoot.transform.IsChildOf(motionRoot),
                Is.False);
            Assert.That(
                visualRoot.transform.localPosition,
                Is.EqualTo(Vector3.zero));
            Assert.That(
                Quaternion.Angle(
                    visualRoot.transform.localRotation,
                    Quaternion.identity),
                Is.LessThan(0.001f));
            Assert.That(
                visualRoot.transform.localScale,
                Is.EqualTo(Vector3.one));
            Assert.That(
                guide.FindPropertyRelative(
                        "_positionOffset")
                    .vector3Value,
                Is.EqualTo(Vector3.zero));
            Assert.That(
                guide.FindPropertyRelative("_yawOffset")
                    .floatValue,
                Is.Zero);
            Assert.That(
                guide.FindPropertyRelative(
                        "_followPosition")
                    .boolValue,
                Is.True);
            Assert.That(
                guide.FindPropertyRelative("_followYaw")
                    .boolValue,
                Is.True);
            Assert.That(
                guide.FindPropertyRelative(
                        "_autoCaptureBaseScale")
                    .boolValue,
                Is.True);

            var expectedRoot = isPlayer
                ? source.transform
                : source.MirrorInstance.transform;
            Assert.That(
                targetRoot,
                Is.SameAs(expectedRoot));

            var playerMotionRoot =
                source.Retargeter.JointPairs[0].Joint;
            var expectedMotionRoot = playerMotionRoot;
            if (!isPlayer)
            {
                var relativePath =
                    AnimationUtility
                        .CalculateTransformPath(
                            playerMotionRoot,
                            source.transform);
                expectedMotionRoot =
                    source.MirrorInstance.transform.Find(
                        relativePath);
            }

            Assert.That(
                motionRoot,
                Is.SameAs(expectedMotionRoot));
            Assert.That(
                motionRoot.name,
                Is.EqualTo("root"));
            Assert.That(
                motionRoot.parent.name,
                Is.EqualTo("Armature"));

            var baseScale =
                guide.FindPropertyRelative("_baseScale")
                    .vector3Value;
            Assert.That(
                Mathf.Sign(baseScale.x),
                Is.EqualTo(isPlayer ? 1f : -1f));

            foreach (var pose in configuredPoses)
            {
                var applier =
                    new GhostAvatarPoseApplier(
                        skeleton,
                        pose);
                Assert.That(
                    applier.CachedBoneCount,
                    Is.EqualTo(pose.Bones.Count),
                    $"{source.DisplayName}/{guideRoot.name}/" +
                    pose.name);
                Assert.That(
                    applier.MissingPathCount,
                    Is.Zero,
                    $"{source.DisplayName}/{guideRoot.name}/" +
                    pose.name);
            }

            foreach (var behaviour in
                     visualRoot.GetComponentsInChildren<
                         Behaviour>(true))
            {
                // The grip controller is the one behaviour a ghost keeps: it
                // is presentation, not animation, and switching it off would
                // leave the guide the only body not holding its dumbbells.
                Assert.That(
                    behaviour.enabled,
                    Is.EqualTo(
                        behaviour is DumbbellGripVisualController),
                    behaviour.GetType().Name);
            }

            Assert.That(
                visualRoot.GetComponent<
                    DumbbellGripVisualController>(),
                Is.Not.Null,
                $"{visualRoot.name} must carry the dumbbell grip.");

            var renderers =
                visualRoot.GetComponentsInChildren<
                    SkinnedMeshRenderer>(true);
            Assert.That(
                renderers,
                Has.Length.EqualTo(3));
            foreach (var renderer in renderers)
            {
                Assert.That(renderer.enabled, Is.False);
                Assert.That(
                    renderer.shadowCastingMode,
                    Is.EqualTo(
                        ShadowCastingMode.Off));
                Assert.That(
                    renderer.receiveShadows,
                    Is.False);
                foreach (var material in
                         renderer.sharedMaterials)
                {
                    Assert.That(
                        material,
                        Is.SameAs(ghostMaterial));
                }
            }

            AssertMuscleTargetsApplied(
                renderers,
                muscleSource);
            return guideRoot;
        }

        private static T FindSingleInScene<T>(Scene scene)
            where T : Component
        {
            T result = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var candidate in
                         root.GetComponentsInChildren<T>(true))
                {
                    Assert.That(
                        result,
                        Is.Null,
                        $"Multiple {typeof(T).Name} components " +
                        "were found.");
                    result = candidate;
                }
            }

            Assert.That(
                result,
                Is.Not.Null,
                $"No {typeof(T).Name} component was found.");
            return result;
        }

        private static void AssertAssigned(
            SerializedObject serialized,
            params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                var property =
                    serialized.FindProperty(propertyName);
                Assert.That(
                    property,
                    Is.Not.Null,
                    propertyName);
                Assert.That(
                    property.objectReferenceValue,
                    Is.Not.Null,
                    propertyName);
            }
        }

        private static void AssertMuscleTargetsApplied(
            IReadOnlyList<SkinnedMeshRenderer> renderers,
            BlendShapeMuscleGrowthOutput source)
        {
            var appliedCount = 0;
            foreach (var binding in source.Bindings)
            {
                if (binding == null)
                {
                    continue;
                }

                foreach (var renderer in renderers)
                {
                    var mesh = renderer.sharedMesh;
                    var shapeIndex = mesh == null
                        ? -1
                        : mesh.GetBlendShapeIndex(
                            binding.BlendShapeName);
                    if (shapeIndex < 0)
                    {
                        continue;
                    }

                    Assert.That(
                        renderer.GetBlendShapeWeight(
                            shapeIndex),
                        Is.EqualTo(
                                binding
                                    .FullGrowthTargetWeight)
                            .Within(0.0001f),
                        binding.BlendShapeName);
                    appliedCount++;
                }
            }

            Assert.That(appliedCount, Is.GreaterThan(0));
        }

        private static TrainingRhythmNote NewNote(
            int index,
            int block,
            BodyRepKind kind,
            int upBeat,
            int downBeat)
        {
            return new TrainingRhythmNote(
                index,
                block,
                index,
                kind,
                TrainingSideMask.Both,
                upBeat,
                downBeat);
        }

        private static void AssertTarget(
            RhythmPoseGuideTarget target,
            int beat,
            BodyRepKind kind,
            RepTimingPoint point)
        {
            Assert.That(
                target.BeatIndex,
                Is.EqualTo(beat));
            Assert.That(target.Kind, Is.EqualTo(kind));
            Assert.That(
                target.TimingPoint,
                Is.EqualTo(point));
        }
    }
}
