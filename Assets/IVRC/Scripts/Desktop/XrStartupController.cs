using System;
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace IVRC.Desktop
{
    public enum XrStartupResolution
    {
        Pending,
        Desktop,
        XrRunning,
        XrUnavailableForced
    }

    /// <summary>
    /// Owns the Standalone XR lifetime so Force Desktop can opt out before
    /// OpenXR is initialized. Android keeps XR Management's normal automatic
    /// startup and is therefore left alone by this component.
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    [DisallowMultipleComponent]
    public sealed class XrStartupController : MonoBehaviour
    {
        [Tooltip(
            "The single mode switch lives on Desktop Simulation Manager. " +
            "Force Desktop never initializes XR; Auto and Force VR try the " +
            "configured Standalone loader.")]
        [SerializeField] private DesktopSimulationManager _simulation;

        [Header("Startup Safety")]
        [SerializeField, Min(1f)]
        private float _initializationTimeoutSeconds = 10f;
        [Tooltip(
            "How long Auto waits for a headset to report in after XR " +
            "starts, before folding XR back up and using desktop.")]
        [SerializeField, Min(0.5f)]
        private float _headsetAppearanceTimeoutSeconds = 4f;

        private Coroutine _initializationRoutine;
        private Coroutine _loaderInitializationRoutine;
        private XRManagerSettings _ownedManager;
        private bool _ownsLifecycle;
        private bool _loaderInitializationFinished;

        public DesktopSimulationMode RequestedMode => ResolveMode();
        public bool StartedXr { get; private set; }
        public string StatusReason { get; private set; } = "NOT RUN";
        public XrStartupResolution Resolution { get; private set; } =
            XrStartupResolution.Pending;
        public bool HasResolved =>
            Resolution != XrStartupResolution.Pending;
        public float InitializationTimeoutSeconds =>
            Mathf.Max(1f, _initializationTimeoutSeconds);
        public float HeadsetAppearanceTimeoutSeconds =>
            Mathf.Max(0.5f, _headsetAppearanceTimeoutSeconds);

        private DesktopSimulationMode ResolveMode()
        {
            if (_simulation == null)
            {
                _simulation =
                    GetComponent<DesktopSimulationManager>();
            }

            if (_simulation == null)
            {
                _simulation =
                    FindFirstObjectByType<
                        DesktopSimulationManager>(
                        FindObjectsInactive.Include);
            }

            // EffectiveMode, not RequestedMode: this is the component that
            // decides whether XR is touched at all, so a machine set to force
            // desktop has to be honoured here or the override protects
            // nothing. Reading the scene value here started XR anyway, waited
            // for a headset that was never coming, and tore it down through
            // MetaXRFeature.OnInstanceDestroy, which is the crash the whole
            // arrangement exists to avoid.
            //
            // Without a manager, keep the headset path available.
            return _simulation == null
                ? DesktopSimulationMode.Auto
                : _simulation.EffectiveMode;
        }

        private void Awake()
        {
            ConfigureTrackingOriginCompatibility();
            DisableUnusedMrukGlobalContext();

            var mode = ResolveMode();
            if (mode == DesktopSimulationMode.ForceDesktop)
            {
                StatusReason =
                    "Force Desktop: XR will not be initialized.";
                Resolution = XrStartupResolution.Desktop;
                return;
            }

            // Auto used to ask OVRPlugin for the headset here and always
            // got "none", because OVRPlugin sits on top of the OpenXR
            // session and there is no session yet. Asking before startup
            // cannot work, so Auto now starts XR and checks afterwards.
        }

        private void Start()
        {
            if (Resolution == XrStartupResolution.Desktop)
            {
                return;
            }

            var settings = XRGeneralSettings.Instance;
            if (settings != null && settings.InitManagerOnStart)
            {
                // Quest/Android uses XR Management's normal lifecycle. It
                // initialized before scene Awake and will clean itself up.
                StartedXr =
                    settings.Manager != null &&
                    settings.Manager.activeLoader != null;
                StatusReason =
                    "XR Management owns this platform's lifecycle.";
                Resolution = StartedXr
                    ? XrStartupResolution.XrRunning
                    : XrStartupResolution.XrUnavailableForced;
                return;
            }

            _initializationRoutine =
                StartCoroutine(InitializeAndStartXr(settings));
        }

        private IEnumerator InitializeAndStartXr(
            XRGeneralSettings settings)
        {
            var manager = settings == null
                ? null
                : settings.Manager;
            if (manager == null)
            {
                StatusReason =
                    "No XRManagerSettings instance is available.";
                Debug.LogWarning(
                    $"[XrStartup] {StatusReason}",
                    this);
                ResolveFailedStartup();
                yield break;
            }

            _ownedManager = manager;
            _ownsLifecycle = true;

            var reusedInitializedLoader =
                manager.isInitializationComplete &&
                manager.activeLoader != null;
            if (manager.isInitializationComplete &&
                manager.activeLoader == null)
            {
                Debug.LogWarning(
                    "[XrStartup] XR Manager reports initialization without " +
                    "an active loader. Cleaning that incomplete state.",
                    this);
                if (!TryDeinitializeManager(
                        manager,
                        "cleaning incomplete XR state"))
                {
                    StatusReason =
                        "Could not clean incomplete XR state. Continuing " +
                        "without XR.";
                    ResolveFailedStartup();
                    _initializationRoutine = null;
                    yield break;
                }
            }

            if (!reusedInitializedLoader)
            {
                // Run loader initialization independently so this owner can
                // still enforce a timeout if the loader coroutine stalls.
                _loaderInitializationFinished = false;
                _loaderInitializationRoutine =
                    StartCoroutine(InitializeLoader(manager));
                var initializationStartedAt =
                    Time.realtimeSinceStartup;
                while (!_loaderInitializationFinished &&
                    Time.realtimeSinceStartup -
                    initializationStartedAt <
                    InitializationTimeoutSeconds)
                {
                    yield return null;
                }

                if (!_loaderInitializationFinished)
                {
                    if (_loaderInitializationRoutine != null)
                    {
                        StopCoroutine(_loaderInitializationRoutine);
                        _loaderInitializationRoutine = null;
                    }

                    StatusReason =
                        $"XR loader did not resolve within " +
                        $"{InitializationTimeoutSeconds:F0} seconds. " +
                        "Continuing without XR.";
                    Debug.LogWarning(
                        $"[XrStartup] {StatusReason}",
                        this);
                    TryDeinitializeManager(
                        manager,
                        "recovering from an initialization timeout");
                    ResolveFailedStartup();
                    _initializationRoutine = null;
                    yield break;
                }

                _loaderInitializationRoutine = null;
            }

            if (manager.activeLoader == null)
            {
                StatusReason =
                    "No XR loader started. Continuing without XR.";
                Debug.LogWarning(
                    $"[XrStartup] {StatusReason}",
                    this);
                ResolveFailedStartup();
                _initializationRoutine = null;
                yield break;
            }

            try
            {
                manager.StartSubsystems();
            }
            catch (Exception exception)
            {
                StatusReason =
                    "XR subsystems failed to start. Continuing without XR.";
                Debug.LogWarning(
                    $"[XrStartup] {StatusReason} {exception.Message}",
                    this);
                TryDeinitializeManager(
                    manager,
                    "recovering from subsystem startup failure");
                ResolveFailedStartup();
                _initializationRoutine = null;
                yield break;
            }

            if (ShouldAttemptXr(ResolveMode()) &&
                ResolveMode() == DesktopSimulationMode.Auto)
            {
                // A loader can start with no headset attached, so wait for
                // a real display and head device before committing to XR.
                var waitStartedAt = Time.realtimeSinceStartup;
                while (!SimulationModeResolver.IsVrRuntimeActive() &&
                    Time.realtimeSinceStartup - waitStartedAt <
                    HeadsetAppearanceTimeoutSeconds)
                {
                    yield return null;
                }

                if (ShouldFallBackToDesktop(
                        DesktopSimulationMode.Auto,
                        SimulationModeResolver.IsVrRuntimeActive()))
                {
                    StatusReason =
                        "Auto started XR but no headset appeared; " +
                        "falling back to desktop.";
                    Debug.Log($"[XrStartup] {StatusReason}", this);
                    TryDeinitializeManager(
                        manager,
                        "no headset appeared under Auto");
                    ResolveFailedStartup();
                    _initializationRoutine = null;
                    yield break;
                }
            }

            StartedXr = true;
            Resolution = XrStartupResolution.XrRunning;
            StatusReason = reusedInitializedLoader
                ? $"Restarted {manager.activeLoader.name} subsystems " +
                  "using the existing Editor loader."
                : $"Started {manager.activeLoader.name}.";
            StartCoroutine(
                RestartActiveAvatarBodyTrackingAfterSceneStart());
            Debug.Log($"[XrStartup] {StatusReason}", this);
            _initializationRoutine = null;
        }

        private static IEnumerator
            RestartActiveAvatarBodyTrackingAfterSceneStart()
        {
            // Reused loaders can finish synchronously from this component's
            // very early Start. Wait until CharacterRetargeter.Start has run;
            // it initializes ApplyRootScale avatars at zero.
            yield return null;
            RestartActiveAvatarBodyTracking();
        }

        /// <summary>
        /// Scene components are enabled before the manually owned Standalone
        /// XR loader starts. OVRBody disables itself when that early startup
        /// attempt fails, which otherwise leaves only the initially selected
        /// avatar collapsed at zero scale. Avatars selected later work because
        /// their provider is first enabled after XR is ready.
        /// </summary>
        private static void RestartActiveAvatarBodyTracking()
        {
            var registry = FindFirstObjectByType<AvatarRegistry>(
                FindObjectsInactive.Include);
            var activeAvatar = registry == null
                ? null
                : registry.Active;
            var provider = activeAvatar == null
                ? null
                : activeAvatar.GetComponent<
                    Meta.XR.Movement.Retargeting.
                        MetaSourceDataProvider>();
            if (provider != null && !provider.enabled)
            {
                provider.enabled = true;
            }

            // CharacterRetargeter initializes an ApplyRootScale avatar at
            // zero until the first valid body pose arrives. Keep the authored
            // avatar visible during that short wait; a valid retargeted pose
            // will replace this with the tracked scale automatically.
            if (activeAvatar != null)
            {
                var scale = activeAvatar.transform.localScale;
                const float minimumVisibleScale = 0.001f;
                if (Mathf.Abs(scale.x) < minimumVisibleScale ||
                    Mathf.Abs(scale.y) < minimumVisibleScale ||
                    Mathf.Abs(scale.z) < minimumVisibleScale)
                {
                    activeAvatar.transform.localScale = Vector3.one;
                }
            }
        }

        private IEnumerator InitializeLoader(
            XRManagerSettings manager)
        {
            yield return manager.InitializeLoader();
            _loaderInitializationFinished = true;
        }

        private void OnDisable()
        {
            ShutdownOwnedXr();
        }

        private void OnDestroy()
        {
            ShutdownOwnedXr();
        }

        private void ShutdownOwnedXr()
        {
            if (_initializationRoutine != null)
            {
                StopCoroutine(_initializationRoutine);
                _initializationRoutine = null;
            }

            if (_loaderInitializationRoutine != null)
            {
                StopCoroutine(_loaderInitializationRoutine);
                _loaderInitializationRoutine = null;
            }

            _loaderInitializationFinished = false;

            var manager = _ownedManager;
            _ownedManager = null;
            if (!_ownsLifecycle)
            {
                return;
            }

            _ownsLifecycle = false;
            StartedXr = false;
            if (manager == null ||
                !manager.isInitializationComplete)
            {
                return;
            }

#if UNITY_EDITOR
            // Unity stops the subsystems during Play Mode teardown, but the
            // initialized OpenXR loader must survive. Meta's Windows runtime
            // can crash when a loader is deinitialized and initialized again
            // inside the same Editor process. The next Play session reuses it
            // and starts only the subsystems.
            StatusReason =
                "Play stopped; the Editor XR loader remains initialized.";
            return;
#else
            // DeinitializeLoader stops the subsystems first, then releases
            // the active loader in standalone players.
            if (TryDeinitializeManager(
                    manager,
                    "shutting down owned XR"))
            {
                StatusReason = "Stopped and deinitialized XR.";
            }
#endif
        }

        /// <summary>
        /// Gives Auto mode a deterministic escape if startup never reports a
        /// result. The desktop manager calls this only while the resolution is
        /// still pending, before it enables the desktop camera/input path.
        /// </summary>
        public void CancelPendingStartupForDesktopFallback(
            string reason)
        {
            if (HasResolved)
            {
                return;
            }

            ShutdownOwnedXr();
            StartedXr = false;
            Resolution = XrStartupResolution.Desktop;
            StatusReason = string.IsNullOrWhiteSpace(reason)
                ? "XR startup was cancelled; using desktop mode."
                : reason;
            Debug.LogWarning($"[XrStartup] {StatusReason}", this);
        }

        private bool TryDeinitializeManager(
            XRManagerSettings manager,
            string operation)
        {
            try
            {
                if (manager != null &&
                    manager.isInitializationComplete)
                {
                    manager.DeinitializeLoader();
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[XrStartup] Failed while {operation}: " +
                    exception.Message,
                    this);
                return false;
            }
        }

        private void ResolveFailedStartup()
        {
            Resolution = ResolveMode() ==
                DesktopSimulationMode.ForceVR
                    ? XrStartupResolution.XrUnavailableForced
                    : XrStartupResolution.Desktop;
        }

        /// <summary>
        /// Whether to bring XR up at all. Only Force Desktop opts out;
        /// whether a headset is really there cannot be known until after
        /// startup, so Auto tries and then checks.
        /// </summary>
        public static bool ShouldAttemptXr(DesktopSimulationMode mode)
        {
            return mode != DesktopSimulationMode.ForceDesktop;
        }

        /// <summary>
        /// Whether to tear XR back down after starting it. Auto falls back
        /// to desktop when no headset actually appeared. Force VR was asked
        /// for explicitly, so it stays on XR and reports the trouble rather
        /// than silently changing what was requested.
        /// </summary>
        public static bool ShouldFallBackToDesktop(
            DesktopSimulationMode mode,
            bool headsetPresent)
        {
            return mode == DesktopSimulationMode.Auto &&
                !headsetPresent;
        }

        private static bool IsMetaHeadsetAvailableBeforeXr()
        {
            try
            {
                return OVRPlugin.GetSystemHeadsetType() !=
                    OVRPlugin.SystemHeadset.None;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[XrStartup] The pre-start headset probe failed; Auto " +
                    $"will use desktop mode. {exception.Message}");
                return false;
            }
        }

        /// <summary>
        /// Meta's OVRManager maps Floor Level to Unity's Floor tracking
        /// origin. Some desktop OpenXR runtimes expose only Device/Eye
        /// tracking; repeatedly asking those runtimes for Floor emits an
        /// error every frame. Keep Floor where it is supported and fall back
        /// only for that unsupported runtime, before OVRManager.Update runs.
        /// </summary>
        private static void ConfigureTrackingOriginCompatibility()
        {
            var ovrManager = FindFirstObjectByType<OVRManager>(
                FindObjectsInactive.Include);
            if (ovrManager == null ||
                ovrManager.trackingOriginType !=
                    OVRManager.TrackingOrigin.FloorLevel)
            {
                return;
            }

            var inputSubsystems = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(inputSubsystems);
            XRInputSubsystem inputSubsystem = null;
            foreach (var candidate in inputSubsystems)
            {
                if (candidate != null && candidate.running)
                {
                    inputSubsystem = candidate;
                    break;
                }
            }

            if (inputSubsystem == null && inputSubsystems.Count > 0)
            {
                inputSubsystem = inputSubsystems[0];
            }

            if (inputSubsystem == null)
            {
                return;
            }

            var supportedOrigins =
                inputSubsystem.GetSupportedTrackingOriginModes();
            if ((supportedOrigins & TrackingOriginModeFlags.Floor) != 0 ||
                (supportedOrigins & TrackingOriginModeFlags.Device) == 0)
            {
                return;
            }

            ovrManager.trackingOriginType =
                OVRManager.TrackingOrigin.EyeLevel;
            Debug.LogWarning(
                "[XrStartup] The active XR runtime does not support " +
                "Floor tracking. Falling back to Eye Level for this run.",
                ovrManager);
        }

        /// <summary>
        /// MRUK creates a hidden global updater in every player. When the
        /// project has no MRUK component, its updater can run while the
        /// desktop path has no valid OpenXR global context. Disable only that
        /// unused updater; do not destroy or recreate its GameObject.
        /// </summary>
        private static void DisableUnusedMrukGlobalContext()
        {
            const string mrukType =
                "Meta.XR.MRUtilityKit.MRUK";
            const string globalContextType =
                "Meta.XR.MRUtilityKit.MRUKGlobalContext";
            var behaviours =
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            foreach (var behaviour in behaviours)
            {
                if (behaviour != null &&
                    behaviour.GetType().FullName == mrukType)
                {
                    return;
                }
            }

            foreach (var behaviour in behaviours)
            {
                if (behaviour != null &&
                    behaviour.GetType().FullName == globalContextType)
                {
                    behaviour.enabled = false;
                }
            }
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterMrukEditorShutdownGuard()
        {
            EditorApplication.playModeStateChanged -=
                HandleEditorPlayModeStateChanged;
            EditorApplication.playModeStateChanged +=
                HandleEditorPlayModeStateChanged;
        }

        private static void HandleEditorPlayModeStateChanged(
            PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingPlayMode)
            {
                return;
            }

            // MRUKGlobalContext is a DontDestroyOnLoad object. Meta's package
            // reports a manual destruction on Editor Play Mode exit unless
            // this flag is set before Unity starts tearing objects down.
            // Mark every context, rather than retaining a single instance,
            // because package reloads can temporarily create more than one.
            const string globalContextType =
                "Meta.XR.MRUtilityKit.MRUKGlobalContext";
            foreach (var behaviour in
                Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (behaviour == null ||
                    behaviour.GetType().FullName != globalContextType)
                {
                    continue;
                }

                try
                {
                    var quittingField =
                        behaviour.GetType().GetField(
                            "_isQuitting",
                            BindingFlags.Instance |
                            BindingFlags.NonPublic);
                    if (quittingField != null)
                    {
                        quittingField.SetValue(behaviour, true);
                    }
                }
                catch (Exception)
                {
                    // The package version can change this private detail.
                    // Never let this Editor-only cleanup interrupt teardown.
                }
            }

            EditorApplication.playModeStateChanged -=
                HandleEditorPlayModeStateChanged;
        }
#endif

    }
}
