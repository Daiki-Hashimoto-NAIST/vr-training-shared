using IVRC.Desktop;
using UnityEditor;

namespace IVRC.EditorTools
{
    /// <summary>
    /// Menu for the per-machine run mode. Lives here rather than on the
    /// DesktopSimulationManager inspector so that choosing it never counts as
    /// a scene edit.
    /// </summary>
    internal static class DesktopModeMenu
    {
        private const string Root = "IVRC/Desktop Mode/";
        private const string FollowScenePath =
            Root + "Follow Scene (Auto)";
        private const string ForceDesktopPath =
            Root + "Force Desktop";
        private const string ForceVrPath = Root + "Force VR";

        [MenuItem(FollowScenePath, priority = 0)]
        private static void FollowScene()
        {
            DesktopModeOverride.Clear();
            Report();
        }

        [MenuItem(FollowScenePath, true)]
        private static bool FollowSceneChecked()
        {
            Menu.SetChecked(
                FollowScenePath,
                !DesktopModeOverride.Current.HasValue);
            return true;
        }

        [MenuItem(ForceDesktopPath, priority = 1)]
        private static void ForceDesktop()
        {
            DesktopModeOverride.Set(
                DesktopSimulationMode.ForceDesktop);
            Report();
        }

        [MenuItem(ForceDesktopPath, true)]
        private static bool ForceDesktopChecked()
        {
            Menu.SetChecked(
                ForceDesktopPath,
                DesktopModeOverride.Current ==
                DesktopSimulationMode.ForceDesktop);
            return true;
        }

        [MenuItem(ForceVrPath, priority = 2)]
        private static void ForceVr()
        {
            DesktopModeOverride.Set(
                DesktopSimulationMode.ForceVR);
            Report();
        }

        [MenuItem(ForceVrPath, true)]
        private static bool ForceVrChecked()
        {
            Menu.SetChecked(
                ForceVrPath,
                DesktopModeOverride.Current ==
                DesktopSimulationMode.ForceVR);
            return true;
        }

        private static void Report()
        {
            var current = DesktopModeOverride.Current;
            UnityEngine.Debug.Log(
                current.HasValue
                    ? $"[DesktopSim] This machine will run " +
                      $"{current.Value} regardless of the scene. " +
                      "The scene is untouched and builds are unaffected."
                    : "[DesktopSim] Following the scene's own mode again.");
        }
    }
}
