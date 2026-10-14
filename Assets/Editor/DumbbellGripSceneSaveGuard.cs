using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IVRC.EditorTools
{
    /// <summary>
    /// Keeps an edit-mode grip preview out of the scene file.
    ///
    /// The grip is real finger rotations on the rig, so previewing it in edit
    /// mode would otherwise be serialized the next time anyone saves and land
    /// on the collaborator as a few hundred changed bones. The controllers put
    /// the hands back before the scene is written and pick the grip up again
    /// on the next editor tick, so the preview stays on screen and out of the
    /// file.
    /// </summary>
    [InitializeOnLoad]
    internal static class DumbbellGripSceneSaveGuard
    {
        static DumbbellGripSceneSaveGuard()
        {
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            EditorSceneManager.sceneSaving += OnSceneSaving;
        }

        private static void OnSceneSaving(Scene scene, string path)
        {
            if (!scene.IsValid())
            {
                return;
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var controller in
                         root.GetComponentsInChildren<
                             DumbbellGripVisualController>(true))
                {
                    controller.ReleaseGrip();
                }
            }
        }
    }
}
