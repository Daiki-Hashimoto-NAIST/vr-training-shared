using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace IVRC.Desktop
{
    /// <summary>
    /// Which input and presentation path drives the experience for this run.
    /// Named with the Desktop prefix so it never collides with
    /// UnityEngine.SimulationMode in files that use both namespaces.
    /// </summary>
    public enum DesktopSimulationMode
    {
        /// <summary>Use VR when a headset is actually running, else desktop.</summary>
        Auto,

        /// <summary>Never run desktop simulation, even without a headset.</summary>
        ForceVR,

        /// <summary>Always run desktop simulation, even with a headset attached.</summary>
        ForceDesktop
    }

    /// <summary>
    /// Resolves the run mode. Detection asks the live XR subsystem rather than
    /// checking whether a package is installed, so Quest Link in the Editor is
    /// reported as VR while an unplugged Editor session is not.
    /// </summary>
    public static class SimulationModeResolver
    {
        /// <summary>
        /// True when an XR loader is initialized and a head-mounted display is
        /// present and tracking-capable.
        /// </summary>
        public static bool IsVrRuntimeActive()
        {
            var manager = XRGeneralSettings.Instance == null
                ? null
                : XRGeneralSettings.Instance.Manager;
            if (manager == null ||
                manager.activeLoader == null ||
                !manager.isInitializationComplete)
            {
                return false;
            }

            var displays = new System.Collections.Generic.List<
                XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            var hasRunningDisplay = false;
            foreach (var display in displays)
            {
                if (display != null && display.running)
                {
                    hasRunningDisplay = true;
                    break;
                }
            }

            if (!hasRunningDisplay)
            {
                return false;
            }

            // A running display can still exist without a headset reporting
            // poses, so require an HMD device as well.
            var headDevice = InputDevices.GetDeviceAtXRNode(
                XRNode.Head);
            return headDevice.isValid;
        }

        /// <summary>
        /// Decides whether desktop simulation should drive this run.
        /// </summary>
        public static bool ShouldUseDesktop(
            DesktopSimulationMode requestedMode,
            out string reason)
        {
            switch (requestedMode)
            {
                case DesktopSimulationMode.ForceDesktop:
                    reason = "ForceDesktop requested.";
                    return true;

                case DesktopSimulationMode.ForceVR:
                    var vrReady = IsVrRuntimeActive();
                    reason = vrReady
                        ? "ForceVR requested and an HMD is active."
                        : "ForceVR requested but no HMD is active. " +
                          "Desktop simulation stays disabled, so VR input " +
                          "is the only source.";
                    return false;

                default:
                    var active = IsVrRuntimeActive();
                    reason = active
                        ? "Auto detected an active HMD."
                        : "Auto found no active HMD.";
                    return !active;
            }
        }
    }
}
