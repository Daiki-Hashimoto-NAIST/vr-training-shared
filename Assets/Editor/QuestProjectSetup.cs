using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

public static class QuestProjectSetup
{
    private const string SettingsRoot = "Assets/XR";
    private const string SettingsFolder = SettingsRoot + "/Settings";
    private const string GeneralSettingsPath = SettingsFolder + "/XRGeneralSettingsPerBuildTarget.asset";

    [MenuItem("Tools/IVRC/Apply Meta Quest Setup")]
    public static string Apply()
    {
        const BuildTargetGroup group = BuildTargetGroup.Android;

        var allSettings = GetOrCreateGeneralSettings();
        if (!allSettings.HasSettingsForBuildTarget(group))
            allSettings.CreateDefaultSettingsForBuildTarget(group);

        if (!allSettings.HasManagerSettingsForBuildTarget(group))
            allSettings.CreateDefaultManagerSettingsForBuildTarget(group);

        var generalSettings = allSettings.SettingsForBuildTarget(group);
        generalSettings.InitManagerOnStart = true;

        var loaderAssigned = XRPackageMetadataStore.AssignLoader(
            generalSettings.Manager,
            typeof(OpenXRLoader).FullName,
            group);

        FeatureHelpers.RefreshFeatures(group);

        var openXrSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
        if (openXrSettings == null)
            throw new System.InvalidOperationException("Android OpenXR settings could not be created.");

        var enabledFeatureCount = 0;
        enabledFeatureCount += EnableFeature(group, MetaQuestFeature.featureId);
        enabledFeatureCount += EnableFeature(group, OculusTouchControllerProfile.featureId);
        enabledFeatureCount += EnableFeature(group, MetaQuestTouchPlusControllerProfile.featureId);
        enabledFeatureCount += EnableFeature(group, MetaQuestTouchProControllerProfile.featureId);
        enabledFeatureCount += EnableFeature(group, "com.unity.openxr.feature.compositionlayers");

        openXrSettings.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeInputPolling;

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        if ((int)PlayerSettings.Android.minSdkVersion < 29)
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        AddScriptingDefine(NamedBuildTarget.Android, "USE_INPUT_SYSTEM_POSE_CONTROL");
        AddScriptingDefine(NamedBuildTarget.Android, "USE_STICK_CONTROL_THUMBSTICKS");
        AddScriptingDefine(NamedBuildTarget.Standalone, "USE_INPUT_SYSTEM_POSE_CONTROL");
        AddScriptingDefine(NamedBuildTarget.Standalone, "USE_STICK_CONTROL_THUMBSTICKS");

        EditorUtility.SetDirty(allSettings);
        EditorUtility.SetDirty(generalSettings);
        EditorUtility.SetDirty(generalSettings.Manager);
        EditorUtility.SetDirty(openXrSettings);
        AssetDatabase.SaveAssets();

        var message = $"Meta Quest setup applied. Loader assigned: {loaderAssigned}; features enabled: {enabledFeatureCount}; min SDK: {(int)PlayerSettings.Android.minSdkVersion}.";
        Debug.Log(message);
        return message;
    }

    [MenuItem("Tools/IVRC/Validate Meta Quest Setup")]
    public static string Validate()
    {
        var issues = new System.Collections.Generic.List<OpenXRFeature.ValidationRule>();
        OpenXRProjectValidation.GetCurrentValidationIssues(issues, BuildTargetGroup.Android);

        if (issues.Count == 0)
            return "OpenXR validation passed with no issues.";

        var report = new System.Text.StringBuilder();
        report.AppendLine($"OpenXR validation found {issues.Count} issue(s):");
        foreach (var issue in issues)
            report.AppendLine($"- {(issue.error ? "ERROR" : "WARNING")}: {issue.message}");

        var result = report.ToString().TrimEnd();
        Debug.LogWarning(result);
        return result;
    }

    [MenuItem("Tools/IVRC/Import XRI Starter Assets")]
    public static string ImportStarterAssets()
    {
        var sample = Sample.FindByPackage("com.unity.xr.interaction.toolkit", string.Empty)
            ?.FirstOrDefault(item => item.displayName == "Starter Assets");

        if (!sample.HasValue)
            throw new System.InvalidOperationException("XR Interaction Toolkit Starter Assets sample was not found.");

        if (!sample.Value.isImported)
            sample.Value.Import(Sample.ImportOptions.OverridePreviousImports);

        return "XR Interaction Toolkit Starter Assets imported.";
    }

    [MenuItem("Tools/IVRC/Prepare Current Scene For Quest")]
    public static string PrepareCurrentScene()
    {
        var existingRig = GameObject.Find("XR Origin (XR Rig)");
        if (existingRig != null)
            return "The current scene already contains XR Origin (XR Rig).";

        var prefabGuid = AssetDatabase.FindAssets("\"XR Origin (XR Rig)\" t:Prefab")
            .FirstOrDefault(guid =>
            {
                var path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                return path.Contains("/Starter Assets/Prefabs/XR Origin (XR Rig).prefab");
            });

        if (string.IsNullOrEmpty(prefabGuid))
            throw new System.InvalidOperationException("XR Origin (XR Rig) prefab was not found. Import Starter Assets first.");

        var prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var oldMainCamera = GameObject.Find("Main Camera");
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        if (oldMainCamera != null && !oldMainCamera.transform.IsChildOf(rig.transform))
            Undo.DestroyObjectImmediate(oldMainCamera);

        EditorSceneManager.MarkSceneDirty(rig.scene);
        EditorSceneManager.SaveScene(rig.scene);
        Selection.activeGameObject = rig;
        return $"Added {rig.name} to {rig.scene.name} and removed the old Main Camera.";
    }

    private static XRGeneralSettingsPerBuildTarget GetOrCreateGeneralSettings()
    {
        EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(
            XRGeneralSettings.k_SettingsKey,
            out var allSettings);

        if (allSettings != null)
            return allSettings;

        EnsureFolder(SettingsRoot);
        EnsureFolder(SettingsFolder);

        allSettings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
        AssetDatabase.CreateAsset(allSettings, GeneralSettingsPath);
        EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, allSettings, true);
        AssetDatabase.SaveAssets();
        return allSettings;
    }

    private static int EnableFeature(BuildTargetGroup group, string featureId)
    {
        var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, featureId);
        if (feature == null)
        {
            Debug.LogWarning($"OpenXR feature was not found: {featureId}");
            return 0;
        }

        feature.enabled = true;
        EditorUtility.SetDirty(feature);
        return 1;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        var parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        var name = System.IO.Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent))
            AssetDatabase.CreateFolder(parent, name);
    }

    private static void AddScriptingDefine(NamedBuildTarget target, string symbol)
    {
        var defines = PlayerSettings.GetScriptingDefineSymbols(target);
        var symbols = new System.Collections.Generic.HashSet<string>(
            defines.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries));

        if (!symbols.Add(symbol))
            return;

        PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
    }
}
