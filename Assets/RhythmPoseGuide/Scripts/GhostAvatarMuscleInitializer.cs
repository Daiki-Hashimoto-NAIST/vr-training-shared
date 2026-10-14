using System.Collections.Generic;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Applies only explicitly configured muscle BlendShapes to a ghost.
    /// No unlisted BlendShape, including facial and eye shapes, is changed.
    /// </summary>
    public static class GhostAvatarMuscleInitializer
    {
        public static int ApplyConfiguredTargets(
            IReadOnlyList<SkinnedMeshRenderer> ghostRenderers,
            BlendShapeMuscleGrowthOutput source)
        {
            return source == null
                ? 0
                : ApplyConfiguredTargets(
                    ghostRenderers,
                    source.Bindings);
        }

        public static int ApplyConfiguredTargets(
            IReadOnlyList<SkinnedMeshRenderer> ghostRenderers,
            IReadOnlyList<MuscleBlendShapeBinding> bindings)
        {
            if (ghostRenderers == null ||
                bindings == null)
            {
                return 0;
            }

            var appliedCount = 0;
            for (var bindingIndex = 0;
                 bindingIndex < bindings.Count;
                 bindingIndex++)
            {
                var binding = bindings[bindingIndex];
                if (binding == null ||
                    string.IsNullOrWhiteSpace(
                        binding.BlendShapeName))
                {
                    continue;
                }

                for (var rendererIndex = 0;
                     rendererIndex < ghostRenderers.Count;
                     rendererIndex++)
                {
                    var renderer =
                        ghostRenderers[rendererIndex];
                    var mesh = renderer == null
                        ? null
                        : renderer.sharedMesh;
                    if (mesh == null)
                    {
                        continue;
                    }

                    var shapeIndex =
                        mesh.GetBlendShapeIndex(
                            binding.BlendShapeName);
                    if (shapeIndex < 0)
                    {
                        continue;
                    }

                    renderer.SetBlendShapeWeight(
                        shapeIndex,
                        binding.FullGrowthTargetWeight);
                    appliedCount++;
                }
            }

            return appliedCount;
        }
    }
}
