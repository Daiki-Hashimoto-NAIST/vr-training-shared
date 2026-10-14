using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace IVRC
{
    /// <summary>
    /// Idempotently creates the dedicated scene duplicates, assets, and
    /// references required by RhythmPoseGuideController. It never edits an
    /// imported FBX or the source avatar's materials.
    /// </summary>
    public static class RhythmPoseGuideSceneInstaller
    {
        private const string RootFolder =
            "Assets/RhythmPoseGuide";
        private const string PoseFolder =
            RootFolder + "/PoseAssets";
        private const string MaterialFolder =
            RootFolder + "/Materials";
        private const string GhostMaterialPath =
            MaterialFolder + "/GhostAvatar.mat";
        private const string GhostShaderName =
            "IVRC/Rhythm Pose Guide/Ghost Avatar Unlit";
        private const string GuideRootName =
            "RhythmGuideRoot";
        private const string LegacyGhostContainerName =
            "GhostAvatar";
        private const string PlayerGhostContainerName =
            "PlayerGhostGuide";
        private const string MirrorGhostContainerName =
            "MirrorGhostGuide";

        [MenuItem(
            "Tools/IVRC/Rhythm Pose Guide/Install Into Active Scene")]
        public static void InstallIntoActiveScene()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning(
                    "[RhythmPoseGuide] Exit Play Mode before " +
                    "installing the scene guide.");
                return;
            }

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError(
                    "[RhythmPoseGuide] No loaded active scene.");
                return;
            }

            var trainingRoot =
                GameObject.Find("Phase_Training");
            var trackedAnchor =
                GameObject.Find(
                    "Player/TrackedAvatarAnchor");
            var mirrorAnchor =
                GameObject.Find(
                    "Player/MirrorAvatarAnchor");
            var registry =
                UnityEngine.Object
                    .FindFirstObjectByType<AvatarRegistry>();
            var rhythmClock =
                UnityEngine.Object
                    .FindFirstObjectByType<RhythmClock>();
            var rhythmChart =
                UnityEngine.Object
                    .FindFirstObjectByType<
                        TrainingRhythmChart>();
            var gameFlow =
                UnityEngine.Object
                    .FindFirstObjectByType<
                        GameFlowController>();
            var ringGuide =
                UnityEngine.Object
                    .FindFirstObjectByType<
                        TrainingRhythmLaneHud>(
                        FindObjectsInactive.Include);

            if (trainingRoot == null ||
                trackedAnchor == null ||
                mirrorAnchor == null ||
                registry == null ||
                rhythmClock == null ||
                rhythmChart == null ||
                gameFlow == null ||
                ringGuide == null)
            {
                Debug.LogError(
                    "[RhythmPoseGuide] Required scene systems " +
                    "were not found. Open VRTrainingPrototype " +
                    "and try again.");
                return;
            }

            EnsureFolder(RootFolder);
            EnsureFolder(PoseFolder);
            EnsureFolder(MaterialFolder);

            var ghostMaterial = EnsureGhostMaterial();
            if (ghostMaterial == null)
            {
                return;
            }

            var armCurlDown = EnsurePoseAsset(
                "ArmCurl_Down",
                BodyRepKind.ElbowFlexion,
                RepTimingPoint.Down);
            var armCurlUp = EnsurePoseAsset(
                "ArmCurl_Up",
                BodyRepKind.ElbowFlexion,
                RepTimingPoint.Up);
            var sideRaiseDown = EnsurePoseAsset(
                "SideRaise_Down",
                BodyRepKind.ArmRaise,
                RepTimingPoint.Down);
            var sideRaiseUp = EnsurePoseAsset(
                "SideRaise_Up",
                BodyRepKind.ArmRaise,
                RepTimingPoint.Up);

            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(
                "Install Rhythm Pose Guide");

            var guideRoot = FindOrCreateGuideRoot(
                trainingRoot.transform,
                scene);
            var playerGhostContainer = FindOrCreateChild(
                guideRoot,
                PlayerGhostContainerName,
                trackedAnchor.layer);
            var mirrorGhostContainer = FindOrCreateChild(
                guideRoot,
                MirrorGhostContainerName,
                mirrorAnchor.layer);
            var legacyGhostContainer =
                guideRoot.Find(
                    LegacyGhostContainerName);
            var controller =
                guideRoot.GetComponent<
                    RhythmPoseGuideController>();
            if (controller == null)
            {
                controller =
                    Undo.AddComponent<
                        RhythmPoseGuideController>(
                        guideRoot.gameObject);
            }

            var installedVariants =
                new List<InstalledVariant>();
            foreach (var avatar in registry.Avatars)
            {
                if (avatar == null)
                {
                    continue;
                }

                var installed = EnsureGhostVariant(
                    avatar,
                    playerGhostContainer,
                    mirrorGhostContainer,
                    legacyGhostContainer,
                    ghostMaterial,
                    trackedAnchor.layer,
                    mirrorAnchor.layer);
                if (installed.Player.IsConfigured ||
                    installed.Mirror.IsConfigured)
                {
                    installedVariants.Add(installed);
                }
            }

            if (installedVariants.Count <= 0)
            {
                Debug.LogError(
                    "[RhythmPoseGuide] No registered avatar " +
                    "could be duplicated for the ghost guide.");
                Undo.CollapseUndoOperations(undoGroup);
                return;
            }

            if (legacyGhostContainer != null &&
                legacyGhostContainer.childCount == 0)
            {
                Undo.DestroyObjectImmediate(
                    legacyGhostContainer.gameObject);
            }

            WireController(
                controller,
                rhythmClock,
                rhythmChart,
                gameFlow,
                ringGuide,
                registry,
                ghostMaterial,
                armCurlDown,
                armCurlUp,
                sideRaiseDown,
                sideRaiseUp,
                installedVariants);

            Undo.RecordObject(
                ringGuide,
                "Disable Legacy Rhythm Rings");
            ringGuide.SetRingVisualsEnabled(false);
            EditorUtility.SetDirty(ringGuide);

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject =
                guideRoot.gameObject;
            Undo.CollapseUndoOperations(undoGroup);

            Debug.Log(
                $"[RhythmPoseGuide] Installed " +
                $"{installedVariants.Count} synchronized " +
                "Player/Mirror ghost avatar variant pairs into " +
                $"'{scene.name}'. " +
                "Existing PoseAsset contents were retained.",
                controller);
        }

        private static Material EnsureGhostMaterial()
        {
            var shader = Shader.Find(GhostShaderName);
            if (shader == null)
            {
                Debug.LogError(
                    $"[RhythmPoseGuide] Shader " +
                    $"'{GhostShaderName}' is not imported yet. " +
                    "Wait for compilation and run Install again.");
                return null;
            }

            var material =
                AssetDatabase.LoadAssetAtPath<Material>(
                    GhostMaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "GhostAvatar",
                    renderQueue =
                        (int)RenderQueue.Transparent
                };
                material.SetColor(
                    "_BaseColor",
                    new Color(
                        0.72f,
                        0.92f,
                        1f,
                        0.35f));
                material.SetFloat("_Intensity", 1f);
                AssetDatabase.CreateAsset(
                    material,
                    GhostMaterialPath);
            }
            else if (material.shader != shader)
            {
                Undo.RecordObject(
                    material,
                    "Update Ghost Avatar Shader");
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        private static PoseAsset EnsurePoseAsset(
            string assetName,
            BodyRepKind kind,
            RepTimingPoint timingPoint)
        {
            var path =
                $"{PoseFolder}/{assetName}.asset";
            var pose =
                AssetDatabase.LoadAssetAtPath<PoseAsset>(
                    path);
            if (pose != null)
            {
                return pose;
            }

            pose = ScriptableObject.CreateInstance<PoseAsset>();
            pose.name = assetName;
            pose.ReplacePoseData(
                assetName,
                kind,
                timingPoint,
                Array.Empty<PoseBoneData>());
            AssetDatabase.CreateAsset(pose, path);
            return pose;
        }

        private static InstalledVariant EnsureGhostVariant(
            AvatarDefinition avatar,
            Transform playerGhostContainer,
            Transform mirrorGhostContainer,
            Transform legacyGhostContainer,
            Material ghostMaterial,
            int playerLayer,
            int mirrorLayer)
        {
            var result = new InstalledVariant
            {
                Avatar = avatar,
                MirrorTransform =
                    avatar.MirrorInstance == null
                        ? null
                        : avatar.MirrorInstance.transform,
                MuscleConfigSource = avatar.GrowthOutput
            };
            var playerRoot = avatar.transform;
            var mirrorRoot =
                avatar.MirrorInstance == null
                    ? null
                    : avatar.MirrorInstance.transform;
            var playerMotionRoot =
                GetRetargetedMotionRoot(avatar);
            var mirrorMotionRoot =
                FindEquivalentMirrorRoot(
                    playerRoot,
                    playerMotionRoot,
                    mirrorRoot);

            var trackedRenderer =
                avatar.TrackedBodyRenderer;
            var modelPath =
                trackedRenderer == null ||
                trackedRenderer.sharedMesh == null
                    ? string.Empty
                    : AssetDatabase.GetAssetPath(
                        trackedRenderer.sharedMesh);
            var modelAsset =
                string.IsNullOrEmpty(modelPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<
                        GameObject>(modelPath);
            if (modelAsset == null)
            {
                Debug.LogWarning(
                    $"[RhythmPoseGuide] Model asset for " +
                    $"'{avatar.DisplayName}' was not found.",
                    avatar);
                return result;
            }

            var legacyObjectName =
                avatar.gameObject.name + "Ghost";
            var legacyVisual =
                legacyGhostContainer == null
                    ? null
                    : legacyGhostContainer.Find(
                        legacyObjectName);
            result.Player = EnsureGhostVisual(
                modelAsset,
                avatar.gameObject.name +
                "PlayerGhost",
                playerGhostContainer,
                null,
                playerRoot,
                playerMotionRoot,
                ghostMaterial,
                result.MuscleConfigSource,
                playerLayer);
            result.Mirror = EnsureGhostVisual(
                modelAsset,
                avatar.gameObject.name +
                "MirrorGhost",
                mirrorGhostContainer,
                legacyVisual,
                mirrorRoot,
                mirrorMotionRoot,
                ghostMaterial,
                result.MuscleConfigSource,
                mirrorLayer);

            if (!result.Player.IsComplete)
            {
                Debug.LogWarning(
                    $"[RhythmPoseGuide] Player ghost for " +
                    $"'{avatar.DisplayName}' is incomplete.",
                    avatar);
            }

            if (!result.Mirror.IsComplete)
            {
                Debug.LogWarning(
                    $"[RhythmPoseGuide] Mirror ghost for " +
                    $"'{avatar.DisplayName}' is incomplete.",
                    avatar);
            }

            return result;
        }

        private static InstalledGuide EnsureGhostVisual(
            GameObject modelAsset,
            string objectName,
            Transform guideContainer,
            Transform legacyVisual,
            Transform targetRoot,
            Transform motionRoot,
            Material ghostMaterial,
            BlendShapeMuscleGrowthOutput muscleConfigSource,
            int layer)
        {
            var result = new InstalledGuide
            {
                GuideRoot = guideContainer.gameObject,
                TargetRoot = targetRoot,
                MotionRoot = motionRoot,
                ScaleSource = targetRoot,
                BaseScale = targetRoot == null
                    ? Vector3.one
                    : targetRoot.lossyScale
            };

            var existing = guideContainer.Find(objectName);
            GameObject visualRoot;
            if (existing != null)
            {
                visualRoot = existing.gameObject;
                Undo.RegisterFullObjectHierarchyUndo(
                    visualRoot,
                    "Update Ghost Avatar Visual");
            }
            else if (legacyVisual != null)
            {
                visualRoot = legacyVisual.gameObject;
                Undo.RegisterFullObjectHierarchyUndo(
                    visualRoot,
                    "Migrate Ghost Avatar Visual");
                Undo.SetTransformParent(
                    visualRoot.transform,
                    guideContainer,
                    "Move Legacy Ghost Avatar Visual");
                visualRoot.name = objectName;
            }
            else
            {
                visualRoot =
                    PrefabUtility.InstantiatePrefab(
                        modelAsset,
                        guideContainer) as GameObject;
                if (visualRoot == null)
                {
                    return result;
                }

                Undo.RegisterCreatedObjectUndo(
                    visualRoot,
                    "Create Ghost Avatar Visual");
                visualRoot.name = objectName;
            }

            visualRoot.SetActive(true);
            visualRoot.transform.localPosition =
                Vector3.zero;
            visualRoot.transform.localRotation =
                Quaternion.identity;
            visualRoot.transform.localScale =
                Vector3.one;
            SetLayerRecursively(visualRoot, layer);

            result.VisualRoot = visualRoot;
            result.SkeletonRoot =
                FindSkeletonRoot(visualRoot);

            foreach (var behaviour in
                     visualRoot.GetComponentsInChildren<
                         Behaviour>(true))
            {
                behaviour.enabled = false;
            }

            var materialController =
                new GhostAvatarMaterialController();
            materialController.Initialize(
                visualRoot,
                ghostMaterial);
            materialController.SetVisual(
                new Color(
                    0.72f,
                    0.92f,
                    1f,
                    0.35f),
                1f);
            GhostAvatarMuscleInitializer
                .ApplyConfiguredTargets(
                    materialController.Renderers,
                    muscleConfigSource);
            materialController.SetVisible(false);
            for (var rendererIndex = 0;
                 rendererIndex <
                 materialController.Renderers.Count;
                 rendererIndex++)
            {
                var renderer =
                    materialController.Renderers[
                        rendererIndex];
                if (renderer == null)
                {
                    continue;
                }

                PrefabUtility
                    .RecordPrefabInstancePropertyModifications(
                        renderer);
                EditorUtility.SetDirty(renderer);
            }

            if (result.SkeletonRoot == null)
            {
                Debug.LogWarning(
                    $"[RhythmPoseGuide] No skeleton root " +
                    $"was found in '{objectName}'.",
                    visualRoot);
            }

            if (result.MotionRoot == null)
            {
                Debug.LogWarning(
                    $"[RhythmPoseGuide] No tracked Armature/root " +
                    $"was found for '{objectName}'. VR world-motion " +
                    "follow will fall back to the avatar GameObject root.",
                    visualRoot);
            }

            return result;
        }

        private static Transform GetRetargetedMotionRoot(
            AvatarDefinition avatar)
        {
            var pairs =
                avatar == null ||
                avatar.Retargeter == null
                    ? null
                    : avatar.Retargeter.JointPairs;
            if (pairs != null &&
                pairs.Length > 0 &&
                pairs[0].Joint != null)
            {
                return pairs[0].Joint;
            }

            return avatar == null
                ? null
                : avatar.transform.Find(
                    "Armature/root");
        }

        private static Transform FindEquivalentMirrorRoot(
            Transform playerAvatarRoot,
            Transform playerMotionRoot,
            Transform mirrorAvatarRoot)
        {
            if (playerAvatarRoot == null ||
                playerMotionRoot == null ||
                mirrorAvatarRoot == null ||
                (!playerMotionRoot.IsChildOf(
                     playerAvatarRoot) &&
                 playerMotionRoot != playerAvatarRoot))
            {
                return null;
            }

            var relativePath =
                AnimationUtility.CalculateTransformPath(
                    playerMotionRoot,
                    playerAvatarRoot);
            return string.IsNullOrEmpty(relativePath)
                ? mirrorAvatarRoot
                : mirrorAvatarRoot.Find(relativePath);
        }

        private static Transform FindSkeletonRoot(
            GameObject ghostRoot)
        {
            SkinnedMeshRenderer best = null;
            var bestBoneCount = -1;
            foreach (var renderer in
                     ghostRoot.GetComponentsInChildren<
                         SkinnedMeshRenderer>(true))
            {
                var boneCount =
                    renderer.bones == null
                        ? 0
                        : renderer.bones.Length;
                if (renderer.rootBone == null ||
                    boneCount <= bestBoneCount)
                {
                    continue;
                }

                best = renderer;
                bestBoneCount = boneCount;
            }

            if (best == null || best.rootBone == null)
            {
                return null;
            }

            var candidate = best.rootBone;
            while (candidate.parent != null &&
                   candidate.parent !=
                   ghostRoot.transform)
            {
                candidate = candidate.parent;
            }

            return candidate;
        }

        private static void WireController(
            RhythmPoseGuideController controller,
            RhythmClock rhythmClock,
            TrainingRhythmChart rhythmChart,
            GameFlowController gameFlow,
            TrainingRhythmLaneHud ringGuide,
            AvatarRegistry registry,
            Material ghostMaterial,
            PoseAsset armCurlDown,
            PoseAsset armCurlUp,
            PoseAsset sideRaiseDown,
            PoseAsset sideRaiseUp,
            IReadOnlyList<InstalledVariant> variants)
        {
            Undo.RecordObject(
                controller,
                "Wire Rhythm Pose Guide");
            var serialized =
                new SerializedObject(controller);
            SetReference(
                serialized,
                "_rhythmClock",
                rhythmClock);
            SetReference(
                serialized,
                "_rhythmChart",
                rhythmChart);
            SetReference(
                serialized,
                "_gameFlow",
                gameFlow);
            SetReference(
                serialized,
                "_ringGuide",
                ringGuide);
            SetReference(
                serialized,
                "_avatarRegistry",
                registry);
            SetReference(
                serialized,
                "_ghostMaterial",
                ghostMaterial);
            SetReference(
                serialized,
                "_armCurlDown",
                armCurlDown);
            SetReference(
                serialized,
                "_armCurlUp",
                armCurlUp);
            SetReference(
                serialized,
                "_sideRaiseDown",
                sideRaiseDown);
            SetReference(
                serialized,
                "_sideRaiseUp",
                sideRaiseUp);
            serialized.FindProperty("_displayMode").intValue =
                (int)RhythmGuideDisplayMode.GhostAvatar;

            var first = variants[0];
            var fallbackGuide =
                first.Mirror.IsComplete
                    ? first.Mirror
                    : first.Player;
            SetReference(
                serialized,
                "_mirrorTransform",
                fallbackGuide.TargetRoot);
            SetReference(
                serialized,
                "_ghostTransform",
                fallbackGuide.VisualRoot == null
                    ? null
                    : fallbackGuide.VisualRoot.transform);
            SetReference(
                serialized,
                "_skeletonRoot",
                fallbackGuide.SkeletonRoot);
            SetReference(
                serialized,
                "_muscleConfigSource",
                first.MuscleConfigSource);

            var variantsProperty =
                serialized.FindProperty("_variants");
            variantsProperty.arraySize = variants.Count;
            for (var index = 0;
                 index < variants.Count;
                 index++)
            {
                var source = variants[index];
                var element =
                    variantsProperty
                        .GetArrayElementAtIndex(index);
                SetRelativeReference(
                    element,
                    "_sourceAvatar",
                    source.Avatar);
                SetRelativeReference(
                    element,
                    "_mirrorTransform",
                    source.MirrorTransform);
                SetRelativeReference(
                    element,
                    "_ghostRoot",
                    source.Mirror.VisualRoot);
                SetRelativeReference(
                    element,
                    "_skeletonRoot",
                    source.Mirror.SkeletonRoot);
                SetRelativeReference(
                    element,
                    "_muscleConfigSource",
                    source.MuscleConfigSource);
                SetGuideReferences(
                    element.FindPropertyRelative(
                        "_playerGuide"),
                    "PlayerGhostGuide",
                    source.Player);
                SetGuideReferences(
                    element.FindPropertyRelative(
                        "_mirrorGuide"),
                    "MirrorGhostGuide",
                    source.Mirror);
            }

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(controller);
        }

        private static Transform FindOrCreateGuideRoot(
            Transform desiredParent,
            Scene scene)
        {
            RhythmPoseGuideController existingController = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                existingController =
                    root.GetComponentInChildren<
                        RhythmPoseGuideController>(true);
                if (existingController != null)
                {
                    break;
                }
            }

            var guideRoot = existingController == null
                ? null
                : existingController.transform;
            if (guideRoot == null)
            {
                var named = FindDescendantByName(
                    scene,
                    GuideRootName);
                guideRoot = named;
            }

            if (guideRoot == null)
            {
                return FindOrCreateChild(
                    desiredParent,
                    GuideRootName,
                    desiredParent.gameObject.layer);
            }

            Undo.RegisterFullObjectHierarchyUndo(
                guideRoot.gameObject,
                "Move Rhythm Guide Root");
            if (guideRoot.parent != desiredParent)
            {
                Undo.SetTransformParent(
                    guideRoot,
                    desiredParent,
                    "Parent Rhythm Guide Root");
            }

            guideRoot.localPosition = Vector3.zero;
            guideRoot.localRotation = Quaternion.identity;
            guideRoot.localScale = Vector3.one;
            guideRoot.gameObject.layer =
                desiredParent.gameObject.layer;
            return guideRoot;
        }

        private static Transform FindDescendantByName(
            Scene scene,
            string objectName)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var transforms =
                    root.GetComponentsInChildren<
                        Transform>(true);
                for (var index = 0;
                     index < transforms.Length;
                     index++)
                {
                    if (transforms[index].name == objectName)
                    {
                        return transforms[index];
                    }
                }
            }

            return null;
        }

        private static Transform FindOrCreateChild(
            Transform parent,
            string objectName,
            int layer)
        {
            var existing = parent.Find(objectName);
            if (existing != null)
            {
                SetLayerRecursively(
                    existing.gameObject,
                    layer);
                return existing;
            }

            var child = new GameObject(objectName);
            Undo.RegisterCreatedObjectUndo(
                child,
                $"Create {objectName}");
            Undo.SetTransformParent(
                child.transform,
                parent,
                $"Parent {objectName}");
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation =
                Quaternion.identity;
            child.transform.localScale = Vector3.one;
            child.layer = layer;
            return child.transform;
        }

        private static void SetLayerRecursively(
            GameObject root,
            int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
            {
                SetLayerRecursively(
                    child.gameObject,
                    layer);
            }
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            var slashIndex =
                folderPath.LastIndexOf('/');
            var parentPath =
                folderPath.Substring(0, slashIndex);
            var folderName =
                folderPath.Substring(slashIndex + 1);
            EnsureFolder(parentPath);
            AssetDatabase.CreateFolder(
                parentPath,
                folderName);
        }

        private static void SetReference(
            SerializedObject serialized,
            string propertyName,
            UnityEngine.Object value)
        {
            var property =
                serialized.FindProperty(propertyName);
            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static void SetRelativeReference(
            SerializedProperty parent,
            string propertyName,
            UnityEngine.Object value)
        {
            var property =
                parent.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static void SetGuideReferences(
            SerializedProperty guide,
            string label,
            InstalledGuide source)
        {
            if (guide == null)
            {
                return;
            }

            guide.FindPropertyRelative("_label")
                .stringValue = label;
            SetRelativeReference(
                guide,
                "_guideRoot",
                source.GuideRoot);
            SetRelativeReference(
                guide,
                "_visualRoot",
                source.VisualRoot);
            SetRelativeReference(
                guide,
                "_skeletonRoot",
                source.SkeletonRoot);
            SetRelativeReference(
                guide,
                "_targetRoot",
                source.TargetRoot);
            SetRelativeReference(
                guide,
                "_motionRoot",
                source.MotionRoot);
            SetRelativeReference(
                guide,
                "_scaleSource",
                source.ScaleSource);
            guide.FindPropertyRelative("_followPosition")
                .boolValue = true;
            guide.FindPropertyRelative("_followYaw")
                .boolValue = true;
            guide.FindPropertyRelative("_offsetSpace")
                .enumValueIndex =
                (int)GhostGuideOffsetSpace.Local;
            guide.FindPropertyRelative("_positionOffset")
                .vector3Value = Vector3.zero;
            guide.FindPropertyRelative("_yawOffset")
                .floatValue = 0f;
            guide.FindPropertyRelative(
                    "_autoCaptureBaseScale")
                .boolValue = true;
            guide.FindPropertyRelative("_baseScale")
                .vector3Value = source.BaseScale;
        }

        private struct InstalledVariant
        {
            public AvatarDefinition Avatar;
            public Transform MirrorTransform;
            public InstalledGuide Player;
            public InstalledGuide Mirror;
            public BlendShapeMuscleGrowthOutput
                MuscleConfigSource;
        }

        private struct InstalledGuide
        {
            public GameObject GuideRoot;
            public GameObject VisualRoot;
            public Transform SkeletonRoot;
            public Transform TargetRoot;
            public Transform MotionRoot;
            public Transform ScaleSource;
            public Vector3 BaseScale;

            public bool IsConfigured =>
                GuideRoot != null &&
                VisualRoot != null &&
                SkeletonRoot != null;

            public bool IsComplete =>
                IsConfigured &&
                TargetRoot != null &&
                MotionRoot != null;
        }
    }
}
