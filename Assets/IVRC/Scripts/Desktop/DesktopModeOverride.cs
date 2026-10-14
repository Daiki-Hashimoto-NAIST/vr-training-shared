using System;

namespace IVRC.Desktop
{
    /// <summary>
    /// A per-machine choice of run mode that outranks the scene's own setting,
    /// in the Editor only.
    ///
    /// The scene value is the exhibition's setting and has to stay Auto, so
    /// that an attached Quest keeps the VR path on the day. But development
    /// needs the opposite answer several times an hour: without a headset Auto
    /// starts XR, waits for one, and tears it down again through
    /// MetaXRFeature.OnInstanceDestroy, which has crashed the Editor.
    ///
    /// Switching the scene value each time made the two uses fight over one
    /// serialized field. It dirtied the scene on every switch, and a switch
    /// left behind would have shipped ForceDesktop to the exhibition. Keeping
    /// the preference outside the scene lets both have their answer, and there
    /// is nothing left to remember to put back.
    ///
    /// In a build there is no override at all and the scene value stands.
    /// </summary>
    public static class DesktopModeOverride
    {
#if UNITY_EDITOR
        private const string KeyPrefix = "IVRC.DesktopMode.";

        // Keyed per project, so two checkouts on one machine do not share a
        // preference that means different things in each.
        private static string Key =>
            KeyPrefix +
            UnityEditor.PlayerSettings.productGUID;
#endif

        /// <summary>
        /// The mode this machine insists on, or null to follow the scene.
        /// </summary>
        public static DesktopSimulationMode? Current
        {
            get
            {
#if UNITY_EDITOR
                var stored = UnityEditor.EditorPrefs.GetInt(Key, -1);
                if (stored >= 0 &&
                    Enum.IsDefined(
                        typeof(DesktopSimulationMode),
                        stored))
                {
                    return (DesktopSimulationMode)stored;
                }
#endif
                return null;
            }
        }

        public static void Set(DesktopSimulationMode mode)
        {
#if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetInt(Key, (int)mode);
#endif
        }

        /// <summary>Go back to whatever the scene asks for.</summary>
        public static void Clear()
        {
#if UNITY_EDITOR
            UnityEditor.EditorPrefs.DeleteKey(Key);
#endif
        }

        /// <summary>
        /// Applies the override to a scene's setting. Returns the scene's own
        /// value untouched when nothing is overridden, which is always the
        /// case in a build.
        /// </summary>
        public static DesktopSimulationMode Resolve(
            DesktopSimulationMode sceneMode,
            out bool overridden)
        {
            var current = Current;
            overridden = current.HasValue;
            return current ?? sceneMode;
        }
    }
}
