using UnityEngine;
using UnityEngine.InputSystem;

namespace IVRC.Desktop
{
    /// <summary>
    /// Frames the avatar and its mirror for the Game view, and offers optional
    /// right-drag orbiting plus wheel zoom. Everything here is disabled in VR
    /// mode so the headset camera stays the only enabled camera.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DesktopCameraController : MonoBehaviour
    {
        [SerializeField] private DesktopSimulationManager _manager;
        [SerializeField] private Camera _desktopCamera;

        [Header("Framing")]
        [Tooltip("Mirror or avatar centre the camera looks at.")]
        [SerializeField] private Transform _lookTarget;

        [Tooltip(
            "Looked at during the stage phases instead. In VR the performer " +
            "is carried to the stage by relocating the tracking space, but " +
            "on desktop the avatar stays at the origin, so following it " +
            "leaves the camera pointing at an empty room while the show " +
            "happens somewhere else.")]
        [SerializeField] private Transform _stageLookTarget;
        [SerializeField] private Vector3 _stageLookTargetOffset =
            new Vector3(0f, 1.1f, 0f);
        [SerializeField] private GameFlowController _gameFlow;
        [SerializeField] private Vector3 _initialPosition =
            new Vector3(0f, 1.5f, -2.2f);
        [SerializeField] private Vector3 _lookTargetOffset =
            new Vector3(0f, 1.1f, 0f);

        [Header("Viewpoints")]
        [Tooltip(
            "Number keys 5-8 jump to a named angle. Orbiting by hand is fine " +
            "for looking around, but checking whether a bicep changed means " +
            "getting to the same place twice, which a drag cannot do. " +
            "1-4 belong to DesktopPoseDriver's poses and are left alone.")]
        [SerializeField] private bool _enableViewpointKeys = true;
        [SerializeField, Min(0.2f)]
        private float _viewpointDistance = 1.8f;
        [SerializeField] private float _viewpointPitch = 8f;

        [Header("Mouse")]
        [SerializeField] private bool _enableMouseLook = true;
        [SerializeField, Min(0f)] private float _mouseSensitivity = 0.15f;
        [SerializeField, Min(0f)] private float _zoomSensitivity = 0.25f;
        [SerializeField, Min(0.1f)] private float _minimumDistance = 0.6f;
        [SerializeField, Min(0.2f)] private float _maximumDistance = 6f;
        [SerializeField] private float _pitchMin = -30f;
        [SerializeField] private float _pitchMax = 70f;

        private float _yaw;
        private float _pitch;
        private float _distance;
        private bool _initialized;

        private bool IsActive =>
            _manager != null && _manager.IsDesktopActive;

        private void Awake()
        {
            _gameFlow ??=
                FindFirstObjectByType<GameFlowController>();
        }

        private void Start()
        {
            if (!IsActive)
            {
                // VR run: make sure the desktop camera cannot draw or listen.
                Disable();
                return;
            }

            if (_desktopCamera == null)
            {
                Debug.LogWarning(
                    "[DesktopSim] Desktop Camera is not assigned, so the " +
                    "Game view will keep using the VR camera.",
                    this);
                return;
            }

            _desktopCamera.enabled = true;
            var listener =
                _desktopCamera.GetComponent<AudioListener>();
            if (listener != null)
            {
                listener.enabled = true;
            }

            InitializeOrbit();

            // The camera is driven every frame, so dragging it in the Scene
            // view or typing into its Transform does nothing and looks broken.
            // Say what does work, once, where someone looking for it will be.
            Debug.Log(
                "[DesktopSim] Camera: right-drag orbits, wheel zooms" +
                (_enableViewpointKeys
                    ? ", 5 front / 6 back / 7 left / 8 right (1-4 pose)"
                    : string.Empty) +
                ". Moving its Transform by hand will not stick.",
                this);
        }

        private void Disable()
        {
            if (_desktopCamera == null)
            {
                return;
            }

            _desktopCamera.enabled = false;
            var listener =
                _desktopCamera.GetComponent<AudioListener>();
            if (listener != null)
            {
                listener.enabled = false;
            }
        }

        private void InitializeOrbit()
        {
            var target = GetTargetPoint();
            var cameraTransform = _desktopCamera.transform;
            cameraTransform.position = _initialPosition;
            cameraTransform.LookAt(target);

            var offset = _initialPosition - target;
            _distance = Mathf.Clamp(
                offset.magnitude,
                _minimumDistance,
                _maximumDistance);
            var euler = cameraTransform.rotation.eulerAngles;
            _yaw = euler.y;
            _pitch = NormalizePitch(euler.x);
            _initialized = true;
            ApplyOrbit();
        }

        private void LateUpdate()
        {
            if (!IsActive ||
                !_initialized ||
                _desktopCamera == null)
            {
                return;
            }

            if (TryReadViewpointKey())
            {
                ApplyOrbit();
            }

            if (!_enableMouseLook)
            {
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            // Rotating only while the right button is held keeps UI clicks and
            // Game view interaction from moving the camera.
            if (mouse.rightButton.isPressed)
            {
                var delta = mouse.delta.ReadValue();
                _yaw += delta.x * _mouseSensitivity;
                _pitch = Mathf.Clamp(
                    _pitch - delta.y * _mouseSensitivity,
                    _pitchMin,
                    _pitchMax);
            }

            var scroll = mouse.scroll.ReadValue().y;
            if (!Mathf.Approximately(scroll, 0f))
            {
                _distance = Mathf.Clamp(
                    _distance - scroll * _zoomSensitivity * 0.01f,
                    _minimumDistance,
                    _maximumDistance);
            }

            ApplyOrbit();
        }

        /// <summary>
        /// Yaw 0 puts the camera on -Z looking towards +Z. The avatar faces
        /// +Z, so yaw 0 is its back and 180 is its face. The default framing
        /// starts behind, which is why the arms were only ever seen from
        /// behind: from there the biceps are hidden by the torso.
        /// </summary>
        private bool TryReadViewpointKey()
        {
            if (!_enableViewpointKeys)
            {
                return false;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            // 1-4 already pose the avatar, and both digits and numpad at
            // that. Sharing them would have snapped the camera every time a
            // pose was chosen, which is the one moment you want it to hold
            // still and let you compare.
            float yaw;
            if (Pressed(keyboard.digit5Key, keyboard.numpad5Key))
            {
                yaw = 180f;
            }
            else if (Pressed(keyboard.digit6Key, keyboard.numpad6Key))
            {
                yaw = 0f;
            }
            else if (Pressed(keyboard.digit7Key, keyboard.numpad7Key))
            {
                yaw = 90f;
            }
            else if (Pressed(keyboard.digit8Key, keyboard.numpad8Key))
            {
                yaw = 270f;
            }
            else
            {
                return false;
            }

            _yaw = yaw;
            _pitch = Mathf.Clamp(
                _viewpointPitch,
                _pitchMin,
                _pitchMax);
            _distance = Mathf.Clamp(
                _viewpointDistance,
                _minimumDistance,
                _maximumDistance);
            return true;
        }

        private static bool Pressed(
            UnityEngine.InputSystem.Controls.KeyControl digit,
            UnityEngine.InputSystem.Controls.KeyControl numpad)
        {
            return (digit != null && digit.wasPressedThisFrame) ||
                   (numpad != null && numpad.wasPressedThisFrame);
        }

        private void ApplyOrbit()
        {
            var target = GetTargetPoint();
            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            _desktopCamera.transform.position =
                target + rotation * new Vector3(0f, 0f, -_distance);
            _desktopCamera.transform.rotation = rotation;
        }

        private Vector3 GetTargetPoint()
        {
            if (IsStagePhase && _stageLookTarget != null)
            {
                return _stageLookTarget.position +
                       _stageLookTargetOffset;
            }

            return _lookTarget != null
                ? _lookTarget.position + _lookTargetOffset
                : _lookTargetOffset;
        }

        private bool IsStagePhase
        {
            get
            {
                if (_gameFlow == null)
                {
                    return false;
                }

                var phase = _gameFlow.CurrentPhase;
                return phase == ExperiencePhase.StageTransition ||
                       phase == ExperiencePhase.StageEntrance ||
                       phase == ExperiencePhase.Posing ||
                       phase == ExperiencePhase.Results;
            }
        }

        private static float NormalizePitch(float pitch)
        {
            return pitch > 180f ? pitch - 360f : pitch;
        }

        private void OnValidate()
        {
            _maximumDistance = Mathf.Max(
                _minimumDistance + 0.1f,
                _maximumDistance);
            _pitchMax = Mathf.Max(_pitchMin + 1f, _pitchMax);
            _viewpointDistance = Mathf.Clamp(
                _viewpointDistance,
                _minimumDistance,
                _maximumDistance);
            _viewpointPitch = Mathf.Clamp(
                _viewpointPitch,
                _pitchMin,
                _pitchMax);
        }
    }
}
