using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace IVRC
{
    public readonly struct StageEntranceWalkBlend
    {
        public StageEntranceWalkBlend(
            float walkTime,
            float stopTime,
            float stopWeight)
        {
            WalkTime = walkTime;
            StopTime = stopTime;
            StopWeight = stopWeight;
        }

        public float WalkTime { get; }
        public float StopTime { get; }

        /// <summary>0 while walking, 1 once the stop has taken over.</summary>
        public float StopWeight { get; }
    }

    public static class StageEntranceCinematicMath
    {
        public static float EvaluateWalkProgress(
            float elapsedSeconds,
            float walkSeconds)
        {
            return EvaluateWalkProgress(
                elapsedSeconds,
                walkSeconds,
                0f);
        }

        /// <summary>
        /// How far along the path the avatar is.
        ///
        /// <paramref name="stillSeconds"/> is the tail of the walk during
        /// which the animation is standing still. The path has to finish
        /// before it starts, or the avatar slides to its mark with its feet
        /// planted — which is what a scripted path does when nothing tells
        /// it the animation has stopped walking.
        ///
        /// Measured from the stop clip rather than guessed: it covers its
        /// 1.13 m in the first 2.1 s of its three seconds and holds still
        /// for the remaining 0.9.
        /// </summary>
        public static float EvaluateWalkProgress(
            float elapsedSeconds,
            float walkSeconds,
            float stillSeconds)
        {
            if (!IsFinite(elapsedSeconds) ||
                !IsFinite(walkSeconds) ||
                walkSeconds <= Mathf.Epsilon)
            {
                return elapsedSeconds > 0f ? 1f : 0f;
            }

            var still = IsFinite(stillSeconds)
                ? Mathf.Clamp(stillSeconds, 0f, walkSeconds)
                : 0f;
            var travelSeconds = walkSeconds - still;
            if (travelSeconds <= Mathf.Epsilon)
            {
                return elapsedSeconds > 0f ? 1f : 0f;
            }

            var linear = Mathf.Clamp01(
                elapsedSeconds / travelSeconds);
            return linear * linear * (3f - 2f * linear);
        }

        public static float EvaluateGaitStrength(
            float walkProgress)
        {
            if (!IsFinite(walkProgress))
            {
                return 0f;
            }

            var progress = Mathf.Clamp01(walkProgress);
            var fadeIn = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0f, 0.12f, progress));
            var fadeOut = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(1f, 0.82f, progress));
            return Mathf.Clamp01(fadeIn * fadeOut);
        }

        public static float EvaluateGaitPhase(
            float elapsedSeconds,
            float stepsPerSecond)
        {
            if (!IsFinite(elapsedSeconds) ||
                !IsFinite(stepsPerSecond))
            {
                return 0f;
            }

            return
                Mathf.Max(0f, elapsedSeconds) *
                Mathf.Max(0f, stepsPerSecond) *
                Mathf.PI;
        }

        /// <summary>
        /// How the looping walk hands over to the clip that stops.
        ///
        /// The stop clip is placed so it finishes exactly when the path
        /// does, and the loop cross-fades into it rather than cutting, so
        /// the legs carry through instead of snapping to a new pose. After
        /// the path ends the stop clip holds its last frame, which is a
        /// person standing still — the old behaviour rewound the loop to
        /// its first frame there, which is a person mid-stride.
        /// </summary>
        public static StageEntranceWalkBlend EvaluateWalkBlend(
            float elapsedSeconds,
            float walkSeconds,
            float playbackSpeed,
            float walkClipLength,
            float stopClipLength,
            float blendSeconds)
        {
            var walkTime = EvaluateWalkAnimationTime(
                elapsedSeconds,
                walkSeconds,
                playbackSpeed,
                walkClipLength);

            if (!IsFinite(elapsedSeconds) ||
                !IsFinite(walkSeconds) ||
                !IsFinite(stopClipLength) ||
                stopClipLength <= Mathf.Epsilon ||
                walkSeconds <= Mathf.Epsilon)
            {
                return new StageEntranceWalkBlend(walkTime, 0f, 0f);
            }

            // The stop cannot be longer than the walk it ends.
            var stopLength = Mathf.Min(stopClipLength, walkSeconds);
            var stopStart = walkSeconds - stopLength;
            var elapsed = Mathf.Max(0f, elapsedSeconds);
            if (elapsed < stopStart)
            {
                return new StageEntranceWalkBlend(walkTime, 0f, 0f);
            }

            var intoStop = Mathf.Min(elapsed - stopStart, stopLength);
            var blend = IsFinite(blendSeconds)
                ? Mathf.Max(0f, blendSeconds)
                : 0f;
            var weight = blend <= Mathf.Epsilon
                ? 1f
                : Mathf.Clamp01((elapsed - stopStart) / blend);

            return new StageEntranceWalkBlend(
                walkTime,
                intoStop,
                weight);
        }

        public static float EvaluateWalkAnimationTime(
            float elapsedSeconds,
            float walkSeconds,
            float playbackSpeed,
            float clipLength)
        {
            if (!IsFinite(elapsedSeconds) ||
                !IsFinite(walkSeconds) ||
                !IsFinite(playbackSpeed) ||
                !IsFinite(clipLength) ||
                walkSeconds <= Mathf.Epsilon ||
                playbackSpeed <= Mathf.Epsilon ||
                clipLength <= Mathf.Epsilon)
            {
                return 0f;
            }

            var walkElapsed = Mathf.Clamp(
                elapsedSeconds,
                0f,
                walkSeconds);
            if (walkElapsed >= walkSeconds)
            {
                // Hold the first loop pose once the scripted path stops.
                // This prevents a walk cycle from continuing at WalkEnd.
                return 0f;
            }

            var unwrappedTime =
                walkElapsed * playbackSpeed;
            return IsFinite(unwrappedTime)
                ? Mathf.Repeat(unwrappedTime, clipLength)
                : 0f;
        }

        public static int SelectShot(
            float elapsedSeconds,
            float wideShotSeconds,
            float muscleShotSeconds)
        {
            var elapsed = IsFinite(elapsedSeconds)
                ? Mathf.Max(0f, elapsedSeconds)
                : 0f;
            var wide = IsFinite(wideShotSeconds)
                ? Mathf.Max(0f, wideShotSeconds)
                : 0f;
            var muscle = IsFinite(muscleShotSeconds)
                ? Mathf.Max(0f, muscleShotSeconds)
                : 0f;
            if (elapsed < wide)
            {
                return 0;
            }

            return elapsed < wide + muscle ? 1 : 2;
        }

        public static bool ShouldKeepCinematicDuringTransition(
            bool isRunning,
            bool isTransitioning,
            bool hasRelocated,
            StageTransitionDestination destination)
        {
            return
                isRunning &&
                isTransitioning &&
                !hasRelocated &&
                destination == StageTransitionDestination.Stage;
        }

        public static Vector3 EvaluateQuadraticPath(
            Vector3 start,
            Vector3 middle,
            Vector3 end,
            float progress)
        {
            var t = Mathf.Clamp01(progress);
            var inverse = 1f - t;
            return
                inverse * inverse * start +
                2f * inverse * t * middle +
                t * t * end;
        }

        private static bool IsFinite(float value)
        {
            return
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Shows a comfortable, world-locked entrance movie without moving the
    /// headset. A clean Humanoid model walks through a dedicated render-only
    /// stage while one mono camera cuts between three views.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageEntranceCinematicController :
        MonoBehaviour
    {
        private const string CinematicCharacterLayer =
            "CinematicCharacter";

        [Header("Systems")]
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private AvatarRegistry _avatarRegistry;
        [SerializeField] private StageTransitionController
            _stageTransition;

        [Header("Phase Presentation")]
        [SerializeField] private GameObject _posingMonitorRoot;
        [SerializeField] private GameObject _posingHudRoot;
        [SerializeField] private GameObject _entranceGuideRoot;
        [Tooltip(
            "Keep this off for the participant build. The stage HUD " +
            "explains that the entrance is passive and that three poses " +
            "come next, while the pose monitor itself stays hidden.")]
        [SerializeField] private bool _hidePosingHudDuringEntrance;
        [Tooltip(
            "Turn the shared stage HUD toward the judge seat during the " +
            "entrance, then restore its posing orientation before play.")]
        [SerializeField]
        private bool _facePosingHudTowardJudgeDuringEntrance = true;
        [SerializeField, Min(1f)]
        private float _entranceHudScaleMultiplier = 1.15f;
        [SerializeField] private Camera _cinematicCamera;
        [SerializeField] private Transform _avatarContainer;
        [SerializeField] private Renderer _screenRenderer;

        [Header("Walk Path")]
        [SerializeField] private Transform _walkStart;
        [SerializeField] private Transform _walkMiddle;
        [SerializeField] private Transform _walkEnd;

        [Header("Camera Cuts")]
        [SerializeField] private Transform _wideShotAnchor;
        [SerializeField] private Transform _muscleShotAnchor;
        [SerializeField] private Transform _heroShotAnchor;
        [SerializeField, Min(0f)] private float _wideShotSeconds =
            2.3f;
        [SerializeField, Min(0f)] private float _muscleShotSeconds =
            2.1f;
        [SerializeField] private Vector3 _shotLookHeights =
            new Vector3(1f, 1.25f, 1.05f);
        [SerializeField] private Vector3 _shotFieldOfView =
            new Vector3(40f, 34f, 42f);

        [Header("Timing")]
        [SerializeField, Min(0.1f)] private float _totalSeconds =
            6.5f;
        [SerializeField, Min(0.1f)] private float _walkSeconds =
            5.2f;
        [SerializeField, Min(0.1f)] private float _stepsPerSecond =
            1.8f;

        [Header("Walk Animation")]
        [SerializeField] private AnimationClip _walkAnimation;
        [Tooltip(
            "Played once at the end of the walk so the avatar comes to a " +
            "halt instead of freezing mid-stride. Placed to finish exactly " +
            "when the path does.")]
        [SerializeField] private AnimationClip _stopWalkAnimation;
        [SerializeField, Min(0.01f)]
        private float _walkPlaybackSpeed = 1f;
        [Tooltip("Cross-fade from the loop into the stop.")]
        [SerializeField, Range(0f, 1f)]
        private float _stopBlendSeconds = 0.25f;
        [Tooltip(
            "The tail of the stop clip during which the feet are planted. " +
            "The path finishes before it begins, so the avatar is not " +
            "sliding while the animation says it has stopped. Measured " +
            "from the clip: it travels for 2.1 s of its 3 and holds for " +
            "the remaining 0.9.")]
        [SerializeField, Min(0f)]
        private float _stopStillSeconds = 0.9f;
        [Tooltip(
            "Plants the feet on the floor. Turning it off was tried and " +
            "was worse: the raw retargeted pose bends the shin instead. " +
            "The ankle it was blamed for came from the clip's root height " +
            "settings, not from here.")]
        [SerializeField] private bool _applyFootIk = true;

        private static Dictionary<string, int>
            _muscleIndices;

        private GameObject _runtimeAvatar;
        private Animator _runtimeAnimator;
        private PlayableGraph _walkPlayableGraph;
        private AnimationClipPlayable _walkClipPlayable;
        private AnimationClipPlayable _stopClipPlayable;
        private AnimationMixerPlayable _walkMixer;
        private HumanPoseHandler _poseHandler;
        private HumanPose _pose;
        private float[] _baseMuscles;
        private float[] _workingMuscles;
        private SkinnedMeshRenderer[] _cinematicRenderers =
            Array.Empty<SkinnedMeshRenderer>();
        private readonly MuscleBlendShapeStateSynchronizer
            _muscleState =
                new MuscleBlendShapeStateSynchronizer();
        private readonly MuscleSurfaceStateSynchronizer
            _surfaceState =
                new MuscleSurfaceStateSynchronizer();
        private MuscleSurfaceSnapshot _surfaceSnapshot;
        private bool _hasLoggedSurfaceFailure;
        private float _elapsedSeconds;
        private bool _isRunning;
        private bool _hasPresentationState;
        private bool _posingMonitorWasActive;
        private bool _posingHudWasActive;
        private bool _hasEntranceGuideTransformState;
        private Quaternion _entranceGuideWorldRotation;
        private Vector3 _entranceGuideLocalScale;
        private bool _awaitingExitRelocation;
        private readonly List<RendererState>
            _trackedRendererStates =
                new List<RendererState>();

        public float TotalDuration =>
            Mathf.Max(0.1f, _totalSeconds);
        public bool IsRunning => _isRunning;
        public int CurrentShotIndex { get; private set; }
        public GameObject RuntimeAvatar => _runtimeAvatar;

        private void Awake()
        {
            EnsureMuscleIndices();
            ResolveReferences();
            ValidateSettings();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= HandlePhaseChanged;
                _gameFlow.PhaseChanged += HandlePhaseChanged;
                if (_gameFlow.CurrentPhase ==
                    ExperiencePhase.StageEntrance)
                {
                    BeginCinematic();
                }
            }
        }

        private void OnDisable()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= HandlePhaseChanged;
            }

            EndCinematic();
        }

        private void OnDestroy()
        {
            DisposeWalkPlayableGraph();
            DisposePoseHandler();
        }

        private void OnValidate()
        {
            ValidateSettings();
        }

        private void LateUpdate()
        {
            if (!_isRunning)
            {
                return;
            }

            if (_awaitingExitRelocation &&
                (_stageTransition == null ||
                 _stageTransition.HasRelocated))
            {
                EndCinematic();
                return;
            }

            _elapsedSeconds = Mathf.Min(
                TotalDuration,
                _elapsedSeconds +
                Mathf.Max(0f, Time.unscaledDeltaTime));
            UpdateWalk();
            UpdateCamera();
            ApplyMuscleBlendShape();
        }

        private void HandlePhaseChanged(ExperiencePhase phase)
        {
            if (phase == ExperiencePhase.StageEntrance)
            {
                BeginCinematic();
            }
            else if (StageEntranceCinematicMath
                     .ShouldKeepCinematicDuringTransition(
                         _isRunning,
                         _stageTransition != null &&
                         _stageTransition.IsTransitioning,
                         _stageTransition != null &&
                         _stageTransition.HasRelocated,
                         _stageTransition == null
                             ? StageTransitionDestination.Training
                             : _stageTransition.Destination))
            {
                _awaitingExitRelocation = true;
            }
            else
            {
                EndCinematic();
            }
        }

        private void BeginCinematic()
        {
            if (_isRunning)
            {
                return;
            }

            _isRunning = true;
            _awaitingExitRelocation = false;
            _elapsedSeconds = 0f;
            CurrentShotIndex = 0;
            CaptureAndHidePosingPresentation();
            CreateRuntimeAvatar();
            CaptureAndHideTrackedAvatar();
            if (_screenRenderer != null)
            {
                _screenRenderer.enabled = true;
            }

            if (_cinematicCamera != null)
            {
                _cinematicCamera.enabled = true;
            }

            UpdateWalk();
            UpdateCamera();
            ApplyMuscleBlendShape();
        }

        private void EndCinematic()
        {
            if (!_isRunning &&
                !_hasPresentationState &&
                _runtimeAvatar == null &&
                !_walkPlayableGraph.IsValid())
            {
                return;
            }

            _isRunning = false;
            _awaitingExitRelocation = false;
            if (_cinematicCamera != null)
            {
                _cinematicCamera.enabled = false;
            }

            if (_screenRenderer != null)
            {
                _screenRenderer.enabled = false;
            }

            RestorePosingPresentation();
            RestoreTrackedAvatar();
            DisposeWalkPlayableGraph();
            DisposePoseHandler();
            if (_runtimeAvatar != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_runtimeAvatar);
                }
                else
                {
                    DestroyImmediate(_runtimeAvatar);
                }
            }

            _runtimeAvatar = null;
            _runtimeAnimator = null;
            _cinematicRenderers =
                Array.Empty<SkinnedMeshRenderer>();
            _muscleState.Clear();
            _surfaceState.ForgetWithoutRestore();
            _hasLoggedSurfaceFailure = false;
        }

        private void CaptureAndHideTrackedAvatar()
        {
            if (_trackedRendererStates.Count > 0)
            {
                return;
            }

            var definition =
                _avatarRegistry == null
                    ? null
                    : _avatarRegistry.Active;
            if (definition == null)
            {
                return;
            }

            foreach (var renderer in
                     definition.GetComponentsInChildren<
                         Renderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                _trackedRendererStates.Add(
                    new RendererState(
                        renderer,
                        renderer.enabled));
                renderer.enabled = false;
            }
        }

        private void RestoreTrackedAvatar()
        {
            foreach (var state in _trackedRendererStates)
            {
                if (state.Renderer != null)
                {
                    state.Renderer.enabled = state.WasEnabled;
                }
            }

            _trackedRendererStates.Clear();
        }

        private void CaptureAndHidePosingPresentation()
        {
            if (_hasPresentationState)
            {
                return;
            }

            _hasPresentationState = true;
            if (_posingMonitorRoot != null)
            {
                _posingMonitorWasActive =
                    _posingMonitorRoot.activeSelf;
                _posingMonitorRoot.SetActive(false);
            }

            if (_hidePosingHudDuringEntrance &&
                _posingHudRoot != null)
            {
                _posingHudWasActive =
                    _posingHudRoot.activeSelf;
                _posingHudRoot.SetActive(false);
            }

            FacePosingHudTowardJudge();
        }

        private void RestorePosingPresentation()
        {
            if (!_hasPresentationState)
            {
                return;
            }

            if (_posingMonitorRoot != null)
            {
                _posingMonitorRoot.SetActive(
                    _posingMonitorWasActive);
            }

            if (_hidePosingHudDuringEntrance &&
                _posingHudRoot != null)
            {
                _posingHudRoot.SetActive(
                    _posingHudWasActive);
            }

            if (_hasEntranceGuideTransformState)
            {
                var guideTransform = GetEntranceGuideTransform();
                if (guideTransform != null)
                {
                    guideTransform.rotation =
                        _entranceGuideWorldRotation;
                    guideTransform.localScale =
                        _entranceGuideLocalScale;
                }
            }

            _hasEntranceGuideTransformState = false;
            _hasPresentationState = false;
        }

        private void FacePosingHudTowardJudge()
        {
            if (_hidePosingHudDuringEntrance ||
                !_facePosingHudTowardJudgeDuringEntrance ||
                _stageTransition == null ||
                _stageTransition.JudgeArrivalPoint == null)
            {
                return;
            }

            var hudTransform = GetEntranceGuideTransform();
            if (hudTransform == null)
            {
                return;
            }

            var judgePosition =
                _stageTransition.JudgeArrivalPoint.position;
            var forward = hudTransform.position - judgePosition;
            forward.y = 0f;
            if (forward.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            _entranceGuideWorldRotation = hudTransform.rotation;
            _entranceGuideLocalScale = hudTransform.localScale;
            _hasEntranceGuideTransformState = true;
            hudTransform.rotation = Quaternion.LookRotation(
                forward.normalized,
                Vector3.up);
            hudTransform.localScale =
                _entranceGuideLocalScale *
                Mathf.Max(1f, _entranceHudScaleMultiplier);
        }

        private Transform GetEntranceGuideTransform()
        {
            if (_entranceGuideRoot != null)
            {
                return _entranceGuideRoot.transform;
            }

            return _posingHudRoot == null
                ? null
                : _posingHudRoot.transform;
        }

        private void CreateRuntimeAvatar()
        {
            var definition =
                _avatarRegistry == null
                    ? null
                    : _avatarRegistry.Active;
            if (definition == null)
            {
                Debug.LogWarning(
                    "[StageEntrance] No active avatar is available. " +
                    "The entrance will continue without a model.",
                    this);
                return;
            }

            if (!definition.ValidateCinematicPrefab(
                    out var error))
            {
                Debug.LogWarning(
                    $"[StageEntrance] {definition.DisplayName}: " +
                    error,
                    definition);
                return;
            }

            _runtimeAvatar = Instantiate(
                definition.CinematicPrefab,
                _avatarContainer == null
                    ? transform
                    : _avatarContainer,
                false);
            _runtimeAvatar.name =
                $"CinematicAvatar_{definition.DisplayName}";
            SetLayerRecursively(
                _runtimeAvatar,
                LayerMask.NameToLayer(
                    CinematicCharacterLayer));

            _runtimeAnimator =
                _runtimeAvatar.GetComponentInChildren<
                    Animator>(true);
            _runtimeAnimator.applyRootMotion = false;
            _runtimeAnimator.cullingMode =
                AnimatorCullingMode.AlwaysAnimate;
            _runtimeAnimator.enabled = true;
            _runtimeAnimator.Rebind();
            _runtimeAnimator.Update(0f);

            _cinematicRenderers =
                _runtimeAvatar.GetComponentsInChildren<
                    SkinnedMeshRenderer>(true);
            foreach (var renderer in _cinematicRenderers)
            {
                renderer.updateWhenOffscreen = true;
            }

            _muscleState.RebuildAllSharedBlendShapes(
                definition.TrackedBodyRenderer,
                _cinematicRenderers);
            var surfaceTemplate =
                definition.GrowthOutput.SurfaceSnapshot;
            var frozenGrowth = _gameFlow == null
                ? surfaceTemplate.NormalizedGrowth
                : _gameFlow.CompletedMuscleGrowth.Overall;
            _surfaceSnapshot = new MuscleSurfaceSnapshot(
                frozenGrowth,
                surfaceTemplate.RestingNormalScale,
                surfaceTemplate.TrainedNormalScale,
                surfaceTemplate.RestingSmoothness,
                surfaceTemplate.TrainedSmoothness);
            if (_surfaceState.RebuildConfiguredPresentation(
                definition.GrowthOutput,
                definition.TrackedBodyRenderer,
                _cinematicRenderers) <= 0 &&
                !_hasLoggedSurfaceFailure)
            {
                _hasLoggedSurfaceFailure = true;
                Debug.LogWarning(
                    "[StageEntrance] Avatar surface presentation " +
                    "could not be copied: " +
                    _surfaceState.ValidationError,
                    this);
            }
            _surfaceState.Apply(_surfaceSnapshot);
            if (!TryCreateWalkPlayableGraph())
            {
                _runtimeAnimator.enabled = false;
                CreatePoseHandler();
            }
        }

        private bool TryCreateWalkPlayableGraph()
        {
            DisposeWalkPlayableGraph();
            if (_walkAnimation == null)
            {
                return false;
            }

            if (!_walkAnimation.isHumanMotion ||
                _walkAnimation.length <= Mathf.Epsilon)
            {
                Debug.LogWarning(
                    "[StageEntrance] The assigned walk animation must " +
                    "be a non-empty Humanoid clip. Using the " +
                    "procedural fallback.",
                    this);
                return false;
            }

            if (_runtimeAnimator == null ||
                _runtimeAnimator.avatar == null ||
                !_runtimeAnimator.avatar.isHuman ||
                !_runtimeAnimator.avatar.isValid)
            {
                return false;
            }

            try
            {
                _runtimeAnimator.applyRootMotion = false;
                _runtimeAnimator.enabled = true;
                _walkPlayableGraph = PlayableGraph.Create(
                    $"{name}_StageEntranceWalk");
                _walkPlayableGraph.SetTimeUpdateMode(
                    DirectorUpdateMode.Manual);
                _walkClipPlayable = AnimationClipPlayable.Create(
                    _walkPlayableGraph,
                    _walkAnimation);
                _walkClipPlayable.SetApplyFootIK(_applyFootIk);
                _walkClipPlayable.SetApplyPlayableIK(false);
                _walkClipPlayable.SetSpeed(0d);

                // Two inputs even when there is no stop clip, so the graph
                // has one shape and the weights alone decide what shows.
                _walkMixer = AnimationMixerPlayable.Create(
                    _walkPlayableGraph,
                    2);
                _walkPlayableGraph.Connect(
                    _walkClipPlayable,
                    0,
                    _walkMixer,
                    0);
                _walkMixer.SetInputWeight(0, 1f);

                if (_stopWalkAnimation != null &&
                    _stopWalkAnimation.isHumanMotion &&
                    _stopWalkAnimation.length > Mathf.Epsilon)
                {
                    _stopClipPlayable =
                        AnimationClipPlayable.Create(
                            _walkPlayableGraph,
                            _stopWalkAnimation);
                    _stopClipPlayable.SetApplyFootIK(_applyFootIk);
                    _stopClipPlayable.SetApplyPlayableIK(false);
                    _stopClipPlayable.SetSpeed(0d);
                    _walkPlayableGraph.Connect(
                        _stopClipPlayable,
                        0,
                        _walkMixer,
                        1);
                    _walkMixer.SetInputWeight(1, 0f);
                }

                var output = AnimationPlayableOutput.Create(
                    _walkPlayableGraph,
                    "Stage Entrance Walk",
                    _runtimeAnimator);
                output.SetSourcePlayable(_walkMixer);
                _walkPlayableGraph.Play();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[StageEntrance] Humanoid walk animation could " +
                    $"not be initialized: {exception.Message}. " +
                    "Using the procedural fallback.",
                    this);
                DisposeWalkPlayableGraph();
                return false;
            }
        }

        private void DisposeWalkPlayableGraph()
        {
            if (_walkPlayableGraph.IsValid())
            {
                _walkPlayableGraph.Destroy();
            }

            _walkPlayableGraph = default;
            _walkClipPlayable = default;
            _stopClipPlayable = default;
            _walkMixer = default;
            _walkClipPlayable = default;
        }

        private void CreatePoseHandler()
        {
            DisposePoseHandler();
            if (_runtimeAnimator == null ||
                _runtimeAnimator.avatar == null ||
                !_runtimeAnimator.avatar.isHuman)
            {
                return;
            }

            try
            {
                _poseHandler = new HumanPoseHandler(
                    _runtimeAnimator.avatar,
                    _runtimeAnimator.transform);
                _pose = new HumanPose();
                _poseHandler.GetHumanPose(ref _pose);
                _baseMuscles =
                    (float[])_pose.muscles.Clone();
                _workingMuscles =
                    new float[_baseMuscles.Length];
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[StageEntrance] Humanoid gait could not be " +
                    $"initialized: {exception.Message}",
                    this);
                DisposePoseHandler();
            }
        }

        private void DisposePoseHandler()
        {
            _poseHandler?.Dispose();
            _poseHandler = null;
            _baseMuscles = null;
            _workingMuscles = null;
        }

        private void UpdateWalk()
        {
            if (_runtimeAvatar == null ||
                _walkStart == null ||
                _walkMiddle == null ||
                _walkEnd == null)
            {
                return;
            }

            var hasWalkAnimation =
                UpdateWalkPlayableGraph();
            var progress =
                StageEntranceCinematicMath
                    .EvaluateWalkProgress(
                        _elapsedSeconds,
                        _walkSeconds,
                        _stopClipPlayable.IsValid()
                            ? _stopStillSeconds
                            : 0f);
            var position =
                StageEntranceCinematicMath
                    .EvaluateQuadraticPath(
                        _walkStart.position,
                        _walkMiddle.position,
                        _walkEnd.position,
                        progress);
            var gaitStrength =
                StageEntranceCinematicMath
                    .EvaluateGaitStrength(progress);
            var phase =
                StageEntranceCinematicMath
                    .EvaluateGaitPhase(
                        _elapsedSeconds,
                        _stepsPerSecond);
            if (!hasWalkAnimation)
            {
                position += Vector3.up *
                    (Mathf.Abs(Mathf.Sin(phase)) *
                     0.012f *
                     gaitStrength);
            }
            _runtimeAvatar.transform.position = position;

            var nextProgress =
                Mathf.Min(1f, progress + 0.01f);
            var tangent =
                StageEntranceCinematicMath
                    .EvaluateQuadraticPath(
                        _walkStart.position,
                        _walkMiddle.position,
                        _walkEnd.position,
                        nextProgress) -
                position;
            tangent.y = 0f;
            var pathRotation =
                tangent.sqrMagnitude <= 0.000001f
                    ? _walkEnd.rotation
                    : Quaternion.LookRotation(
                        tangent.normalized,
                        Vector3.up);
            var faceForward = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    0.72f,
                    1f,
                    progress));
            _runtimeAvatar.transform.rotation =
                Quaternion.Slerp(
                    pathRotation,
                    _walkEnd.rotation,
                    faceForward);

            if (!hasWalkAnimation)
            {
                ApplyProceduralGait(phase, gaitStrength);
            }
        }

        private bool UpdateWalkPlayableGraph()
        {
            if (!_walkPlayableGraph.IsValid() ||
                !_walkClipPlayable.IsValid() ||
                _walkAnimation == null ||
                _runtimeAnimator == null)
            {
                return false;
            }

            _runtimeAnimator.applyRootMotion = false;
            var blend = StageEntranceCinematicMath.EvaluateWalkBlend(
                _elapsedSeconds,
                _walkSeconds,
                _walkPlaybackSpeed,
                _walkAnimation.length,
                _stopClipPlayable.IsValid()
                    ? _stopWalkAnimation.length
                    : 0f,
                _stopBlendSeconds);

            _walkClipPlayable.SetTime(blend.WalkTime);
            if (_stopClipPlayable.IsValid())
            {
                _stopClipPlayable.SetTime(blend.StopTime);
                _walkMixer.SetInputWeight(0, 1f - blend.StopWeight);
                _walkMixer.SetInputWeight(1, blend.StopWeight);
            }

            _walkPlayableGraph.Evaluate(0f);
            return true;
        }

        private void ApplyProceduralGait(
            float phase,
            float strength)
        {
            if (_poseHandler == null ||
                _baseMuscles == null ||
                _workingMuscles == null)
            {
                return;
            }

            Array.Copy(
                _baseMuscles,
                _workingMuscles,
                _baseMuscles.Length);
            var swing = Mathf.Sin(phase) * strength;

            SetMuscle(
                "Left Upper Leg Front-Back",
                swing * 0.2f);
            SetMuscle(
                "Right Upper Leg Front-Back",
                -swing * 0.2f);
            SetMuscle(
                "Left Foot Up-Down",
                -swing * 0.05f);
            SetMuscle(
                "Right Foot Up-Down",
                swing * 0.05f);

            SetMuscle("Left Shoulder Down-Up", -0.12f);
            SetMuscle("Right Shoulder Down-Up", -0.12f);
            SetMuscle("Left Arm Down-Up", -0.72f);
            SetMuscle("Right Arm Down-Up", -0.72f);
            SetMuscle(
                "Left Arm Front-Back",
                -swing * 0.22f);
            SetMuscle(
                "Right Arm Front-Back",
                swing * 0.22f);
            SetMuscle("Left Forearm Stretch", 0.72f);
            SetMuscle("Right Forearm Stretch", 0.72f);
            SetMuscle(
                "Chest Twist Left-Right",
                -swing * 0.06f);
            SetMuscle(
                "Spine Twist Left-Right",
                swing * 0.04f);

            _pose.muscles = _workingMuscles;
            _poseHandler.SetHumanPose(ref _pose);
        }

        private void SetMuscle(string name, float value)
        {
            EnsureMuscleIndices();
            if (!_muscleIndices.TryGetValue(
                    name,
                    out var index) ||
                index < 0 ||
                index >= _workingMuscles.Length)
            {
                return;
            }

            _workingMuscles[index] =
                Mathf.Clamp(value, -1f, 1f);
        }

        private void UpdateCamera()
        {
            if (_cinematicCamera == null)
            {
                return;
            }

            CurrentShotIndex =
                StageEntranceCinematicMath.SelectShot(
                    _elapsedSeconds,
                    _wideShotSeconds,
                    _muscleShotSeconds);
            var anchor = CurrentShotIndex switch
            {
                0 => _wideShotAnchor,
                1 => _muscleShotAnchor,
                _ => _heroShotAnchor
            };
            if (anchor == null)
            {
                return;
            }

            _cinematicCamera.transform.SetPositionAndRotation(
                anchor.position,
                anchor.rotation);
            if (_runtimeAvatar != null)
            {
                var lookHeight = CurrentShotIndex switch
                {
                    0 => _shotLookHeights.x,
                    1 => _shotLookHeights.y,
                    _ => _shotLookHeights.z
                };
                var target =
                    _runtimeAvatar.transform.position +
                    Vector3.up * lookHeight;
                var direction =
                    target -
                    _cinematicCamera.transform.position;
                if (direction.sqrMagnitude > 0.000001f)
                {
                    _cinematicCamera.transform.rotation =
                        Quaternion.LookRotation(
                            direction.normalized,
                            Vector3.up);
                }
            }

            _cinematicCamera.fieldOfView =
                CurrentShotIndex switch
                {
                    0 => _shotFieldOfView.x,
                    1 => _shotFieldOfView.y,
                    _ => _shotFieldOfView.z
                };
        }

        private void ApplyMuscleBlendShape()
        {
            _muscleState.Apply();
            _surfaceState.Apply(_surfaceSnapshot);
        }

        private void ResolveReferences()
        {
            _gameFlow ??=
                FindFirstObjectByType<GameFlowController>();
            _avatarRegistry ??=
                FindFirstObjectByType<AvatarRegistry>();
            _stageTransition ??=
                FindFirstObjectByType<StageTransitionController>();
        }

        private void ValidateSettings()
        {
            _totalSeconds =
                Mathf.Max(0.1f, _totalSeconds);
            _walkSeconds = Mathf.Clamp(
                _walkSeconds,
                0.1f,
                _totalSeconds);
            _wideShotSeconds =
                Mathf.Max(0f, _wideShotSeconds);
            _muscleShotSeconds =
                Mathf.Max(0f, _muscleShotSeconds);
            _stepsPerSecond =
                Mathf.Max(0.1f, _stepsPerSecond);
            _walkPlaybackSpeed =
                Mathf.Max(0.01f, _walkPlaybackSpeed);
            _shotFieldOfView.x = Mathf.Clamp(
                _shotFieldOfView.x,
                10f,
                100f);
            _shotFieldOfView.y = Mathf.Clamp(
                _shotFieldOfView.y,
                10f,
                100f);
            _shotFieldOfView.z = Mathf.Clamp(
                _shotFieldOfView.z,
                10f,
                100f);
        }

        private static void SetLayerRecursively(
            GameObject root,
            int layer)
        {
            if (root == null || layer < 0)
            {
                return;
            }

            foreach (var child in
                     root.GetComponentsInChildren<
                         Transform>(true))
            {
                child.gameObject.layer = layer;
            }
        }

        private static Dictionary<string, int>
            BuildMuscleIndices()
        {
            var indices =
                new Dictionary<string, int>(
                    StringComparer.Ordinal);
            for (var index = 0;
                 index < HumanTrait.MuscleName.Length;
                 index++)
            {
                indices[HumanTrait.MuscleName[index]] =
                    index;
            }

            return indices;
        }

        private readonly struct RendererState
        {
            public RendererState(
                Renderer renderer,
                bool wasEnabled)
            {
                Renderer = renderer;
                WasEnabled = wasEnabled;
            }

            public Renderer Renderer { get; }
            public bool WasEnabled { get; }
        }

        private static void EnsureMuscleIndices()
        {
            _muscleIndices ??= BuildMuscleIndices();
        }
    }
}
