using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Connects tutorial practice steps to the rhythm guide Training already
    /// owns. The tutorial AudioSource is the clock, while pose assets, pose
    /// transitions, visual timing and the synchronized Player/Mirror ghosts
    /// remain owned by RhythmPoseGuideController.
    /// </summary>
    [DefaultExecutionOrder(10250)]
    [DisallowMultipleComponent]
    public sealed class TutorialPoseGuide : MonoBehaviour
    {
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private TutorialController _tutorial;
        [SerializeField] private TutorialMusicController _music;
        [SerializeField] private RhythmPoseGuideController _guide;
        private bool _isDriving;

        public bool IsDriving => _isDriving;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            Refresh();
        }

        private void OnDisable()
        {
            Release();
        }

        private void Update()
        {
            Refresh();
        }

        private void Refresh()
        {
            var shouldDrive =
                _gameFlow != null &&
                _gameFlow.CurrentPhase ==
                    ExperiencePhase.Tutorial &&
                _tutorial != null &&
                _tutorial.IsRunning &&
                _tutorial.CurrentStepIsPractice;
            if (!shouldDrive)
            {
                Release();
                return;
            }

            if (_music == null ||
                !_music.TryGetBeatPosition(
                    out var beatPosition) ||
                _guide == null ||
                !_guide.ShowLoopingExercise(
                    _tutorial.CurrentExercise,
                    beatPosition,
                    _music.SecondsPerBeat))
            {
                Release();
                return;
            }

            _isDriving = true;
        }

        private void Release()
        {
            if (!_isDriving)
            {
                return;
            }

            _isDriving = false;
            _guide?.ReleaseLoopingExercise();
        }

        private void ResolveReferences()
        {
            _gameFlow ??=
                FindFirstObjectByType<GameFlowController>();
            _tutorial ??=
                FindFirstObjectByType<TutorialController>(
                    FindObjectsInactive.Include);
            _music ??=
                FindFirstObjectByType<TutorialMusicController>(
                    FindObjectsInactive.Include);
            _guide ??=
                FindFirstObjectByType<RhythmPoseGuideController>(
                    FindObjectsInactive.Include);
        }
    }
}
