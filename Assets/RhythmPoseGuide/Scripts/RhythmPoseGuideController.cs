using System;
using System.Collections.Generic;
using UnityEngine;

namespace IVRC
{
    [Serializable]
    public sealed class GhostAvatarVariantBinding
    {
        [SerializeField] private AvatarDefinition _sourceAvatar;

        [Header("Legacy Mirror Guide References")]
        [Tooltip(
            "Kept so scenes authored with the original one-guide version " +
            "retain their serialized references.")]
        [SerializeField] private Transform _mirrorTransform;
        [SerializeField] private GameObject _ghostRoot;
        [SerializeField] private Transform _skeletonRoot;
        [SerializeField] private BlendShapeMuscleGrowthOutput
            _muscleConfigSource;

        [Header("Synchronized Guide Destinations")]
        [SerializeField] private GhostGuideInstance _playerGuide =
            new GhostGuideInstance();
        [SerializeField] private GhostGuideInstance _mirrorGuide =
            new GhostGuideInstance();

        [NonSerialized] private bool _isInitialized;
        [NonSerialized] private MuscleBlendShapeStateSynchronizer
            _playerMuscleState;
        [NonSerialized] private MuscleBlendShapeStateSynchronizer
            _mirrorMuscleState;

        public AvatarDefinition SourceAvatar => _sourceAvatar;
        public Transform MirrorTransform => _mirrorTransform;
        public Transform GhostTransform =>
            _ghostRoot == null ? null : _ghostRoot.transform;
        public Transform SkeletonRoot => _skeletonRoot;
        public BlendShapeMuscleGrowthOutput MuscleConfigSource =>
            _muscleConfigSource;
        public GhostGuideInstance PlayerGuide => _playerGuide;
        public GhostGuideInstance MirrorGuide => _mirrorGuide;
        public GhostAvatarMaterialController MaterialController =>
            _mirrorGuide?.MaterialController;
        public GhostAvatarPoseApplier PoseApplier =>
            _mirrorGuide?.PoseApplier;
        public bool HasConfiguredGuide =>
            (_playerGuide != null &&
             _playerGuide.IsConfigured) ||
            (_mirrorGuide != null &&
             _mirrorGuide.IsConfigured);
        public bool IsUsable =>
            (_playerGuide != null &&
             _playerGuide.IsUsable) ||
            (_mirrorGuide != null &&
             _mirrorGuide.IsUsable);

        /// <summary>
        /// Compatibility entry point for tests and scenes created by the
        /// original single-mirror implementation.
        /// </summary>
        public void SetRuntimeReferences(
            AvatarDefinition sourceAvatar,
            Transform mirrorTransform,
            GameObject ghostRoot,
            Transform skeletonRoot,
            BlendShapeMuscleGrowthOutput muscleConfigSource)
        {
            _sourceAvatar = sourceAvatar;
            _mirrorTransform = mirrorTransform;
            _ghostRoot = ghostRoot;
            _skeletonRoot = skeletonRoot;
            _muscleConfigSource = muscleConfigSource;
            _mirrorGuide ??= new GhostGuideInstance();
            _mirrorGuide.SetRuntimeReferences(
                "MirrorGhostGuide",
                ghostRoot,
                ghostRoot,
                skeletonRoot,
                mirrorTransform,
                mirrorTransform);
            _isInitialized = false;
        }

        public void SetRuntimeReferences(
            AvatarDefinition sourceAvatar,
            GhostGuideInstance playerGuide,
            GhostGuideInstance mirrorGuide,
            BlendShapeMuscleGrowthOutput muscleConfigSource)
        {
            _sourceAvatar = sourceAvatar;
            _playerGuide =
                playerGuide ?? new GhostGuideInstance();
            _mirrorGuide =
                mirrorGuide ?? new GhostGuideInstance();
            _muscleConfigSource = muscleConfigSource;
            _isInitialized = false;
        }

        public void Initialize(
            Material ghostMaterial,
            UnityEngine.Object logContext)
        {
            if (_isInitialized)
            {
                return;
            }

            _playerGuide ??= new GhostGuideInstance();
            _mirrorGuide ??= new GhostGuideInstance();
            MigrateLegacyMirrorGuideIfNeeded();
            ResolveAvatarRoots();
            _playerGuide.Initialize(
                ghostMaterial,
                logContext);
            _mirrorGuide.Initialize(
                ghostMaterial,
                logContext);
            _isInitialized = true;
        }

        public void Initialize(Material ghostMaterial)
        {
            Initialize(ghostMaterial, null);
        }

        public void RefreshBaseScales()
        {
            _playerGuide?.RefreshBaseScale();
            _mirrorGuide?.RefreshBaseScale();
        }

        public int ApplyPose(PoseAsset pose)
        {
            var appliedCount = 0;
            if (_playerGuide != null)
            {
                appliedCount +=
                    _playerGuide.ApplyPose(pose);
            }

            if (_mirrorGuide != null)
            {
                appliedCount +=
                    _mirrorGuide.ApplyPose(pose);
            }

            return appliedCount;
        }

        public void ApplyState(
            RhythmPoseGuideVisualState state,
            GhostGuideVisualStyle playerStyle,
            GhostGuideVisualStyle mirrorStyle)
        {
            SynchronizeMuscleState();
            _playerGuide?.ApplyState(
                state,
                playerStyle);
            _mirrorGuide?.ApplyState(
                state,
                mirrorStyle);
        }

        public void SetVisible(bool visible)
        {
            _playerGuide?.SetVisible(visible);
            _mirrorGuide?.SetVisible(visible);
        }

        public void ApplyFullMuscleState()
        {
            if (!_isInitialized)
            {
                return;
            }

            var output = _muscleConfigSource != null
                ? _muscleConfigSource
                : _sourceAvatar == null
                    ? null
                    : _sourceAvatar.GrowthOutput;
            var authoritativeRenderer =
                ResolveAuthoritativeRenderer(output);
            if (output == null ||
                authoritativeRenderer == null)
            {
                _playerMuscleState?.Clear();
                _mirrorMuscleState?.Clear();
                return;
            }

            _playerMuscleState ??=
                new MuscleBlendShapeStateSynchronizer();
            _mirrorMuscleState ??=
                new MuscleBlendShapeStateSynchronizer();
            _playerGuide?.RebuildMuscleState(
                output,
                authoritativeRenderer,
                _playerMuscleState);
            _mirrorGuide?.RebuildMuscleState(
                output,
                authoritativeRenderer,
                _mirrorMuscleState);
            SynchronizeMuscleState();
        }

        private void SynchronizeMuscleState()
        {
            _playerMuscleState?.Apply();
            _mirrorMuscleState?.Apply();
        }

        private SkinnedMeshRenderer ResolveAuthoritativeRenderer(
            BlendShapeMuscleGrowthOutput output)
        {
            if (_sourceAvatar != null &&
                _sourceAvatar.TrackedBodyRenderer != null)
            {
                return _sourceAvatar.TrackedBodyRenderer;
            }

            var bindings = output == null
                ? null
                : output.Bindings;
            if (bindings == null)
            {
                return null;
            }

            for (var index = 0;
                 index < bindings.Count;
                 index++)
            {
                var renderer = bindings[index] == null
                    ? null
                    : bindings[index].Renderer;
                if (renderer == null ||
                    (_mirrorTransform != null &&
                     renderer.transform.IsChildOf(
                         _mirrorTransform)))
                {
                    continue;
                }

                return renderer;
            }

            return null;
        }

        private void MigrateLegacyMirrorGuideIfNeeded()
        {
            if (_mirrorGuide.IsConfigured ||
                _ghostRoot == null ||
                _skeletonRoot == null)
            {
                return;
            }

            _mirrorGuide.SetRuntimeReferences(
                "MirrorGhostGuide",
                _ghostRoot,
                _ghostRoot,
                _skeletonRoot,
                _mirrorTransform,
                _mirrorTransform);
        }

        private void ResolveAvatarRoots()
        {
            if (_sourceAvatar == null)
            {
                return;
            }

            var playerRoot = _sourceAvatar.transform;
            var playerMotionRoot =
                GetRetargetedMotionRoot(
                    _sourceAvatar);
            ResolveGuideRoots(
                _playerGuide,
                playerRoot,
                _sourceAvatar.Animator,
                playerMotionRoot);

            var mirrorRoot =
                _sourceAvatar.MirrorInstance == null
                    ? null
                    : _sourceAvatar.MirrorInstance
                        .transform;
            var mirrorAnimator =
                mirrorRoot == null
                    ? null
                    : mirrorRoot
                        .GetComponentInChildren<
                            Animator>(true);
            var mirrorMotionRoot =
                FindEquivalentMirrorRoot(
                    playerRoot,
                    playerMotionRoot,
                    mirrorRoot);
            ResolveGuideRoots(
                _mirrorGuide,
                mirrorRoot,
                mirrorAnimator,
                mirrorMotionRoot);
        }

        private static void ResolveGuideRoots(
            GhostGuideInstance guide,
            Transform avatarRoot,
            Animator animator,
            Transform motionRoot)
        {
            if (guide == null || avatarRoot == null)
            {
                return;
            }

            var replaceTarget =
                ShouldReplaceWithAvatarRoot(
                    guide,
                    animator);
            var targetRoot = replaceTarget
                ? avatarRoot
                : guide.TargetRoot;
            var scaleSource =
                replaceTarget ||
                guide.ScaleSource == null
                    ? avatarRoot
                    : guide.ScaleSource;
            var resolvedMotionRoot =
                guide.MotionRoot != null
                    ? guide.MotionRoot
                    : motionRoot;

            if (replaceTarget ||
                guide.ScaleSource == null ||
                guide.MotionRoot == null)
            {
                guide.SetFollowRoot(
                    targetRoot,
                    scaleSource,
                    resolvedMotionRoot);
            }
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
                mirrorAvatarRoot == null)
            {
                return null;
            }

            var relativePath =
                GetRelativePath(
                    playerAvatarRoot,
                    playerMotionRoot);
            if (relativePath == null)
            {
                return null;
            }

            return relativePath.Length == 0
                ? mirrorAvatarRoot
                : mirrorAvatarRoot.Find(relativePath);
        }

        private static string GetRelativePath(
            Transform root,
            Transform descendant)
        {
            if (root == null || descendant == null)
            {
                return null;
            }

            if (root == descendant)
            {
                return string.Empty;
            }

            var names = new Stack<string>();
            var current = descendant;
            while (current != null &&
                   current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return current == root
                ? string.Join("/", names)
                : null;
        }

        private static bool ShouldReplaceWithAvatarRoot(
            GhostGuideInstance guide,
            Animator animator)
        {
            if (guide.TargetRoot == null)
            {
                return true;
            }

            var legacyHips = GetHumanoidHips(animator);
            return legacyHips != null &&
                   guide.TargetRoot == legacyHips;
        }

        private static Transform GetHumanoidHips(
            Animator animator)
        {
            return animator != null &&
                   animator.isHuman
                ? animator.GetBoneTransform(
                    HumanBodyBones.Hips)
                : null;
        }
    }

    /// <summary>
    /// Displays synchronized Player and Mirror ghost models at the next target
    /// pose. Timing, pose selection, scale, and beat flash are calculated once
    /// from RhythmClock DSP time and distributed to both destinations.
    /// </summary>
    [DefaultExecutionOrder(20100)]
    [DisallowMultipleComponent]
    public sealed class RhythmPoseGuideController : MonoBehaviour
    {
        [Header("Existing Rhythm Systems")]
        [SerializeField] private RhythmClock _rhythmClock;
        [SerializeField] private TrainingRhythmChart _rhythmChart;
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private TrainingRhythmLaneHud _ringGuide;

        [Header("Avatar Selection")]
        [SerializeField] private AvatarRegistry _avatarRegistry;
        [SerializeField] private GhostAvatarVariantBinding[] _variants =
            Array.Empty<GhostAvatarVariantBinding>();

        [Header("Fallback Avatar References")]
        [Tooltip(
            "Used when no AvatarRegistry variant mapping is configured.")]
        [SerializeField] private Transform _mirrorTransform;
        [SerializeField] private Transform _ghostTransform;
        [SerializeField] private Transform _skeletonRoot;
        [SerializeField] private BlendShapeMuscleGrowthOutput
            _muscleConfigSource;

        [Header("Pose Assets")]
        [SerializeField] private PoseAsset _armCurlDown;
        [SerializeField] private PoseAsset _armCurlUp;
        [SerializeField] private PoseAsset _sideRaiseDown;
        [SerializeField] private PoseAsset _sideRaiseUp;

        [Header("Display")]
        [SerializeField] private RhythmGuideDisplayMode _displayMode =
            RhythmGuideDisplayMode.GhostAvatar;
        [SerializeField] private Material _ghostMaterial;
        [SerializeField] private Color _ghostColor =
            new Color(0.72f, 0.92f, 1f, 1f);
        [SerializeField, Range(0f, 1f)]
        private float _normalAlpha = 0.35f;
        [SerializeField, Min(0f)]
        private float _emissionIntensity = 1f;
        [SerializeField, Range(0f, 1f)]
        private float _beatAlpha = 0.72f;
        [SerializeField, Min(1f)]
        private float _beatIntensity = 1.65f;

        [Header("Per Destination Visual Overrides")]
        [SerializeField] private bool _useSeparateVisualSettings;
        [SerializeField] private Color _playerGhostColor =
            new Color(0.72f, 0.92f, 1f, 1f);
        [SerializeField, Range(0f, 1f)]
        private float _playerNormalAlpha = 0.35f;
        [SerializeField, Min(0f)]
        private float _playerEmissionIntensity = 1f;
        [SerializeField, Range(0f, 1f)]
        private float _playerBeatAlpha = 0.72f;
        [SerializeField, Min(1f)]
        private float _playerBeatIntensity = 1.65f;
        [SerializeField] private Color _mirrorGhostColor =
            new Color(0.72f, 0.92f, 1f, 1f);
        [SerializeField, Range(0f, 1f)]
        private float _mirrorNormalAlpha = 0.35f;
        [SerializeField, Min(0f)]
        private float _mirrorEmissionIntensity = 1f;
        [SerializeField, Range(0f, 1f)]
        private float _mirrorBeatAlpha = 0.72f;
        [SerializeField, Min(1f)]
        private float _mirrorBeatIntensity = 1.65f;

        [Header("Line Art")]
        [Tooltip(
            "Used instead of Ghost Material while the Line Art display mode " +
            "is enabled. Assign the Ghost Avatar Line Art shader.")]
        [SerializeField] private Material _lineArtMaterial;
        [Tooltip(
            "How far outside the goal outline the approaching contour starts, " +
            "in metres. Line art holds the pose on the body and pulls this " +
            "distance in evenly, rather than scaling the whole guide, which " +
            "would lift the head far more than it widens the body.")]
        [SerializeField, Min(0f)]
        private float _lineArtStartContourOffset = 0.12f;
        [SerializeField, Min(0f)]
        private float _lineArtBeatContourOffset;

        [Header("Timing")]
        [Tooltip(
            "During the chart's unscored lead-in and block-transition " +
            "beats, demonstrate DOWN -> UP -> DOWN once before scoring.")]
        [SerializeField] private bool _showPreparationPreview = true;
        [SerializeField, Min(0f)]
        private float _leadTimeSeconds = 1f;
        [SerializeField, Min(0f)]
        private float _startScaleMultiplier = 1.2f;
        [SerializeField, Min(0f)]
        private float _beatScaleMultiplier = 1f;
        [SerializeField, Min(0f)]
        private float _postBeatVisibleSeconds = 0.15f;
        [SerializeField] private AnimationCurve _scaleCurve =
            AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("Adaptive Guidance")]
        [Tooltip(
            "Temporary visibility boost used by the training HUD when a " +
            "participant needs to reacquire the line-art example.")]
        [SerializeField, Range(1f, 3f)]
        private float _adaptiveHintAlphaMultiplier = 1.8f;
        [SerializeField, Range(1f, 3f)]
        private float _adaptiveHintIntensityMultiplier = 1.25f;

        [Header("Diagnostics")]
        [SerializeField] private bool _debugLogging;

        private readonly List<RhythmPoseGuideTarget> _targets =
            new List<RhythmPoseGuideTarget>(80);
        private GhostAvatarVariantBinding _activeVariant;
        private GhostAvatarVariantBinding _fallbackVariant;
        private int _targetIndex;
        private int _preparedTargetIndex = -1;
        private int _lastBeatEventIndex = -1;
        private PoseAsset _preparedPose;
        private BodyRepKind _lastMuscleExercise;
        private bool _hasLastMuscleExercise;
        private bool _warnedMissingRhythm;
        private bool _warnedMissingAvatar;
        private bool _warnedMissingMaterial;
        private bool _warnedMissingPose;
        private bool _warnedEmptyPose;
        private bool _adaptiveHintActive;
        private bool _loopingExerciseActive;
        private BodyRepKind _loopingExercise;
        private LoopingRhythmPoseSample _loopingSample;

        public RhythmGuideDisplayMode DisplayMode =>
            _displayMode;
        public int ScheduledTargetCount => _targets.Count;
        public bool IsAdaptiveHintActive => _adaptiveHintActive;

        private bool GhostDisplayEnabled =>
            (_displayMode &
             (RhythmGuideDisplayMode.GhostAvatar |
              RhythmGuideDisplayMode.LineArt)) != 0;

        private bool LineArtDisplayEnabled =>
            (_displayMode &
             RhythmGuideDisplayMode.LineArt) != 0;

        /// <summary>
        /// Line art has its own multi-pass contour shader, so it swaps the
        /// material rather than only changing colours.
        /// </summary>
        private Material ActiveGuideMaterial =>
            LineArtDisplayEnabled && _lineArtMaterial != null
                ? _lineArtMaterial
                : _ghostMaterial;

        private void Awake()
        {
            ResolveReferences();
            ValidateSettings();
            InitializeVariants();
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            ApplyDisplayMode();
            SelectAvatar(
                _avatarRegistry == null
                    ? null
                    : _avatarRegistry.Active);
            if (_rhythmChart != null &&
                _rhythmChart.HasSession)
            {
                RebuildTargets();
            }
            else
            {
                HideGhost();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            _adaptiveHintActive = false;
            HideAllVariants();
        }

        /// <summary>
        /// Holds one pose on the ghost with no timing behaviour at all: no
        /// approach contour, no beat flash, no chart.
        ///
        /// The introduction teaches how far to move, and the beat teaches
        /// when. Splitting them is the point — a contracting outline is a
        /// timing cue, and a first-timer meeting both at once has two new
        /// things to read instead of one. Training's guide is untouched.
        ///
        /// Pass null to hand the ghost back to the normal path.
        /// </summary>
        public void ShowStaticPose(PoseAsset pose)
        {
            _loopingExerciseActive = false;
            _staticPose = pose;
            if (pose == null)
            {
                HideGhost();
                return;
            }

            ApplyStaticPose();
        }

        public PoseAsset StaticPose => _staticPose;

        /// <summary>
        /// Drives the same two ghost avatars, pose assets and visual timing
        /// used by Training from a caller-supplied music beat position.
        /// This method does not advance time; the tutorial passes the current
        /// AudioSource playhead every frame.
        /// </summary>
        public bool ShowLoopingExercise(
            BodyRepKind exercise,
            float beatPosition,
            float secondsPerBeat)
        {
            if (!TryGetExerciseTiming(
                    exercise,
                    out var beatsPerRep,
                    out var upToDownBeats) ||
                !LoopingRhythmPoseSchedule.TrySample(
                    beatPosition,
                    secondsPerBeat,
                    beatsPerRep,
                    upToDownBeats,
                    _postBeatVisibleSeconds,
                    out _loopingSample))
            {
                ReleaseLoopingExercise();
                return false;
            }

            _staticPose = null;
            _loopingExercise = exercise;
            _loopingExerciseActive = true;
            return true;
        }

        public void ReleaseLoopingExercise()
        {
            if (!_loopingExerciseActive)
            {
                return;
            }

            _loopingExerciseActive = false;
            if (_staticPose == null &&
                (_gameFlow == null ||
                 _gameFlow.CurrentPhase != ExperiencePhase.Training))
            {
                HideGhost();
            }
        }

        private void ApplyStaticPose()
        {
            if (_staticPose == null ||
                _activeVariant == null ||
                !_activeVariant.IsUsable ||
                ActiveGuideMaterial == null)
            {
                return;
            }

            _activeVariant.ApplyState(
                new RhythmPoseGuideVisualState(
                    _staticPose,
                    1f,
                    // Settled: the guide has arrived and stays there.
                    1f,
                    true,
                    false,
                    // No outward offset, so there is no contour to contract
                    // and nothing implying a moment to hit.
                    0f),
                GetPlayerVisualStyle(),
                GetMirrorVisualStyle());
        }

        [NonSerialized] private PoseAsset _staticPose;

        private void LateUpdate()
        {
            if (_loopingExerciseActive)
            {
                ApplyLoopingExercise();
                return;
            }

            // A held pose outranks the scheduled guide. It is only ever set
            // outside Training, where the scheduled path would hide the ghost
            // anyway, so the two cannot both be driving it.
            if (_staticPose != null)
            {
                ApplyStaticPose();
                return;
            }

            if (!GhostDisplayEnabled)
            {
                HideGhost();
                return;
            }

            if (!CanPresentGuide())
            {
                HideGhost();
                return;
            }

            if (_targets.Count <= 0)
            {
                RebuildTargets();
                if (_targets.Count <= 0)
                {
                    HideGhost();
                    return;
                }
            }

            var currentDspTime = AudioSettings.dspTime;
            while (_targetIndex < _targets.Count)
            {
                var expiredTargetTime =
                    _rhythmClock.GetBeatDspTime(
                        _targets[_targetIndex].BeatIndex);
                if (double.IsNaN(expiredTargetTime) ||
                    currentDspTime <=
                    expiredTargetTime +
                    _postBeatVisibleSeconds)
                {
                    break;
                }

                _targetIndex++;
                _preparedTargetIndex = -1;
                _preparedPose = null;
            }

            if (_targetIndex >= _targets.Count)
            {
                HideGhost();
                return;
            }

            var target = _targets[_targetIndex];
            var targetDspTime =
                _rhythmClock.GetBeatDspTime(
                    target.BeatIndex);
            if (double.IsNaN(targetDspTime))
            {
                HideGhost();
                return;
            }

            var intervalSeconds =
                RhythmPoseGuideSchedule
                    .GetTargetIntervalSeconds(
                        _targets,
                        _targetIndex,
                        _rhythmClock.SecondsPerBeat);
            var secondsUntilBeat =
                (float)(targetDspTime - currentDspTime);
            var effectiveLeadTime =
                RhythmPoseGuideMath.GetEffectiveLeadTime(
                    _leadTimeSeconds,
                    intervalSeconds);
            if (secondsUntilBeat > effectiveLeadTime)
            {
                HideGhost();
                return;
            }

            PrepareTarget(target);

            var isBeatWindow =
                secondsUntilBeat <= 0f ||
                _lastBeatEventIndex == target.BeatIndex;
            ApplyTimedPose(
                _preparedPose,
                secondsUntilBeat,
                intervalSeconds,
                isBeatWindow);
        }

        private void ApplyLoopingExercise()
        {
            if (!GhostDisplayEnabled ||
                _gameFlow == null ||
                _gameFlow.CurrentPhase != ExperiencePhase.Tutorial ||
                _activeVariant == null ||
                !_activeVariant.IsUsable ||
                ActiveGuideMaterial == null)
            {
                HideGhost();
                return;
            }

            if (!_hasLastMuscleExercise ||
                _lastMuscleExercise != _loopingExercise)
            {
                _activeVariant.ApplyFullMuscleState();
                _lastMuscleExercise = _loopingExercise;
                _hasLastMuscleExercise = true;
            }

            var pose = GetPose(
                _loopingExercise,
                _loopingSample.TimingPoint);
            if (pose == null)
            {
                HideGhost();
                return;
            }

            var effectiveLeadTime =
                RhythmPoseGuideMath.GetEffectiveLeadTime(
                    _leadTimeSeconds,
                    _loopingSample.TargetIntervalSeconds);
            if (!_loopingSample.IsBeatWindow &&
                _loopingSample.SecondsUntilBeat > effectiveLeadTime)
            {
                HideGhost();
                return;
            }

            ApplyTimedPose(
                pose,
                _loopingSample.SecondsUntilBeat,
                _loopingSample.TargetIntervalSeconds,
                _loopingSample.IsBeatWindow);
        }

        private void ApplyTimedPose(
            PoseAsset pose,
            float secondsUntilBeat,
            float intervalSeconds,
            bool isBeatWindow)
        {
            var effectiveLeadTime =
                RhythmPoseGuideMath.GetEffectiveLeadTime(
                    _leadTimeSeconds,
                    intervalSeconds);
            var lineArt = LineArtDisplayEnabled;
            var multiplier = lineArt
                ? 1f
                : isBeatWindow
                    ? _beatScaleMultiplier
                    : RhythmPoseGuideMath
                        .EvaluateScaleMultiplier(
                            secondsUntilBeat,
                            effectiveLeadTime,
                            _startScaleMultiplier,
                            _beatScaleMultiplier,
                            _scaleCurve);
            var contourOffset = !lineArt
                ? 0f
                : isBeatWindow
                    ? _lineArtBeatContourOffset
                    : RhythmPoseGuideMath
                        .EvaluateContourOffset(
                            secondsUntilBeat,
                            effectiveLeadTime,
                            _lineArtStartContourOffset,
                            _lineArtBeatContourOffset,
                            _scaleCurve);
            var beatProgress =
                effectiveLeadTime <= Mathf.Epsilon
                    ? 1f
                    : 1f - Mathf.Clamp01(
                        secondsUntilBeat /
                        effectiveLeadTime);
            _activeVariant.ApplyState(
                new RhythmPoseGuideVisualState(
                    pose,
                    multiplier,
                    beatProgress,
                    true,
                    isBeatWindow,
                    contourOffset),
                GetPlayerVisualStyle(),
                GetMirrorVisualStyle());
        }

        private bool TryGetExerciseTiming(
            BodyRepKind exercise,
            out int beatsPerRep,
            out int upToDownBeats)
        {
            beatsPerRep = 0;
            upToDownBeats = 0;
            var blocks = _rhythmChart == null ||
                         _rhythmChart.Pattern == null
                ? null
                : _rhythmChart.Pattern.Blocks;
            if (blocks == null)
            {
                return false;
            }

            foreach (var block in blocks)
            {
                if (block == null || block.Kind != exercise)
                {
                    continue;
                }

                beatsPerRep = block.BeatsPerRep;
                upToDownBeats = block.UpToDownBeats;
                return true;
            }

            return false;
        }

        public void SetDisplayMode(
            RhythmGuideDisplayMode displayMode)
        {
            _displayMode = displayMode;
            ApplyDisplayMode();
        }

        public void SetAdaptiveHintActive(bool active)
        {
            _adaptiveHintActive = active;
        }

        private void PrepareTarget(
            RhythmPoseGuideTarget target)
        {
            if (_activeVariant == null)
            {
                return;
            }

            if (!_hasLastMuscleExercise ||
                _lastMuscleExercise != target.Kind)
            {
                _activeVariant.ApplyFullMuscleState();
                _lastMuscleExercise = target.Kind;
                _hasLastMuscleExercise = true;
            }

            if (_preparedTargetIndex == _targetIndex)
            {
                return;
            }

            _preparedPose = GetPose(
                target.Kind,
                target.TimingPoint);
            if (_preparedPose == null)
            {
                WarnOnce(
                    ref _warnedMissingPose,
                    "[RhythmPoseGuide] A required PoseAsset is " +
                    "not assigned. Open Tools > IVRC > Rhythm " +
                    "Pose Guide > Pose Capture to create it.");
            }
            else if (_preparedPose.Bones.Count == 0)
            {
                WarnOnce(
                    ref _warnedEmptyPose,
                    "[RhythmPoseGuide] The assigned PoseAssets " +
                    "are still empty. Capture ArmCurl Down/Up " +
                    "and SideRaise Down/Up before the final " +
                    "runtime check.");
            }
            else
            {
                var appliedCount =
                    _activeVariant.ApplyPose(
                        _preparedPose);
                if (appliedCount <= 0 &&
                    _debugLogging)
                {
                    Debug.LogWarning(
                        $"[RhythmPoseGuide] Pose " +
                        $"'{_preparedPose.PoseName}' " +
                        "contains no applicable bones yet.",
                        this);
                }
            }

            _preparedTargetIndex = _targetIndex;
            if (_debugLogging)
            {
                Debug.Log(
                    $"[RhythmPoseGuide] Prepared " +
                    $"{target.Kind}/{target.TimingPoint} for " +
                    $"beat {target.BeatIndex}.",
                    this);
            }
        }

        private PoseAsset GetPose(
            BodyRepKind kind,
            RepTimingPoint timingPoint)
        {
            var avatarPose = _activeVariant?.SourceAvatar
                ?.GetExercisePose(kind, timingPoint);
            if (avatarPose != null)
            {
                return avatarPose;
            }

            if (kind == BodyRepKind.ElbowFlexion)
            {
                return timingPoint == RepTimingPoint.Down
                    ? _armCurlDown
                    : _armCurlUp;
            }

            return timingPoint == RepTimingPoint.Down
                ? _sideRaiseDown
                : _sideRaiseUp;
        }

        private GhostGuideVisualStyle
            GetPlayerVisualStyle()
        {
            var style = _useSeparateVisualSettings
                ? new GhostGuideVisualStyle(
                    _playerGhostColor,
                    _playerNormalAlpha,
                    _playerBeatAlpha,
                    _playerEmissionIntensity,
                    _playerBeatIntensity)
                : GetCommonVisualStyle();
            return ApplyAdaptiveHintEmphasis(style);
        }

        private GhostGuideVisualStyle
            GetMirrorVisualStyle()
        {
            var style = _useSeparateVisualSettings
                ? new GhostGuideVisualStyle(
                    _mirrorGhostColor,
                    _mirrorNormalAlpha,
                    _mirrorBeatAlpha,
                    _mirrorEmissionIntensity,
                    _mirrorBeatIntensity)
                : GetCommonVisualStyle();
            return ApplyAdaptiveHintEmphasis(style);
        }

        private GhostGuideVisualStyle ApplyAdaptiveHintEmphasis(
            GhostGuideVisualStyle style)
        {
            if (!_adaptiveHintActive)
            {
                return style;
            }

            return new GhostGuideVisualStyle(
                style.BaseColor,
                Mathf.Clamp01(
                    style.NormalAlpha *
                    _adaptiveHintAlphaMultiplier),
                Mathf.Clamp01(
                    style.BeatAlpha *
                    _adaptiveHintAlphaMultiplier),
                style.NormalIntensity *
                    _adaptiveHintIntensityMultiplier,
                style.BeatIntensity *
                    _adaptiveHintIntensityMultiplier);
        }

        private GhostGuideVisualStyle
            GetCommonVisualStyle()
        {
            return new GhostGuideVisualStyle(
                _ghostColor,
                _normalAlpha,
                _beatAlpha,
                _emissionIntensity,
                _beatIntensity);
        }

        private void HideGhost()
        {
            _activeVariant?.SetVisible(false);
        }

        private void HideAllVariants()
        {
            if (_variants != null)
            {
                foreach (var variant in _variants)
                {
                    variant?.SetVisible(false);
                }
            }

            _fallbackVariant?.SetVisible(false);
        }

        private bool CanPresentGuide()
        {
            if (_rhythmClock == null ||
                _rhythmChart == null ||
                _gameFlow == null)
            {
                WarnOnce(
                    ref _warnedMissingRhythm,
                    "[RhythmPoseGuide] RhythmClock, " +
                    "TrainingRhythmChart, and GameFlowController " +
                    "references are required.");
                return false;
            }

            if (_activeVariant == null ||
                !_activeVariant.IsUsable)
            {
                WarnOnce(
                    ref _warnedMissingAvatar,
                    "[RhythmPoseGuide] No usable ghost avatar " +
                    "variant is configured.");
                return false;
            }

            if (ActiveGuideMaterial == null)
            {
                WarnOnce(
                    ref _warnedMissingMaterial,
                    LineArtDisplayEnabled
                        ? "[RhythmPoseGuide] Line Art Material is missing."
                        : "[RhythmPoseGuide] Ghost material is missing.");
                return false;
            }

            return
                _gameFlow.CurrentPhase ==
                    ExperiencePhase.Training &&
                !_gameFlow.IsPausedForTracking &&
                _rhythmClock.IsRunning &&
                _rhythmChart.HasSession;
        }

        private void RebuildTargets()
        {
            RhythmPoseGuideSchedule.Build(
                _rhythmChart == null
                    ? null
                    : _rhythmChart.ScheduledNotes,
                _targets,
                _showPreparationPreview);
            _targetIndex = 0;
            _preparedTargetIndex = -1;
            _preparedPose = null;
            _lastBeatEventIndex = -1;
            _hasLastMuscleExercise = false;

            if (_debugLogging)
            {
                Debug.Log(
                    $"[RhythmPoseGuide] Built " +
                    $"{_targets.Count} visual pose targets.",
                    this);
            }
        }

        private void InitializeVariants()
        {
            var hasUsableVariant = false;
            if (_variants != null)
            {
                foreach (var variant in _variants)
                {
                    variant?.Initialize(
                        ActiveGuideMaterial,
                        this);
                    hasUsableVariant |=
                        variant != null &&
                        variant.IsUsable;
                }
            }

            if (!hasUsableVariant &&
                _ghostTransform != null)
            {
                var fallbackGuideRoot =
                    ResolveFallbackGuideRoot(
                        _ghostTransform);
                var fallbackGuide =
                    new GhostGuideInstance();
                fallbackGuide.SetRuntimeReferences(
                    "FallbackGhostGuide",
                    fallbackGuideRoot,
                    _ghostTransform.gameObject,
                    _skeletonRoot,
                    _mirrorTransform,
                    _mirrorTransform);
                _fallbackVariant =
                    new GhostAvatarVariantBinding();
                _fallbackVariant.SetRuntimeReferences(
                    null,
                    null,
                    fallbackGuide,
                    _muscleConfigSource);
                _fallbackVariant.Initialize(
                    ActiveGuideMaterial,
                    this);
            }

        }

        private GameObject ResolveFallbackGuideRoot(
            Transform fallbackVisual)
        {
            var parent = fallbackVisual.parent;
            if (parent != null &&
                parent.parent == transform &&
                (parent.name == "PlayerGhostGuide" ||
                 parent.name == "MirrorGhostGuide"))
            {
                return parent.gameObject;
            }

            return fallbackVisual.gameObject;
        }

        private void SelectAvatar(AvatarDefinition avatar)
        {
            HideGhost();
            _activeVariant = null;
            if (_variants != null)
            {
                foreach (var variant in _variants)
                {
                    if (variant == null ||
                        variant.SourceAvatar != avatar ||
                        !variant.IsUsable)
                    {
                        continue;
                    }

                    _activeVariant = variant;
                    break;
                }
            }

            if (_activeVariant == null &&
                _fallbackVariant != null &&
                _fallbackVariant.IsUsable)
            {
                _activeVariant = _fallbackVariant;
            }

            if (_activeVariant == null &&
                _variants != null)
            {
                foreach (var variant in _variants)
                {
                    if (variant == null ||
                        !variant.IsUsable)
                    {
                        continue;
                    }

                    _activeVariant = variant;
                    break;
                }
            }

            _preparedTargetIndex = -1;
            _preparedPose = null;
            _hasLastMuscleExercise = false;
            _activeVariant?.RefreshBaseScales();
            _activeVariant?.ApplyFullMuscleState();
            HideAllInactiveVariants();
        }

        private void HideAllInactiveVariants()
        {
            if (_variants == null)
            {
                return;
            }

            foreach (var variant in _variants)
            {
                if (variant != null &&
                    variant != _activeVariant)
                {
                    variant.SetVisible(false);
                }
            }
        }

        private void HandleSessionStarted()
        {
            _activeVariant?.RefreshBaseScales();
            RebuildTargets();
        }

        private void HandlePhaseChanged(ExperiencePhase phase)
        {
            if (phase == ExperiencePhase.Training)
            {
                // Training owns the ghost through the scheduled path, so any
                // pose the introduction was holding is released here.
                _staticPose = null;
                _loopingExerciseActive = false;
                return;
            }

            if (phase != ExperiencePhase.Tutorial)
            {
                _loopingExerciseActive = false;
            }

            _adaptiveHintActive = false;
            if (_staticPose == null)
            {
                HideGhost();
            }
        }

        private void HandleBeatOccurred(RhythmBeat beat)
        {
            _lastBeatEventIndex = beat.BeatIndex;
        }

        private void HandleAvatarChanged(
            AvatarDefinition avatar)
        {
            SelectAvatar(avatar);
        }

        private void ApplyDisplayMode()
        {
            var ringEnabled =
                (_displayMode &
                 RhythmGuideDisplayMode.Ring) != 0;
            _ringGuide?.SetRingVisualsEnabled(ringEnabled);
            ApplyGuideMaterial();
            if (!GhostDisplayEnabled)
            {
                HideGhost();
            }
        }

        private void ApplyGuideMaterial()
        {
            var material = ActiveGuideMaterial;
            if (material == null)
            {
                return;
            }

            if (_variants != null)
            {
                foreach (var variant in _variants)
                {
                    ApplyGuideMaterial(variant, material);
                }
            }

            ApplyGuideMaterial(_fallbackVariant, material);
        }

        private static void ApplyGuideMaterial(
            GhostAvatarVariantBinding variant,
            Material material)
        {
            if (variant == null)
            {
                return;
            }

            variant.PlayerGuide?.MaterialController
                ?.SetMaterial(material);
            variant.MirrorGuide?.MaterialController
                ?.SetMaterial(material);
        }

        private void Subscribe()
        {
            if (_rhythmClock != null)
            {
                _rhythmClock.BeatOccurred -=
                    HandleBeatOccurred;
                _rhythmClock.BeatOccurred +=
                    HandleBeatOccurred;
            }

            if (_rhythmChart != null)
            {
                _rhythmChart.SessionStarted -=
                    HandleSessionStarted;
                _rhythmChart.SessionStarted +=
                    HandleSessionStarted;
            }

            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -=
                    HandlePhaseChanged;
                _gameFlow.PhaseChanged +=
                    HandlePhaseChanged;
            }

            if (_avatarRegistry != null)
            {
                _avatarRegistry.OnAvatarChanged -=
                    HandleAvatarChanged;
                _avatarRegistry.OnAvatarChanged +=
                    HandleAvatarChanged;
            }
        }

        private void Unsubscribe()
        {
            if (_rhythmClock != null)
            {
                _rhythmClock.BeatOccurred -=
                    HandleBeatOccurred;
            }

            if (_rhythmChart != null)
            {
                _rhythmChart.SessionStarted -=
                    HandleSessionStarted;
            }

            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -=
                    HandlePhaseChanged;
            }

            if (_avatarRegistry != null)
            {
                _avatarRegistry.OnAvatarChanged -=
                    HandleAvatarChanged;
            }
        }

        private void ResolveReferences()
        {
            _rhythmClock ??=
                FindFirstObjectByType<RhythmClock>();
            _rhythmChart ??=
                FindFirstObjectByType<TrainingRhythmChart>();
            _gameFlow ??=
                FindFirstObjectByType<GameFlowController>();
            _ringGuide ??=
                FindFirstObjectByType<TrainingRhythmLaneHud>(
                    FindObjectsInactive.Include);
            _avatarRegistry ??=
                FindFirstObjectByType<AvatarRegistry>();
        }

        private void ValidateSettings()
        {
            _variants ??=
                Array.Empty<GhostAvatarVariantBinding>();
            _leadTimeSeconds = Mathf.Max(
                0f,
                _leadTimeSeconds);
            _startScaleMultiplier = Mathf.Max(
                0f,
                _startScaleMultiplier);
            _beatScaleMultiplier = Mathf.Max(
                0f,
                _beatScaleMultiplier);
            _postBeatVisibleSeconds = Mathf.Max(
                0f,
                _postBeatVisibleSeconds);
            _lineArtBeatContourOffset = Mathf.Max(
                0f,
                _lineArtBeatContourOffset);
            _lineArtStartContourOffset = Mathf.Max(
                _lineArtBeatContourOffset,
                _lineArtStartContourOffset);
            _normalAlpha = Mathf.Clamp01(_normalAlpha);
            _emissionIntensity = Mathf.Max(
                0f,
                _emissionIntensity);
            _beatAlpha = Mathf.Clamp01(_beatAlpha);
            _beatIntensity = Mathf.Max(
                1f,
                _beatIntensity);
            _playerNormalAlpha = Mathf.Clamp01(
                _playerNormalAlpha);
            _playerEmissionIntensity = Mathf.Max(
                0f,
                _playerEmissionIntensity);
            _playerBeatAlpha = Mathf.Clamp01(
                _playerBeatAlpha);
            _playerBeatIntensity = Mathf.Max(
                1f,
                _playerBeatIntensity);
            _mirrorNormalAlpha = Mathf.Clamp01(
                _mirrorNormalAlpha);
            _mirrorEmissionIntensity = Mathf.Max(
                0f,
                _mirrorEmissionIntensity);
            _mirrorBeatAlpha = Mathf.Clamp01(
                _mirrorBeatAlpha);
            _mirrorBeatIntensity = Mathf.Max(
                1f,
                _mirrorBeatIntensity);
            _scaleCurve ??=
                AnimationCurve.Linear(
                    0f,
                    0f,
                    1f,
                    1f);
            _adaptiveHintAlphaMultiplier = Mathf.Clamp(
                _adaptiveHintAlphaMultiplier,
                1f,
                3f);
            _adaptiveHintIntensityMultiplier = Mathf.Clamp(
                _adaptiveHintIntensityMultiplier,
                1f,
                3f);
        }

        private void OnValidate()
        {
            ResolveReferences();
            ValidateSettings();
            ApplyDisplayMode();
        }

        private void WarnOnce(
            ref bool hasWarned,
            string message)
        {
            if (hasWarned)
            {
                return;
            }

            hasWarned = true;
            Debug.LogWarning(message, this);
        }

    }
}
