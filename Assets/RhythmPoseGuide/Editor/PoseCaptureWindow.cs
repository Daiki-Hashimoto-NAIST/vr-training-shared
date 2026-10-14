using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IVRC
{
    public sealed class PoseCaptureWindow : EditorWindow
    {
        private const string WindowTitle = "Rhythm Pose Capture";

        [Serializable]
        private sealed class BaselineTransformData
        {
            public BaselineTransformData(Transform transform)
            {
                Transform = transform;
                LocalPosition = transform.localPosition;
                LocalRotation = transform.localRotation;
                LocalScale = transform.localScale;
            }

            public Transform Transform;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
        }

        private readonly struct CaptureTransform
        {
            public CaptureTransform(
                Transform transform,
                string relativePath)
            {
                Transform = transform;
                RelativePath = relativePath;
            }

            public Transform Transform { get; }
            public string RelativePath { get; }
        }

        [SerializeField] private Transform _skeletonRoot;
        [SerializeField] private PoseAsset _poseAsset;
        [SerializeField] private bool _deformBonesOnly = true;
        [SerializeField] private bool _includeScale;

        [SerializeField] private Transform _baselineRoot;
        [SerializeField]
        private List<BaselineTransformData> _baseline =
            new List<BaselineTransformData>();

        [MenuItem(
            "Tools/IVRC/Rhythm Pose Guide/Pose Capture")]
        public static void Open()
        {
            GetWindow<PoseCaptureWindow>(WindowTitle);
        }

        private void OnEnable()
        {
            if (_baseline == null)
            {
                _baseline =
                    new List<BaselineTransformData>();
            }

            if (_skeletonRoot != null &&
                (_baselineRoot != _skeletonRoot ||
                 _baseline.Count == 0))
            {
                CaptureBaseline();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "Pose Source",
                EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var nextRoot = (Transform)EditorGUILayout.ObjectField(
                "Skeleton Root",
                _skeletonRoot,
                typeof(Transform),
                true);
            if (EditorGUI.EndChangeCheck())
            {
                _skeletonRoot = nextRoot;
                CaptureBaseline();
            }

            _poseAsset = (PoseAsset)EditorGUILayout.ObjectField(
                "Pose Asset",
                _poseAsset,
                typeof(PoseAsset),
                false);
            _deformBonesOnly = EditorGUILayout.Toggle(
                "Deform Bones Only",
                _deformBonesOnly);
            _includeScale = EditorGUILayout.Toggle(
                "Include Scale",
                _includeScale);

            DrawPoseSummary();

            if (_skeletonRoot != null &&
                EditorUtility.IsPersistent(_skeletonRoot))
            {
                EditorGUILayout.HelpBox(
                    "Use a scene or Prefab Stage instance as the " +
                    "skeleton root. Imported model assets cannot be " +
                    "posed in place.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                var canEditPose =
                    _skeletonRoot != null &&
                    _poseAsset != null &&
                    !EditorUtility.IsPersistent(_skeletonRoot);
                using (new EditorGUI.DisabledScope(!canEditPose))
                {
                    if (GUILayout.Button("Capture"))
                    {
                        CapturePose();
                    }

                    if (GUILayout.Button("Apply"))
                    {
                        ApplyPose();
                    }
                }

                var canReset =
                    _skeletonRoot != null &&
                    _baselineRoot == _skeletonRoot &&
                    _baseline != null &&
                    _baseline.Count > 0 &&
                    !EditorUtility.IsPersistent(_skeletonRoot);
                using (new EditorGUI.DisabledScope(!canReset))
                {
                    if (GUILayout.Button("Reset"))
                    {
                        ResetToBaseline();
                    }
                }
            }

            EditorGUILayout.HelpBox(
                "Assigning a skeleton root remembers its current " +
                "local transforms as the Reset baseline. Adjust the " +
                "avatar manually, then Capture into one of the four " +
                "exercise pose assets.",
                MessageType.Info);
        }

        private void DrawPoseSummary()
        {
            if (_poseAsset == null)
            {
                return;
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(
                    "Pose Name",
                    _poseAsset.PoseName);
                EditorGUILayout.EnumPopup(
                    "Exercise",
                    _poseAsset.Kind);
                EditorGUILayout.EnumPopup(
                    "Timing",
                    _poseAsset.TimingPoint);
                EditorGUILayout.IntField(
                    "Captured Bones",
                    _poseAsset.Bones.Count);
            }
        }

        private void CaptureBaseline()
        {
            _baselineRoot = _skeletonRoot;
            _baseline.Clear();
            if (_skeletonRoot == null ||
                EditorUtility.IsPersistent(_skeletonRoot))
            {
                return;
            }

            var transforms =
                _skeletonRoot.GetComponentsInChildren<Transform>(
                    true);
            for (var index = 0;
                 index < transforms.Length;
                 index++)
            {
                _baseline.Add(
                    new BaselineTransformData(
                        transforms[index]));
            }
        }

        private void CapturePose()
        {
            var transforms = CollectCaptureTransforms();
            if (transforms.Count == 0)
            {
                Debug.LogWarning(
                    "[RhythmPoseGuide] No pose transforms were " +
                    "found under the selected skeleton root.",
                    _skeletonRoot);
                return;
            }

            var capturedBones =
                new List<PoseBoneData>(transforms.Count);
            for (var index = 0;
                 index < transforms.Count;
                 index++)
            {
                var entry = transforms[index];
                var transform = entry.Transform;
                capturedBones.Add(
                    new PoseBoneData(
                        entry.RelativePath,
                        transform.localPosition,
                        transform.localRotation,
                        _includeScale,
                        transform.localScale));
            }

            Undo.RecordObject(
                _poseAsset,
                "Capture Rhythm Guide Pose");
            _poseAsset.ReplacePoseData(capturedBones);
            EditorUtility.SetDirty(_poseAsset);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[RhythmPoseGuide] Captured " +
                $"{capturedBones.Count} transforms into " +
                $"'{_poseAsset.name}'.",
                _poseAsset);
        }

        private List<CaptureTransform>
            CollectCaptureTransforms()
        {
            var uniqueTransforms = new HashSet<Transform>();
            if (_deformBonesOnly)
            {
                CollectDeformBones(uniqueTransforms);
            }
            else
            {
                var allTransforms =
                    _skeletonRoot
                        .GetComponentsInChildren<Transform>(
                            true);
                for (var index = 0;
                     index < allTransforms.Length;
                     index++)
                {
                    uniqueTransforms.Add(
                        allTransforms[index]);
                }
            }

            var entries =
                new List<CaptureTransform>(
                    uniqueTransforms.Count);
            var uniquePaths =
                new HashSet<string>(
                    StringComparer.Ordinal);
            foreach (var transform in uniqueTransforms)
            {
                if (transform == null)
                {
                    continue;
                }

                var path =
                    AnimationUtility
                        .CalculateTransformPath(
                            transform,
                            _skeletonRoot);
                if (!uniquePaths.Add(path))
                {
                    Debug.LogWarning(
                        $"[RhythmPoseGuide] Duplicate animation " +
                        $"path '{path}' was skipped. Rename " +
                        "duplicate sibling transforms before " +
                        "capturing.",
                        transform);
                    continue;
                }

                entries.Add(
                    new CaptureTransform(
                        transform,
                        path));
            }

            entries.Sort(
                (left, right) =>
                    string.CompareOrdinal(
                        left.RelativePath,
                        right.RelativePath));
            return entries;
        }

        private void CollectDeformBones(
            HashSet<Transform> transforms)
        {
            // In imported FBX hierarchies the SkinnedMeshRenderers are often
            // siblings of the Armature selected as the skeleton root.
            var rendererSearchRoot =
                _skeletonRoot.parent == null
                    ? _skeletonRoot
                    : _skeletonRoot.parent;
            var renderers =
                rendererSearchRoot
                    .GetComponentsInChildren<
                        SkinnedMeshRenderer>(true);
            for (var rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                if (renderer.rootBone == null ||
                    (renderer.rootBone != _skeletonRoot &&
                     !renderer.rootBone.IsChildOf(
                         _skeletonRoot)))
                {
                    continue;
                }

                AddBoneAndAncestors(
                    renderer.rootBone,
                    transforms);

                var bones = renderer.bones;
                for (var boneIndex = 0;
                     boneIndex < bones.Length;
                     boneIndex++)
                {
                    AddBoneAndAncestors(
                        bones[boneIndex],
                        transforms);
                }
            }
        }

        private void AddBoneAndAncestors(
            Transform bone,
            HashSet<Transform> transforms)
        {
            if (bone == null ||
                (bone != _skeletonRoot &&
                 !bone.IsChildOf(_skeletonRoot)))
            {
                return;
            }

            var current = bone;
            while (current != null)
            {
                transforms.Add(current);
                if (current == _skeletonRoot)
                {
                    break;
                }

                current = current.parent;
            }
        }

        private void ApplyPose()
        {
            var applier = new GhostAvatarPoseApplier(
                _skeletonRoot,
                _poseAsset);
            var resolvedTransforms =
                new List<Transform>(
                    applier.CachedBoneCount);
            var uniqueTransforms = new HashSet<Transform>();
            for (var index = 0;
                 index < applier.CachedBoneCount;
                 index++)
            {
                var transform =
                    applier.GetCachedTransform(index);
                if (transform != null &&
                    uniqueTransforms.Add(transform))
                {
                    resolvedTransforms.Add(transform);
                }
            }

            if (resolvedTransforms.Count > 0)
            {
                Undo.RecordObjects(
                    resolvedTransforms.ToArray(),
                    "Apply Rhythm Guide Pose");
            }

            var missingCount = applier.Apply();
            MarkSceneDirty();
            Debug.Log(
                $"[RhythmPoseGuide] Applied " +
                $"'{_poseAsset.name}' with {missingCount} " +
                "missing pose paths.",
                _skeletonRoot);
        }

        private void ResetToBaseline()
        {
            var validEntries =
                new List<BaselineTransformData>(
                    _baseline.Count);
            var transforms =
                new List<Transform>(_baseline.Count);
            for (var index = 0;
                 index < _baseline.Count;
                 index++)
            {
                var entry = _baseline[index];
                if (entry == null ||
                    entry.Transform == null)
                {
                    continue;
                }

                validEntries.Add(entry);
                transforms.Add(entry.Transform);
            }

            if (transforms.Count == 0)
            {
                return;
            }

            Undo.RecordObjects(
                transforms.ToArray(),
                "Reset Rhythm Guide Pose");
            for (var index = 0;
                 index < validEntries.Count;
                 index++)
            {
                var entry = validEntries[index];
                entry.Transform.localPosition =
                    entry.LocalPosition;
                entry.Transform.localRotation =
                    entry.LocalRotation;
                entry.Transform.localScale =
                    entry.LocalScale;
            }

            MarkSceneDirty();
        }

        private void MarkSceneDirty()
        {
            if (_skeletonRoot == null)
            {
                return;
            }

            var scene = _skeletonRoot.gameObject.scene;
            if (scene.IsValid() && scene.isLoaded)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }
    }
}
