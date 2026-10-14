using System;
using System.Collections.Generic;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Explicit adapter for the production avatar. Each body renderer and
    /// BlendShape name must be assigned; no fuzzy lookup can accidentally
    /// drive facial expression shapes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlendShapeMuscleGrowthOutput :
        MonoBehaviour,
        IMuscleGrowthOutput,
        IMuscleGrowthChannelOutput,
        IMusclePulseOutput
    {
        [SerializeField] private MuscleBlendShapeBinding[] _bindings =
            Array.Empty<MuscleBlendShapeBinding>();
        [SerializeField] private AnimationCurve _responseCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Muscle Surface")]
        [SerializeField] private bool _driveSurface = true;
        [SerializeField, Range(0f, 2f)]
        private float _restingNormalScale = 0.72f;
        [SerializeField, Range(0f, 2f)]
        private float _trainedNormalScale = 1.08f;
        [SerializeField, Range(0f, 1f)]
        private float _restingSmoothness = 0.14f;
        [SerializeField, Range(0f, 1f)]
        private float _trainedSmoothness = 0.25f;

        [Tooltip(
            "How brightly one repetition flashes the body. Set to 0 to turn " +
            "the pulse light off without touching the shape response.")]
        [SerializeField, Range(0f, 8f)]
        private float _pulseEmissionIntensity = 0.7f;

        [Tooltip(
            "How far the verdict colour is pulled towards white. At full " +
            "saturation the flash reads as paint on the skin rather than as " +
            "light coming off it.")]
        [SerializeField, Range(0f, 1f)]
        private float _pulseColorWhiteness = 0.6f;

        private float _pulseGlow;
        private TrainingTimingGrade _pulseGrade =
            TrainingTimingGrade.Perfect;
        private MuscleGrowthChannels _overshoot;
        private bool _hasLoggedMissingEmission;

        // One dispatch sets growth, then channels, then the pulse, and
        // LateUpdate follows: four writes of the whole body for one frame's
        // worth of change. Rather than defer — these are public calls and a
        // caller is entitled to see them take effect — the write itself
        // remembers what it last put on the renderers and declines to repeat
        // it. Outside Training, where nothing changes at all, that is every
        // frame.
        private float _writtenGrowth;
        private MuscleGrowthChannels _writtenChannels;
        private MuscleGrowthChannels _writtenOvershoot;
        private bool _writtenHasChannels;
        private bool _hasWritten;

        private static bool ChannelsMatch(
            in MuscleGrowthChannels a,
            in MuscleGrowthChannels b)
        {
            return
                Mathf.Approximately(a.Overall, b.Overall) &&
                Mathf.Approximately(
                    a.ElbowFlexionLeft,
                    b.ElbowFlexionLeft) &&
                Mathf.Approximately(
                    a.ElbowFlexionRight,
                    b.ElbowFlexionRight) &&
                Mathf.Approximately(
                    a.ArmRaiseLeft,
                    b.ArmRaiseLeft) &&
                Mathf.Approximately(
                    a.ArmRaiseRight,
                    b.ArmRaiseRight) &&
                Mathf.Approximately(a.ChestLeft, b.ChestLeft) &&
                Mathf.Approximately(a.ChestRight, b.ChestRight);
        }

        private int _validBindingCount;
        private bool _hasLoggedMissingBindings;
        private MuscleGrowthChannels _channels;
        private bool _hasChannelSnapshot;
        private MuscleBlendShapeBinding[] _cachedBindingObjects =
            Array.Empty<MuscleBlendShapeBinding>();
        private readonly MuscleSurfaceStateSynchronizer
            _surfaceState =
                new MuscleSurfaceStateSynchronizer();
        private bool _surfaceBuildAttempted;
        private bool _hasLoggedSurfaceFailure;

        public string OutputLabel => "BLENDSHAPE";
        public bool IsAvailable
        {
            get
            {
                if (!isActiveAndEnabled)
                {
                    return false;
                }

                ResolveBindings(false);
                return _validBindingCount > 0;
            }
        }

        public bool IsMock => false;
        public float LastAppliedGrowthNormalized { get; private set; }
        public IReadOnlyList<MuscleBlendShapeBinding> Bindings =>
            _bindings ?? Array.Empty<MuscleBlendShapeBinding>();
        public bool DrivesSurface => _driveSurface;
        public int SurfaceTargetCount => _surfaceState.TargetCount;
        public bool SurfaceReady =>
            !_driveSurface || _surfaceState.IsComplete;
        public MuscleSurfaceSnapshot SurfaceSnapshot =>
            new MuscleSurfaceSnapshot(
                LastAppliedGrowthNormalized,
                _restingNormalScale,
                _trainedNormalScale,
                _restingSmoothness,
                _trainedSmoothness,
                _pulseGlow,
                _pulseGrade,
                _pulseEmissionIntensity,
                _pulseColorWhiteness);

        /// <summary>
        /// One repetition's flash. Written into the same property blocks as
        /// the rest of the surface, which is what keeps the mirror in step:
        /// its renderer is bound here too, so it cannot be left dark while the
        /// tracked body lights up.
        /// </summary>
        public void ApplyPulse(
            float glow,
            TrainingTimingGrade grade,
            MuscleGrowthChannels overshoot)
        {
            _pulseGlow =
                MuscleGrowthMath.SanitizeNormalized(glow);
            _pulseGrade = grade;
            _overshoot = overshoot;
            ResolveBindings(false);
            ApplyResolvedGrowth();
        }

        private void Awake()
        {
            ValidateSettings();
            ResolveBindings(false);
        }

        private void OnEnable()
        {
            ValidateSettings();
            ResolveBindings(true);
            ApplyResolvedGrowth();
        }

        private void OnDisable()
        {
            ResetGrowth();
            _surfaceState.Clear();
            _surfaceBuildAttempted = false;
            _hasLoggedSurfaceFailure = false;
        }

        private void OnValidate()
        {
            ValidateSettings();
            RebindFromBaseline();
            ResolveBindings(false);
        }

        private void LateUpdate()
        {
            ResolveBindings(false);
            if (_validBindingCount <= 0)
            {
                return;
            }

            ApplyResolvedGrowth();
        }

        [ContextMenu("Refresh BlendShape Bindings")]
        public void RefreshBindings()
        {
            RebindFromBaseline();
            ResolveBindings(true);
            ApplyResolvedGrowth();
        }

        public void ApplyGrowth(float normalizedGrowth)
        {
            LastAppliedGrowthNormalized =
                MuscleGrowthMath.SanitizeNormalized(
                    normalizedGrowth);
            ResolveBindings(true);
            ApplyResolvedGrowth();
        }

        public void ResetGrowth()
        {
            ResolveBindings(false);
            LastAppliedGrowthNormalized = 0f;
            // Drop the stale per-group snapshot so every binding returns to
            // its untrained baseline instead of the last scored values.
            _channels = default;
            _hasChannelSnapshot = false;
            _pulseGlow = 0f;
            _overshoot = default;
            ApplyResolvedGrowth();
        }

        private void ApplyResolvedGrowth()
        {
            if (_hasWritten &&
                _writtenHasChannels == _hasChannelSnapshot &&
                Mathf.Approximately(
                    _writtenGrowth,
                    LastAppliedGrowthNormalized) &&
                ChannelsMatch(_channels, _writtenChannels) &&
                ChannelsMatch(_overshoot, _writtenOvershoot))
            {
                // Nothing that shapes the body has changed. The surface still
                // gets a look in, because the flash is not part of this
                // comparison and has its own early-out.
                ApplySurface();
                return;
            }

            _writtenGrowth = LastAppliedGrowthNormalized;
            _writtenChannels = _channels;
            _writtenOvershoot = _overshoot;
            _writtenHasChannels = _hasChannelSnapshot;
            _hasWritten = true;

            foreach (var binding in _bindings)
            {
                if (binding == null)
                {
                    continue;
                }

                // The response curve shapes earned growth only. The overshoot
                // is added afterwards so a curve that eases out near the top
                // cannot flatten the very moment meant to stand out.
                binding.TryApply(
                    EvaluateResponse(
                        _hasChannelSnapshot
                            ? _channels.Get(binding.Channel)
                            : LastAppliedGrowthNormalized),
                    _overshoot.Get(binding.Channel));
            }

            ApplySurface();
        }

        private void ApplySurface()
        {
            if (!_driveSurface)
            {
                _surfaceState.Clear();
                return;
            }

            if (!_surfaceBuildAttempted)
            {
                _surfaceBuildAttempted = true;
                var renderers = new List<Renderer>();
                var seen = new HashSet<Renderer>();
                foreach (var binding in _bindings)
                {
                    var renderer = binding == null
                        ? null
                        : binding.Renderer;
                    if (renderer != null && seen.Add(renderer))
                    {
                        renderers.Add(renderer);
                    }
                }

                if (_surfaceState.Rebuild(renderers) <= 0 &&
                    !_hasLoggedSurfaceFailure)
                {
                    _hasLoggedSurfaceFailure = true;
                    if (Application.isPlaying)
                    {
                        Debug.LogWarning(
                            "[MuscleGrowth] Surface presentation is " +
                            "disabled for this avatar: " +
                            _surfaceState.ValidationError,
                            this);
                    }
                }

                if (_surfaceState.EmissionIsUnavailable &&
                    !_hasLoggedMissingEmission &&
                    _pulseEmissionIntensity > 0f)
                {
                    _hasLoggedMissingEmission = true;
                    if (Application.isPlaying)
                    {
                        Debug.LogWarning(
                            "[MuscleGrowth] A body material has emission " +
                            "switched off, so repetitions will not light " +
                            "the body. Enable Emission on the material " +
                            "and leave its colour black.",
                            this);
                    }
                }
            }

            _surfaceState.Apply(SurfaceSnapshot);
        }

        private float EvaluateResponse(float normalizedGrowth)
        {
            var value =
                MuscleGrowthMath.SanitizeNormalized(
                    normalizedGrowth);
            return MuscleGrowthMath.SanitizeNormalized(
                _responseCurve == null
                    ? value
                    : _responseCurve.Evaluate(value));
        }

        /// <summary>
        /// Receives per-muscle-group growth. Bindings set to a specific group
        /// follow it; bindings left on Overall keep the whole-body signal.
        /// </summary>
        public void ApplyChannelGrowth(
            MuscleGrowthChannels channels)
        {
            _channels = channels;
            _hasChannelSnapshot = true;
            ResolveBindings(false);
            ApplyResolvedGrowth();
        }

        private void ResolveBindings(bool logIfMissing)
        {
            _bindings ??=
                Array.Empty<MuscleBlendShapeBinding>();
            EnsureBindingCacheIsCurrent();
            var validCount = 0;
            foreach (var binding in _bindings)
            {
                if (binding != null &&
                    binding.TryResolve(
                        LastAppliedGrowthNormalized))
                {
                    validCount++;
                }
            }

            _validBindingCount = validCount;
            if (!logIfMissing ||
                validCount > 0 ||
                _bindings.Length <= 0 ||
                _hasLoggedMissingBindings)
            {
                return;
            }

            _hasLoggedMissingBindings = true;
            Debug.LogWarning(
                "[MuscleGrowth] No assigned body BlendShape binding " +
                "could be resolved. Check the renderer and exact name.",
                this);
        }

        private void RebindFromBaseline()
        {
            _bindings ??=
                Array.Empty<MuscleBlendShapeBinding>();

            RestoreAndInvalidate(_cachedBindingObjects);
            foreach (var binding in _bindings)
            {
                if (!ContainsReference(
                        _cachedBindingObjects,
                        binding))
                {
                    binding?.RestoreResolvedBaseline();
                    binding?.Invalidate();
                }
            }

            _cachedBindingObjects =
                SnapshotBindings(_bindings);
            _surfaceState.Clear();
            _surfaceBuildAttempted = false;
            _hasLoggedSurfaceFailure = false;
            _hasLoggedMissingEmission = false;
            _validBindingCount = 0;
            _hasLoggedMissingBindings = false;

            // Different bindings, so whatever was last written no longer says
            // anything about what these renderers are showing.
            _hasWritten = false;
        }

        private void EnsureBindingCacheIsCurrent()
        {
            if (MatchesCachedBindings())
            {
                return;
            }

            RebindFromBaseline();
        }

        private bool MatchesCachedBindings()
        {
            if (_cachedBindingObjects == null ||
                _cachedBindingObjects.Length !=
                _bindings.Length)
            {
                return false;
            }

            for (var index = 0;
                 index < _bindings.Length;
                 index++)
            {
                if (!ReferenceEquals(
                        _cachedBindingObjects[index],
                        _bindings[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static void RestoreAndInvalidate(
            MuscleBlendShapeBinding[] bindings)
        {
            if (bindings == null)
            {
                return;
            }

            foreach (var binding in bindings)
            {
                binding?.RestoreResolvedBaseline();
                binding?.Invalidate();
            }
        }

        private static bool ContainsReference(
            MuscleBlendShapeBinding[] bindings,
            MuscleBlendShapeBinding candidate)
        {
            if (bindings == null ||
                candidate == null)
            {
                return false;
            }

            foreach (var binding in bindings)
            {
                if (ReferenceEquals(binding, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static MuscleBlendShapeBinding[]
            SnapshotBindings(
                MuscleBlendShapeBinding[] bindings)
        {
            if (bindings == null ||
                bindings.Length <= 0)
            {
                return
                    Array.Empty<MuscleBlendShapeBinding>();
            }

            var snapshot =
                new MuscleBlendShapeBinding[
                    bindings.Length];
            Array.Copy(
                bindings,
                snapshot,
                bindings.Length);
            return snapshot;
        }

        private void ValidateSettings()
        {
            _bindings ??=
                Array.Empty<MuscleBlendShapeBinding>();
            _responseCurve ??=
                AnimationCurve.EaseInOut(
                    0f,
                    0f,
                    1f,
                    1f);
            foreach (var binding in _bindings)
            {
                binding?.Validate();
            }

            _restingNormalScale = SanitizeSurfaceScale(
                _restingNormalScale,
                0.72f);
            _trainedNormalScale = SanitizeSurfaceScale(
                _trainedNormalScale,
                1.08f);
            _restingSmoothness = SanitizeSurfaceNormalized(
                _restingSmoothness,
                0.14f);
            _trainedSmoothness = SanitizeSurfaceNormalized(
                _trainedSmoothness,
                0.25f);
            _trainedNormalScale = Mathf.Max(
                _restingNormalScale,
                _trainedNormalScale);
            _trainedSmoothness = Mathf.Max(
                _restingSmoothness,
                _trainedSmoothness);
            _pulseEmissionIntensity =
                float.IsNaN(_pulseEmissionIntensity) ||
                float.IsInfinity(_pulseEmissionIntensity)
                    ? 0.7f
                    : Mathf.Clamp(
                        _pulseEmissionIntensity,
                        0f,
                        8f);
            _pulseColorWhiteness = SanitizeSurfaceNormalized(
                _pulseColorWhiteness,
                0.6f);
        }

        private static float SanitizeSurfaceScale(
            float value,
            float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? fallback
                : Mathf.Clamp(value, 0f, 2f);
        }

        private static float SanitizeSurfaceNormalized(
            float value,
            float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? fallback
                : Mathf.Clamp01(value);
        }
    }

    [Serializable]
    public sealed class MuscleBlendShapeBinding
    {
        private const int UnresolvedIndex = -2;

        [SerializeField] private SkinnedMeshRenderer _renderer;
        [SerializeField] private string _blendShapeName = string.Empty;
        [Tooltip(
            "Which muscle group drives this shape. Overall uses the " +
            "whole-body growth signal.")]
        [SerializeField]
        private MuscleGrowthChannel _channel =
            MuscleGrowthChannel.Overall;
        [SerializeField]
        private bool _useCurrentWeightAsBaseline = true;
        [SerializeField, Range(0f, 100f)]
        private float _configuredBaselineWeight;
        [SerializeField, Range(0f, 100f)]
        private float _targetWeight = 100f;

        public MuscleGrowthChannel Channel => _channel;
        public SkinnedMeshRenderer Renderer => _renderer;
        public string BlendShapeName => _blendShapeName;
        public float FullGrowthTargetWeight =>
            SanitizeWeight(_targetWeight, 100f);

        [NonSerialized] private Mesh _resolvedMesh;
        [NonSerialized]
        private SkinnedMeshRenderer _resolvedRenderer;
        [NonSerialized] private string _resolvedName;
        [NonSerialized] private int _resolvedIndex =
            UnresolvedIndex;
        [NonSerialized] private float _baselineWeight;
        [NonSerialized]
        private System.Collections.Generic.List<
            KnownBaseline> _knownBaselines;
        [NonSerialized]
        private bool _baselineSettingsInitialized;
        [NonSerialized]
        private bool _lastUseCurrentWeightAsBaseline;
        [NonSerialized]
        private float _lastConfiguredBaselineWeight;

        public bool TryResolve(
            float normalizedGrowth = 0f)
        {
            var mesh = _renderer == null
                ? null
                : _renderer.sharedMesh;
            if (mesh == null ||
                string.IsNullOrWhiteSpace(
                    _blendShapeName))
            {
                RestoreResolvedBaseline();
                Invalidate();
                return false;
            }

            if (_resolvedMesh == mesh &&
                _resolvedRenderer == _renderer &&
                _resolvedName == _blendShapeName &&
                _resolvedIndex != UnresolvedIndex)
            {
                return _resolvedIndex >= 0;
            }

            RestoreResolvedBaseline();
            _resolvedMesh = mesh;
            _resolvedRenderer = _renderer;
            _resolvedName = _blendShapeName;
            _resolvedIndex =
                mesh.GetBlendShapeIndex(_blendShapeName);
            if (_resolvedIndex < 0)
            {
                return false;
            }

            if (!_useCurrentWeightAsBaseline)
            {
                _baselineWeight =
                    _configuredBaselineWeight;
                RememberBaseline(
                    _resolvedRenderer,
                    _resolvedMesh,
                    _resolvedIndex,
                    _baselineWeight);
            }
            else if (!TryGetKnownBaseline(
                         _resolvedRenderer,
                         _resolvedMesh,
                         _resolvedIndex,
                         out _baselineWeight))
            {
                _baselineWeight =
                    MuscleGrowthMath.SanitizeNormalized(
                        normalizedGrowth) <= 0f
                        ? SanitizeWeight(
                            _renderer
                                .GetBlendShapeWeight(
                                    _resolvedIndex),
                            _configuredBaselineWeight)
                        : _configuredBaselineWeight;
                RememberBaseline(
                    _resolvedRenderer,
                    _resolvedMesh,
                    _resolvedIndex,
                    _baselineWeight);
            }

            _baselineWeight = SanitizeWeight(
                _baselineWeight,
                0f);
            return true;
        }

        /// <summary>
        /// Largest overshoot honoured, as a multiple of the whole slim-to-
        /// trained travel. Unity extrapolates a BlendShape linearly beyond its
        /// endpoints, which is what lets a milestone read as a pump without a
        /// dedicated shape. A body held there would stop looking like a body,
        /// but the peak lasts under a tenth of a second, and for that long the
        /// eye reads force rather than anatomy.
        /// </summary>
        private const float MaximumOvershoot = 1f;

        /// <summary>
        /// How far below the earned shape a milestone may dip. The move both
        /// anticipates and settles through thinner-than-resting, and a body
        /// that cannot go below its earned shape cannot do either.
        /// </summary>
        private const float MaximumUndershoot = -0.25f;

        public bool TryApply(
            float normalizedGrowth,
            float overshoot = 0f)
        {
            if (!TryResolve(normalizedGrowth))
            {
                return false;
            }

            overshoot =
                float.IsNaN(overshoot) ||
                float.IsInfinity(overshoot)
                    ? 0f
                    : Mathf.Clamp(
                        overshoot,
                        MaximumUndershoot,
                        MaximumOvershoot);

            // Unclamped on purpose: past 1 the shape carries on in the same
            // direction, which is a muscle pumping rather than a new pose.
            var weight = Mathf.LerpUnclamped(
                _baselineWeight,
                SanitizeWeight(_targetWeight, 100f),
                MuscleGrowthMath.SanitizeNormalized(
                    normalizedGrowth) + overshoot);
            _renderer.SetBlendShapeWeight(
                _resolvedIndex,
                float.IsNaN(weight) || float.IsInfinity(weight)
                    ? 0f
                    : weight);
            return true;
        }

        public void Invalidate()
        {
            _resolvedMesh = null;
            _resolvedRenderer = null;
            _resolvedName = null;
            _resolvedIndex = UnresolvedIndex;
            _baselineWeight = 0f;
        }

        public void RestoreResolvedBaseline()
        {
            if (_resolvedRenderer == null ||
                _resolvedMesh == null ||
                _resolvedRenderer.sharedMesh !=
                _resolvedMesh ||
                _resolvedIndex < 0 ||
                _resolvedIndex >=
                _resolvedMesh.blendShapeCount)
            {
                return;
            }

            _resolvedRenderer.SetBlendShapeWeight(
                _resolvedIndex,
                SanitizeWeight(_baselineWeight, 0f));
        }

        public void Validate()
        {
            _configuredBaselineWeight = SanitizeWeight(
                _configuredBaselineWeight,
                0f);
            _targetWeight = SanitizeWeight(
                _targetWeight,
                100f);

            if (_baselineSettingsInitialized &&
                (_lastUseCurrentWeightAsBaseline !=
                    _useCurrentWeightAsBaseline ||
                 !Mathf.Approximately(
                     _lastConfiguredBaselineWeight,
                     _configuredBaselineWeight)))
            {
                _knownBaselines?.Clear();
            }

            _baselineSettingsInitialized = true;
            _lastUseCurrentWeightAsBaseline =
                _useCurrentWeightAsBaseline;
            _lastConfiguredBaselineWeight =
                _configuredBaselineWeight;
        }

        private bool TryGetKnownBaseline(
            SkinnedMeshRenderer renderer,
            Mesh mesh,
            int index,
            out float baseline)
        {
            if (_knownBaselines != null)
            {
                foreach (var known in _knownBaselines)
                {
                    if (known.Matches(
                            renderer,
                            mesh,
                            index))
                    {
                        baseline = known.Baseline;
                        return true;
                    }
                }
            }

            baseline = 0f;
            return false;
        }

        private void RememberBaseline(
            SkinnedMeshRenderer renderer,
            Mesh mesh,
            int index,
            float baseline)
        {
            _knownBaselines ??=
                new System.Collections.Generic.List<
                    KnownBaseline>();
            for (var knownIndex = 0;
                 knownIndex < _knownBaselines.Count;
                 knownIndex++)
            {
                if (!_knownBaselines[knownIndex].Matches(
                        renderer,
                        mesh,
                        index))
                {
                    continue;
                }

                _knownBaselines[knownIndex] =
                    new KnownBaseline(
                        renderer,
                        mesh,
                        index,
                        baseline);
                return;
            }

            _knownBaselines.Add(
                new KnownBaseline(
                    renderer,
                    mesh,
                    index,
                    baseline));
        }

        private static float SanitizeWeight(
            float value,
            float fallback)
        {
            if (float.IsNaN(fallback) ||
                float.IsInfinity(fallback))
            {
                fallback = 0f;
            }

            return float.IsNaN(value) ||
                   float.IsInfinity(value)
                ? Mathf.Clamp(fallback, 0f, 100f)
                : Mathf.Clamp(value, 0f, 100f);
        }

        private readonly struct KnownBaseline
        {
            public KnownBaseline(
                SkinnedMeshRenderer renderer,
                Mesh mesh,
                int index,
                float baseline)
            {
                Renderer = renderer;
                Mesh = mesh;
                Index = index;
                Baseline = SanitizeWeight(
                    baseline,
                    0f);
            }

            private SkinnedMeshRenderer Renderer
            {
                get;
            }

            private Mesh Mesh { get; }
            private int Index { get; }
            public float Baseline { get; }

            public bool Matches(
                SkinnedMeshRenderer renderer,
                Mesh mesh,
                int index)
            {
                return Renderer == renderer &&
                       Mesh == mesh &&
                       Index == index;
            }
        }
    }
}
