using System;
using System.Collections.Generic;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Caches and copies only the muscle BlendShapes explicitly configured on
    /// the authoritative tracked renderer. Mirror bindings, duplicate targets,
    /// facial shapes, and stale mesh references are ignored.
    /// </summary>
    public sealed class MuscleBlendShapeStateSynchronizer
    {
        private readonly List<SourceChannel> _channels =
            new List<SourceChannel>();
        private readonly HashSet<string> _sourceNames =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<TargetKey> _targetKeys =
            new HashSet<TargetKey>();

        public int ChannelCount => _channels.Count;
        public int TargetCount { get; private set; }
        public bool IsComplete { get; private set; }
        public string ValidationError { get; private set; } =
            string.Empty;

        public int Rebuild(
            BlendShapeMuscleGrowthOutput output,
            SkinnedMeshRenderer authoritativeRenderer,
            IReadOnlyList<SkinnedMeshRenderer> targetRenderers)
        {
            Clear();
            if (!TryValidateTargets(
                    output,
                    authoritativeRenderer,
                    targetRenderers,
                    out var validationError))
            {
                ValidationError = validationError;
                return 0;
            }

            IsComplete = true;
            var sourceMesh = authoritativeRenderer == null
                ? null
                : authoritativeRenderer.sharedMesh;
            var bindings = output == null
                ? null
                : output.Bindings;
            if (sourceMesh == null ||
                bindings == null ||
                targetRenderers == null)
            {
                return 0;
            }

            for (var bindingIndex = 0;
                 bindingIndex < bindings.Count;
                 bindingIndex++)
            {
                var binding = bindings[bindingIndex];
                if (binding == null ||
                    binding.Renderer != authoritativeRenderer ||
                    string.IsNullOrWhiteSpace(
                        binding.BlendShapeName) ||
                    !_sourceNames.Add(binding.BlendShapeName))
                {
                    continue;
                }

                var sourceIndex =
                    sourceMesh.GetBlendShapeIndex(
                        binding.BlendShapeName);
                if (sourceIndex < 0)
                {
                    continue;
                }

                var targets = new List<TargetSlot>();
                for (var targetIndex = 0;
                     targetIndex < targetRenderers.Count;
                     targetIndex++)
                {
                    var renderer = targetRenderers[targetIndex];
                    var targetMesh = renderer == null
                        ? null
                        : renderer.sharedMesh;
                    if (targetMesh == null)
                    {
                        continue;
                    }

                    var shapeIndex =
                        targetMesh.GetBlendShapeIndex(
                            binding.BlendShapeName);
                    var key = new TargetKey(
                        renderer,
                        shapeIndex);
                    if (shapeIndex < 0 ||
                        !_targetKeys.Add(key))
                    {
                        continue;
                    }

                    targets.Add(
                        new TargetSlot(
                            renderer,
                            targetMesh,
                            shapeIndex));
                }

                if (targets.Count <= 0)
                {
                    continue;
                }

                _channels.Add(
                    new SourceChannel(
                        authoritativeRenderer,
                        sourceMesh,
                        sourceIndex,
                        targets.ToArray(),
                        true));
                TargetCount += targets.Count;
            }

            return TargetCount;
        }

        /// <summary>
        /// Copies every same-named BlendShape from the authoritative body to a
        /// presentation clone. This includes fixed character-shape values that
        /// are intentionally not controlled by muscle growth (for example the
        /// female model's base Muscle shape), while the regular Rebuild method
        /// remains limited to configured growth channels for ghosts and other
        /// gameplay consumers.
        /// </summary>
        public int RebuildAllSharedBlendShapes(
            SkinnedMeshRenderer authoritativeRenderer,
            IReadOnlyList<SkinnedMeshRenderer> targetRenderers)
        {
            Clear();
            var sourceMesh = authoritativeRenderer == null
                ? null
                : authoritativeRenderer.sharedMesh;
            if (sourceMesh == null)
            {
                ValidationError =
                    "The authoritative body mesh is not assigned.";
                return 0;
            }

            if (targetRenderers == null ||
                targetRenderers.Count <= 0)
            {
                ValidationError =
                    "The presentation clone has no skinned mesh renderer.";
                return 0;
            }

            IsComplete = true;
            for (var sourceIndex = 0;
                 sourceIndex < sourceMesh.blendShapeCount;
                 sourceIndex++)
            {
                var shapeName =
                    sourceMesh.GetBlendShapeName(sourceIndex);
                if (string.IsNullOrWhiteSpace(shapeName) ||
                    !_sourceNames.Add(shapeName))
                {
                    continue;
                }

                var targets = new List<TargetSlot>();
                for (var targetIndex = 0;
                     targetIndex < targetRenderers.Count;
                     targetIndex++)
                {
                    var renderer = targetRenderers[targetIndex];
                    var targetMesh = renderer == null
                        ? null
                        : renderer.sharedMesh;
                    if (targetMesh == null)
                    {
                        continue;
                    }

                    var shapeIndex =
                        targetMesh.GetBlendShapeIndex(shapeName);
                    var key = new TargetKey(
                        renderer,
                        shapeIndex);
                    if (shapeIndex < 0 ||
                        !_targetKeys.Add(key))
                    {
                        continue;
                    }

                    targets.Add(
                        new TargetSlot(
                            renderer,
                            targetMesh,
                            shapeIndex));
                }

                if (targets.Count <= 0)
                {
                    continue;
                }

                _channels.Add(
                    new SourceChannel(
                        authoritativeRenderer,
                        sourceMesh,
                        sourceIndex,
                        targets.ToArray(),
                        false));
                TargetCount += targets.Count;
            }

            if (TargetCount <= 0)
            {
                IsComplete = false;
                ValidationError =
                    "The presentation clone has no matching BlendShape.";
            }

            return TargetCount;
        }

        /// <summary>
        /// Verifies that every unique muscle shape configured on the tracked
        /// renderer exists on at least one renderer in a presentation clone.
        /// Facial and mirror-only bindings are deliberately excluded.
        /// </summary>
        public static bool TryValidateTargets(
            BlendShapeMuscleGrowthOutput output,
            SkinnedMeshRenderer authoritativeRenderer,
            IReadOnlyList<SkinnedMeshRenderer> targetRenderers,
            out string error)
        {
            if (output == null)
            {
                error = "Muscle growth output is not assigned.";
                return false;
            }

            var sourceMesh = authoritativeRenderer == null
                ? null
                : authoritativeRenderer.sharedMesh;
            if (sourceMesh == null)
            {
                error =
                    "The authoritative tracked body mesh is not assigned.";
                return false;
            }

            if (targetRenderers == null ||
                targetRenderers.Count <= 0)
            {
                error =
                    "The presentation clone has no skinned mesh renderer.";
                return false;
            }

            var configuredNames = new List<string>();
            var uniqueNames =
                new HashSet<string>(StringComparer.Ordinal);
            var bindings = output.Bindings;
            for (var bindingIndex = 0;
                 bindingIndex < bindings.Count;
                 bindingIndex++)
            {
                var binding = bindings[bindingIndex];
                if (binding == null ||
                    binding.Renderer != authoritativeRenderer)
                {
                    continue;
                }

                var shapeName = binding.BlendShapeName;
                if (string.IsNullOrWhiteSpace(shapeName))
                {
                    error =
                        "A tracked muscle BlendShape binding has no name.";
                    return false;
                }

                if (sourceMesh.GetBlendShapeIndex(shapeName) < 0)
                {
                    error =
                        $"Tracked body mesh has no configured " +
                        $"'{shapeName}' BlendShape.";
                    return false;
                }

                if (uniqueNames.Add(shapeName))
                {
                    configuredNames.Add(shapeName);
                }
            }

            if (configuredNames.Count <= 0)
            {
                error =
                    "No muscle BlendShape is configured on the tracked " +
                    "body renderer.";
                return false;
            }

            var missingNames = new List<string>();
            foreach (var shapeName in configuredNames)
            {
                var found = false;
                for (var targetIndex = 0;
                     targetIndex < targetRenderers.Count;
                     targetIndex++)
                {
                    var mesh = targetRenderers[targetIndex] == null
                        ? null
                        : targetRenderers[targetIndex].sharedMesh;
                    if (mesh != null &&
                        mesh.GetBlendShapeIndex(shapeName) >= 0)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    missingNames.Add(shapeName);
                }
            }

            if (missingNames.Count > 0)
            {
                error =
                    "Presentation clone is missing configured muscle " +
                    $"BlendShape(s): {string.Join(", ", missingNames)}.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public int Apply()
        {
            var appliedCount = 0;
            foreach (var channel in _channels)
            {
                if (!channel.TryReadWeight(out var weight))
                {
                    continue;
                }

                foreach (var target in channel.Targets)
                {
                    if (!target.IsValid)
                    {
                        continue;
                    }

                    target.Renderer.SetBlendShapeWeight(
                        target.Index,
                        weight);
                    appliedCount++;
                }
            }

            return appliedCount;
        }

        public void Clear()
        {
            _channels.Clear();
            _sourceNames.Clear();
            _targetKeys.Clear();
            TargetCount = 0;
            IsComplete = false;
            ValidationError = string.Empty;
        }

        private sealed class SourceChannel
        {
            public SourceChannel(
                SkinnedMeshRenderer renderer,
                Mesh mesh,
                int index,
                TargetSlot[] targets,
                bool clampToMuscleRange)
            {
                Renderer = renderer;
                Mesh = mesh;
                Index = index;
                Targets = targets;
                ClampToMuscleRange = clampToMuscleRange;
            }

            private SkinnedMeshRenderer Renderer { get; }
            private Mesh Mesh { get; }
            private int Index { get; }
            public TargetSlot[] Targets { get; }
            private bool ClampToMuscleRange { get; }

            public bool TryReadWeight(out float weight)
            {
                weight = 0f;
                if (Renderer == null ||
                    Renderer.sharedMesh != Mesh ||
                    Index < 0 ||
                    Index >= Mesh.blendShapeCount)
                {
                    return false;
                }

                weight = Renderer.GetBlendShapeWeight(Index);
                if (float.IsNaN(weight) ||
                    float.IsInfinity(weight))
                {
                    weight = 0f;
                    return false;
                }

                if (ClampToMuscleRange)
                {
                    weight = Mathf.Clamp(weight, 0f, 100f);
                }

                return true;
            }
        }

        private readonly struct TargetSlot
        {
            public TargetSlot(
                SkinnedMeshRenderer renderer,
                Mesh mesh,
                int index)
            {
                Renderer = renderer;
                Mesh = mesh;
                Index = index;
            }

            public SkinnedMeshRenderer Renderer { get; }
            private Mesh Mesh { get; }
            public int Index { get; }
            public bool IsValid =>
                Renderer != null &&
                Renderer.sharedMesh == Mesh &&
                Index >= 0 &&
                Index < Mesh.blendShapeCount;
        }

        private readonly struct TargetKey : IEquatable<TargetKey>
        {
            public TargetKey(
                SkinnedMeshRenderer renderer,
                int index)
            {
                Renderer = renderer;
                Index = index;
            }

            private SkinnedMeshRenderer Renderer { get; }
            private int Index { get; }

            public bool Equals(TargetKey other)
            {
                return ReferenceEquals(
                           Renderer,
                           other.Renderer) &&
                       Index == other.Index;
            }

            public override bool Equals(object obj)
            {
                return obj is TargetKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Renderer == null
                                ? 0
                                : Renderer.GetInstanceID()) * 397) ^
                           Index;
                }
            }
        }
    }
}
