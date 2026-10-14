using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace IVRC
{
    /// <summary>
    /// Configures and controls the renderers of one persistent ghost avatar.
    /// Renderer lookup and material-slot replacement happen only when
    /// initialized; visual updates reuse cached property blocks.
    /// </summary>
    public sealed class GhostAvatarMaterialController
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int IntensityId =
            Shader.PropertyToID("_Intensity");
        private static readonly int ApproachOffsetId =
            Shader.PropertyToID("_ApproachOffset");

        private SkinnedMeshRenderer[] _renderers =
            Array.Empty<SkinnedMeshRenderer>();
        private MaterialPropertyBlock[] _propertyBlocks =
            Array.Empty<MaterialPropertyBlock>();

        public IReadOnlyList<SkinnedMeshRenderer> Renderers =>
            _renderers;

        public bool Initialize(
            GameObject root,
            Material ghostMaterial)
        {
            _renderers = Array.Empty<SkinnedMeshRenderer>();
            _propertyBlocks =
                Array.Empty<MaterialPropertyBlock>();

            if (root == null || ghostMaterial == null)
            {
                return false;
            }

            _renderers =
                root.GetComponentsInChildren<
                    SkinnedMeshRenderer>(true);
            _propertyBlocks =
                new MaterialPropertyBlock[_renderers.Length];

            for (var rendererIndex = 0;
                 rendererIndex < _renderers.Length;
                 rendererIndex++)
            {
                var renderer = _renderers[rendererIndex];
                if (renderer == null)
                {
                    continue;
                }

                ReplaceAllMaterialSlots(
                    renderer,
                    ghostMaterial);
                ConfigureLightweightRendering(renderer);
                _propertyBlocks[rendererIndex] =
                    new MaterialPropertyBlock();
            }

            return _renderers.Length > 0;
        }

        public void SetVisual(
            Color color,
            float intensity)
        {
            SetVisual(color, intensity, 0f);
        }

        /// <summary>
        /// Replaces the ghost material on every cached renderer. Used when the
        /// display mode switches between the filled ghost and the line art.
        /// </summary>
        public void SetMaterial(Material ghostMaterial)
        {
            if (ghostMaterial == null)
            {
                return;
            }

            foreach (var renderer in _renderers)
            {
                if (renderer != null)
                {
                    ReplaceAllMaterialSlots(
                        renderer,
                        ghostMaterial);
                }
            }
        }

        public void SetVisual(
            Color color,
            float intensity,
            float contourOffset)
        {
            intensity = SanitizeIntensity(intensity);
            contourOffset = SanitizeContourOffset(contourOffset);

            for (var rendererIndex = 0;
                 rendererIndex < _renderers.Length;
                 rendererIndex++)
            {
                var renderer = _renderers[rendererIndex];
                var propertyBlock =
                    _propertyBlocks[rendererIndex];
                if (renderer == null ||
                    propertyBlock == null)
                {
                    continue;
                }

                propertyBlock.Clear();
                propertyBlock.SetColor(
                    BaseColorId,
                    color);
                propertyBlock.SetFloat(
                    IntensityId,
                    intensity);
                propertyBlock.SetFloat(
                    ApproachOffsetId,
                    contourOffset);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        public void SetVisible(bool visible)
        {
            for (var rendererIndex = 0;
                 rendererIndex < _renderers.Length;
                 rendererIndex++)
            {
                var renderer = _renderers[rendererIndex];
                if (renderer != null)
                {
                    renderer.enabled = visible;
                }
            }
        }

        private static void ReplaceAllMaterialSlots(
            SkinnedMeshRenderer renderer,
            Material ghostMaterial)
        {
            var sharedMaterials = renderer.sharedMaterials;
            for (var materialIndex = 0;
                 materialIndex < sharedMaterials.Length;
                 materialIndex++)
            {
                sharedMaterials[materialIndex] =
                    ghostMaterial;
            }

            renderer.sharedMaterials = sharedMaterials;
        }

        private static void ConfigureLightweightRendering(
            SkinnedMeshRenderer renderer)
        {
            renderer.shadowCastingMode =
                ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage =
                LightProbeUsage.Off;
            renderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            renderer.skinnedMotionVectors = false;
        }

        private static float SanitizeIntensity(float intensity)
        {
            if (float.IsNaN(intensity) ||
                float.IsInfinity(intensity))
            {
                return 1f;
            }

            return Mathf.Max(0f, intensity);
        }

        private static float SanitizeContourOffset(
            float contourOffset)
        {
            if (float.IsNaN(contourOffset) ||
                float.IsInfinity(contourOffset))
            {
                return 0f;
            }

            return Mathf.Max(0f, contourOffset);
        }
    }
}
