using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace IVRC.Tests
{
    public sealed class GhostGuideInstanceTests
    {
        [Test]
        public void OneVisualStateUpdatesBothGhosts()
        {
            var playerTarget =
                new GameObject("PlayerRoot");
            var mirrorTarget =
                new GameObject("MirrorRoot");
            var player = CreateGuide(
                "Player",
                playerTarget.transform);
            var mirror = CreateGuide(
                "Mirror",
                mirrorTarget.transform);
            var down = CreatePose(
                "Down",
                Quaternion.identity);
            var upRotation =
                Quaternion.Euler(0f, 0f, 55f);
            var up = CreatePose(
                "Up",
                upRotation);

            try
            {
                playerTarget.transform.position =
                    new Vector3(1f, 2f, 3f);
                mirrorTarget.transform.position =
                    new Vector3(-1f, 2f, 3f);
                mirrorTarget.transform.rotation =
                    Quaternion.Euler(0f, 180f, 0f);
                player.Instance.ConfigureBaseScale(
                    Vector3.one,
                    false);
                mirror.Instance.ConfigureBaseScale(
                    new Vector3(-1f, 1f, 1f),
                    false);

                var variant =
                    new GhostAvatarVariantBinding();
                variant.SetRuntimeReferences(
                    null,
                    player.Instance,
                    mirror.Instance,
                    null);
                variant.Initialize(null);
                var style = new GhostGuideVisualStyle(
                    Color.cyan,
                    0.35f,
                    0.72f,
                    1.65f);

                variant.ApplyState(
                    new RhythmPoseGuideVisualState(
                        down,
                        1.2f,
                        0f,
                        true,
                        false),
                    style,
                    style);

                Assert.That(
                    player.Root.transform.position,
                    Is.EqualTo(playerTarget.transform.position));
                Assert.That(
                    mirror.Root.transform.position,
                    Is.EqualTo(mirrorTarget.transform.position));
                Assert.That(
                    player.Root.transform.localScale,
                    Is.EqualTo(
                        new Vector3(1.2f, 1.2f, 1.2f)));
                Assert.That(
                    mirror.Root.transform.localScale,
                    Is.EqualTo(
                        new Vector3(-1.2f, 1.2f, 1.2f)));
                Assert.That(
                    Vector3.Angle(
                        mirror.Root.transform.forward,
                        mirrorTarget.transform.forward),
                    Is.LessThan(0.001f));

                variant.ApplyState(
                    new RhythmPoseGuideVisualState(
                        up,
                        1f,
                        1f,
                        true,
                        true),
                    style,
                    style);

                Assert.That(
                    Quaternion.Angle(
                        player.Arm.localRotation,
                        upRotation),
                    Is.LessThan(0.001f));
                Assert.That(
                    Quaternion.Angle(
                        mirror.Arm.localRotation,
                        upRotation),
                    Is.LessThan(0.001f));
                Assert.That(
                    player.Root.transform.localScale,
                    Is.EqualTo(Vector3.one));
                Assert.That(
                    mirror.Root.transform.localScale,
                    Is.EqualTo(
                        new Vector3(-1f, 1f, 1f)));
            }
            finally
            {
                DestroyGuide(player);
                DestroyGuide(mirror);
                Object.DestroyImmediate(playerTarget);
                Object.DestroyImmediate(mirrorTarget);
                Object.DestroyImmediate(down);
                Object.DestroyImmediate(up);
            }
        }

        [Test]
        public void FollowUsesRootWorldPositionAndYawOnly()
        {
            var targetParent =
                new GameObject("TargetParent");
            var target =
                new GameObject("TargetRoot");
            target.transform.SetParent(
                targetParent.transform,
                false);
            var guide = CreateGuide(
                "YawGuide",
                target.transform);
            var pose = CreatePose(
                "Pose",
                Quaternion.identity);

            try
            {
                targetParent.transform.rotation =
                    Quaternion.Euler(0f, 25f, 0f);
                target.transform.position =
                    new Vector3(2f, 1.3f, -4f);
                target.transform.rotation =
                    Quaternion.Euler(35f, 70f, 20f);
                var offset =
                    new Vector3(0.25f, 0.1f, 0.5f);
                guide.Instance.ConfigureFollow(
                    offset,
                    0f);
                guide.Instance.ConfigureBaseScale(
                    Vector3.one,
                    false);
                guide.Instance.Initialize(null, null);

                var forward = target.transform.forward;
                forward.y = 0f;
                var expectedYaw =
                    Quaternion.LookRotation(
                        forward.normalized,
                        Vector3.up);
                guide.Instance.ApplyState(
                    new RhythmPoseGuideVisualState(
                        pose,
                        1f,
                        0.5f,
                        true,
                        false),
                    new GhostGuideVisualStyle(
                        Color.white,
                        0.35f,
                        0.72f,
                        1.65f));

                Assert.That(
                    Vector3.Distance(
                        guide.Root.transform.position,
                        target.transform.position +
                        expectedYaw * offset),
                    Is.LessThan(0.0001f));
                Assert.That(
                    Quaternion.Angle(
                        guide.Root.transform.rotation,
                        expectedYaw),
                    Is.LessThan(0.001f));
                Assert.That(
                    Vector3.Angle(
                        guide.Root.transform.up,
                        Vector3.up),
                    Is.LessThan(0.001f));
            }
            finally
            {
                DestroyGuide(guide);
                Object.DestroyImmediate(targetParent);
                Object.DestroyImmediate(pose);
            }
        }

        [Test]
        public void ChildHipsMotionDoesNotMoveRootFollowGuide()
        {
            var targetRoot =
                new GameObject("TargetRoot");
            var hips =
                new GameObject("Hips");
            hips.transform.SetParent(
                targetRoot.transform,
                false);
            var guide = CreateGuide(
                "RootGuide",
                targetRoot.transform);
            var pose = CreatePose(
                "Pose",
                Quaternion.identity);

            try
            {
                targetRoot.transform.position =
                    new Vector3(2f, 0.4f, -3f);
                hips.transform.localPosition =
                    new Vector3(0f, 1f, 0f);
                guide.Instance.ConfigureBaseScale(
                    Vector3.one,
                    false);
                guide.Instance.Initialize(null, null);
                var state =
                    new RhythmPoseGuideVisualState(
                        pose,
                        1f,
                        0.5f,
                        true,
                        false);
                var style =
                    new GhostGuideVisualStyle(
                        Color.white,
                        0.35f,
                        0.72f,
                        1.65f);

                guide.Instance.ApplyState(
                    state,
                    style);
                var positionBeforeHipsMotion =
                    guide.Root.transform.position;

                hips.transform.localPosition +=
                    Vector3.up * 0.75f;
                guide.Instance.ApplyState(
                    state,
                    style);

                Assert.That(
                    guide.Root.transform.position,
                    Is.EqualTo(positionBeforeHipsMotion));

                targetRoot.transform.position +=
                    new Vector3(1f, 0.25f, 2f);
                guide.Instance.ApplyState(
                    state,
                    style);

                Assert.That(
                    guide.Root.transform.position,
                    Is.EqualTo(targetRoot.transform.position));
            }
            finally
            {
                DestroyGuide(guide);
                Object.DestroyImmediate(targetRoot);
                Object.DestroyImmediate(pose);
            }
        }

        [Test]
        public void MotionRootTracksVrWorldMotionWithoutDoubleYaw()
        {
            var avatarRoot =
                new GameObject("AvatarRoot");
            var motionRoot =
                new GameObject("root");
            motionRoot.transform.SetParent(
                avatarRoot.transform,
                false);
            var hips =
                new GameObject("pelvis");
            hips.transform.SetParent(
                motionRoot.transform,
                false);
            avatarRoot.transform.position =
                new Vector3(1f, 0.2f, -2f);
            avatarRoot.transform.rotation =
                Quaternion.Euler(0f, 20f, 0f);
            avatarRoot.transform.localScale =
                new Vector3(1.1f, 1.2f, 1.3f);
            motionRoot.transform.localRotation =
                Quaternion.Euler(0f, 180f, 0f);
            motionRoot.transform.localScale =
                new Vector3(2f, 3f, 4f);
            hips.transform.localPosition =
                Vector3.up;

            var guide = CreateGuide(
                "VrMotion",
                avatarRoot.transform,
                null,
                motionRoot.transform);
            var ghostMotionRoot =
                new GameObject("root");
            ghostMotionRoot.transform.SetParent(
                guide.Skeleton,
                false);
            var pose = CreateRootPose(
                "Pose",
                Quaternion.Euler(0f, 180f, 0f));

            try
            {
                guide.Instance.Initialize(null, null);
                var state =
                    new RhythmPoseGuideVisualState(
                        pose,
                        1f,
                        0.5f,
                        true,
                        false);
                var style =
                    new GhostGuideVisualStyle(
                        Color.white,
                        0.35f,
                        0.72f,
                        1.65f);

                guide.Instance.ApplyState(
                    state,
                    style);
                Assert.That(
                    Vector3.Distance(
                        guide.Root.transform.position,
                        motionRoot.transform.position),
                    Is.LessThan(0.0001f));
                Assert.That(
                    Quaternion.Angle(
                        guide.Root.transform.rotation,
                        avatarRoot.transform.rotation),
                    Is.LessThan(0.001f));

                var trackedPosition =
                    new Vector3(3f, 0.45f, 4f);
                motionRoot.transform.SetPositionAndRotation(
                    trackedPosition,
                    Quaternion.Euler(0f, 235f, 0f));
                guide.Instance.ApplyState(
                    state,
                    style);

                Assert.That(
                    Vector3.Distance(
                        guide.Root.transform.position,
                        trackedPosition),
                    Is.LessThan(0.0001f));
                Assert.That(
                    Quaternion.Angle(
                        guide.Root.transform.rotation,
                        Quaternion.Euler(
                            0f,
                            55f,
                            0f)),
                    Is.LessThan(0.001f));
                Assert.That(
                    guide.Root.transform.localScale,
                    Is.EqualTo(
                        new Vector3(
                            1.1f,
                            1.2f,
                            1.3f)));
                Assert.That(
                    Vector3.Angle(
                        ghostMotionRoot.transform.forward,
                        motionRoot.transform.forward),
                    Is.LessThan(0.001f));

                hips.transform.localPosition +=
                    Vector3.up * 0.75f;
                guide.Instance.ApplyState(
                    state,
                    style);

                Assert.That(
                    Vector3.Distance(
                        guide.Root.transform.position,
                        trackedPosition),
                    Is.LessThan(0.0001f));
            }
            finally
            {
                DestroyGuide(guide);
                Object.DestroyImmediate(avatarRoot);
                Object.DestroyImmediate(pose);
            }
        }

        [Test]
        public void MissingRootSuppressesOnlyMissingGuideAndWarnsOnce()
        {
            var material = CreateGhostMaterial();
            var playerTarget =
                new GameObject("PlayerRoot");
            var player = CreateGuide(
                "PlayerGhostGuide",
                playerTarget.transform,
                material);
            var mirror = CreateGuide(
                "MirrorGhostGuide",
                null,
                material);
            var pose = CreatePose(
                "Pose",
                Quaternion.identity);

            try
            {
                player.Instance.Initialize(
                    material,
                    player.Root);
                mirror.Instance.Initialize(
                    material,
                    mirror.Root);
                var variant =
                    new GhostAvatarVariantBinding();
                variant.SetRuntimeReferences(
                    null,
                    player.Instance,
                    mirror.Instance,
                    null);
                variant.Initialize(material);
                var state =
                    new RhythmPoseGuideVisualState(
                        pose,
                        1f,
                        0.5f,
                        true,
                        false);
                var style =
                    new GhostGuideVisualStyle(
                        Color.white,
                        0.35f,
                        0.72f,
                        1.65f);

                LogAssert.Expect(
                    LogType.Warning,
                    new Regex(
                        "MirrorGhostGuide has no avatar Root"));
                Assert.That(
                    variant.IsUsable,
                    Is.True);
                variant.ApplyState(
                    state,
                    style,
                    style);
                variant.ApplyState(
                    state,
                    style,
                    style);
                Assert.That(
                    player.Renderer.enabled,
                    Is.True);
                Assert.That(
                    mirror.Renderer.enabled,
                    Is.False);
            }
            finally
            {
                DestroyGuide(player);
                DestroyGuide(mirror);
                Object.DestroyImmediate(playerTarget);
                Object.DestroyImmediate(pose);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void LegacyMirrorReferencesMigrateToRuntimeGuide()
        {
            var mirrorTarget =
                new GameObject("LegacyMirrorModel");
            var guide = CreateGuide(
                "Legacy",
                mirrorTarget.transform);
            var variant =
                new GhostAvatarVariantBinding();

            try
            {
                SetPrivateField(
                    variant,
                    "_mirrorTransform",
                    mirrorTarget.transform);
                SetPrivateField(
                    variant,
                    "_ghostRoot",
                    guide.Visual);
                SetPrivateField(
                    variant,
                    "_skeletonRoot",
                    guide.Skeleton);

                variant.Initialize(null);

                Assert.That(
                    variant.IsUsable,
                    Is.True);
                Assert.That(
                    variant.MirrorGuide.GuideRoot,
                    Is.SameAs(guide.Visual));
                Assert.That(
                    variant.MirrorGuide.VisualRoot,
                    Is.SameAs(guide.Visual));
                Assert.That(
                    variant.MirrorGuide.TargetRoot,
                    Is.SameAs(mirrorTarget.transform));
                Assert.That(
                    variant.MirrorGuide.SkeletonRoot,
                    Is.SameAs(guide.Skeleton));
            }
            finally
            {
                DestroyGuide(guide);
                Object.DestroyImmediate(mirrorTarget);
            }
        }

        [Test]
        public void SeparateVisualsUseIndependentPropertyBlocks()
        {
            var material = CreateGhostMaterial();
            var firstTarget =
                new GameObject("FirstRoot");
            var secondTarget =
                new GameObject("SecondRoot");
            var first = CreateGuide(
                "First",
                firstTarget.transform,
                material);
            var second = CreateGuide(
                "Second",
                secondTarget.transform,
                material);
            var pose = CreatePose(
                "Pose",
                Quaternion.identity);

            try
            {
                first.Instance.Initialize(
                    material,
                    first.Root);
                second.Instance.Initialize(
                    material,
                    second.Root);
                var state =
                    new RhythmPoseGuideVisualState(
                        pose,
                        1f,
                        1f,
                        true,
                        true);
                var firstStyle =
                    new GhostGuideVisualStyle(
                        Color.cyan,
                        0.2f,
                        0.6f,
                        0.7f,
                        1.5f);
                var secondStyle =
                    new GhostGuideVisualStyle(
                        Color.magenta,
                        0.3f,
                        0.8f,
                        0.9f,
                        2f);

                first.Instance.ApplyState(
                    state,
                    firstStyle);
                second.Instance.ApplyState(
                    state,
                    secondStyle);

                var firstBlock =
                    new MaterialPropertyBlock();
                var secondBlock =
                    new MaterialPropertyBlock();
                first.Renderer.GetPropertyBlock(firstBlock);
                second.Renderer.GetPropertyBlock(secondBlock);
                var colorId =
                    Shader.PropertyToID("_BaseColor");
                var intensityId =
                    Shader.PropertyToID("_Intensity");

                Assert.That(
                    firstBlock.GetColor(colorId),
                    Is.EqualTo(
                        new Color(0f, 1f, 1f, 0.6f)));
                Assert.That(
                    secondBlock.GetColor(colorId),
                    Is.EqualTo(
                        new Color(1f, 0f, 1f, 0.8f)));
                Assert.That(
                    firstBlock.GetFloat(intensityId),
                    Is.EqualTo(1.5f));
                Assert.That(
                    secondBlock.GetFloat(intensityId),
                    Is.EqualTo(2f));

                var normalState =
                    new RhythmPoseGuideVisualState(
                        pose,
                        1f,
                        0.5f,
                        true,
                        false);
                first.Instance.ApplyState(
                    normalState,
                    firstStyle);
                second.Instance.ApplyState(
                    normalState,
                    secondStyle);
                first.Renderer.GetPropertyBlock(firstBlock);
                second.Renderer.GetPropertyBlock(secondBlock);

                Assert.That(
                    firstBlock.GetColor(colorId).a,
                    Is.EqualTo(0.2f));
                Assert.That(
                    secondBlock.GetColor(colorId).a,
                    Is.EqualTo(0.3f));
                Assert.That(
                    firstBlock.GetFloat(intensityId),
                    Is.EqualTo(0.7f));
                Assert.That(
                    secondBlock.GetFloat(intensityId),
                    Is.EqualTo(0.9f));
                Assert.That(
                    first.Renderer.sharedMaterial,
                    Is.SameAs(material));
                Assert.That(
                    second.Renderer.sharedMaterial,
                    Is.SameAs(material));
            }
            finally
            {
                DestroyGuide(first);
                DestroyGuide(second);
                Object.DestroyImmediate(firstTarget);
                Object.DestroyImmediate(secondTarget);
                Object.DestroyImmediate(pose);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void MuscleStateCopiesFixedAndConfiguredShapes()
        {
            var material = CreateGhostMaterial();
            var sourceRoot = new GameObject("MuscleSyncSource");
            var targetRoot = new GameObject("MuscleSyncTarget");
            var guide = CreateGuide(
                "MuscleSyncGuide",
                targetRoot.transform,
                material);
            var mesh = CreateMesh("MuscleSyncMesh");

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

                var sourceRenderer =
                    sourceRoot.AddComponent<SkinnedMeshRenderer>();
                sourceRenderer.sharedMesh = mesh;
                guide.Renderer.sharedMesh = mesh;
                sourceRenderer.SetBlendShapeWeight(0, 63f);
                sourceRenderer.SetBlendShapeWeight(1, 100f);
                sourceRenderer.SetBlendShapeWeight(2, 27f);

                var output =
                    sourceRoot.AddComponent<
                        BlendShapeMuscleGrowthOutput>();
                var serializedOutput = new SerializedObject(output);
                var bindings =
                    serializedOutput.FindProperty("_bindings");
                bindings.arraySize = 1;
                var binding = bindings.GetArrayElementAtIndex(0);
                binding.FindPropertyRelative("_renderer")
                    .objectReferenceValue = sourceRenderer;
                binding.FindPropertyRelative("_blendShapeName")
                    .stringValue = "UpperArm.L";
                serializedOutput.ApplyModifiedPropertiesWithoutUndo();

                guide.Instance.Initialize(material, null);
                var synchronizer =
                    new MuscleBlendShapeStateSynchronizer();
                Assert.That(
                    guide.Instance.RebuildMuscleState(
                        output,
                        sourceRenderer,
                        synchronizer),
                    Is.EqualTo(3));
                Assert.That(
                    guide.Renderer.GetBlendShapeWeight(0),
                    Is.EqualTo(63f).Within(0.001f));
                Assert.That(
                    guide.Renderer.GetBlendShapeWeight(1),
                    Is.EqualTo(100f).Within(0.001f),
                    "The fixed Muscle shape must be copied from the body.");
                Assert.That(
                    guide.Renderer.GetBlendShapeWeight(2),
                    Is.EqualTo(27f).Within(0.001f));
            }
            finally
            {
                DestroyGuide(guide);
                Object.DestroyImmediate(sourceRoot);
                Object.DestroyImmediate(targetRoot);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(mesh);
            }
        }

        private static GuideFixture CreateGuide(
            string name,
            Transform target,
            Material material = null,
            Transform motionRoot = null)
        {
            var root = new GameObject(name + "Root");
            var visual = new GameObject(name + "Visual");
            visual.transform.SetParent(
                root.transform,
                false);
            var skeleton = new GameObject("Armature");
            skeleton.transform.SetParent(
                visual.transform,
                false);
            var arm = new GameObject("Arm");
            arm.transform.SetParent(
                skeleton.transform,
                false);
            SkinnedMeshRenderer renderer = null;
            Mesh mesh = null;
            if (material != null)
            {
                renderer =
                    visual.AddComponent<
                        SkinnedMeshRenderer>();
                mesh = CreateMesh(name + "Mesh");
                renderer.sharedMesh = mesh;
                renderer.sharedMaterial = material;
            }

            var instance = new GhostGuideInstance();
            instance.SetRuntimeReferences(
                name,
                root,
                visual,
                skeleton.transform,
                target,
                target,
                motionRoot);
            return new GuideFixture(
                root,
                visual,
                skeleton.transform,
                arm.transform,
                renderer,
                mesh,
                instance);
        }

        private static PoseAsset CreatePose(
            string name,
            Quaternion armRotation)
        {
            var pose =
                ScriptableObject.CreateInstance<PoseAsset>();
            pose.ReplacePoseData(
                name,
                BodyRepKind.ElbowFlexion,
                RepTimingPoint.Up,
                new[]
                {
                    new PoseBoneData(
                        "Arm",
                        Vector3.zero,
                        armRotation,
                        false,
                        Vector3.one)
                });
            return pose;
        }

        private static PoseAsset CreateRootPose(
            string name,
            Quaternion rootRotation)
        {
            var pose =
                ScriptableObject.CreateInstance<PoseAsset>();
            pose.ReplacePoseData(
                name,
                BodyRepKind.ElbowFlexion,
                RepTimingPoint.Up,
                new[]
                {
                    new PoseBoneData(
                        "root",
                        Vector3.zero,
                        rootRotation,
                        false,
                        Vector3.one)
                });
            return pose;
        }

        private static Material CreateGhostMaterial()
        {
            var shader = Shader.Find(
                "IVRC/Rhythm Pose Guide/" +
                "Ghost Avatar Unlit");
            Assert.That(shader, Is.Not.Null);
            return new Material(shader);
        }

        private static Mesh CreateMesh(string name)
        {
            return new Mesh
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
        }

        private static void SetPrivateField(
            GhostAvatarVariantBinding target,
            string fieldName,
            object value)
        {
            var field =
                typeof(GhostAvatarVariantBinding).GetField(
                    fieldName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private static void DestroyGuide(
            GuideFixture fixture)
        {
            if (fixture.Root != null)
            {
                Object.DestroyImmediate(fixture.Root);
            }

            if (fixture.Mesh != null)
            {
                Object.DestroyImmediate(fixture.Mesh);
            }
        }

        private readonly struct GuideFixture
        {
            public GuideFixture(
                GameObject root,
                GameObject visual,
                Transform skeleton,
                Transform arm,
                SkinnedMeshRenderer renderer,
                Mesh mesh,
                GhostGuideInstance instance)
            {
                Root = root;
                Visual = visual;
                Skeleton = skeleton;
                Arm = arm;
                Renderer = renderer;
                Mesh = mesh;
                Instance = instance;
            }

            public GameObject Root { get; }
            public GameObject Visual { get; }
            public Transform Skeleton { get; }
            public Transform Arm { get; }
            public SkinnedMeshRenderer Renderer { get; }
            public Mesh Mesh { get; }
            public GhostGuideInstance Instance { get; }
        }
    }
}
