using System;
using System.Collections.Generic;
using UnityEngine;

namespace IVRC
{
    [Serializable]
    public sealed class PoseBoneData
    {
        [SerializeField] private string _relativePath = string.Empty;
        [SerializeField] private Vector3 _localPosition;
        [SerializeField] private Quaternion _localRotation =
            Quaternion.identity;
        [SerializeField] private bool _includeScale;
        [SerializeField] private Vector3 _localScale = Vector3.one;

        public PoseBoneData()
        {
        }

        public PoseBoneData(
            string relativePath,
            Vector3 localPosition,
            Quaternion localRotation,
            bool includeScale,
            Vector3 localScale)
        {
            _relativePath = relativePath ?? string.Empty;
            _localPosition = localPosition;
            _localRotation = localRotation;
            _includeScale = includeScale;
            _localScale = localScale;
        }

        public PoseBoneData(PoseBoneData source)
            : this(
                source != null
                    ? source.RelativePath
                    : string.Empty,
                source != null
                    ? source.LocalPosition
                    : Vector3.zero,
                source != null
                    ? source.LocalRotation
                    : Quaternion.identity,
                source != null && source.IncludeScale,
                source != null
                    ? source.LocalScale
                    : Vector3.one)
        {
        }

        public string RelativePath => _relativePath ?? string.Empty;
        public Vector3 LocalPosition => _localPosition;
        public Quaternion LocalRotation => _localRotation;
        public bool IncludeScale => _includeScale;
        public Vector3 LocalScale => _localScale;
    }

    /// <summary>
    /// A captured local-space skeleton pose for one exercise timing point.
    /// Pose values are intentionally authored by capture instead of generated
    /// from guessed joint angles.
    /// </summary>
    [CreateAssetMenu(
        fileName = "RhythmPose",
        menuName = "IVRC/Rhythm Pose Guide/Pose Asset")]
    public sealed class PoseAsset : ScriptableObject
    {
        [SerializeField] private string _poseName = "New Pose";
        [SerializeField] private BodyRepKind _kind =
            BodyRepKind.ElbowFlexion;
        [SerializeField] private RepTimingPoint _timingPoint =
            RepTimingPoint.Down;
        [SerializeField] private PoseBoneData[] _bones =
            Array.Empty<PoseBoneData>();

        public string PoseName => _poseName ?? string.Empty;
        public BodyRepKind Kind => _kind;
        public RepTimingPoint TimingPoint => _timingPoint;
        public IReadOnlyList<PoseBoneData> Bones =>
            _bones ?? Array.Empty<PoseBoneData>();

        /// <summary>
        /// Replaces only the captured transforms while keeping the identity
        /// fields configured in the Inspector. Callers should register Undo
        /// before invoking this method when editing an asset.
        /// </summary>
        public void ReplacePoseData(
            IReadOnlyList<PoseBoneData> bones)
        {
            _bones = CopyBones(bones);
        }

        /// <summary>
        /// Replaces the complete asset payload. This overload is useful for
        /// editor tooling and deterministic test setup.
        /// </summary>
        public void ReplacePoseData(
            string poseName,
            BodyRepKind kind,
            RepTimingPoint timingPoint,
            IReadOnlyList<PoseBoneData> bones)
        {
            _poseName = poseName ?? string.Empty;
            _kind = kind;
            _timingPoint = timingPoint;
            _bones = CopyBones(bones);
        }

        private static PoseBoneData[] CopyBones(
            IReadOnlyList<PoseBoneData> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<PoseBoneData>();
            }

            var result = new PoseBoneData[source.Count];
            for (var index = 0; index < source.Count; index++)
            {
                var bone = source[index];
                result[index] =
                    bone == null ? null : new PoseBoneData(bone);
            }

            return result;
        }

        private void OnValidate()
        {
            _poseName ??= string.Empty;
            _bones ??= Array.Empty<PoseBoneData>();
        }
    }
}
