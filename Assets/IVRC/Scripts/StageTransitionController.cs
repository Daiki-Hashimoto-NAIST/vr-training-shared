using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using IVRC.Desktop;

namespace IVRC
{
    public enum StageTransitionDestination
    {
        Stage,
        Training,
        Judges
    }

    public interface IStageTransitionDriver
    {
        bool IsTransitioning { get; }
        bool HasCompleted { get; }
        bool LastTransitionSucceeded { get; }
        bool IsAtStage { get; }
        bool HasRelocated { get; }
        float TotalDuration { get; }

        bool BeginTransition(
            StageTransitionDestination destination);
        bool BeginSafeRecoveryToTraining();
        void CancelAtCurrentLocation();
        void RestoreTrainingPose();
    }

    /// <summary>
    /// Supplies the live HMD tracking state at the exact frame a black-screen
    /// relocation is about to use the eye pose. The interface keeps the safety
    /// gate deterministic in EditMode tests without fabricating an XR device.
    /// </summary>
    public interface IHeadTrackingValiditySource
    {
        bool IsHeadTrackingValid();
    }

    internal sealed class UnityXrHeadTrackingValiditySource :
        IHeadTrackingValiditySource
    {
        public bool IsHeadTrackingValid()
        {
            var device = InputDevices.GetDeviceAtXRNode(
                XRNode.Head);
            if (!device.isValid)
            {
                return false;
            }

            if (device.TryGetFeatureValue(
                    CommonUsages.isTracked,
                    out var isTracked) &&
                !isTracked)
            {
                return false;
            }

            if (device.TryGetFeatureValue(
                    CommonUsages.trackingState,
                    out var trackingState))
            {
                const InputTrackingState required =
                    InputTrackingState.Position |
                    InputTrackingState.Rotation;
                return (trackingState & required) == required;
            }

            return true;
        }
    }

    /// <summary>
    /// Fades to black and aligns the current HMD pose to a fixed destination.
    /// Only OVRCameraRig.TrackingSpace moves; tracked avatar roots stay at world identity.
    /// </summary>
    public sealed class StageTransitionController :
        MonoBehaviour,
        IStageTransitionDriver
    {
        [Header("Play Space")]
        [SerializeField] private OVRCameraRig _cameraRig;
        [SerializeField] private DesktopSimulationManager
            _desktopSimulation;
        [SerializeField] private Transform _trainingArrivalPoint;
        [SerializeField] private Transform _stageArrivalPoint;
        [SerializeField] private Transform _judgeArrivalPoint;

        [Header("Fade")]
        [SerializeField] private OVRScreenFade _screenFade;
        [SerializeField, Min(0.01f)] private float _fadeOutSeconds = 0.4f;
        [SerializeField, Min(0f)] private float _blackHoldSeconds = 0.15f;
        [SerializeField, Min(0.01f)] private float _fadeInSeconds = 0.6f;

        private Vector3 _fallbackTrainingPosition;
        private Quaternion _fallbackTrainingRotation;
        private bool _hasFallbackTrainingPose;
        private Coroutine _transitionRoutine;
        private bool _fadeFailed;
        private IHeadTrackingValiditySource
            _headTrackingValiditySource;

        public bool IsTransitioning { get; private set; }
        public bool HasCompleted { get; private set; }
        public bool LastTransitionSucceeded { get; private set; }
        public bool IsAtStage { get; private set; }
        public bool HasRelocated { get; private set; }
        public float Progress { get; private set; }
        public StageTransitionDestination Destination { get; private set; }
        public Transform StageArrivalPoint => _stageArrivalPoint;
        public Transform JudgeArrivalPoint => _judgeArrivalPoint;
        public float TotalDuration =>
            Mathf.Max(0.01f, _fadeOutSeconds) +
            Mathf.Max(0f, _blackHoldSeconds) +
            Mathf.Max(0.01f, _fadeInSeconds);

        private void Awake()
        {
            ResolveReferences();
            ValidateSettings();
            SetFade(0f);
        }

        private void OnValidate()
        {
            ResolveReferences();
            ValidateSettings();
        }

        [ContextMenu("Begin Stage Transition")]
        public void BeginTransitionToStage()
        {
            BeginTransition(StageTransitionDestination.Stage);
        }

        public bool BeginTransition(StageTransitionDestination destination)
        {
            if (IsTransitioning || !isActiveAndEnabled)
            {
                return false;
            }

            ResolveReferences();
            if (!CanBeginTransition(destination))
            {
                return false;
            }

            if (destination != StageTransitionDestination.Training &&
                !IsAtStage)
            {
                CaptureFallbackTrainingPose();
            }

            Destination = destination;
            HasCompleted = false;
            LastTransitionSucceeded = false;
            HasRelocated = false;
            Progress = 0f;
            _fadeFailed = false;
            _transitionRoutine = StartCoroutine(
                TransitionRoutine(0f));
            return true;
        }

        /// <summary>
        /// Cancels any in-flight transition, fades from the current screen
        /// opacity to black, and then returns to training. This is intended
        /// for timeout and competing-transition recovery, where an immediate
        /// TrackingSpace move could otherwise be visible.
        /// </summary>
        public bool BeginSafeRecoveryToTraining()
        {
            if (!isActiveAndEnabled)
            {
                return false;
            }

            ResolveReferences();
            if (!CanBeginTransition(
                    StageTransitionDestination.Training))
            {
                return false;
            }

            var startingFadeAlpha = Mathf.Clamp01(
                _screenFade.currentAlpha);
            CancelTransition();
            Destination = StageTransitionDestination.Training;
            HasCompleted = false;
            LastTransitionSucceeded = false;
            HasRelocated = false;
            Progress = 0f;
            _fadeFailed = false;
            _transitionRoutine = StartCoroutine(
                TransitionRoutine(startingFadeAlpha));
            return true;
        }

        /// <summary>
        /// Stops transition work without moving TrackingSpace. Used only when
        /// even the guarded recovery path cannot complete. The current
        /// location is preserved so revealing the view cannot expose a
        /// teleport.
        /// </summary>
        public void CancelAtCurrentLocation()
        {
            CancelTransition();
            SetFade(0f);
            IsTransitioning = false;
            HasCompleted = true;
            LastTransitionSucceeded = false;
            HasRelocated = false;
            Progress = 0f;
            _fadeFailed = false;
        }

        [ContextMenu("Move To Stage Immediately")]
        public bool MoveToStageImmediately()
        {
            ResolveReferences();
            if (!IsAtStage)
            {
                CaptureFallbackTrainingPose();
            }

            Destination = StageTransitionDestination.Stage;
            HasRelocated = false;
            LastTransitionSucceeded = TryAlignTrackingSpace(
                _stageArrivalPoint,
                out var yawCorrection,
                out var eyeBefore,
                out var eyeAfter,
                true);
            if (LastTransitionSucceeded)
            {
                IsAtStage = true;
                HasRelocated = true;
            }

            LogAlignmentResult(
                LastTransitionSucceeded,
                eyeBefore,
                eyeAfter,
                yawCorrection,
                "stage");
            return LastTransitionSucceeded;
        }

        [ContextMenu("Restore Training Pose")]
        public void RestoreTrainingPose()
        {
            CancelTransition();
            ResolveReferences();

            var restored = TryRestoreTrainingLocation(
                out var yawCorrection,
                out var eyeBefore,
                out var eyeAfter);

            if (restored)
            {
                LogAlignmentResult(
                    true,
                    eyeBefore,
                    eyeAfter,
                    yawCorrection,
                    "training");
            }

            SetFade(0f);
            IsTransitioning = false;
            HasCompleted = false;
            LastTransitionSucceeded = false;
            HasRelocated = false;
            Progress = 0f;
            _fadeFailed = false;
            Destination = StageTransitionDestination.Training;
        }

        private IEnumerator TransitionRoutine(
            float startingFadeAlpha)
        {
            IsTransitioning = true;

            var fadeOutDuration = Mathf.Max(
                0.01f,
                _fadeOutSeconds *
                (1f - Mathf.Clamp01(startingFadeAlpha)));
            yield return AnimateFade(
                startingFadeAlpha,
                1f,
                fadeOutDuration,
                0f,
                _fadeOutSeconds / TotalDuration);
            if (_fadeFailed || !IsFadeReady())
            {
                AbortTransition(
                    "Screen fade became unavailable before relocation.");
                yield break;
            }

            var target = GetArrivalPoint(Destination);
            var requestedAlignmentSucceeded = TryAlignTrackingSpace(
                target,
                out var yawCorrection,
                out var eyeBefore,
                out var eyeAfter,
                false);

            var recoveredToTraining = false;
            if (requestedAlignmentSucceeded)
            {
                LastTransitionSucceeded = true;
                IsAtStage =
                    Destination != StageTransitionDestination.Training;
            }
            else if (Destination == StageTransitionDestination.Training)
            {
                LastTransitionSucceeded = TryApplyFallbackTrainingPose(
                    out eyeBefore,
                    out eyeAfter);
                if (LastTransitionSucceeded)
                {
                    IsAtStage = false;
                }
            }
            else
            {
                LastTransitionSucceeded = false;
                recoveredToTraining = TryRestoreTrainingLocation(
                    out _,
                    out _,
                    out _);
            }
            HasRelocated =
                requestedAlignmentSucceeded ||
                recoveredToTraining ||
                (Destination == StageTransitionDestination.Training &&
                 LastTransitionSucceeded);

            // A full Update/LateUpdate pass lets body tracking observe the new
            // TrackingSpace transform before either eye renders again.
            yield return null;
            if (!IsFadeReady())
            {
                _fadeFailed = true;
                AbortTransition(
                    "Screen fade became unavailable immediately after relocation.");
                yield break;
            }

            var holdStartProgress = _fadeOutSeconds / TotalDuration;
            var holdEndProgress =
                (_fadeOutSeconds + _blackHoldSeconds) / TotalDuration;
            yield return HoldBlack(
                Mathf.Max(0f, _blackHoldSeconds),
                holdStartProgress,
                holdEndProgress);
            if (_fadeFailed)
            {
                AbortTransition(
                    "Screen fade became unavailable while fully black.");
                yield break;
            }

            yield return AnimateFade(
                1f,
                0f,
                Mathf.Max(0.01f, _fadeInSeconds),
                holdEndProgress,
                1f);
            if (_fadeFailed)
            {
                AbortTransition(
                    "Screen fade became unavailable during fade-in.");
                yield break;
            }

            SetFade(0f);
            Progress = 1f;
            IsTransitioning = false;
            HasCompleted = true;
            _transitionRoutine = null;

            var destinationName =
                GetDestinationName(Destination);
            LogAlignmentResult(
                LastTransitionSucceeded,
                eyeBefore,
                eyeAfter,
                yawCorrection,
                destinationName);
            if (!requestedAlignmentSucceeded &&
                recoveredToTraining)
            {
                Debug.LogWarning(
                    $"[StageTransition] {destinationName} alignment " +
                    "failed, so the " +
                    "play space was recovered to training while still black.",
                    this);
            }
        }

        private IEnumerator AnimateFade(
            float from,
            float to,
            float duration,
            float progressFrom,
            float progressTo)
        {
            var elapsed = 0f;
            while (elapsed < duration)
            {
                if (!IsFadeReady())
                {
                    _fadeFailed = true;
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                SetFade(Mathf.Lerp(from, to, t));
                Progress = Mathf.Lerp(progressFrom, progressTo, t);
                yield return null;
            }

            if (!IsFadeReady())
            {
                _fadeFailed = true;
                yield break;
            }

            SetFade(to);
            Progress = progressTo;
        }

        private IEnumerator HoldBlack(
            float duration,
            float progressFrom,
            float progressTo)
        {
            if (duration <= Mathf.Epsilon)
            {
                if (!IsFadeReady())
                {
                    _fadeFailed = true;
                    yield break;
                }

                Progress = progressTo;
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < duration)
            {
                if (!IsFadeReady())
                {
                    _fadeFailed = true;
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                SetFade(1f);
                Progress = Mathf.Lerp(progressFrom, progressTo, t);
                yield return null;
            }

            if (!IsFadeReady())
            {
                _fadeFailed = true;
                yield break;
            }

            Progress = progressTo;
        }

        private bool TryAlignTrackingSpace(
            Transform arrivalPoint,
            out float yawCorrection,
            out Vector3 eyeBefore,
            out Vector3 eyeAfter,
            bool logErrors)
        {
            yawCorrection = 0f;
            eyeBefore = Vector3.zero;
            eyeAfter = Vector3.zero;

            if (_cameraRig == null ||
                _cameraRig.trackingSpace == null ||
                _cameraRig.centerEyeAnchor == null)
            {
                if (logErrors)
                {
                    Debug.LogError(
                        "[StageTransition] Camera Rig or tracking anchors are missing.",
                        this);
                }

                return false;
            }

            if (arrivalPoint == null)
            {
                if (logErrors)
                {
                    Debug.LogError(
                        "[StageTransition] The destination arrival point is not assigned.",
                        this);
                }

                return false;
            }

            if (!IsHeadTrackingValid())
            {
                if (logErrors)
                {
                    Debug.LogError(
                        "[StageTransition] HMD position and rotation " +
                        "tracking are not valid. Relocation was refused.",
                        this);
                }

                return false;
            }

            var trackingSpace = _cameraRig.trackingSpace;
            var centerEye = _cameraRig.centerEyeAnchor;
            var currentForward = Vector3.ProjectOnPlane(
                centerEye.forward,
                Vector3.up);
            var targetForward = Vector3.ProjectOnPlane(
                arrivalPoint.forward,
                Vector3.up);
            if (currentForward.sqrMagnitude < 0.0001f ||
                targetForward.sqrMagnitude < 0.0001f)
            {
                if (logErrors)
                {
                    Debug.LogError(
                        "[StageTransition] A horizontal facing direction " +
                        "could not be calculated.",
                        this);
                }

                return false;
            }

            currentForward.Normalize();
            targetForward.Normalize();
            eyeBefore = centerEye.position;
            yawCorrection = Vector3.SignedAngle(
                currentForward,
                targetForward,
                Vector3.up);

            var yawRotation = Quaternion.AngleAxis(
                yawCorrection,
                Vector3.up);
            var desiredEyePosition = new Vector3(
                arrivalPoint.position.x,
                eyeBefore.y,
                arrivalPoint.position.z);
            var newTrackingPosition =
                desiredEyePosition +
                yawRotation * (trackingSpace.position - eyeBefore);
            var newTrackingRotation =
                yawRotation * trackingSpace.rotation;

            trackingSpace.SetPositionAndRotation(
                newTrackingPosition,
                newTrackingRotation);
            eyeAfter = centerEye.position;
            return true;
        }

        private bool TryRestoreTrainingLocation(
            out float yawCorrection,
            out Vector3 eyeBefore,
            out Vector3 eyeAfter)
        {
            var restored = TryAlignTrackingSpace(
                _trainingArrivalPoint,
                out yawCorrection,
                out eyeBefore,
                out eyeAfter,
                false);
            if (!restored)
            {
                restored = TryApplyFallbackTrainingPose(
                    out eyeBefore,
                    out eyeAfter);
                yawCorrection = 0f;
            }

            if (restored)
            {
                IsAtStage = false;
            }

            return restored;
        }

        private bool TryApplyFallbackTrainingPose(
            out Vector3 eyeBefore,
            out Vector3 eyeAfter)
        {
            eyeBefore = Vector3.zero;
            eyeAfter = Vector3.zero;
            if (!_hasFallbackTrainingPose ||
                _cameraRig == null ||
                _cameraRig.trackingSpace == null)
            {
                return false;
            }

            if (_cameraRig.centerEyeAnchor != null)
            {
                eyeBefore = _cameraRig.centerEyeAnchor.position;
            }

            _cameraRig.trackingSpace.SetPositionAndRotation(
                _fallbackTrainingPosition,
                _fallbackTrainingRotation);
            if (_cameraRig.centerEyeAnchor != null)
            {
                eyeAfter = _cameraRig.centerEyeAnchor.position;
            }

            return true;
        }

        private void CaptureFallbackTrainingPose()
        {
            if (_cameraRig == null ||
                _cameraRig.trackingSpace == null)
            {
                return;
            }

            _fallbackTrainingPosition = _cameraRig.trackingSpace.position;
            _fallbackTrainingRotation = _cameraRig.trackingSpace.rotation;
            _hasFallbackTrainingPose = true;
        }

        private bool CanBeginTransition(
            StageTransitionDestination destination)
        {
            if (_cameraRig == null ||
                _cameraRig.trackingSpace == null ||
                _cameraRig.centerEyeAnchor == null)
            {
                Debug.LogError(
                    "[StageTransition] Camera Rig or tracking anchors are missing.",
                    this);
                return false;
            }

            var target = GetArrivalPoint(destination);
            var hasFallbackDestination =
                destination == StageTransitionDestination.Training &&
                _hasFallbackTrainingPose;
            if (target == null && !hasFallbackDestination)
            {
                Debug.LogError(
                    "[StageTransition] The requested arrival point is missing.",
                    this);
                return false;
            }

            if (!IsFadeReady())
            {
                Debug.LogError(
                    "[StageTransition] OVRScreenFade is missing or disabled. " +
                    "Relocation was refused to prevent a visible teleport.",
                    this);
                return false;
            }

            return true;
        }

        private Transform GetArrivalPoint(
            StageTransitionDestination destination)
        {
            return destination switch
            {
                StageTransitionDestination.Stage =>
                    _stageArrivalPoint,
                StageTransitionDestination.Training =>
                    _trainingArrivalPoint,
                StageTransitionDestination.Judges =>
                    _judgeArrivalPoint,
                _ => null
            };
        }

        private static string GetDestinationName(
            StageTransitionDestination destination)
        {
            return destination switch
            {
                StageTransitionDestination.Stage => "stage",
                StageTransitionDestination.Training => "training",
                StageTransitionDestination.Judges => "judges",
                _ => destination.ToString().ToLowerInvariant()
            };
        }

        private bool IsFadeReady()
        {
            return _screenFade != null &&
                   _screenFade.isActiveAndEnabled;
        }

        private void AbortTransition(string reason)
        {
            SetFade(0f);
            Progress = 0f;
            IsTransitioning = false;
            HasCompleted = true;
            LastTransitionSucceeded = false;
            _transitionRoutine = null;
            Debug.LogError($"[StageTransition] {reason}", this);
        }

        private void CancelTransition()
        {
            if (_transitionRoutine != null)
            {
                StopCoroutine(_transitionRoutine);
                _transitionRoutine = null;
            }
        }

        private void SetFade(float alpha)
        {
            if (_screenFade != null)
            {
                _screenFade.SetExplicitFade(Mathf.Clamp01(alpha));
            }
        }

        private void ResolveReferences()
        {
            _headTrackingValiditySource ??=
                new UnityXrHeadTrackingValiditySource();
            _cameraRig ??= FindFirstObjectByType<OVRCameraRig>();
            _desktopSimulation ??=
                FindFirstObjectByType<DesktopSimulationManager>(
                    FindObjectsInactive.Include);
            if (_screenFade == null &&
                _cameraRig != null &&
                _cameraRig.centerEyeAnchor != null)
            {
                _screenFade =
                    _cameraRig.centerEyeAnchor.GetComponent<OVRScreenFade>();
            }
        }

        private bool IsHeadTrackingValid()
        {
            // Desktop simulation has no XR head device by design. Its camera
            // pose is authored by the desktop subsystem, so it is safe to use
            // only after that subsystem has explicitly resolved as active.
            if (_desktopSimulation != null &&
                _desktopSimulation.IsDesktopActive)
            {
                return true;
            }

            _headTrackingValiditySource ??=
                new UnityXrHeadTrackingValiditySource();
            return _headTrackingValiditySource
                .IsHeadTrackingValid();
        }

        private void ValidateSettings()
        {
            _fadeOutSeconds = Mathf.Max(0.01f, _fadeOutSeconds);
            _blackHoldSeconds = Mathf.Max(0f, _blackHoldSeconds);
            _fadeInSeconds = Mathf.Max(0.01f, _fadeInSeconds);
        }

        private void LogAlignmentResult(
            bool succeeded,
            Vector3 eyeBefore,
            Vector3 eyeAfter,
            float yawCorrection,
            string destinationName)
        {
            if (succeeded)
            {
                Debug.Log(
                    $"[StageTransition] Arrived at {destinationName}. " +
                    $"Eye XZ ({eyeBefore.x:F2}, {eyeBefore.z:F2}) -> " +
                    $"({eyeAfter.x:F2}, {eyeAfter.z:F2}), " +
                    $"yaw correction {yawCorrection:F1} deg.",
                    this);
            }
            else
            {
                Debug.LogError(
                    $"[StageTransition] Fade completed, but the play space " +
                    $"could not be aligned to {destinationName}.",
                    this);
            }
        }

        private void OnDisable()
        {
            var shouldRestoreTraining =
                IsTransitioning;
            CancelTransition();
            if (shouldRestoreTraining)
            {
                TryRestoreTrainingLocation(
                    out _,
                    out _,
                    out _);
            }

            SetFade(0f);
            IsTransitioning = false;
            HasCompleted = false;
            LastTransitionSucceeded = false;
            HasRelocated = false;
            Progress = 0f;
            _fadeFailed = false;
        }
    }
}
