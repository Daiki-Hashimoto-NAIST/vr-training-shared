using System.Collections.Generic;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Maps earned growth to a restrained surface response. Shape remains the
    /// primary body change; normal strength and smoothness only improve how
    /// existing muscle planes catch the light.
    /// </summary>
    public static class MuscleSurfacePresentationMath
    {
        public static float EvaluateNormalScale(
            float normalizedGrowth,
            float restingScale,
            float trainedScale)
        {
            return Mathf.Lerp(
                SanitizeScale(restingScale, 0.72f),
                SanitizeScale(trainedScale, 1.08f),
                Smooth(normalizedGrowth));
        }

        public static float EvaluateSmoothness(
            float normalizedGrowth,
            float restingSmoothness,
            float trainedSmoothness)
        {
            return Mathf.Lerp(
                SanitizeNormalized(restingSmoothness, 0.14f),
                SanitizeNormalized(trainedSmoothness, 0.25f),
                Smooth(normalizedGrowth));
        }

        private static float Smooth(float value)
        {
            value = SanitizeNormalized(value, 0f);
            return value * value * (3f - 2f * value);
        }

        private static float SanitizeScale(
            float value,
            float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? fallback
                : Mathf.Clamp(value, 0f, 2f);
        }

        private static float SanitizeNormalized(
            float value,
            float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? fallback
                : Mathf.Clamp01(value);
        }
    }

    /// <summary>
    /// The colour each timing verdict is shown in. Defined once so the body
    /// and the judgement sounds can name the same verdict; a participant on a
    /// loud exhibition floor may only have the colour to go on.
    ///
    /// <see cref="TrainingRhythmLaneHud"/> keeps serialized copies of the hit
    /// and miss colours for its lane visuals. They agree with these values and
    /// should be pointed here once the per-verdict sounds land.
    /// </summary>
    public static class TrainingTimingPalette
    {
        public static Color GetGradeColor(
            TrainingTimingGrade grade)
        {
            return grade switch
            {
                TrainingTimingGrade.Perfect =>
                    new Color(0.35f, 1f, 0.62f, 1f),
                TrainingTimingGrade.Good =>
                    new Color(0.45f, 0.9f, 1f, 1f),
                TrainingTimingGrade.Ok =>
                    new Color(1f, 0.8f, 0.3f, 1f),
                _ => new Color(1f, 0.68f, 0.28f, 1f)
            };
        }

        /// <summary>
        /// The same verdict colour pulled towards white. On the headset the
        /// full-strength colours read as paint on the skin rather than as
        /// light coming off it; whitening keeps the verdict legible while
        /// letting the body underneath show through.
        /// </summary>
        public static Color GetGradeColor(
            TrainingTimingGrade grade,
            float whiteness)
        {
            whiteness =
                float.IsNaN(whiteness) ||
                float.IsInfinity(whiteness)
                    ? 0f
                    : Mathf.Clamp01(whiteness);
            var color = Color.Lerp(
                GetGradeColor(grade),
                Color.white,
                whiteness);
            color.a = 1f;
            return color;
        }
    }

    public readonly struct MuscleSurfaceSnapshot
    {
        public MuscleSurfaceSnapshot(
            float normalizedGrowth,
            float restingNormalScale,
            float trainedNormalScale,
            float restingSmoothness,
            float trainedSmoothness)
            : this(
                normalizedGrowth,
                restingNormalScale,
                trainedNormalScale,
                restingSmoothness,
                trainedSmoothness,
                0f,
                TrainingTimingGrade.Perfect,
                0f,
                0f)
        {
        }

        public MuscleSurfaceSnapshot(
            float normalizedGrowth,
            float restingNormalScale,
            float trainedNormalScale,
            float restingSmoothness,
            float trainedSmoothness,
            float pulseGlow,
            TrainingTimingGrade pulseGrade,
            float pulseIntensity,
            float pulseWhiteness)
        {
            NormalizedGrowth = Mathf.Clamp01(
                float.IsNaN(normalizedGrowth) ||
                float.IsInfinity(normalizedGrowth)
                    ? 0f
                    : normalizedGrowth);
            RestingNormalScale = restingNormalScale;
            TrainedNormalScale = trainedNormalScale;
            RestingSmoothness = restingSmoothness;
            TrainedSmoothness = trainedSmoothness;
            PulseGlow = Mathf.Clamp01(
                float.IsNaN(pulseGlow) ||
                float.IsInfinity(pulseGlow)
                    ? 0f
                    : pulseGlow);
            PulseGrade = pulseGrade;
            PulseIntensity =
                float.IsNaN(pulseIntensity) ||
                float.IsInfinity(pulseIntensity)
                    ? 0f
                    : Mathf.Clamp(pulseIntensity, 0f, 8f);
            PulseWhiteness =
                float.IsNaN(pulseWhiteness) ||
                float.IsInfinity(pulseWhiteness)
                    ? 0f
                    : Mathf.Clamp01(pulseWhiteness);
        }

        public float NormalizedGrowth { get; }
        public float RestingNormalScale { get; }
        public float TrainedNormalScale { get; }
        public float RestingSmoothness { get; }
        public float TrainedSmoothness { get; }

        /// <summary>Strength of the current repetition's flash, 0 to 1.</summary>
        public float PulseGlow { get; }

        /// <summary>Verdict that flash is reporting.</summary>
        public TrainingTimingGrade PulseGrade { get; }

        /// <summary>How far above 1 the emission is allowed to be driven.</summary>
        public float PulseIntensity { get; }

        /// <summary>How far the verdict colour is pulled towards white.</summary>
        public float PulseWhiteness { get; }

        /// <summary>
        /// The emission the body should show right now. Black when nothing is
        /// pulsing, which is also the value the materials ship with, so a
        /// silent body is left exactly as authored.
        /// </summary>
        public Color EvaluateEmission()
        {
            if (PulseGlow <= 0f || PulseIntensity <= 0f)
            {
                return Color.black;
            }

            var color =
                TrainingTimingPalette.GetGradeColor(
                    PulseGrade,
                    PulseWhiteness) *
                (PulseGlow * PulseIntensity);
            color.a = 1f;
            return color;
        }
    }

    /// <summary>
    /// Applies one surface snapshot to renderers without instantiating or
    /// modifying their shared materials. Existing property blocks are
    /// preserved and can be restored exactly on retry or avatar swaps.
    /// </summary>
    public sealed class MuscleSurfaceStateSynchronizer
    {
        private static readonly int BumpScaleId =
            Shader.PropertyToID("_BumpScale");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int EmissionColorId =
            Shader.PropertyToID("_EmissionColor");

        private readonly List<Target> _targets =
            new List<Target>();
        private readonly HashSet<Renderer> _seenRenderers =
            new HashSet<Renderer>();
        private float _lastNormalScale;
        private float _lastSmoothness;
        private Color _lastEmission = Color.black;
        private bool _hasAppliedValues;

        /// <summary>
        /// Set when a target material has emission switched off, in which case
        /// the pulse colour is written and silently ignored by the shader.
        /// Reported rather than repaired: turning a keyword on belongs to the
        /// material asset, not to a runtime that also has to put it back.
        /// </summary>
        public bool EmissionIsUnavailable { get; private set; }

        public int TargetCount => _targets.Count;
        public bool IsComplete { get; private set; }
        public string ValidationError { get; private set; } =
            string.Empty;

        public int Rebuild(
            IReadOnlyList<Renderer> renderers)
        {
            Clear();
            if (!TryValidateTargets(renderers, out var error))
            {
                ValidationError = error;
                return 0;
            }

            for (var index = 0;
                 index < renderers.Count;
                 index++)
            {
                var renderer = renderers[index];
                if (renderer == null ||
                    !_seenRenderers.Add(renderer))
                {
                    continue;
                }

                var original = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(original);
                _targets.Add(
                    new Target(
                        renderer,
                        original,
                        !original.isEmpty));
            }

            IsComplete = true;
            RefreshEmissionAvailability();
            return TargetCount;
        }

        public int RebuildConfiguredPresentation(
            BlendShapeMuscleGrowthOutput output,
            SkinnedMeshRenderer authoritativeRenderer,
            IReadOnlyList<SkinnedMeshRenderer> renderers)
        {
            Clear();
            var targets = new List<Renderer>();
            if (!TryCollectConfiguredTargets(
                    output,
                    authoritativeRenderer,
                    renderers,
                    targets,
                    out var error) ||
                !TryValidateTargets(targets, out error))
            {
                ValidationError = error;
                return 0;
            }

            for (var index = 0;
                 index < targets.Count;
                 index++)
            {
                var renderer = targets[index];
                if (renderer == null ||
                    !_seenRenderers.Add(renderer))
                {
                    continue;
                }

                var original = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(original);
                _targets.Add(
                    new Target(
                        renderer,
                        original,
                        !original.isEmpty));
            }

            IsComplete = true;
            RefreshEmissionAvailability();
            return TargetCount;
        }

        public int Apply(
            float normalizedGrowth,
            float restingNormalScale,
            float trainedNormalScale,
            float restingSmoothness,
            float trainedSmoothness)
        {
            return Apply(
                new MuscleSurfaceSnapshot(
                    normalizedGrowth,
                    restingNormalScale,
                    trainedNormalScale,
                    restingSmoothness,
                    trainedSmoothness));
        }

        public int Apply(MuscleSurfaceSnapshot snapshot)
        {
            var normalScale =
                MuscleSurfacePresentationMath.EvaluateNormalScale(
                    snapshot.NormalizedGrowth,
                    snapshot.RestingNormalScale,
                    snapshot.TrainedNormalScale);
            var smoothness =
                MuscleSurfacePresentationMath.EvaluateSmoothness(
                    snapshot.NormalizedGrowth,
                    snapshot.RestingSmoothness,
                    snapshot.TrainedSmoothness);
            var emission = snapshot.EvaluateEmission();
            if (_hasAppliedValues &&
                Mathf.Approximately(
                    _lastNormalScale,
                    normalScale) &&
                Mathf.Approximately(
                    _lastSmoothness,
                    smoothness) &&
                _lastEmission == emission)
            {
                return TargetCount;
            }

            var applied = 0;
            foreach (var target in _targets)
            {
                if (target.Renderer == null)
                {
                    continue;
                }

                target.Renderer.GetPropertyBlock(target.Working);
                target.Working.SetFloat(BumpScaleId, normalScale);
                target.Working.SetFloat(SmoothnessId, smoothness);
                target.Working.SetColor(EmissionColorId, emission);
                target.Renderer.SetPropertyBlock(target.Working);
                applied++;
            }

            _lastNormalScale = normalScale;
            _lastSmoothness = smoothness;
            _lastEmission = emission;
            _hasAppliedValues = applied > 0;

            return applied;
        }

        public void ForgetWithoutRestore()
        {
            _targets.Clear();
            _seenRenderers.Clear();
            _hasAppliedValues = false;
            _lastEmission = Color.black;
            EmissionIsUnavailable = false;
            IsComplete = false;
            ValidationError = string.Empty;
        }

        /// <summary>
        /// A material with emission switched off will accept the colour and
        /// draw nothing, which looks like a broken pulse rather than a
        /// misconfigured asset. Record it so the caller can say so once.
        /// </summary>
        private void RefreshEmissionAvailability()
        {
            var unavailable = false;
            foreach (var target in _targets)
            {
                var materials = target.Renderer == null
                    ? null
                    : target.Renderer.sharedMaterials;
                if (materials == null)
                {
                    continue;
                }

                foreach (var material in materials)
                {
                    if (material != null &&
                        material.HasProperty(EmissionColorId) &&
                        !material.IsKeywordEnabled("_EMISSION"))
                    {
                        unavailable = true;
                    }
                }
            }

            EmissionIsUnavailable = unavailable;
        }

        public void Clear()
        {
            foreach (var target in _targets)
            {
                if (target.Renderer == null)
                {
                    continue;
                }

                target.Renderer.SetPropertyBlock(
                    target.HadOriginalBlock
                        ? target.Original
                        : null);
            }

            _targets.Clear();
            _seenRenderers.Clear();
            _hasAppliedValues = false;
            _lastEmission = Color.black;
            EmissionIsUnavailable = false;
            IsComplete = false;
            ValidationError = string.Empty;
        }

        public static bool TryValidateTargets(
            IReadOnlyList<Renderer> renderers,
            out string error)
        {
            if (renderers == null || renderers.Count <= 0)
            {
                error = "No muscle surface renderer is assigned.";
                return false;
            }

            var validCount = 0;
            for (var index = 0;
                 index < renderers.Count;
                 index++)
            {
                var renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                if (!SupportsSurfaceProperties(renderer))
                {
                    error =
                        $"Renderer '{renderer.name}' requires a normal " +
                        "mapped material with Bump Scale and Smoothness.";
                    return false;
                }

                validCount++;
            }

            if (validCount <= 0)
            {
                error = "No valid muscle surface renderer is assigned.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public static bool TryCollectConfiguredTargets(
            BlendShapeMuscleGrowthOutput output,
            SkinnedMeshRenderer authoritativeRenderer,
            IReadOnlyList<SkinnedMeshRenderer> renderers,
            ICollection<Renderer> targets,
            out string error)
        {
            if (output == null || authoritativeRenderer == null)
            {
                error =
                    "Muscle output and authoritative renderer are required.";
                return false;
            }

            if (renderers == null || targets == null)
            {
                error = "Presentation renderers are not assigned.";
                return false;
            }

            var names = new HashSet<string>(
                System.StringComparer.Ordinal);
            var bindings = output.Bindings;
            for (var index = 0;
                 index < bindings.Count;
                 index++)
            {
                var binding = bindings[index];
                if (binding != null &&
                    binding.Renderer == authoritativeRenderer &&
                    !string.IsNullOrWhiteSpace(
                        binding.BlendShapeName))
                {
                    names.Add(binding.BlendShapeName);
                }
            }

            if (names.Count <= 0)
            {
                error =
                    "No muscle BlendShape is configured on the tracked " +
                    "body renderer.";
                return false;
            }

            var seen = new HashSet<Renderer>();
            for (var rendererIndex = 0;
                 rendererIndex < renderers.Count;
                 rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                var mesh = renderer == null
                    ? null
                    : renderer.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                foreach (var name in names)
                {
                    if (mesh.GetBlendShapeIndex(name) < 0)
                    {
                        continue;
                    }

                    if (seen.Add(renderer))
                    {
                        targets.Add(renderer);
                    }

                    break;
                }
            }

            if (targets.Count <= 0)
            {
                error =
                    "The presentation clone has no configured muscle " +
                    "surface renderer.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool SupportsSurfaceProperties(
            Renderer renderer)
        {
            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length <= 0)
            {
                return false;
            }

            var foundSurface = false;
            foreach (var material in materials)
            {
                if (material == null)
                {
                    continue;
                }

                if (!material.HasProperty(BumpScaleId) ||
                    !material.HasProperty(SmoothnessId) ||
                    !material.HasProperty("_BumpMap") ||
                    material.GetTexture("_BumpMap") == null ||
                    !material.IsKeywordEnabled("_NORMALMAP"))
                {
                    return false;
                }

                foundSurface = true;
            }

            return foundSurface;
        }

        private sealed class Target
        {
            public Target(
                Renderer renderer,
                MaterialPropertyBlock original,
                bool hadOriginalBlock)
            {
                Renderer = renderer;
                Original = original;
                HadOriginalBlock = hadOriginalBlock;
                Working = new MaterialPropertyBlock();
            }

            public Renderer Renderer { get; }
            public MaterialPropertyBlock Original { get; }
            public bool HadOriginalBlock { get; }
            public MaterialPropertyBlock Working { get; }
        }
    }
}
