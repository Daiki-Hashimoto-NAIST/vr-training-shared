using Meta.XR.Movement.Retargeting;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// The four authored exercise poses that belong to one avatar rig.
    /// Keeping them on the avatar definition prevents a pose captured from a
    /// different rig from being applied by mistake.
    /// </summary>
    [System.Serializable]
    public sealed class AvatarExercisePoseSet
    {
        [SerializeField] private PoseAsset _armCurlDown;
        [SerializeField] private PoseAsset _armCurlUp;
        [SerializeField] private PoseAsset _sideRaiseDown;
        [SerializeField] private PoseAsset _sideRaiseUp;

        public PoseAsset GetPose(
            BodyRepKind kind,
            RepTimingPoint timingPoint)
        {
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
    }

    /// <summary>
    /// Describes one complete tracked-avatar presentation: the live body, its
    /// mirror, and the BlendShape output used by the muscle-growth system.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AvatarDefinition : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string _displayName;

        [Header("Tracked Avatar")]
        [SerializeField] private Animator _animator;
        [SerializeField] private CharacterRetargeter _retargeter;
        [SerializeField] private SkinnedMeshRenderer
            _trackedBodyRenderer;

        [Header("Mirror")]
        [SerializeField] private GameObject _mirrorInstance;
        [SerializeField] private SkinnedMeshRenderer
            _mirrorBodyRenderer;

        [Header("Muscle Growth")]
        [SerializeField] private BlendShapeMuscleGrowthOutput
            _growthOutput;

        [Header("Cinematic Presentation")]
        [SerializeField] private GameObject _cinematicPrefab;

        [Header("Exercise Poses")]
        [SerializeField] private AvatarExercisePoseSet _exercisePoses =
            new AvatarExercisePoseSet();

        [Header("Posing Profiles")]
        [Tooltip(
            "Reference poses used while this avatar is active. Leave empty " +
            "to keep using the scorer's shared Profiles list.")]
        [SerializeField] private PosingScoreProfile[] _posingProfiles;

        public string DisplayName =>
            string.IsNullOrWhiteSpace(_displayName)
                ? name
                : _displayName;
        public Animator Animator => _animator;
        public CharacterRetargeter Retargeter => _retargeter;
        public SkinnedMeshRenderer TrackedBodyRenderer =>
            _trackedBodyRenderer;
        public GameObject MirrorInstance => _mirrorInstance;
        public SkinnedMeshRenderer MirrorBodyRenderer =>
            _mirrorBodyRenderer;
        public BlendShapeMuscleGrowthOutput GrowthOutput =>
            _growthOutput;
        public GameObject CinematicPrefab =>
            _cinematicPrefab;
        public AvatarExercisePoseSet ExercisePoses =>
            _exercisePoses;
        public PosingScoreProfile[] PosingProfiles =>
            _posingProfiles;
        public bool IsActive =>
            gameObject.activeSelf &&
            (_mirrorInstance == null ||
             _mirrorInstance.activeSelf);

        public PoseAsset GetExercisePose(
            BodyRepKind kind,
            RepTimingPoint timingPoint)
        {
            return _exercisePoses?.GetPose(kind, timingPoint);
        }

        /// <summary>
        /// Activates or deactivates every visual belonging to this avatar.
        /// The growth output is switched in the same transaction so an inactive
        /// mesh can never keep receiving values intended for another avatar.
        /// </summary>
        public void SetActive(bool active)
        {
            if (!active)
            {
                if (_growthOutput != null)
                {
                    _growthOutput.ResetGrowth();
                    _growthOutput.enabled = false;
                }

                if (_mirrorInstance != null)
                {
                    _mirrorInstance.SetActive(false);
                }

                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            if (_mirrorInstance != null)
            {
                _mirrorInstance.SetActive(true);
            }

            if (_growthOutput != null)
            {
                _growthOutput.enabled = true;
                _growthOutput.RefreshBindings();
            }
        }

        private void Reset()
        {
            AutoWire();
        }

        [ContextMenu("Auto-wire from hierarchy")]
        public void AutoWire()
        {
            _animator ??= GetComponent<Animator>();
            _retargeter ??= GetComponent<CharacterRetargeter>();

            if (_trackedBodyRenderer == null)
            {
                _trackedBodyRenderer =
                    FindRendererWithMostBlendShapes(gameObject);
            }
        }

        [ContextMenu("Validate Avatar Definition")]
        public void ValidateAndLog()
        {
            if (Validate(out var error))
            {
                Debug.Log(
                    $"[AvatarDefinition] '{DisplayName}' is ready.",
                    this);
                return;
            }

            Debug.LogWarning(
                $"[AvatarDefinition] '{DisplayName}' is not ready: " +
                error,
                this);
        }

        public bool Validate(out string error)
        {
            if (_animator == null ||
                _animator.avatar == null ||
                !_animator.isHuman ||
                !_animator.avatar.isValid)
            {
                error =
                    "Animator is not assigned to a valid Humanoid.";
                return false;
            }

            if (_retargeter == null)
            {
                error = "CharacterRetargeter is not assigned.";
                return false;
            }

            if (_retargeter.GetComponent<ISourceDataProvider>() ==
                null)
            {
                error =
                    "The retargeter has no ISourceDataProvider.";
                return false;
            }

            if (_mirrorInstance == null)
            {
                error = "Mirror instance is not assigned.";
                return false;
            }

            if (_trackedBodyRenderer == null ||
                _mirrorBodyRenderer == null)
            {
                error =
                    "Tracked and mirror body renderers are required.";
                return false;
            }

            if (_growthOutput == null)
            {
                error =
                    "BlendShapeMuscleGrowthOutput is not assigned.";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Validates the clean model used by non-tracked cinematics. This is
        /// intentionally separate from Validate so a missing presentation
        /// asset can never block the tracked avatar itself.
        /// </summary>
        public bool ValidateCinematicPrefab(out string error)
        {
            if (_cinematicPrefab == null)
            {
                error = "Cinematic prefab is not assigned.";
                return false;
            }

            var animator =
                _cinematicPrefab.GetComponentInChildren<
                    Animator>(true);
            if (animator == null ||
                animator.avatar == null ||
                !animator.isHuman ||
                !animator.avatar.isValid)
            {
                error =
                    "Cinematic prefab requires a valid Humanoid Animator.";
                return false;
            }

            if (_cinematicPrefab.GetComponentInChildren<
                    CharacterRetargeter>(true) != null ||
                _cinematicPrefab.GetComponentInChildren<
                    MetaSourceDataProvider>(true) != null)
            {
                error =
                    "Cinematic prefab must not contain Meta body tracking.";
                return false;
            }

            var renderers =
                _cinematicPrefab.GetComponentsInChildren<
                    SkinnedMeshRenderer>(true);
            if (!MuscleBlendShapeStateSynchronizer
                    .TryValidateTargets(
                        _growthOutput,
                        _trackedBodyRenderer,
                        renderers,
                        out var muscleError))
            {
                error =
                    $"Cinematic prefab muscle setup is incomplete. " +
                    muscleError;
                return false;
            }

            error = null;
            return true;
        }

        private static SkinnedMeshRenderer
            FindRendererWithMostBlendShapes(GameObject root)
        {
            SkinnedMeshRenderer best = null;
            var bestCount = -1;
            foreach (var renderer in
                     root.GetComponentsInChildren<
                         SkinnedMeshRenderer>(true))
            {
                var count = renderer.sharedMesh == null
                    ? 0
                    : renderer.sharedMesh.blendShapeCount;
                if (count <= bestCount)
                {
                    continue;
                }

                best = renderer;
                bestCount = count;
            }

            return best;
        }
    }
}
