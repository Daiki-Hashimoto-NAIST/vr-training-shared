using System;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Resolves pose paths once and applies the cached local transforms without
    /// a MonoBehaviour, Animator, per-frame hierarchy search, or allocation.
    /// </summary>
    public sealed class GhostAvatarPoseApplier
    {
        private readonly struct CachedBone
        {
            public CachedBone(
                Transform transform,
                PoseBoneData poseData)
            {
                Transform = transform;
                PoseData = poseData;
            }

            public Transform Transform { get; }
            public PoseBoneData PoseData { get; }
        }

        private CachedBone[] _cachedBones =
            Array.Empty<CachedBone>();

        public GhostAvatarPoseApplier()
        {
        }

        public GhostAvatarPoseApplier(
            Transform skeletonRoot,
            PoseAsset pose)
        {
            RebuildCache(skeletonRoot, pose);
        }

        public Transform SkeletonRoot { get; private set; }
        public PoseAsset Pose { get; private set; }
        public bool ApplyLocalPositions { get; set; } = true;
        public int CachedBoneCount => _cachedBones.Length;
        public int MissingPathCount { get; private set; }

        /// <summary>
        /// Resolves every relative path once. An empty path targets the supplied
        /// skeleton root. Missing paths are cached as null and returned as a
        /// count instead of throwing.
        /// </summary>
        public int RebuildCache(
            Transform skeletonRoot,
            PoseAsset pose)
        {
            SkeletonRoot = skeletonRoot;
            Pose = pose;

            var bones = pose != null ? pose.Bones : null;
            if (bones == null || bones.Count == 0)
            {
                _cachedBones = Array.Empty<CachedBone>();
                MissingPathCount = 0;
                return 0;
            }

            _cachedBones = new CachedBone[bones.Count];
            var missingCount = 0;

            for (var index = 0; index < bones.Count; index++)
            {
                var poseData = bones[index];
                Transform target = null;

                if (skeletonRoot != null && poseData != null)
                {
                    target = string.IsNullOrEmpty(
                        poseData.RelativePath)
                        ? skeletonRoot
                        : skeletonRoot.Find(
                            poseData.RelativePath);
                }

                if (target == null)
                {
                    missingCount++;
                }

                _cachedBones[index] = new CachedBone(
                    target,
                    poseData);
            }

            MissingPathCount = missingCount;
            return missingCount;
        }

        public int SetSkeletonRoot(Transform skeletonRoot)
        {
            return RebuildCache(skeletonRoot, Pose);
        }

        /// <summary>
        /// Applies cached local values and returns the number of entries that
        /// could not be applied. Destroyed transforms are handled as missing.
        /// </summary>
        public int Apply()
        {
            var missingCount = 0;

            for (var index = 0;
                 index < _cachedBones.Length;
                 index++)
            {
                var cachedBone = _cachedBones[index];
                var target = cachedBone.Transform;
                var poseData = cachedBone.PoseData;
                if (target == null || poseData == null)
                {
                    missingCount++;
                    continue;
                }

                if (ApplyLocalPositions)
                {
                    target.localPosition =
                        poseData.LocalPosition;
                }

                target.localRotation = poseData.LocalRotation;
                if (poseData.IncludeScale)
                {
                    target.localScale = poseData.LocalScale;
                }
            }

            MissingPathCount = missingCount;
            return missingCount;
        }

        /// <summary>
        /// Selects and applies a pose, rebuilding the path cache only when the
        /// pose changes. The return value is the number of applied entries;
        /// path failures remain available through MissingPathCount.
        /// </summary>
        public int Apply(PoseAsset pose)
        {
            if (Pose != pose)
            {
                RebuildCache(SkeletonRoot, pose);
            }

            var missingCount = Apply();
            return _cachedBones.Length - missingCount;
        }

        public int ApplyPose()
        {
            return Apply();
        }

        public int ApplyPose(PoseAsset pose)
        {
            return Apply(pose);
        }

        /// <summary>
        /// Exposes a resolved transform for editor Undo registration without
        /// requiring another hierarchy lookup.
        /// </summary>
        public Transform GetCachedTransform(int index)
        {
            return index >= 0 && index < _cachedBones.Length
                ? _cachedBones[index].Transform
                : null;
        }
    }
}
