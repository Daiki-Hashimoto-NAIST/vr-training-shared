using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

namespace IVRC
{
    /// <summary>
    /// Plays short, positionally distinct audience calls during the posing
    /// phase. It listens to the existing phase and pose-completion signals;
    /// it does not own or advance the posing routine.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudienceCallController : MonoBehaviour
    {
        [Serializable]
        public sealed class AudienceCall
        {
            [TextArea(1, 2)]
            public string Text = "キレてる！";
            public AudioClip AudioClip;
            [Range(0f, 1f)] public float Volume = 0.75f;
        }

        private sealed class ActiveCall
        {
            public GameObject Root;
            public Transform CallPoint;
            public Transform Bubble;
            public AudioSource Source;
            public float StartedAt;
            public float ExpectedEndAt;
            public bool ObservedPlaying;
        }

        [Header("Flow")]
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private ThreePointPosingScorer _posingScorer;
        [Tooltip(
            "The VR viewer used to face each World Space speech bubble. " +
            "When empty, the active main camera is used.")]
        [SerializeField] private Transform _viewer;

        [Header("Call Data")]
        [Tooltip(
            "Text and its matching voice clip. Entries without both are " +
            "ignored at runtime.")]
        [SerializeField] private List<AudienceCall> _calls =
            new List<AudienceCall>
            {
                new AudienceCall { Text = "キレてる！" },
                new AudienceCall { Text = "仕上がってるよ！" },
                new AudienceCall { Text = "ナイスバルク！" }
            };
        [Tooltip("Possible origins for audience calls around the seating.")]
        [SerializeField] private List<Transform> _callPoints =
            new List<Transform>();

        [Header("Normal Calls")]
        [SerializeField, Min(0.1f)] private float _minInterval = 3f;
        [SerializeField, Min(0.1f)] private float _maxInterval = 7f;
        [Tooltip("Chance that a normal call is followed by a second caller.")]
        [SerializeField, Range(0f, 1f)] private float _doubleCallChance =
            0.16f;
        [SerializeField, Min(0f)] private float _doubleCallMinDelay = 0.1f;
        [SerializeField, Min(0f)] private float _doubleCallMaxDelay = 0.5f;

        [Header("Pose Success Burst")]
        [SerializeField, Min(1)] private int _successMinCalls = 2;
        [SerializeField, Min(1)] private int _successMaxCalls = 4;
        [SerializeField, Min(0f)] private float _successMinDelay = 0f;
        [SerializeField, Min(0f)] private float _successMaxDelay = 1.5f;

        [Header("3D Audio")]
        [SerializeField, Range(0f, 1f)] private float _spatialBlend = 1f;
        [SerializeField, Min(0.1f)] private float _minDistance = 3f;
        [SerializeField, Min(0.1f)] private float _maxDistance = 26f;
        [SerializeField, Range(0f, 5f)] private float _dopplerLevel = 0f;

        [Header("Speech Bubble")]
        [Tooltip(
            "The shared World Space Canvas prefab used by every audience " +
            "call. Its WordBubble child owns the layout and 9-slice image.")]
        [SerializeField] private AudienceSpeechBubbleView _bubblePrefab;
        [Tooltip(
            "Optional 9-sliced bubble sprites. A call chooses one at random; " +
            "when empty, the prefab's assigned sprite is kept.")]
        [SerializeField] private List<Sprite> _bubbleSprites =
            new List<Sprite>();
        [SerializeField] private TMP_FontAsset _bubbleFont;
        [SerializeField, Min(0f)] private float _bubbleHeight = 1.5f;
        [SerializeField, Min(0.0001f)] private float _bubbleScale = 0.002f;
        [SerializeField, Min(1f)] private float _bubbleFontSize = 42f;
        [SerializeField] private Color _bubbleColor =
            new Color(1f, 0.75f, 0.18f, 0.98f);[SerializeField] private Color _textColor = Color.white;

        private readonly List<ActiveCall> _activeCalls =
            new List<ActiveCall>();
        private bool _isCallPhase;
        private bool _isPosing;
        private float _nextNormalCallAt;
        private int _scheduleVersion;

        private void Awake()
        {
            ResolveReferences();
            ValidateSettings();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ValidateSettings();
            Subscribe();
            HandlePhaseChanged(
                _gameFlow == null
                    ? ExperiencePhase.Setup
                    : _gameFlow.CurrentPhase);
        }

        private void OnDisable()
        {
            Unsubscribe();
            StopCallsImmediate();
        }

        private void OnValidate()
        {
            ResolveReferences();
            ValidateSettings();
        }

        private void Update()
        {
            UpdateActiveCalls();
            FaceBubblesToViewer();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // This shortcut deliberately uses the same one-call path as a
            // normal audience reaction, but is available only for debug
            // sessions and only while posing or results is active.
            var keyboard = Keyboard.current;
            if (_isCallPhase && keyboard != null &&
                keyboard.cKey.wasPressedThisFrame)
            {
                EmitRandomCall();
            }
#endif

            if (!_isCallPhase || Time.unscaledTime < _nextNormalCallAt)
            {
                return;
            }

            TriggerNormalCall();
            ScheduleNextNormalCall();
        }

        private void HandlePhaseChanged(ExperiencePhase phase)
        {
            _isPosing = phase == ExperiencePhase.Posing;
            if (phase == ExperiencePhase.Posing ||
                phase == ExperiencePhase.Results)
            {
                BeginCallPhase();
                return;
            }

            StopCallsImmediate();
        }

        private void HandlePoseCompleted(PoseCompletionResult result)
        {
            if (!_isPosing || !result.Succeeded)
            {
                return;
            }

            // Do not let a delayed second normal caller overlap the
            // deliberately denser reaction to this success.
            _scheduleVersion++;
            TriggerSuccessBurst();
            ScheduleNextNormalCall();
        }

        private void BeginCallPhase()
        {
            if (_isCallPhase)
            {
                return;
            }

            _isCallPhase = true;
            ScheduleNextNormalCall();
        }

        private void TriggerNormalCall()
        {
            EmitRandomCall();
            if (UnityEngine.Random.value < _doubleCallChance)
            {
                StartCoroutine(
                    EmitAfterDelay(
                        RandomRange(
                            _doubleCallMinDelay,
                            _doubleCallMaxDelay),
                        _scheduleVersion));
            }
        }

        private void TriggerSuccessBurst()
        {
            var count = UnityEngine.Random.Range(
                _successMinCalls,
                _successMaxCalls + 1);
            var delays = new List<float>(count);
            for (var i = 0; i < count; i++)
            {
                delays.Add(
                    RandomRange(_successMinDelay, _successMaxDelay));
            }

            // Sorting keeps a burst feeling like several people reacting in
            // sequence while ensuring every requested delay stays in range.
            delays.Sort();
            foreach (var delay in delays)
            {
                StartCoroutine(EmitAfterDelay(delay, _scheduleVersion));
            }
        }

        private IEnumerator EmitAfterDelay(float delay, int scheduleVersion)
        {
            if (delay > 0f)
            {
                yield return new WaitForSecondsRealtime(delay);
            }

            if (_isCallPhase && scheduleVersion == _scheduleVersion)
            {
                EmitRandomCall();
            }
        }

        private void EmitRandomCall()
        {
            if (!TryGetRandomCall(out var call) ||
                !TryGetRandomCallPoint(out var callPoint))
            {
                return;
            }

            var root = new GameObject("Audience Call");
            root.transform.SetParent(transform, false);
            root.transform.SetPositionAndRotation(
                callPoint.position,
                Quaternion.identity);

            var source = root.AddComponent<AudioSource>();
            source.clip = call.AudioClip;
            source.volume = call.Volume;
            source.spatialBlend = _spatialBlend;
            source.spatialize = true;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = _minDistance;
            source.maxDistance = _maxDistance;
            source.dopplerLevel = _dopplerLevel;
            source.loop = false;
            source.Play();

            var bubble = CreateBubble(root.transform, call.Text);
            var duration = Mathf.Max(0.1f, call.AudioClip.length);
            _activeCalls.Add(
                new ActiveCall
                {
                    Root = root,
                    CallPoint = callPoint,
                    Bubble = bubble,
                    Source = source,
                    StartedAt = Time.unscaledTime,
                    ExpectedEndAt = Time.unscaledTime + duration + 0.5f
                });
        }

        private Transform CreateBubble(Transform callRoot, string text)
        {
            if (_bubblePrefab == null)
            {
                Debug.LogWarning(
                    "[AudienceCall] No speech-bubble prefab is assigned.",
                    this);
                return null;
            }

            var bubble = Instantiate(_bubblePrefab, callRoot);
            bubble.name = "Canvas";
            var bubbleTransform = bubble.transform;
            bubbleTransform.localPosition =
                Vector3.up * _bubbleHeight;
            bubbleTransform.localScale =
                Vector3.one * _bubbleScale;
            bubble.Configure(
                text,
                _bubbleFont,
                _bubbleFontSize,
                _textColor,
                GetRandomBubbleSprite(),
                _bubbleColor,
                ResolveViewerCamera());
            return bubbleTransform;
        }

        private void UpdateActiveCalls()
        {
            for (var i = _activeCalls.Count - 1; i >= 0; i--)
            {
                var active = _activeCalls[i];
                if (active.Source != null && active.Source.isPlaying)
                {
                    active.ObservedPlaying = true;
                    continue;
                }

                if (!active.ObservedPlaying &&
                    Time.unscaledTime < active.ExpectedEndAt)
                {
                    continue;
                }

                DestroyActiveCall(i);
            }
        }

        private void FaceBubblesToViewer()
        {
            var viewer = ResolveViewer();
            if (viewer == null)
            {
                return;
            }

            foreach (var active in _activeCalls)
            {
                if (active.Bubble == null)
                {
                    continue;
                }

                var directionFromViewer =
                    active.Bubble.position - viewer.position;
                directionFromViewer.y = 0f;
                if (directionFromViewer.sqrMagnitude > 0.0001f)
                {
                    // A World Space Canvas draws its UI on local -Z, rather
                    // than Transform.forward (+Z). Pointing +Z away from the
                    // viewer therefore makes the Canvas surface face the HMD
                    // without mirroring its text or sprite.
                    active.Bubble.rotation = Quaternion.LookRotation(
                        directionFromViewer.normalized,
                        Vector3.up);
                }
            }
        }

        private void DestroyActiveCall(int index)
        {
            var active = _activeCalls[index];
            _activeCalls.RemoveAt(index);
            if (active.Root != null)
            {
                Destroy(active.Root);
            }
        }

        private void StopCallsImmediate()
        {
            _isCallPhase = false;
            _isPosing = false;
            _scheduleVersion++;
            StopAllCoroutines();
            for (var i = _activeCalls.Count - 1; i >= 0; i--)
            {
                var active = _activeCalls[i];
                if (active.Source != null)
                {
                    active.Source.Stop();
                }

                if (active.Root != null)
                {
                    Destroy(active.Root);
                }
            }

            _activeCalls.Clear();
        }

        private void ScheduleNextNormalCall()
        {
            _nextNormalCallAt = Time.unscaledTime + RandomRange(
                _minInterval,
                _maxInterval);
        }

        private bool TryGetRandomCall(out AudienceCall call)
        {
            var validCount = 0;
            foreach (var candidate in _calls)
            {
                if (IsValidCall(candidate))
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                call = null;
                return false;
            }

            var selection = UnityEngine.Random.Range(0, validCount);
            foreach (var candidate in _calls)
            {
                if (!IsValidCall(candidate))
                {
                    continue;
                }

                if (selection-- == 0)
                {
                    call = candidate;
                    return true;
                }
            }

            call = null;
            return false;
        }

        private bool TryGetRandomCallPoint(out Transform callPoint)
        {
            var validCount = 0;
            foreach (var candidate in _callPoints)
            {
                if (candidate != null && !IsCallPointInUse(candidate))
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                callPoint = null;
                return false;
            }

            var selection = UnityEngine.Random.Range(0, validCount);
            foreach (var candidate in _callPoints)
            {
                if (candidate == null || IsCallPointInUse(candidate))
                {
                    continue;
                }

                if (selection-- == 0)
                {
                    callPoint = candidate;
                    return true;
                }
            }

            callPoint = null;
            return false;
        }

        private bool IsCallPointInUse(Transform callPoint)
        {
            foreach (var active in _activeCalls)
            {
                if (active.CallPoint == callPoint)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsValidCall(AudienceCall call)
        {
            return call != null &&
                   !string.IsNullOrWhiteSpace(call.Text) &&
                   call.AudioClip != null;
        }

        private Transform ResolveViewer()
        {
            if (_viewer != null)
            {
                return _viewer;
            }

            return Camera.main == null ? null : Camera.main.transform;
        }

        private Camera ResolveViewerCamera()
        {
            var viewer = ResolveViewer();
            if (viewer == null)
            {
                return null;
            }

            var viewerCamera = viewer.GetComponent<Camera>();
            return viewerCamera == null
                ? viewer.GetComponentInChildren<Camera>(true)
                : viewerCamera;
        }

        private Sprite GetRandomBubbleSprite()
        {
            var validCount = 0;
            foreach (var sprite in _bubbleSprites)
            {
                if (sprite != null)
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                return null;
            }

            var selection = UnityEngine.Random.Range(0, validCount);
            foreach (var sprite in _bubbleSprites)
            {
                if (sprite == null)
                {
                    continue;
                }

                if (selection-- == 0)
                {
                    return sprite;
                }
            }

            return null;
        }

        private void ResolveReferences()
        {
            if (_gameFlow == null)
            {
                _gameFlow = FindFirstObjectByType<GameFlowController>();
            }

            if (_posingScorer == null)
            {
                _posingScorer = FindFirstObjectByType<
                    ThreePointPosingScorer>();
            }
        }

        private void Subscribe()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= HandlePhaseChanged;
                _gameFlow.PhaseChanged += HandlePhaseChanged;
            }

            if (_posingScorer != null)
            {
                _posingScorer.PoseCompleted -= HandlePoseCompleted;
                _posingScorer.PoseCompleted += HandlePoseCompleted;
            }
        }

        private void Unsubscribe()
        {
            if (_gameFlow != null)
            {
                _gameFlow.PhaseChanged -= HandlePhaseChanged;
            }

            if (_posingScorer != null)
            {
                _posingScorer.PoseCompleted -= HandlePoseCompleted;
            }
        }

        private void ValidateSettings()
        {
            _minInterval = Mathf.Max(0.1f, _minInterval);
            _maxInterval = Mathf.Max(_minInterval, _maxInterval);
            _doubleCallMinDelay = Mathf.Max(0f, _doubleCallMinDelay);
            _doubleCallMaxDelay = Mathf.Max(
                _doubleCallMinDelay,
                _doubleCallMaxDelay);
            _successMinCalls = Mathf.Max(1, _successMinCalls);
            _successMaxCalls = Mathf.Max(
                _successMinCalls,
                _successMaxCalls);
            _successMinDelay = Mathf.Max(0f, _successMinDelay);
            _successMaxDelay = Mathf.Max(
                _successMinDelay,
                _successMaxDelay);
            _minDistance = Mathf.Max(0.1f, _minDistance);
            _maxDistance = Mathf.Max(_minDistance, _maxDistance);
            _bubbleHeight = Mathf.Max(0f, _bubbleHeight);
            _bubbleScale = Mathf.Max(0.0001f, _bubbleScale);
            _bubbleFontSize = Mathf.Max(1f, _bubbleFontSize);
        }

        private static float RandomRange(float minimum, float maximum)
        {
            return UnityEngine.Random.Range(
                Mathf.Min(minimum, maximum),
                Mathf.Max(minimum, maximum));
        }
    }
}
