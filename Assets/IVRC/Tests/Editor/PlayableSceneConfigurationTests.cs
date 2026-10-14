using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace IVRC.Tests
{
    public sealed class PlayableSceneConfigurationTests
    {
        private const string PrototypeScenePath =
            "Assets/Scenes/InstantMuscle_proto.unity";
        private const string PlayerFontPath =
            "Assets/IVRC/ThirdParty/Fonts/MPlus1p/" +
            "MPLUS1p-PlayerUI-SDF.asset";

        [Test]
        public void PrototypeIsFirstEnabledBuildScene()
        {
            var enabledScenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .ToArray();

            Assert.That(enabledScenes, Is.Not.Empty);
            Assert.That(
                enabledScenes[0].path,
                Is.EqualTo(PrototypeScenePath));
        }

        [Test]
        public void MetaCapabilitiesMatchControllerBodyTracking()
        {
            var config = AssetDatabase.LoadMainAssetAtPath(
                "Assets/Oculus/OculusProjectConfig.asset");
            Assert.That(config, Is.Not.Null);

            var serialized = new SerializedObject(config);
            AssertIntegerProperty(
                serialized,
                "bodyTrackingSupport",
                1);
            AssertIntegerProperty(
                serialized,
                "handTrackingSupport",
                0);
            AssertIntegerProperty(
                serialized,
                "faceTrackingSupport",
                0);
            AssertIntegerProperty(
                serialized,
                "eyeTrackingSupport",
                0);
            AssertIntegerProperty(
                serialized,
                "sceneSupport",
                0);
            AssertIntegerProperty(
                serialized,
                "anchorSupport",
                0);
        }

        [Test]
        public void AndroidManifestRemovesUnusedMicrophonePrompt()
        {
            var manifest = File.ReadAllText(
                Path.Combine(
                    Application.dataPath,
                    "Plugins/Android/AndroidManifest.xml"));

            StringAssert.Contains(
                "android.permission.RECORD_AUDIO\" " +
                "tools:node=\"remove\"",
                manifest);
            StringAssert.Contains(
                "android.hardware.microphone\" " +
                "tools:node=\"remove\"",
                manifest);
            StringAssert.Contains(
                "unityplayer.SkipPermissionsDialog\" " +
                "android:value=\"true\"",
                manifest);
        }

        [Test]
        public void PrototypeContainsWiredVerticalSlice()
        {
            var scene = SceneManager.GetSceneByPath(
                PrototypeScenePath);
            var openedForTest =
                !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    PrototypeScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                AssertNoMissingScripts(scene);

                var gameFlow = GetSingle<GameFlowController>(
                    scene);
                AssertReferences(
                    gameFlow,
                    "_rhythmClock",
                    "_rhythmScorer",
                    "_trainingRhythmChart",
                    "_metrics",
                    "_repCounter",
                    "_alignmentCalibration",
                    "_heightCalibration",
                    "_stageTransition",
                    "_stageEntrance",
                    "_posingScorer",
                    "_avatarSwapInput",
                    "_trainingRoot",
                    "_posingRoot",
                    "_stageEntranceRoot");

                var desktopInput =
                    GetSingle<
                        IVRC.Desktop.DesktopInputBridge>(
                        scene);
                AssertReferences(
                    desktopInput,
                    "_manager",
                    "_heightProvider",
                    "_alignmentCalibration",
                    "_avatarSwap",
                    "_gameFlow");

                var desktopManager =
                    GetSingle<
                        IVRC.Desktop
                            .DesktopSimulationManager>(
                        scene);
                AssertReferences(
                    desktopManager,
                    "_avatarRegistry",
                    "_bodyPoseMetrics",
                    "_vrCamera",
                    "_desktopRoot");
                Assert.That(
                    new SerializedObject(desktopManager)
                        .FindProperty("_mode")
                        .enumValueIndex,
                    Is.Zero,
                    "Desktop simulation must stay Auto so an " +
                    "active Quest runtime keeps the VR path.");

                var alignmentCalibration =
                    GetSingle<AvatarCalibrationController>(scene);
                AssertReferences(
                    alignmentCalibration,
                    "_cameraRig",
                    "_desktopSimulation",
                    "_calibrationPoint");

                var beatHaptics =
                    GetSingle<BeatHaptics>(scene);
                AssertReferences(
                    beatHaptics,
                    "_rhythmClock",
                    "_rhythmScorer");
                var beatHapticsSerialized =
                    new SerializedObject(beatHaptics);
                foreach (var durationProperty in new[]
                         {
                             "_durationSeconds",
                             "_completionDurationSeconds",
                             "_successDurationSeconds"
                         })
                {
                    Assert.That(
                        beatHapticsSerialized
                            .FindProperty(durationProperty)
                            .floatValue,
                        Is.InRange(
                            BeatHapticsSafety
                                .MinimumPulseDurationSeconds,
                            BeatHapticsSafety
                                .MaximumPulseDurationSeconds),
                        $"{durationProperty} must remain a short, " +
                        "comfortable controller pulse.");
                }

                var transformationPresenter =
                    GetSingle<
                        TransformationPresentationController>(scene);
                AssertReferences(
                    transformationPresenter,
                    "_gameFlow",
                    "_keyLight",
                    "_fillLight",
                    "_audioSource");
                var presenterObject = FindGameObjectByPath(
                    scene,
                    "Systems/TransformationPresentation");
                Assert.That(
                    presenterObject,
                    Is.EqualTo(
                        transformationPresenter.gameObject));
                var presenterSerialized =
                    new SerializedObject(
                        transformationPresenter);
                var expectedKey = FindGameObjectByPath(
                        scene,
                        "Phase_Training/TrainingLighting/" +
                        "Training Key Directional")
                    .GetComponent<Light>();
                var expectedFill = FindGameObjectByPath(
                        scene,
                        "Phase_Training/TrainingLighting/" +
                        "Training Fill Point")
                    .GetComponent<Light>();
                Assert.That(
                    presenterSerialized
                        .FindProperty("_keyLight")
                        .objectReferenceValue,
                    Is.EqualTo(expectedKey));
                Assert.That(
                    presenterSerialized
                        .FindProperty("_fillLight")
                        .objectReferenceValue,
                    Is.EqualTo(expectedFill));
                var mirrorFrame = FindGameObjectByPath(
                    scene,
                    "Phase_Training/MirrorWall_Opening/MirrorFrame");
                Assert.That(mirrorFrame, Is.Not.Null);
                var expectedFrameRenderers = mirrorFrame
                    .GetComponentsInChildren<MeshRenderer>(true)
                    .Cast<Renderer>()
                    .ToArray();
                var frameRenderers = presenterSerialized
                    .FindProperty("_mirrorFrameRenderers");
                Assert.That(frameRenderers, Is.Not.Null);
                Assert.That(
                    frameRenderers.arraySize,
                    Is.EqualTo(expectedFrameRenderers.Length));
                var configuredFrameRenderers = Enumerable
                    .Range(0, frameRenderers.arraySize)
                    .Select(index => frameRenderers
                        .GetArrayElementAtIndex(index)
                        .objectReferenceValue as Renderer)
                    .ToArray();
                Assert.That(
                    configuredFrameRenderers,
                    Is.EquivalentTo(expectedFrameRenderers));
                Assert.That(
                    configuredFrameRenderers,
                    Has.None.Null);
                Assert.That(
                    configuredFrameRenderers.Distinct().Count(),
                    Is.EqualTo(configuredFrameRenderers.Length));
                Assert.That(
                    presenterSerialized
                        .FindProperty("_mirrorFrameColorStrength")
                        .floatValue,
                    Is.InRange(0.5f, 1f));
                Assert.That(
                    presenterSerialized
                        .FindProperty("_peakKeyMultiplier")
                        .floatValue,
                    Is.InRange(1f, 1.5f));
                Assert.That(
                    presenterSerialized
                        .FindProperty("_heroHapticAmplitude")
                        .floatValue,
                    Is.InRange(0f, 0.7f));
                Assert.That(
                    presenterSerialized
                        .FindProperty(
                            "_heroHapticDurationSeconds")
                        .floatValue,
                    Is.InRange(0.01f, 0.2f));
                var revealClip = presenterSerialized
                    .FindProperty("_revealClip")
                    .objectReferenceValue as AudioClip;
                var heroClip = presenterSerialized
                    .FindProperty("_heroClip")
                    .objectReferenceValue as AudioClip;
                var usesGeneratedTransformationCues =
                    presenterSerialized
                        .FindProperty(
                            "_useGeneratedPlaceholderCues")
                        .boolValue;
                // Transformation is deliberately silent. The whistle
                // that ends the set already announces the change, and a
                // second sound landing on top of it was heard as one
                // cue too many. If production clips are ever assigned
                // they must be assigned in full, but no sound at all is
                // the intended state rather than an oversight.
                Assert.That(
                    !usesGeneratedTransformationCues &&
                    revealClip == null &&
                    heroClip == null,
                    Is.True,
                    "Transformation follows the whistle and stays " +
                    "quiet. Turning the placeholder cues back on puts " +
                    "a second sound under the whistle.");
                Assert.That(
                    presenterSerialized
                        .FindProperty("_revealVolume")
                        .floatValue,
                    Is.InRange(0f, 0.7f));
                Assert.That(
                    presenterSerialized
                        .FindProperty("_heroVolume")
                        .floatValue,
                    Is.InRange(0f, 0.85f));

                var setupFeedback =
                    GetSingle<SetupCompletionFeedbackController>(scene);
                AssertReferences(
                    setupFeedback,
                    "_gameFlow",
                    "_alignmentCalibration",
                    "_heightCalibration",
                    "_audioSource");
                Assert.That(
                    setupFeedback.gameObject,
                    Is.EqualTo(
                        FindGameObjectByPath(
                            scene,
                            "Systems/SetupFeedback")));
                var serializedSetupFeedback =
                    new SerializedObject(setupFeedback);
                var setupFeedbackSource =
                    serializedSetupFeedback
                        .FindProperty("_audioSource")
                        .objectReferenceValue as AudioSource;
                Assert.That(setupFeedbackSource, Is.Not.Null);
                Assert.That(setupFeedbackSource.playOnAwake, Is.False);
                Assert.That(setupFeedbackSource.loop, Is.False);
                Assert.That(setupFeedbackSource.spatialBlend, Is.Zero);
                Assert.That(
                    serializedSetupFeedback
                        .FindProperty("_useGeneratedPlaceholders")
                        .boolValue,
                    Is.True);
                Assert.That(
                    serializedSetupFeedback
                        .FindProperty("_hapticAmplitude")
                        .floatValue,
                    Is.InRange(0.1f, 0.4f));
                Assert.That(
                    serializedSetupFeedback
                        .FindProperty("_hapticSeconds")
                        .floatValue,
                    Is.InRange(0.02f, 0.08f));

                var avatarRegistry =
                    GetSingle<AvatarRegistry>(scene);
                Assert.That(
                    avatarRegistry.Avatars,
                    Is.Not.Empty);
                Assert.That(
                    avatarRegistry.Avatars
                        .Select(definition =>
                            definition.DisplayName),
                    Is.EquivalentTo(new[]
                    {
                        "男性アバター",
                        "女性アバター"
                    }),
                    "Setup must show player-facing avatar names, not " +
                    "internal costume identifiers.");
                foreach (var definition in avatarRegistry.Avatars)
                {
                    Assert.That(definition, Is.Not.Null);
                    var growthOutput = definition.GrowthOutput;
                    Assert.That(growthOutput, Is.Not.Null);
                    var serializedGrowth =
                        new SerializedObject(growthOutput);
                    Assert.That(
                        serializedGrowth
                            .FindProperty("_driveSurface")
                            .boolValue,
                        Is.True,
                        $"{definition.DisplayName} must present earned " +
                        "growth through its existing normal-mapped skin.");
                    var restingNormal = serializedGrowth
                        .FindProperty("_restingNormalScale")
                        .floatValue;
                    var trainedNormal = serializedGrowth
                        .FindProperty("_trainedNormalScale")
                        .floatValue;
                    var restingSmoothness = serializedGrowth
                        .FindProperty("_restingSmoothness")
                        .floatValue;
                    var trainedSmoothness = serializedGrowth
                        .FindProperty("_trainedSmoothness")
                        .floatValue;
                    Assert.That(restingNormal, Is.InRange(0.5f, 1f));
                    Assert.That(
                        trainedNormal,
                        Is.InRange(restingNormal, 1.25f));
                    Assert.That(
                        restingSmoothness,
                        Is.InRange(0.05f, 0.35f));
                    Assert.That(
                        trainedSmoothness,
                        Is.InRange(restingSmoothness, 0.4f));
                    var bodyRenderers = new Renderer[]
                    {
                        definition.TrackedBodyRenderer,
                        definition.MirrorBodyRenderer
                    };
                    Assert.That(
                        MuscleSurfaceStateSynchronizer
                            .TryValidateTargets(
                                bodyRenderers,
                                out var surfaceError),
                        Is.True,
                        $"{definition.DisplayName}: {surfaceError}");
                    foreach (var bodyRenderer in bodyRenderers)
                    {
                        foreach (var material in
                                 bodyRenderer.sharedMaterials)
                        {
                            Assert.That(material, Is.Not.Null);
                            Assert.That(
                                material.IsKeywordEnabled("_NORMALMAP"),
                                Is.True,
                                $"{definition.DisplayName} skin must " +
                                "compile its assigned normal map.");
                        }
                    }
                    var cinematicRenderers =
                        definition.CinematicPrefab
                            .GetComponentsInChildren<
                                SkinnedMeshRenderer>(true);
                    var cinematicSurfaceRenderers =
                        new List<Renderer>();
                    Assert.That(
                        MuscleSurfaceStateSynchronizer
                            .TryCollectConfiguredTargets(
                                growthOutput,
                                definition.TrackedBodyRenderer,
                                cinematicRenderers,
                                cinematicSurfaceRenderers,
                                out surfaceError),
                        Is.True,
                        $"{definition.DisplayName}: {surfaceError}");
                    Assert.That(
                        MuscleSurfaceStateSynchronizer
                            .TryValidateTargets(
                                cinematicSurfaceRenderers,
                                out surfaceError),
                        Is.True,
                        $"{definition.DisplayName}: {surfaceError}");
                }
                var transformationAudio =
                    presenterObject.GetComponent<AudioSource>();
                Assert.That(transformationAudio, Is.Not.Null);
                Assert.That(
                    transformationAudio.playOnAwake,
                    Is.False);
                Assert.That(transformationAudio.loop, Is.False);
                Assert.That(
                    transformationAudio.spatialBlend,
                    Is.EqualTo(0f).Within(0.0001f));

                var transition =
                    GetSingle<StageTransitionController>(scene);
                AssertReferences(
                    transition,
                    "_cameraRig",
                    "_desktopSimulation",
                    "_trainingArrivalPoint",
                    "_stageArrivalPoint",
                    "_judgeArrivalPoint",
                    "_screenFade");

                var posing =
                    GetSingle<ThreePointPosingScorer>(scene);
                AssertReferences(
                    posing,
                    "_gameFlow",
                    "_cameraRig",
                    "_stageFrame",
                    "_heightCalibration");
                Assert.That(
                    new SerializedObject(posing)
                        .FindProperty("_trackingRecoverySeconds")
                        .floatValue,
                    Is.InRange(0.25f, 0.5f),
                    "Posing must wait briefly after tracking returns so " +
                    "a reacquisition frame cannot become an accidental " +
                    "hold or success.");

                // The routine is what the phase length and the final
                // score are both derived from, so an empty or half wired
                // list would silently shorten the posing phase.
                var posingProfiles =
                    new SerializedObject(posing)
                        .FindProperty("_profiles");
                Assert.That(
                    posingProfiles.arraySize,
                    Is.GreaterThanOrEqualTo(1),
                    "The posing routine must list at least one pose.");
                for (var index = 0;
                     index < posingProfiles.arraySize;
                     index++)
                {
                    Assert.That(
                        posingProfiles
                            .GetArrayElementAtIndex(index)
                            .objectReferenceValue,
                        Is.Not.Null,
                        $"Posing routine slot {index} is empty.");
                }

                Assert.That(
                    posing.TotalRoutineSeconds,
                    Is.GreaterThan(0f));
                Assert.That(
                    posing.CurrentProfile,
                    Is.Not.Null);

                var poseGuide =
                    GetSingle<PoseReferenceGuide>(scene);
                AssertReferences(
                    poseGuide,
                    "_posingScorer",
                    "_gameFlow",
                    "_visualRoot",
                    "_primaryGuide",
                    "_contentRect",
                    "_headMarker",
                    "_leftHandMarker",
                    "_rightHandMarker",
                    "_leftArmLine",
                    "_rightArmLine",
                    "_sideHeadMarker",
                    "_sideLeftHandMarker",
                    "_sideRightHandMarker",
                    "_sideLeftArmLine",
                    "_sideRightArmLine");

                // The reference figure now hangs on the jumbotron rather
                // than in the floating flow HUD, as a front view above a
                // profile view. The profile view is what tells the player
                // how far forward a hand belongs, which is also what keeps
                // them from reaching back out of controller tracking.
                var guideCanvas = FindGameObjectByPath(
                    scene,
                    "Phase_Posing/Stage/PrototypeStageVisuals/" +
                    "PoseMonitor/PoseGuideCanvas");
                Assert.That(guideCanvas, Is.Not.Null);
                foreach (var panelName in new[]
                         {
                             "FrontPanel",
                             "SidePanel"
                         })
                {
                    var panel =
                        guideCanvas.transform.Find(panelName);
                    Assert.That(
                        panel,
                        Is.Not.Null,
                        $"{panelName} must hang on the jumbotron.");
                    Assert.That(
                        panel.Find("Title"),
                        Is.Not.Null);
                    Assert.That(
                        panel.Find("Silhouette/Torso"),
                        Is.Not.Null);
                }

                var stageArrivalForGuide = new SerializedObject(
                        GetSingle<StageTransitionController>(scene))
                    .FindProperty("_stageArrivalPoint")
                    .objectReferenceValue as Transform;
                Assert.That(stageArrivalForGuide, Is.Not.Null);
                Assert.That(
                    Vector3.Dot(
                        guideCanvas.transform.position -
                        stageArrivalForGuide.position,
                        stageArrivalForGuide.forward),
                    Is.GreaterThan(14f),
                    "The guide must sit out on the jumbotron, not " +
                    "overlaid on the performer.");

                var poseGhost =
                    GetSingle<PoseGhostGuideController>(scene);
                AssertReferences(
                    poseGhost,
                    "_gameFlow",
                    "_avatarRegistry",
                    "_profile",
                    "_cameraRig",
                    "_heightCalibration",
                    "_stageFrame",
                    "_ghostParent",
                    "_ghostMaterial");
                Assert.That(
                    poseGhost.gameObject.layer,
                    Is.EqualTo(
                        LayerMask.NameToLayer("PoseGuide")));
                var poseGuideSerialized =
                    new SerializedObject(poseGuide);
                Assert.That(
                    poseGuideSerialized
                        .FindProperty("_primaryGuide")
                        .objectReferenceValue,
                    Is.EqualTo(poseGhost));
                Assert.That(
                    poseGuideSerialized
                        .FindProperty("_displayMode")
                        .enumValueIndex,
                    Is.EqualTo((int)
                        PoseReferenceDisplayMode
                            .AutomaticFallback),
                    "The 3D guide is the production reference; the flat " +
                    "guide must remain an automatic fallback.");

                var result =
                    GetSingle<ExperienceResultController>(scene);
                AssertReferences(
                    result,
                    "_gameFlow",
                    "_profile");

                var muscle =
                    GetSingle<MuscleGrowthController>(scene);
                AssertReferences(
                    muscle,
                    "_gameFlow",
                    "_rhythmScorer");
                AssertNonEmptyArray(
                    muscle,
                    "_outputBehaviours");
                AssertProductionAvatarWiring(
                    scene,
                    muscle);

                var stageFeedback =
                    GetSingle<PoseStageFeedbackController>(scene);
                AssertReferences(
                    stageFeedback,
                    "_gameFlow",
                    "_posingScorer",
                    "_audienceRoot",
                    "_poseSuccessSource",
                    "_cheerSource");
                AssertNonEmptyArray(
                    stageFeedback,
                    "_stageLights");
                var stageFeedbackSettings =
                    new SerializedObject(stageFeedback);
                Assert.That(
                    stageFeedbackSettings
                        .FindProperty("_poseSuccessPulseSeconds")
                        .floatValue,
                    Is.EqualTo(0.65f).Within(0.0001f));
                Assert.That(
                    stageFeedbackSettings
                        .FindProperty("_poseSuccessHapticAmplitude")
                        .floatValue,
                    Is.InRange(0.1f, 0.6f),
                    "Pose success haptics should be clear but gentler " +
                    "than the transformation payoff.");
                Assert.That(
                    stageFeedbackSettings
                        .FindProperty("_poseSuccessHapticSeconds")
                        .floatValue,
                    Is.InRange(
                        PoseStageFeedbackMath.MinimumHapticSeconds,
                        PoseStageFeedbackMath.MaximumHapticSeconds),
                    "Pose success haptics must remain a short finite pulse.");
                var poseSuccessClip = stageFeedbackSettings
                    .FindProperty("_poseSuccessClip")
                    .objectReferenceValue as AudioClip;
                var usesGeneratedPoseSuccessCue = stageFeedbackSettings
                    .FindProperty("_useGeneratedPoseSuccessCue")
                    .boolValue;
                Assert.That(
                    usesGeneratedPoseSuccessCue ||
                    poseSuccessClip != null,
                    Is.True,
                    "Each confirmed pose needs either its production " +
                    "success clip or the explicit prototype fallback.");
                Assert.That(
                    stageFeedbackSettings
                        .FindProperty("_poseSuccessVolume")
                        .floatValue,
                    Is.InRange(0f, 0.3f),
                    "Pose confirmation stays below the Results cheer.");
                var poseSuccessSource = stageFeedbackSettings
                    .FindProperty("_poseSuccessSource")
                    .objectReferenceValue as AudioSource;
                Assert.That(poseSuccessSource, Is.Not.Null);
                Assert.That(
                    poseSuccessSource,
                    Is.Not.SameAs(stageFeedbackSettings
                        .FindProperty("_cheerSource")
                        .objectReferenceValue),
                    "The short confirmation must not stop an audience cheer.");
                Assert.That(poseSuccessSource.playOnAwake, Is.False);
                Assert.That(poseSuccessSource.loop, Is.False);
                Assert.That(poseSuccessSource.spatialBlend, Is.Zero);

                var stageMusic =
                    GetSingle<StageMusicController>(scene);
                AssertReferences(
                    stageMusic,
                    "_gameFlow",
                    "_musicSource");
                Assert.That(
                    stageMusic.gameObject,
                    Is.EqualTo(
                        FindGameObjectByPath(
                            scene,
                            "Systems/StageMusic")));
                var serializedStageMusic =
                    new SerializedObject(stageMusic);
                var stageMusicSource =
                    serializedStageMusic
                        .FindProperty("_musicSource")
                        .objectReferenceValue as AudioSource;
                Assert.That(stageMusicSource, Is.Not.Null);
                Assert.That(stageMusicSource.playOnAwake, Is.False);
                Assert.That(stageMusicSource.loop, Is.True);
                Assert.That(stageMusicSource.spatialBlend, Is.Zero);
                Assert.That(stageMusicSource.dopplerLevel, Is.Zero);
                Assert.That(
                    stageMusicSource,
                    Is.Not.SameAs(poseSuccessSource));
                Assert.That(
                    stageMusicSource,
                    Is.Not.SameAs(stageFeedbackSettings
                        .FindProperty("_cheerSource")
                        .objectReferenceValue));
                Assert.That(
                    serializedStageMusic
                        .FindProperty("_stageVolume")
                        .floatValue,
                    Is.InRange(0.06f, 0.16f),
                    "Stage music must stay under pose confirmation and " +
                    "audience feedback.");
                Assert.That(
                    serializedStageMusic
                        .FindProperty("_trackingVolumeMultiplier")
                        .floatValue,
                    Is.InRange(0.15f, 0.5f),
                    "Tracking recovery should be calmer without becoming " +
                    "an abrupt silence or failure cue.");
                Assert.That(
                    serializedStageMusic
                        .FindProperty("_useGeneratedPlaceholder")
                        .boolValue,
                    Is.True,
                    "Until a licensed stage track is assigned, keep the " +
                    "explicit quiet prototype fallback.");
                var stageForegroundSources =
                    serializedStageMusic
                        .FindProperty("_foregroundSources");
                Assert.That(
                    stageForegroundSources.arraySize,
                    Is.EqualTo(2));
                Assert.That(
                    Enumerable.Range(0, stageForegroundSources.arraySize)
                        .Select(index => stageForegroundSources
                            .GetArrayElementAtIndex(index)
                            .objectReferenceValue),
                    Is.EquivalentTo(new Object[]
                    {
                        poseSuccessSource,
                        stageFeedbackSettings
                            .FindProperty("_cheerSource")
                            .objectReferenceValue
                    }),
                    "Stage BGM must duck under both pose confirmation " +
                    "and audience cheers.");
                Assert.That(
                    serializedStageMusic
                        .FindProperty("_foregroundDuckMultiplier")
                        .floatValue,
                    Is.InRange(0.25f, 0.55f));
                AssertPosingStagePresentation(
                    scene,
                    transition,
                    stageFeedback);
                AssertStageEntrancePresentation(scene);

                var rhythmScorer =
                    GetSingle<TrainingRhythmScorer>(scene);
                AssertReferences(
                    rhythmScorer,
                    "_rhythmClock",
                    "_repCounter",
                    "_gameFlow",
                    "_rhythmChart");
                Assert.That(
                    new SerializedObject(rhythmScorer)
                        .FindProperty("_growthPerRep")
                        .floatValue,
                    Is.EqualTo(0.04f)
                        .Within(0.000001f));
                Assert.That(
                    new SerializedObject(rhythmScorer)
                        .FindProperty("_targetChartGrowth")
                        .floatValue,
                    Is.EqualTo(1f)
                        .Within(0.000001f));
                AssertRhythmLaneWiring(scene);
                AssertQuestTrainingLighting(scene);
                var repCounter =
                    GetSingle<BodyRepCounter>(scene);
                AssertReferences(
                    repCounter,
                    "_gameFlow");
                Assert.That(
                    new SerializedObject(repCounter)
                        .FindProperty(
                            "_countOnlyDuringTraining")
                        .boolValue,
                    Is.True);
                GetSingle<PneumaticCuffController>(scene);
                AssertMinimumStartupPermissions(
                    GetSingle<OVRManager>(scene));

                var legacyCalibrationMenu =
                    FindGameObjectByPath(
                        scene,
                        "UI/BodyTrackingCalibrationMenu");
                Assert.That(
                    legacyCalibrationMenu,
                    Is.Not.Null);
                Assert.That(
                    legacyCalibrationMenu.activeSelf,
                    Is.True,
                    "The root must stay active so Meta's " +
                    "HMD remount tracking recovery keeps " +
                    "running.");
                Assert.That(
                    legacyCalibrationMenu.GetComponent<
                        Meta.XR.Movement.Utils
                            .HMDRemountRestartTracking>(),
                    Is.Not.Null);
                foreach (Transform child in
                         legacyCalibrationMenu.transform)
                {
                    Assert.That(
                        child.gameObject.activeSelf,
                        Is.False,
                        $"Legacy menu visual {child.name} " +
                        "must stay hidden.");
                }

                var huds = GetComponents<ExperienceFlowHud>(
                    scene);
                Assert.That(huds, Has.Length.EqualTo(2));
                foreach (var hud in huds)
                {
                    AssertReferences(
                        hud,
                        "_gameFlow",
                        "_rhythmClock",
                        "_repCounter",
                        "_rhythmScorer",
                        "_posingScorer",
                        "_resultController",
                        "_muscleGrowth",
                        "_pneumaticCuff",
                        "_avatarRegistry",
                        "_bodyPoseMetrics",
                        "_alignmentCalibration",
                        "_heightCalibration",
                        "_text",
                        "_cuffStatusText");
                    AssertPlayerFont(hud, "_text");
                    if (hud.gameObject.name ==
                        "ExperienceFlowHUD")
                    {
                        AssertSetupButtonMapsFit(hud);
                    }
                    else if (hud.gameObject.name ==
                              "StageFlowHUD")
                    {
                        AssertPlayerFacingResultsFit(hud);
                        AssertFirstPoseGuide(scene, hud);
                        var serializedStageHud =
                            new SerializedObject(hud);
                        var poseGhostGuide =
                            GetSingle<PoseGhostGuideController>(scene);
                        Assert.That(
                            serializedStageHud
                                .FindProperty("_poseGhostGuide")
                                .objectReferenceValue,
                            Is.SameAs(poseGhostGuide));
                        Assert.That(
                            serializedStageHud
                                .FindProperty(
                                    "_poseAdaptiveHintDelaySeconds")
                                .floatValue,
                            Is.InRange(2.5f, 4f));
                        Assert.That(
                            serializedStageHud
                                .FindProperty(
                                    "_poseAdaptiveHintDurationSeconds")
                                .floatValue,
                            Is.InRange(2f, 3.5f));
                        var fallbackHintThreshold =
                            serializedStageHud
                                .FindProperty(
                                    "_poseAdaptiveHintMatchThreshold")
                                .floatValue;
                        Assert.That(
                            posing.CurrentProfile,
                            Is.Not.Null);
                        Assert.That(
                            fallbackHintThreshold,
                            Is.EqualTo(
                                posing.CurrentProfile
                                    .ToSettings()
                                    .Hold
                                    .MinimumSpatialScore)
                                .Within(0.0001f),
                            "The fallback guidance threshold must match " +
                            "the authored hold gate; runtime guidance " +
                            "reads each active profile directly.");
                        var serializedPoseGhost =
                            new SerializedObject(poseGhostGuide);
                        Assert.That(
                            serializedPoseGhost
                                .FindProperty(
                                    "_adaptiveHintScaleMultiplier")
                                .floatValue,
                            Is.InRange(1.05f, 1.2f));
                        Assert.That(
                            serializedPoseGhost
                                .FindProperty(
                                    "_adaptiveHintAlphaMultiplier")
                                .floatValue,
                            Is.InRange(1.4f, 2.2f));
                    }
                    Assert.That(
                        new SerializedObject(hud)
                            .FindProperty(
                                "_poseCompletionMessageSeconds")
                            .floatValue,
                        Is.EqualTo(0.9f).Within(0.0001f));
                    Assert.That(
                        new SerializedObject(hud)
                            .FindProperty(
                                "_resultRevealDurationSeconds")
                            .floatValue,
                        Is.EqualTo(0.6f).Within(0.0001f));
                    Assert.That(
                        new SerializedObject(hud)
                            .FindProperty(
                                "_resultRankRevealAtNormalized")
                            .floatValue,
                        Is.EqualTo(0.75f).Within(0.0001f));
                }

                var adaptiveHintThresholds = huds
                    .Select(hud =>
                        new SerializedObject(hud)
                            .FindProperty(
                                "_poseAdaptiveHintMatchThreshold")
                            .floatValue)
                    .Distinct()
                    .ToArray();
                Assert.That(
                    adaptiveHintThresholds,
                    Has.Length.EqualTo(1),
                    "Both player-facing HUDs must use the same recovery " +
                    "threshold so the posing guidance cannot depend on " +
                    "which phase root is visible.");
            }
            finally
            {
                if (openedForTest && scene.IsValid())
                {
                    EditorSceneManager.CloseScene(
                        scene,
                        true);
                }
            }
        }

        private static T GetSingle<T>(Scene scene)
            where T : Component
        {
            var components = GetComponents<T>(scene);
            Assert.That(
                components,
                Has.Length.EqualTo(1),
                $"Expected exactly one {typeof(T).Name} " +
                $"in {PrototypeScenePath}.");
            return components[0];
        }

        private static T[] GetComponents<T>(Scene scene)
            where T : Component
        {
            return Resources.FindObjectsOfTypeAll<T>()
                .Where(component =>
                    component != null &&
                    component.gameObject.scene == scene)
                .ToArray();
        }

        private static GameObject FindGameObjectByPath(
            Scene scene,
            string path)
        {
            var segments = path.Split('/');
            var current = scene.GetRootGameObjects()
                .FirstOrDefault(root =>
                    root.name == segments[0]);
            for (var index = 1;
                 current != null &&
                 index < segments.Length;
                 index++)
            {
                var child = current.transform.Find(
                    segments[index]);
                current = child == null
                    ? null
                    : child.gameObject;
            }

            return current;
        }

        private static void AssertReferences(
            Object target,
            params string[] propertyNames)
        {
            var serialized = new SerializedObject(target);
            foreach (var propertyName in propertyNames)
            {
                var property =
                    serialized.FindProperty(propertyName);
                Assert.That(
                    property,
                    Is.Not.Null,
                    $"{target.name} is missing serialized " +
                    $"property {propertyName}.");
                Assert.That(
                    property.objectReferenceValue,
                    Is.Not.Null,
                    $"{target.name}.{propertyName} is not wired.");
            }
        }

        private static void AssertPlayerFont(
            Object target,
            params string[] propertyNames)
        {
            var serialized = new SerializedObject(target);
            foreach (var propertyName in propertyNames)
            {
                var text = serialized.FindProperty(propertyName)
                    ?.objectReferenceValue as TMPro.TMP_Text;
                Assert.That(
                    text,
                    Is.Not.Null,
                    $"{target.name}.{propertyName} must reference TMP text.");
                Assert.That(
                    AssetDatabase.GetAssetPath(text.font),
                    Is.EqualTo(PlayerFontPath),
                    $"{target.name}.{propertyName} must use the " +
                    "Japanese player UI font.");
            }
        }

        private static void AssertSetupButtonMapsFit(
            ExperienceFlowHud hud)
        {
            var hudRect = hud.GetComponent<RectTransform>();
            Assert.That(hudRect, Is.Not.Null);
            Assert.That(
                hudRect.sizeDelta,
                Is.EqualTo(new Vector2(500f, 580f)),
                "Setup guidance needs enough vertical room for the three " +
                "checks, recovery copy, controller map, and safety footer.");
            var text = new SerializedObject(hud)
                .FindProperty("_text")
                .objectReferenceValue as TMPro.TMP_Text;
            Assert.That(text, Is.Not.Null);
            var models = new[]
            {
                SetupReadyUiModelBuilder.Build(
                    new SetupReadyUiState(
                        SetupReadyPhase.Setup,
                        true,
                        false,
                        false)),
                SetupReadyUiModelBuilder.Build(
                    new SetupReadyUiState(
                        SetupReadyPhase.Setup,
                        true,
                        true,
                        false)),
                SetupReadyUiModelBuilder.Build(
                    new SetupReadyUiState(
                        SetupReadyPhase.Ready,
                        true,
                        true,
                        true))
            };

            var availableWidth = text.rectTransform.rect.width;
            Assert.That(
                availableWidth,
                Is.GreaterThan(0f));
            Assert.That(
                text.rectTransform.rect.height,
                Is.GreaterThanOrEqualTo(510f),
                "The authored text rect should already match the runtime " +
                "32px margins and avoid a first-frame layout jump.");
            Assert.That(
                text.enableAutoSizing,
                Is.False,
                "Setup copy should fit at the authored player-readable " +
                "font size instead of silently shrinking on Quest.");
            foreach (var model in models)
            {
                var richText =
                    SetupReadyButtonMapFormatter.FormatRichText(
                        model.PrimaryHint);
                Assert.That(richText, Is.Not.Empty);
                Assert.That(
                    richText.TrimEnd('\n'),
                    Does.Not.Contain("\n"),
                    "Each button instruction must remain a single glanceable " +
                    "line in the Setup HUD.");
                var preferred = text.GetPreferredValues(
                    richText,
                    10000f,
                    10000f);
                Assert.That(
                    preferred.x,
                    Is.LessThanOrEqualTo(availableWidth),
                    $"The {model.PrimaryHint.ButtonLabel} map is " +
                    $"{preferred.x:F1}px wide but the Setup HUD only " +
                    $"has {availableWidth:F1}px.");
            }
        }

        private static void AssertPlayerFacingResultsFit(
            ExperienceFlowHud hud)
        {
            var hudRect = hud.GetComponent<RectTransform>();
            Assert.That(hudRect, Is.Not.Null);
            Assert.That(
                hudRect.sizeDelta,
                Is.EqualTo(new Vector2(780f, 520f)),
                "Results are compact after removing the pose-score block " +
                "while retaining a readable safe exit instruction.");
            var text = new SerializedObject(hud)
                .FindProperty("_text")
                .objectReferenceValue as TMPro.TMP_Text;
            Assert.That(text, Is.Not.Null);
            Assert.That(
                text.rectTransform.rect.size,
                Is.EqualTo(new Vector2(716f, 440f)),
                "The authored Results text rect must already match the " +
                "runtime margins and avoid a first-frame layout jump.");

            var result = new ExperienceScoreResult(
                88f,
                76f,
                64f,
                81.2f,
                78.1f,
                ExperienceRank.A);
            var input = new ExperienceScoreInput(
                16,
                14,
                0.82f,
                0.64f);
            var view =
                PlayerFacingPoseCopyFormatter.FormatResults(
                    true,
                    result,
                    input,
                    hasTrainingTimingData: true,
                    poseSuccessCount: 2,
                    poseCount: 3);
            var initialView =
                PlayerFacingResultsRevealFormatter.Apply(
                    view,
                    PlayerFacingResultsRevealMath.Evaluate(
                        0f,
                        0.6f,
                        0.75f,
                        result.FinalScore));
            var states = new[]
            {
                (Name: "initial reveal",
                    View: initialView,
                    RankColor:
                        PlayerFacingResultsStyle.MissingColor),
                (Name: "final result",
                    View: view,
                    RankColor:
                        PlayerFacingResultsStyle.GetRankColor(
                            result.Rank))
            };
            foreach (var state in states)
            {
                var richText =
                    PlayerFacingResultsRichTextFormatter.Format(
                        state.View,
                        state.RankColor);
                var preferred = text.GetPreferredValues(
                    richText,
                    text.rectTransform.rect.width,
                    10000f);

                Assert.That(
                    preferred.x,
                    Is.LessThanOrEqualTo(
                        text.rectTransform.rect.width),
                    $"The {state.Name} text needs " +
                    $"{preferred.x:F1}px of width but only " +
                    $"{text.rectTransform.rect.width:F1}px is " +
                    "available.");
                Assert.That(
                    preferred.y,
                    Is.LessThanOrEqualTo(
                        text.rectTransform.rect.height),
                    $"The {state.Name} text needs " +
                    $"{preferred.y:F1}px of height but only " +
                    $"{text.rectTransform.rect.height:F1}px is " +
                    "available.");
            }
        }

        private static void AssertFirstPoseGuide(
            Scene scene,
            ExperienceFlowHud hud)
        {
            var guide = FindGameObjectByPath(
                scene,
                "Phase_Posing/Stage/PrototypeStageVisuals/" +
                "PoseMonitor/FirstPoseGuideCanvas");
            Assert.That(guide, Is.Not.Null);
            Assert.That(
                guide.activeSelf,
                Is.False,
                "The first-pose guide must remain hidden until the " +
                "first countdown begins.");

            var serializedHud = new SerializedObject(hud);
            Assert.That(
                serializedHud.FindProperty("_firstPoseGuideRoot")
                    .objectReferenceValue,
                Is.SameAs(guide));
            var guideText = serializedHud
                .FindProperty("_firstPoseGuideText")
                .objectReferenceValue as TMPro.TMP_Text;
            Assert.That(guideText, Is.Not.Null);
            Assert.That(
                guideText.transform.IsChildOf(guide.transform),
                Is.True);
            Assert.That(
                AssetDatabase.GetAssetPath(guideText.font),
                Is.EqualTo(PlayerFontPath));
            Assert.That(guideText.enableAutoSizing, Is.True);
            Assert.That(guideText.raycastTarget, Is.False);
            Assert.That(
                guideText.text,
                Does.Contain("大きい人影＝あなた"));
            Assert.That(
                guideText.text,
                Does.Contain("腕だけ合わせる"));
            Assert.That(guideText.text, Does.Contain("3秒"));

            var canvas = guide.GetComponent<Canvas>();
            var guideRect = guide.GetComponent<RectTransform>();
            Assert.That(canvas, Is.Not.Null);
            Assert.That(guideRect, Is.Not.Null);
            Assert.That(
                canvas.renderMode,
                Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(
                canvas.sortingOrder,
                Is.EqualTo(20),
                "The guide must render above the monitor screen.");
            Assert.That(
                guideRect.sizeDelta,
                Is.EqualTo(new Vector2(800f, 450f)));
            Assert.That(
                guideRect.localScale,
                Is.EqualTo(Vector3.one * 0.01f));

            var screen = guide.transform.parent.Find("Screen");
            Assert.That(screen, Is.Not.Null);
            Assert.That(
                guideRect.localPosition.x,
                Is.EqualTo(screen.localPosition.x).Within(0.001f));
            Assert.That(
                guideRect.localPosition.y,
                Is.EqualTo(screen.localPosition.y).Within(0.001f));
            Assert.That(
                guideRect.localPosition.z,
                Is.LessThan(screen.localPosition.z),
                "The guide must sit just in front of the monitor screen " +
                "toward the participant.");
            Assert.That(
                screen.localPosition.z - guideRect.localPosition.z,
                Is.InRange(0.02f, 0.12f));

            var panel = guide.transform.Find("FirstPoseGuidePanel")
                as RectTransform;
            Assert.That(panel, Is.Not.Null);
            Assert.That(
                panel.anchorMin,
                Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(
                panel.anchorMax,
                Is.EqualTo(new Vector2(1f, 1f)));
            Assert.That(
                panel.pivot,
                Is.EqualTo(new Vector2(0.5f, 1f)));
            Assert.That(
                panel.anchoredPosition.y,
                Is.EqualTo(-20f).Within(0.001f),
                "The guide must stay at the top of the monitor so the " +
                "audience cannot hide it.");
            Assert.That(
                panel.sizeDelta.y,
                Is.LessThanOrEqualTo(guideRect.rect.height * 0.38f),
                "The onboarding strip must leave most of the live avatar " +
                "visible.");
            var panelImage = panel.GetComponent<UnityEngine.UI.Image>();
            Assert.That(panelImage, Is.Not.Null);
            Assert.That(panelImage.raycastTarget, Is.False);
            Assert.That(panelImage.color.a, Is.InRange(0.75f, 0.9f));

            var preferred = guideText.GetPreferredValues(
                guideText.text,
                guideText.rectTransform.rect.width,
                10000f);
            Assert.That(
                preferred.y,
                Is.LessThanOrEqualTo(
                    guideText.rectTransform.rect.height + 0.01f),
                $"First-pose guide needs {preferred.y:F1}px but has " +
                $"{guideText.rectTransform.rect.height:F1}px.");
        }

        private static void AssertNonEmptyArray(
            Object target,
            string propertyName)
        {
            var property = new SerializedObject(target)
                .FindProperty(propertyName);
            Assert.That(property, Is.Not.Null);
            Assert.That(property.isArray, Is.True);
            Assert.That(
                property.arraySize,
                Is.GreaterThan(0),
                $"{target.name}.{propertyName} is empty.");
            for (var index = 0;
                 index < property.arraySize;
                 index++)
            {
                Assert.That(
                    property
                        .GetArrayElementAtIndex(index)
                        .objectReferenceValue,
                    Is.Not.Null,
                    $"{target.name}.{propertyName}[{index}] " +
                    "is not wired.");
            }
        }

        private static void AssertRhythmLaneWiring(
            Scene scene)
        {
            const string patternPath =
                "Assets/IVRC/Config/" +
                "DefaultTrainingRhythmPattern.asset";
            var pattern =
                AssetDatabase.LoadAssetAtPath<
                    TrainingRhythmPattern>(patternPath);
            Assert.That(pattern, Is.Not.Null);
            Assert.That(
                pattern.TotalRepetitions,
                Is.EqualTo(28));
            Assert.That(
                pattern.TotalRequiredSideRepetitions,
                Is.EqualTo(56));
            Assert.That(pattern.TotalBeats, Is.EqualTo(132));

            // Every block plus the gap after it has to be a whole number
            // of bars, and eight of them, or the exercise walks away from
            // the music's phrasing a bar at a time. Eight repetitions plus
            // a bar came to nine bars and did exactly that.
            var cursor = pattern.LeadInBeats;
            Assert.That(
                cursor % 4,
                Is.EqualTo(0),
                "The lead-in has to end on a bar line.");
            for (var index = 0;
                 index < pattern.Blocks.Count;
                 index++)
            {
                var block = pattern.Blocks[index];
                var cycle =
                    block.Repetitions * block.BeatsPerRep +
                    block.TransitionBeats;
                var isLast = index == pattern.Blocks.Count - 1;
                Assert.That(
                    cursor % 4,
                    Is.EqualTo(0),
                    $"Block {index} must start on a bar line.");
                Assert.That(
                    isLast ? cycle + 4 : cycle,
                    Is.EqualTo(32),
                    $"Block {index} and its gap must span eight bars.");
                cursor += cycle;
            }

            var chart =
                GetSingle<TrainingRhythmChart>(scene);
            AssertReferences(
                chart,
                "_pattern",
                "_rhythmClock",
                "_gameFlow",
                "_repCounter");
            var serializedChart = new SerializedObject(chart);
            Assert.That(
                serializedChart
                    .FindProperty("_pattern")
                    .objectReferenceValue,
                Is.SameAs(pattern));
            Assert.That(
                serializedChart
                    .FindProperty("_blockPreviewReleaseLeadBeats")
                    .floatValue,
                Is.EqualTo(1f),
                "Rep detection should stay off through the unscored " +
                "DOWN -> UP -> DOWN preview, then resume one beat " +
                "before the first scored UP.");

            var trainingAudio =
                GetSingle<TrainingAudioFeedbackController>(scene);
            AssertReferences(
                trainingAudio,
                "_gameFlow",
                "_rhythmClock",
                "_rhythmChart",
                "_rhythmScorer",
                "_audioSource");
            Assert.That(
                trainingAudio.gameObject,
                Is.SameAs(
                    FindGameObjectByPath(
                        scene,
                        "Systems/ExerciseSystem")),
                "Training audio belongs beside scoring and haptics, " +
                "not under a phase root that can disable it early.");
            var trainingAudioSource =
                trainingAudio.GetComponent<AudioSource>();
            Assert.That(trainingAudioSource, Is.Not.Null);
            var serializedTrainingAudio =
                new SerializedObject(trainingAudio);
            trainingAudioSource =
                serializedTrainingAudio
                    .FindProperty("_audioSource")
                    .objectReferenceValue as AudioSource;
            Assert.That(trainingAudioSource, Is.Not.Null);
            Assert.That(trainingAudioSource.playOnAwake, Is.False);
            Assert.That(trainingAudioSource.loop, Is.False);
            Assert.That(trainingAudioSource.spatialBlend, Is.Zero);
            foreach (var volumeProperty in new[]
                     {
                         "_countdownTickVolume",
                         "_goVolume",
                         "_motionCompleteVolume",
                         "_rhythmClearVolume",
                         "_exerciseSwitchVolume"
                     })
            {
                Assert.That(
                    serializedTrainingAudio
                        .FindProperty(volumeProperty)
                        .floatValue,
                    Is.InRange(0.05f, 0.35f),
                    $"{volumeProperty} should remain audible but " +
                    "restrained beside the existing haptics.");
            }

            var trainingMusic =
                GetSingle<TrainingMusicController>(scene);
            AssertReferences(
                trainingMusic,
                "_gameFlow",
                "_rhythmClock",
                "_musicSource");
            Assert.That(
                trainingMusic.gameObject,
                Is.SameAs(
                    FindGameObjectByPath(
                        scene,
                        "Systems/ExerciseSystem")),
                "Training music belongs beside the DSP clock and chart, " +
                "not under a phase root that can disable it early.");
            var serializedTrainingMusic =
                new SerializedObject(trainingMusic);
            var trainingMusicSource =
                serializedTrainingMusic
                    .FindProperty("_musicSource")
                    .objectReferenceValue as AudioSource;
            Assert.That(trainingMusicSource, Is.Not.Null);
            Assert.That(
                trainingMusicSource,
                Is.Not.SameAs(trainingAudioSource),
                "Looping music must not share the one-shot semantic cue " +
                "AudioSource.");
            Assert.That(trainingMusicSource.playOnAwake, Is.False);
            Assert.That(trainingMusicSource.loop, Is.True);
            Assert.That(trainingMusicSource.spatialBlend, Is.Zero);
            Assert.That(trainingMusicSource.dopplerLevel, Is.Zero);
            Assert.That(
                serializedTrainingMusic
                    .FindProperty("_musicVolume")
                    .floatValue,
                Is.InRange(0.08f, 0.2f),
                "The music bed should support the chart without masking " +
                "instructions, one-shot cues, or staff speech.");
            // Training must never be silent. Either a real track is
            // assigned or the DSP-synced placeholder stands in for one; the
            // state to catch is neither, which leaves a rhythm game with no
            // rhythm to hear and no error to explain it.
            Assert.That(
                serializedTrainingMusic
                    .FindProperty("_trainingMusicClip")
                    .objectReferenceValue != null ||
                serializedTrainingMusic
                    .FindProperty("_useGeneratedPlaceholder")
                    .boolValue,
                Is.True,
                "Training has neither a music clip nor the placeholder.");
            Assert.That(
                serializedTrainingMusic
                    .FindProperty("_foregroundCueSource")
                    .objectReferenceValue,
                Is.SameAs(trainingAudioSource),
                "Training BGM should duck under semantic one-shot cues.");
            Assert.That(
                serializedTrainingMusic
                    .FindProperty("_foregroundDuckMultiplier")
                    .floatValue,
                Is.InRange(0.25f, 0.6f));

            var lane =
                GetSingle<TrainingRhythmLaneHud>(scene);
            AssertReferences(
                lane,
                "_chart",
                "_rhythmClock",
                "_gameFlow",
                "_rhythmScorer",
                "_noteArea",
                "_judgementRing",
                "_roundedSprite",
                "_movementText",
                "_nextMovementText",
                "_progressText",
                "_feedbackText",
                "_countdownText");
            AssertPlayerFont(
                lane,
                "_movementText",
                "_nextMovementText",
                "_progressText",
                "_feedbackText",
                "_countdownText");

            var trainingHud = FindGameObjectByPath(
                scene,
                "Phase_Training/TrainingHUD");
            Assert.That(trainingHud, Is.Not.Null);
            Assert.That(
                trainingHud.transform.localPosition,
                Is.EqualTo(Vector3.zero));
            Assert.That(
                trainingHud.transform.localRotation,
                Is.EqualTo(Quaternion.identity));
            Assert.That(
                trainingHud.transform.localScale,
                Is.EqualTo(Vector3.one));

            var laneObject = FindGameObjectByPath(
                scene,
                "Phase_Training/TrainingHUD/" +
                "RhythmPulseCanvas");
            Assert.That(laneObject, Is.Not.Null);
            Assert.That(
                lane.gameObject,
                Is.SameAs(laneObject));
            Assert.That(
                laneObject.activeSelf,
                Is.False,
                "The rhythm lane should start hidden " +
                "during setup.");

            var instructionPanel = FindGameObjectByPath(
                scene,
                "Phase_Training/TrainingHUD/" +
                "RhythmPulseCanvas/InstructionPanel");
            Assert.That(
                instructionPanel,
                Is.Not.Null,
                "Training guidance needs a readable backing panel.");
            Assert.That(instructionPanel.activeSelf, Is.True);
            var instructionPanelImage =
                instructionPanel.GetComponent<UnityEngine.UI.Image>();
            Assert.That(instructionPanelImage, Is.Not.Null);
            Assert.That(
                instructionPanelImage.color.a,
                Is.InRange(0.35f, 0.85f),
                "The panel should separate guidance from the avatar " +
                "without hiding the mirror view.");

            var serializedLane = new SerializedObject(lane);
            var rhythmPoseGuide =
                GetSingle<RhythmPoseGuideController>(scene);
            Assert.That(
                serializedLane
                    .FindProperty("_poseGuide")
                    .objectReferenceValue,
                Is.SameAs(rhythmPoseGuide),
                "The adaptive training hint must emphasize the existing " +
                "line-art guide instead of creating a second tutorial.");
            Assert.That(
                serializedLane
                    .FindProperty("_adaptiveHintMissThreshold")
                    .intValue,
                Is.EqualTo(2));
            Assert.That(
                serializedLane
                    .FindProperty("_adaptiveHintDurationSeconds")
                    .floatValue,
                Is.InRange(2f, 3.5f),
                "The reminder should be readable but brief enough not to " +
                "compete with the moving chart.");
            var serializedRhythmPoseGuide =
                new SerializedObject(rhythmPoseGuide);
            Assert.That(
                serializedRhythmPoseGuide
                    .FindProperty("_adaptiveHintAlphaMultiplier")
                    .floatValue,
                Is.InRange(1.4f, 2.2f));
            Assert.That(
                serializedRhythmPoseGuide
                    .FindProperty("_adaptiveHintIntensityMultiplier")
                    .floatValue,
                Is.InRange(1.1f, 1.6f));
            foreach (var textProperty in new[]
                     {
                         "_movementText",
                         "_nextMovementText",
                         "_progressText",
                         "_feedbackText",
                         "_countdownText"
                     })
            {
                var text = serializedLane
                    .FindProperty(textProperty)
                    .objectReferenceValue as TMPro.TMP_Text;
                Assert.That(text, Is.Not.Null);
                Assert.That(
                    text.rectTransform.anchoredPosition.x,
                    Is.GreaterThan(0f),
                    $"{textProperty} should stay beside the avatar, " +
                    "not over its silhouette.");
            }

            var tutorialText = serializedLane
                .FindProperty("_nextMovementText")
                .objectReferenceValue as TMPro.TMP_Text;
            var progressText = serializedLane
                .FindProperty("_progressText")
                .objectReferenceValue as TMPro.TMP_Text;
            var feedbackText = serializedLane
                .FindProperty("_feedbackText")
                .objectReferenceValue as TMPro.TMP_Text;
            Assert.That(tutorialText, Is.Not.Null);
            Assert.That(progressText, Is.Not.Null);
            Assert.That(feedbackText, Is.Not.Null);
            Assert.That(
                tutorialText.rectTransform.anchoredPosition,
                Is.EqualTo(new Vector2(410f, 190f)));
            Assert.That(
                tutorialText.rectTransform.sizeDelta,
                Is.EqualTo(new Vector2(330f, 140f)),
                "The four-line practice and safety copy must fit without " +
                "shrinking below its configured readable range.");

            var tutorialTop =
                tutorialText.rectTransform.anchoredPosition.y +
                tutorialText.rectTransform.sizeDelta.y * 0.5f;
            var tutorialBottom =
                tutorialText.rectTransform.anchoredPosition.y -
                tutorialText.rectTransform.sizeDelta.y * 0.5f;
            var progressBottom =
                progressText.rectTransform.anchoredPosition.y -
                progressText.rectTransform.sizeDelta.y * 0.5f;
            var feedbackTop =
                feedbackText.rectTransform.anchoredPosition.y +
                feedbackText.rectTransform.sizeDelta.y * 0.5f;
            Assert.That(
                tutorialTop,
                Is.LessThanOrEqualTo(progressBottom),
                "Practice copy should not overlap the repetition count.");
            Assert.That(
                tutorialBottom,
                Is.GreaterThanOrEqualTo(feedbackTop),
                "Practice copy should not overlap success feedback.");

            var legacyLaneLegend = FindGameObjectByPath(
                scene,
                "Phase_Training/TrainingHUD/" +
                "RhythmPulseCanvas/LaneLegend");
            if (legacyLaneLegend != null)
            {
                Assert.That(
                    legacyLaneLegend.activeSelf,
                    Is.False,
                    "Legacy edge instructions conflict with the " +
                    "current line-art timing guide.");
            }

            var canvas = lane.GetComponent<Canvas>();
            Assert.That(canvas, Is.Not.Null);
            Assert.That(
                canvas.renderMode,
                Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(
                canvas.sortingOrder,
                Is.EqualTo(30));
            Assert.That(
                Vector3.Distance(
                    lane.transform.position,
                    new Vector3(0f, 1.35f, 0.02f)),
                Is.LessThan(0.000001f));
            Assert.That(
                Vector3.Distance(
                    lane.transform.localScale,
                    Vector3.one * 0.0015f),
                Is.LessThan(0.000001f));
            var canvasRect =
                lane.GetComponent<RectTransform>();
            Assert.That(
                Vector2.Distance(
                    canvasRect.sizeDelta,
                    new Vector2(520f, 620f)),
                Is.LessThan(0.000001f));
            var noteArea = lane.transform
                .Find("NoteArea")
                .GetComponent<RectTransform>();
            var judgementRing = lane.transform
                .Find("JudgementRing")
                .GetComponent<RectTransform>();
            Assert.That(
                noteArea.GetComponent<
                    UnityEngine.UI.RectMask2D>(),
                Is.Not.Null);
            Assert.That(
                noteArea.anchoredPosition,
                Is.EqualTo(Vector2.zero));
            Assert.That(
                noteArea.sizeDelta,
                Is.EqualTo(new Vector2(500f, 500f)));
            Assert.That(
                judgementRing.anchoredPosition,
                Is.EqualTo(Vector2.zero));
            Assert.That(
                judgementRing.sizeDelta,
                Is.EqualTo(Vector2.one * 112f));
            Assert.That(
                judgementRing.position.y,
                Is.EqualTo(1.35f)
                    .Within(0.000001f));
            Assert.That(
                lane.transform.Find("LaneBackdrop")
                    .gameObject.activeSelf,
                Is.False);
            Assert.That(
                lane.transform.Find("LaneTrack")
                    .gameObject.activeSelf,
                Is.False);
            Assert.That(
                lane.transform.Find("HeaderAccent")
                    .gameObject.activeSelf,
                Is.False);
            Assert.That(
                lane.transform.Find("JudgementLine")
                    .gameObject.activeSelf,
                Is.False);
            Assert.That(
                lane.transform.Find("LaneLegend")
                    .gameObject.activeSelf,
                Is.False);
            var clockTempo =
                new SerializedObject(
                        GetSingle<RhythmClock>(scene))
                    .FindProperty("_beatsPerMinute")
                    .floatValue;
            Assert.That(clockTempo, Is.EqualTo(154f));

            // The bed is resampled from the clip's declared tempo to the
            // clock's, and pitch and speed are one control in Unity, so the
            // two disagreeing transposes the music rather than erroring.
            // They were 120 against a clip written at 154, which is four
            // semitones of silent wrongness.
            Assert.That(
                new SerializedObject(
                        GetSingle<TrainingMusicController>(scene))
                    .FindProperty("_clipBeatsPerMinute")
                    .floatValue,
                Is.EqualTo(clockTempo),
                "The training bed must not be transposed. Match the " +
                "clip's declared tempo to the clock, or retime the " +
                "clock to the clip.");

            var visibility =
                GetSingle<TrainingUiVisibilityController>(
                    scene);
            AssertReferences(
                visibility,
                "_gameFlow",
                "_bodyMetricsHudRoot",
                "_trainingFlowHudRoot",
                "_rhythmLaneRoot");
            var serializedVisibility =
                new SerializedObject(visibility);
            Assert.That(
                serializedVisibility.FindProperty(
                        "_showBodyMetricsDebug")
                    .boolValue,
                Is.False,
                "Body metrics are debug-only in the player experience.");
            Assert.That(
                serializedVisibility.FindProperty(
                        "_bodyMetricsHudRoot")
                    .objectReferenceValue,
                Is.SameAs(
                    FindGameObjectByPath(
                        scene,
                        "Phase_Training/" +
                        "MirrorWall_Opening/" +
                        "BodyMetricsHUD")));
            Assert.That(
                serializedVisibility.FindProperty(
                        "_trainingFlowHudRoot")
                    .objectReferenceValue,
                Is.SameAs(
                    FindGameObjectByPath(
                        scene,
                        "Phase_Training/" +
                        "MirrorWall_Opening/" +
                        "ExperienceFlowHUD")));
            Assert.That(
                serializedVisibility.FindProperty(
                        "_rhythmLaneRoot")
                    .objectReferenceValue,
                Is.SameAs(laneObject));

            var scoreProfile =
                AssetDatabase.LoadAssetAtPath<
                    ExperienceScoreProfile>(
                    "Assets/IVRC/Config/" +
                    "DefaultExperienceScoreProfile.asset");
            Assert.That(scoreProfile, Is.Not.Null);
            var settings = scoreProfile.ToSettings();
            Assert.That(settings.TargetElbowReps, Is.EqualTo(32));
            Assert.That(settings.TargetArmReps, Is.EqualTo(32));
        }

        private static void AssertQuestTrainingLighting(
            Scene scene)
        {
            const string roomMaterialPath =
                "Assets/IVRC/Materials/" +
                "TrainingRoomSurface.mat";
            const string reflectionPath =
                "Assets/IVRC/Materials/" +
                "TrainingRoomReflection.exr";
            const string mobilePipelinePath =
                "Assets/Settings/Mobile_RPAsset.asset";

            var mobilePipeline =
                AssetDatabase.LoadMainAssetAtPath(
                    mobilePipelinePath);
            Assert.That(mobilePipeline, Is.Not.Null);
            var serializedPipeline =
                new SerializedObject(mobilePipeline);
            AssertBooleanProperty(
                serializedPipeline,
                "m_MainLightShadowsSupported",
                true);
            AssertBooleanProperty(
                serializedPipeline,
                "m_SoftShadowsSupported",
                true);
            AssertBooleanProperty(
                serializedPipeline,
                "m_AdditionalLightShadowsSupported",
                false);
            AssertBooleanProperty(
                serializedPipeline,
                "m_ReflectionProbeBoxProjection",
                true);
            AssertIntegerProperty(
                serializedPipeline,
                "m_MainLightShadowmapResolution",
                1024);
            AssertIntegerProperty(
                serializedPipeline,
                "m_ShadowCascadeCount",
                1);
            Assert.That(
                serializedPipeline.FindProperty(
                        "m_ShadowDistance")
                    .floatValue,
                Is.LessThanOrEqualTo(15f),
                "Quest shadow distance should stay room-sized.");

            var lightingRoot = FindGameObjectByPath(
                scene,
                "Phase_Training/TrainingLighting");
            Assert.That(lightingRoot, Is.Not.Null);
            Assert.That(lightingRoot.activeInHierarchy, Is.True);

            var trainingLights =
                lightingRoot.GetComponentsInChildren<Light>(
                    true);
            Assert.That(
                trainingLights,
                Has.Length.EqualTo(2),
                "Training uses one key and one fill light.");

            var directionalObject = FindGameObjectByPath(
                scene,
                "Phase_Training/TrainingLighting/" +
                "Training Key Directional");
            var pointObject = FindGameObjectByPath(
                scene,
                "Phase_Training/TrainingLighting/" +
                "Training Fill Point");
            Assert.That(directionalObject, Is.Not.Null);
            Assert.That(pointObject, Is.Not.Null);

            var directional =
                directionalObject.GetComponent<Light>();
            var point = pointObject.GetComponent<Light>();
            Assert.That(directional, Is.Not.Null);
            Assert.That(point, Is.Not.Null);

            var sharedLightMask = LayerMask.GetMask(
                "Default",
                "Character",
                "MirroredCharacter");
            Assert.That(
                directional.type,
                Is.EqualTo(LightType.Directional));
            Assert.That(directional.enabled, Is.True);
            Assert.That(
                directional.lightmapBakeType,
                Is.EqualTo(LightmapBakeType.Realtime));
            Assert.That(
                directional.shadows,
                Is.EqualTo(LightShadows.Soft));
            Assert.That(
                directional.cullingMask,
                Is.EqualTo(sharedLightMask));
            Assert.That(
                Mathf.Abs(
                    directional.transform.forward.z),
                Is.LessThan(0.0001f),
                "The key direction must be invariant across " +
                "the mirror plane.");

            Assert.That(
                point.type,
                Is.EqualTo(LightType.Point));
            Assert.That(point.enabled, Is.True);
            Assert.That(
                point.lightmapBakeType,
                Is.EqualTo(LightmapBakeType.Realtime));
            Assert.That(
                point.shadows,
                Is.EqualTo(LightShadows.None));
            Assert.That(
                point.cullingMask,
                Is.EqualTo(sharedLightMask));
            Assert.That(
                point.transform.position.z,
                Is.EqualTo(0.06f).Within(0.001f),
                "The fill light must stay on the mirror plane.");

            foreach (var legacyPath in new[]
                     {
                         "Environment/Directional Light",
                         "Environment/" +
                         "Directional Light Mirrored",
                         "Environment/" +
                         "Directional Light Mirrored Rim",
                         "Environment/Spot Light"
                     })
            {
                var legacyObject = FindGameObjectByPath(
                    scene,
                    legacyPath);
                Assert.That(
                    legacyObject,
                    Is.Not.Null,
                    $"Legacy light was not found: " +
                    $"{legacyPath}");
                Assert.That(
                    legacyObject.activeSelf,
                    Is.False,
                    $"{legacyPath} must stay disabled.");
            }

            var probeObject = FindGameObjectByPath(
                scene,
                "Phase_Training/TrainingLighting/" +
                "Training Room Reflection Probe");
            Assert.That(probeObject, Is.Not.Null);
            var probe =
                probeObject.GetComponent<ReflectionProbe>();
            Assert.That(probe, Is.Not.Null);
            Assert.That(probe.enabled, Is.True);
            Assert.That(
                probe.mode,
                Is.EqualTo(ReflectionProbeMode.Custom));
            Assert.That(probe.boxProjection, Is.True);
            Assert.That(probe.resolution, Is.EqualTo(128));
            Assert.That(
                probe.size,
                Is.EqualTo(new Vector3(20f, 3.1f, 20f)));
            Assert.That(
                probe.cullingMask,
                Is.EqualTo(
                    LayerMask.GetMask("Default")));
            Assert.That(
                probe.customBakedTexture,
                Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(
                    probe.customBakedTexture),
                Is.EqualTo(reflectionPath));

            var roomSurfacePaths = new[]
            {
                "Phase_Training/TrainingRoom/Wall1",
                "Phase_Training/TrainingRoom/Wall2",
                "Phase_Training/TrainingRoom/Wall3",
                "Phase_Training/TrainingRoom/Wall4",
                "Phase_Training/TrainingRoom/Ceiling",
                "Phase_Training/MirrorWall_Opening/" +
                "Wall_Left",
                "Phase_Training/MirrorWall_Opening/" +
                "Wall_Right",
                "Phase_Training/MirrorWall_Opening/" +
                "Wall_Top"
            };
            foreach (var surfacePath in roomSurfacePaths)
            {
                var surface = FindGameObjectByPath(
                    scene,
                    surfacePath);
                Assert.That(
                    surface,
                    Is.Not.Null,
                    $"Room surface was not found: " +
                    $"{surfacePath}");
                var renderer =
                    surface.GetComponent<MeshRenderer>();
                Assert.That(renderer, Is.Not.Null);
                Assert.That(
                    AssetDatabase.GetAssetPath(
                        renderer.sharedMaterial),
                    Is.EqualTo(roomMaterialPath));
                Assert.That(
                    renderer.sharedMaterial.shader.name,
                    Is.EqualTo(
                        "Universal Render Pipeline/Unlit"));
                Assert.That(
                    renderer.shadowCastingMode,
                    Is.EqualTo(ShadowCastingMode.Off));
                Assert.That(
                    renderer.receiveShadows,
                    Is.False);
            }

            var ceiling = FindGameObjectByPath(
                scene,
                "Phase_Training/TrainingRoom/Ceiling");
            Assert.That(
                ceiling.transform.localPosition,
                Is.EqualTo(new Vector3(0f, 3.15f, 0f)));
            Assert.That(
                ceiling.transform.localScale,
                Is.EqualTo(new Vector3(20f, 0.1f, 20f)));

            var lightmappedRenderers =
                GetComponents<Renderer>(scene)
                    .Where(renderer =>
                        renderer.lightmapIndex >= 0 &&
                        renderer.lightmapIndex < 0xFFFE)
                    .ToArray();
            Assert.That(
                lightmappedRenderers,
                Is.Empty,
                "The prototype must not depend on lightmaps.");
        }

        private static void AssertPosingStagePresentation(
            Scene scene,
            StageTransitionController transition,
            PoseStageFeedbackController stageFeedback)
        {
            const string renderTexturePath =
                "Assets/IVRC/Materials/" +
                "PoseMonitor.renderTexture";
            const string screenMaterialPath =
                "Assets/IVRC/Materials/" +
                "PoseMonitorScreen.mat";

            var monitor = FindGameObjectByPath(
                scene,
                "Phase_Posing/Stage/" +
                "PrototypeStageVisuals/PoseMonitor");
            var screen = FindGameObjectByPath(
                scene,
                "Phase_Posing/Stage/" +
                "PrototypeStageVisuals/PoseMonitor/Screen");
            var cameraObject = FindGameObjectByPath(
                scene,
                "Phase_Posing/Stage/" +
                "PrototypeStageVisuals/PoseMonitor/" +
                "PoseMonitorCamera");
            Assert.That(monitor, Is.Not.Null);
            Assert.That(screen, Is.Not.Null);
            Assert.That(cameraObject, Is.Not.Null);
            Assert.That(monitor.activeSelf, Is.True);
            Assert.That(screen.activeSelf, Is.True);
            Assert.That(cameraObject.activeSelf, Is.True);

            var renderTexture =
                AssetDatabase.LoadAssetAtPath<RenderTexture>(
                    renderTexturePath);
            Assert.That(renderTexture, Is.Not.Null);
            Assert.That(renderTexture.width, Is.EqualTo(1024));
            Assert.That(renderTexture.height, Is.EqualTo(576));
            Assert.That(renderTexture.depth, Is.EqualTo(16));
            Assert.That(renderTexture.antiAliasing, Is.EqualTo(1));
            Assert.That(renderTexture.useMipMap, Is.False);
            Assert.That(
                renderTexture.filterMode,
                Is.EqualTo(FilterMode.Bilinear));
            Assert.That(
                renderTexture.wrapMode,
                Is.EqualTo(TextureWrapMode.Clamp));

            var screenRenderer =
                screen.GetComponent<MeshRenderer>();
            Assert.That(screenRenderer, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(
                    screenRenderer.sharedMaterial),
                Is.EqualTo(screenMaterialPath));
            Assert.That(
                screenRenderer.sharedMaterial.GetTexture(
                    "_BaseMap"),
                Is.SameAs(renderTexture));
            Assert.That(
                screenRenderer.sharedMaterial.GetTextureScale(
                    "_BaseMap"),
                Is.EqualTo(new Vector2(-1f, 1f)),
                "The self-view must behave like a mirror.");
            Assert.That(
                screenRenderer.shadowCastingMode,
                Is.EqualTo(ShadowCastingMode.Off));
            Assert.That(screenRenderer.receiveShadows, Is.False);
            Assert.That(
                screen.GetComponents<Collider>(),
                Is.Empty);
            Assert.That(
                screen.transform.localScale,
                Is.EqualTo(
                    new Vector3(8f, 4.5f, 1f)));

            var monitorCamera =
                cameraObject.GetComponent<Camera>();
            Assert.That(monitorCamera, Is.Not.Null);
            Assert.That(
                monitorCamera.targetTexture,
                Is.SameAs(renderTexture));
            Assert.That(
                monitorCamera.cullingMask,
                Is.EqualTo(
                    (1 << LayerMask.NameToLayer(
                        "Character")) |
                    (1 << LayerMask.NameToLayer(
                        "PoseGuide"))),
                "The monitor must render only the tracked " +
                "avatar and its pose guide, and never recurse " +
                "into the stage.");
            Assert.That(
                monitorCamera.stereoTargetEye,
                Is.EqualTo(StereoTargetEyeMask.None));
            Assert.That(monitorCamera.allowHDR, Is.False);
            Assert.That(monitorCamera.allowMSAA, Is.False);
            Assert.That(
                monitorCamera.allowDynamicResolution,
                Is.False);
            Assert.That(
                monitorCamera.useOcclusionCulling,
                Is.False);
            Assert.That(
                monitorCamera.fieldOfView,
                Is.EqualTo(22f).Within(0.001f));
            Assert.That(
                monitorCamera.nearClipPlane,
                Is.EqualTo(0.1f).Within(0.001f));
            Assert.That(
                monitorCamera.farClipPlane,
                Is.EqualTo(10f).Within(0.001f));
            Assert.That(
                cameraObject.GetComponent<AudioListener>(),
                Is.Null);
            Assert.That(
                cameraObject.CompareTag("MainCamera"),
                Is.False);

            var cameraData =
                cameraObject.GetComponent<
                    UniversalAdditionalCameraData>();
            Assert.That(cameraData, Is.Not.Null);
            Assert.That(cameraData.allowXRRendering, Is.False);
            Assert.That(
                cameraData.renderPostProcessing,
                Is.False);
            Assert.That(cameraData.renderShadows, Is.False);

            var serializedTransition =
                new SerializedObject(transition);
            var stageArrival = serializedTransition
                .FindProperty("_stageArrivalPoint")
                .objectReferenceValue as Transform;
            Assert.That(stageArrival, Is.Not.Null);

            var screenDistance = Vector3.Dot(
                screen.transform.position -
                stageArrival.position,
                stageArrival.forward);
            Assert.That(
                screenDistance,
                Is.InRange(14f, 20f),
                "The jumbotron hangs beyond the audience so it " +
                "stays readable without moving the HMD camera.");

            // The capture camera stands between the performer and
            // the judges, looking back at the performer, so the
            // jumbotron shows the front of the pose.
            var cameraDistance = Vector3.Dot(
                cameraObject.transform.position -
                stageArrival.position,
                stageArrival.forward);
            Assert.That(
                cameraDistance,
                Is.InRange(1.5f, 4f),
                "The capture camera must stand in front of the " +
                "performer.");
            Assert.That(
                Vector3.Dot(
                    cameraObject.transform.forward,
                    stageArrival.forward),
                Is.LessThan(-0.9f),
                "The capture camera must look back at the " +
                "performer, not out at the audience.");
            var cameraTarget =
                stageArrival.position + Vector3.up * 1.35f;
            Assert.That(
                Vector3.Angle(
                    cameraObject.transform.forward,
                    cameraTarget -
                    cameraObject.transform.position),
                Is.LessThan(0.1f));

            // Upper body only: the framed height at the performer
            // must cover a pose from the hips up, not the legs.
            var framedHeight =
                2f *
                Vector3.Distance(
                    cameraObject.transform.position,
                    cameraTarget) *
                Mathf.Tan(
                    monitorCamera.fieldOfView *
                    0.5f *
                    Mathf.Deg2Rad);
            Assert.That(
                framedHeight,
                Is.InRange(0.8f, 1.3f),
                "The jumbotron must frame the upper body.");

            var audienceVisuals = FindGameObjectByPath(
                scene,
                "Phase_Posing/Audience/" +
                "PrototypeAudienceVisuals");
            Assert.That(audienceVisuals, Is.Not.Null);
            Assert.That(
                new SerializedObject(stageFeedback)
                    .FindProperty("_audienceRoot")
                    .objectReferenceValue,
                Is.SameAs(audienceVisuals.transform));

            var audienceMembers =
                audienceVisuals.transform
                    .Cast<Transform>()
                    .Where(member =>
                        member.name.StartsWith(
                            "Audience_R"))
                    .ToArray();
            Assert.That(
                audienceMembers,
                Has.Length.EqualTo(55));
            Assert.That(
                audienceMembers
                    .Select(member =>
                        Mathf.RoundToInt(
                            member.localPosition.z * 100f))
                    .Distinct()
                    .Count(),
                Is.EqualTo(5));
            var rowElevations = audienceMembers
                .GroupBy(member =>
                    Mathf.RoundToInt(
                        member.localPosition.z * 100f))
                .OrderBy(group => group.Key)
                .Select(group =>
                    group.First().localPosition.y)
                .ToArray();
            var expectedElevations =
                new[] { 0f, 0.28f, 0.56f, 0.84f, 1.12f };
            Assert.That(
                rowElevations,
                Has.Length.EqualTo(
                    expectedElevations.Length));
            for (var row = 0;
                 row < expectedElevations.Length;
                 row++)
            {
                Assert.That(
                    rowElevations[row],
                    Is.EqualTo(expectedElevations[row])
                        .Within(0.0001f),
                    "Rear rows should rise in clear tiers.");
            }

            foreach (var member in audienceMembers)
            {
                Assert.That(
                    member.GetComponent<Renderer>(),
                    Is.Null,
                    "Animation must move an empty member root.");
                var body = member.Find("Body");
                var head = member.Find("Head");
                Assert.That(body, Is.Not.Null);
                Assert.That(head, Is.Not.Null);
                Assert.That(
                    body.GetComponent<MeshFilter>()
                        .sharedMesh.name,
                    Is.EqualTo("Capsule"));
                Assert.That(
                    head.GetComponent<MeshFilter>()
                        .sharedMesh.name,
                    Is.EqualTo("Sphere"));
                Assert.That(
                    head.localPosition.y,
                    Is.GreaterThan(body.localPosition.y));
                Assert.That(
                    member.GetComponentsInChildren<
                        Collider>(true),
                    Is.Empty);

                foreach (var renderer in
                         member.GetComponentsInChildren<
                             Renderer>(true))
                {
                    Assert.That(
                        renderer.shadowCastingMode,
                        Is.EqualTo(
                            ShadowCastingMode.Off));
                    Assert.That(
                        renderer.receiveShadows,
                        Is.False);
                    Assert.That(
                        renderer.lightProbeUsage,
                        Is.EqualTo(LightProbeUsage.Off));
                    Assert.That(
                        renderer.reflectionProbeUsage,
                        Is.EqualTo(
                            ReflectionProbeUsage.Off));
                }

                var forwardDistance = Vector3.Dot(
                    member.position - stageArrival.position,
                    stageArrival.forward);
                Assert.That(
                    forwardDistance,
                    Is.GreaterThanOrEqualTo(7.49f),
                    "Audience members must stay outside " +
                    "the participant's immediate space.");
            }

            var audienceFloor =
                audienceVisuals.transform.Find(
                    "AudienceFloor");
            Assert.That(audienceFloor, Is.Not.Null);
            Assert.That(
                audienceFloor.localPosition,
                Is.EqualTo(new Vector3(0f, -0.1f, 11f)));
            Assert.That(
                audienceFloor.localScale,
                Is.EqualTo(new Vector3(15f, 0.2f, 14f)));
            var audienceTiers =
                audienceVisuals.transform.Find(
                    "AudienceTiers");
            Assert.That(audienceTiers, Is.Not.Null);
            Assert.That(
                audienceTiers.childCount,
                Is.EqualTo(5));
            Assert.That(
                audienceTiers.GetComponentsInChildren<
                    Collider>(true),
                Is.Empty);
            Assert.That(
                audienceMembers.Max(member =>
                    Vector3.Dot(
                        member.position -
                        stageArrival.position,
                        stageArrival.forward)),
                Is.LessThan(screenDistance),
                "The jumbotron must hang behind the back row.");

            AssertAudienceLightPoints(scene, stageArrival);
        }

        private static void AssertAudienceLightPoints(
            Scene scene,
            Transform stageArrival)
        {
            var go = FindGameObjectByPath(
                scene,
                "Phase_Posing/Audience/AudienceLightPoints");
            Assert.That(go, Is.Not.Null);

            var lights =
                go.GetComponent<AudienceLightPoints>();
            Assert.That(lights, Is.Not.Null);
            AssertReferences(
                lights,
                "_stageFeedback",
                "_points",
                "_stageOrigin");

            var serialized = new SerializedObject(lights);
            var seats =
                serialized.FindProperty("_seatPositions");
            // The theatre has 132 bench meshes and they overlap in twos
            // and threes, so both failure modes have a number. One point
            // per mesh is 132 and reads as three columns; one point per
            // seat without removing the overlaps is 782 and stacks people
            // on each other's heads.
            Assert.That(
                seats.arraySize,
                Is.InRange(300, 500),
                "The captured crowd must fill the benches without " +
                "seating anyone twice.");

            var head = serialized
                .FindProperty("_seatedHeadHeight")
                .floatValue;
            for (var index = 0;
                 index < seats.arraySize;
                 index++)
            {
                var seat = seats
                    .GetArrayElementAtIndex(index)
                    .vector3Value;
                var world = go.transform.TransformPoint(
                    AudienceLightPointsMath
                        .CalculatePointPosition(
                            seat,
                            head));
                var forwardDistance = Vector3.Dot(
                    world - stageArrival.position,
                    stageArrival.forward);
                Assert.That(
                    forwardDistance,
                    Is.GreaterThan(1.5f),
                    "No light point may sit on the stage " +
                    "with the performer.");
            }

            var particles =
                go.GetComponent<ParticleSystem>();
            Assert.That(particles, Is.Not.Null);
            Assert.That(
                particles.emission.enabled,
                Is.False,
                "The field is placed, not emitted.");
            Assert.That(
                particles.main.simulationSpeed,
                Is.EqualTo(0f),
                "A running clock would drift the crowd off " +
                "its seats.");

            var renderer =
                go.GetComponent<ParticleSystemRenderer>();
            Assert.That(
                renderer.sharedMaterial,
                Is.Not.Null);
            Assert.That(
                renderer.shadowCastingMode,
                Is.EqualTo(ShadowCastingMode.Off));
            Assert.That(renderer.receiveShadows, Is.False);
            Assert.That(
                renderer.lightProbeUsage,
                Is.EqualTo(LightProbeUsage.Off));
            Assert.That(
                renderer.reflectionProbeUsage,
                Is.EqualTo(ReflectionProbeUsage.Off));
        }

        private static void AssertStageEntrancePresentation(
            Scene scene)
        {
            const string phasePath =
                "Phase_StageEntrance";
            const string cinemaPath =
                phasePath + "/CinematicStageSpace";

            var phase = FindGameObjectByPath(
                scene,
                phasePath);
            Assert.That(phase, Is.Not.Null);
            Assert.That(phase.activeSelf, Is.False);
            Assert.That(
                phase.transform.localPosition,
                Is.EqualTo(Vector3.zero));
            Assert.That(
                phase.transform.localRotation,
                Is.EqualTo(Quaternion.identity));
            Assert.That(
                phase.transform.localScale,
                Is.EqualTo(Vector3.one));

            var controller = phase.GetComponent<
                StageEntranceCinematicController>();
            Assert.That(controller, Is.Not.Null);
            AssertReferences(
                controller,
                "_gameFlow",
                "_avatarRegistry",
                "_stageTransition",
                "_posingMonitorRoot",
                "_posingHudRoot",
                "_entranceGuideRoot",
                "_avatarContainer",
                "_walkStart",
                "_walkMiddle",
                "_walkEnd",
                "_walkAnimation");
            Assert.That(
                controller.TotalDuration,
                Is.EqualTo(6.5f).Within(0.000001f));

            var serializedController =
                new SerializedObject(controller);
            var entranceGuide =
                serializedController
                    .FindProperty("_entranceGuideRoot")
                    .objectReferenceValue as GameObject;
            Assert.That(entranceGuide, Is.Not.Null);
            Assert.That(
                entranceGuide.name,
                Is.EqualTo("StageFlowHUD"));
            Assert.That(
                serializedController
                    .FindProperty("_hidePosingHudDuringEntrance")
                    .boolValue,
                Is.False,
                "The stage HUD must remain visible during the passive " +
                "entrance so a first-time participant knows not to walk " +
                "and that posing comes next.");
            Assert.That(
                serializedController
                    .FindProperty(
                        "_facePosingHudTowardJudgeDuringEntrance")
                    .boolValue,
                Is.True,
                "The shared stage HUD must face the participant at the " +
                "judge seat during the entrance instead of showing its " +
                "mirrored back side.");
            Assert.That(
                serializedController
                    .FindProperty("_entranceHudScaleMultiplier")
                    .floatValue,
                Is.EqualTo(1.15f).Within(0.000001f),
                "The entrance guide must remain large enough to read " +
                "from the judge seat, then restore before posing.");
            Assert.That(
                serializedController
                    .FindProperty("_stepsPerSecond")
                    .floatValue,
                Is.EqualTo(1.8f).Within(0.000001f));

            // The entrance is watched live from the judge seat, so the
            // earlier three-camera cut is retired. Leaving its fields
            // unwired is what keeps the cut from running.
            foreach (var retiredName in new[]
                     {
                         "_cinematicCamera",
                         "_screenRenderer",
                         "_wideShotAnchor",
                         "_muscleShotAnchor",
                         "_heroShotAnchor"
                     })
            {
                Assert.That(
                    serializedController
                        .FindProperty(retiredName)
                        .objectReferenceValue,
                    Is.Null,
                    $"{retiredName} belongs to the retired " +
                    "three-camera entrance and must stay unwired.");
            }

            var walkAnimation =
                serializedController
                    .FindProperty("_walkAnimation")
                    .objectReferenceValue as AnimationClip;
            Assert.That(walkAnimation, Is.Not.Null);
            Assert.That(
                walkAnimation.isHumanMotion,
                Is.True,
                "The entrance walk must be a Humanoid clip so it " +
                "can retarget onto the cinematic avatar.");

            var stageSpace = FindGameObjectByPath(
                scene,
                cinemaPath);
            Assert.That(stageSpace, Is.Not.Null);
            Assert.That(
                stageSpace.transform.position,
                Is.EqualTo(new Vector3(0f, 0f, 15f)));
            var walkStart = FindGameObjectByPath(
                scene,
                cinemaPath + "/Path/WalkStart");
            var walkMiddle = FindGameObjectByPath(
                scene,
                cinemaPath + "/Path/WalkMiddle");
            var walkEnd = FindGameObjectByPath(
                scene,
                cinemaPath + "/Path/WalkEnd");
            Assert.That(
                walkStart.transform.localPosition,
                Is.EqualTo(
                    new Vector3(-3.35f, 0f, -2.15f)));
            Assert.That(
                walkMiddle.transform.localPosition,
                Is.EqualTo(
                    new Vector3(-1.75f, 0f, -0.9f)));
            Assert.That(
                walkEnd.transform.localPosition,
                Is.EqualTo(Vector3.zero));
            Assert.That(
                walkEnd.transform.localRotation,
                Is.EqualTo(Quaternion.identity));

            var avatarLayer =
                LayerMask.NameToLayer(
                    "CinematicCharacter");
            Assert.That(
                avatarLayer,
                Is.GreaterThanOrEqualTo(0));

            var rig = FindGameObjectByPath(
                scene,
                "[BuildingBlock] Camera Rig");
            Assert.That(rig, Is.Not.Null);
            foreach (var eyeCamera in
                     rig.GetComponentsInChildren<Camera>(true))
            {
                Assert.That(
                    eyeCamera.cullingMask &
                    (1 << avatarLayer),
                    Is.Not.Zero,
                    $"{eyeCamera.name} must show the entrance " +
                    "walk directly, because the judge seat is " +
                    "the camera.");
            }

            var transition =
                GetSingle<StageTransitionController>(scene);
            var serializedTransition =
                new SerializedObject(transition);
            var judgeArrival = serializedTransition
                .FindProperty("_judgeArrivalPoint")
                .objectReferenceValue as Transform;
            Assert.That(
                judgeArrival,
                Is.Not.Null,
                "The entrance is watched from the judge seat, so " +
                "the judge arrival point must be wired.");
            var stageArrival = serializedTransition
                .FindProperty("_stageArrivalPoint")
                .objectReferenceValue as Transform;
            Assert.That(stageArrival, Is.Not.Null);
            Assert.That(
                Vector3.Dot(
                    judgeArrival.position -
                    stageArrival.position,
                    stageArrival.forward),
                Is.GreaterThan(0f),
                "The judge seat must sit in front of the " +
                "performer.");
            Assert.That(
                Vector3.Dot(
                    judgeArrival.forward,
                    stageArrival.forward),
                Is.LessThan(-0.9f),
                "The judge seat must face back towards the stage.");
            Assert.That(
                Vector3.Dot(
                    walkEnd.transform.position -
                    judgeArrival.position,
                    judgeArrival.forward),
                Is.GreaterThan(0f),
                "The walk must finish inside the judge's view.");
        }

        private static void AssertMinimumStartupPermissions(
            OVRManager manager)
        {
            var serialized = new SerializedObject(manager);
            AssertBooleanProperty(
                serialized,
                "requestBodyTrackingPermissionOnStartup",
                true);
            AssertBooleanProperty(
                serialized,
                "requestFaceTrackingPermissionOnStartup",
                false);
            AssertBooleanProperty(
                serialized,
                "requestEyeTrackingPermissionOnStartup",
                false);
            AssertBooleanProperty(
                serialized,
                "requestScenePermissionOnStartup",
                false);
            AssertBooleanProperty(
                serialized,
                "requestRecordAudioPermissionOnStartup",
                false);
            AssertBooleanProperty(
                serialized,
                "requestPassthroughCameraAccessPermissionOnStartup",
                false);
        }

        private static void AssertBooleanProperty(
            SerializedObject serialized,
            string propertyName,
            bool expected)
        {
            var property =
                serialized.FindProperty(propertyName);
            Assert.That(
                property,
                Is.Not.Null,
                $"OVRManager is missing serialized property " +
                $"{propertyName}.");
            Assert.That(
                property.boolValue,
                Is.EqualTo(expected),
                $"OVRManager.{propertyName} should be " +
                $"{expected}.");
        }

        private static void AssertIntegerProperty(
            SerializedObject serialized,
            string propertyName,
            int expected)
        {
            var property =
                serialized.FindProperty(propertyName);
            Assert.That(
                property,
                Is.Not.Null,
                $"Project config is missing serialized " +
                $"property {propertyName}.");
            Assert.That(
                property.intValue,
                Is.EqualTo(expected),
                $"{propertyName} should be {expected}.");
        }

        private static void AssertProductionAvatarWiring(
            Scene scene,
            MuscleGrowthController muscleGrowth)
        {
            const string maleModelPath =
                "Assets/IVRC/Avatar/" +
                "2_UniversalBaseCharacters.fbx";
            const string maleConfigPath =
                "Assets/IVRC/Avatar/" +
                "2_UniversalBaseCharacters.json";
            const string femaleModelPath =
                "Assets/IVRC/Avatar/" +
                "UniversalBaseCharacters_famale.fbx";
            const string femaleConfigPath =
                "Assets/IVRC/Avatar/" +
                "UniversalBaseCharacters_famale.json";

            AssertHumanoidModel(maleModelPath);
            AssertHumanoidModel(femaleModelPath);

            var registry = GetSingle<AvatarRegistry>(scene);
            var swapInput = GetSingle<AvatarSwapInput>(scene);
            var coordinator =
                GetSingle<AvatarSwapCoordinator>(scene);
            AssertReferences(
                swapInput,
                "_registry",
                "_gameFlow");
            AssertReferences(
                coordinator,
                "_registry",
                "_bodyPoseMetrics",
                "_heightCalibration",
                "_avatarCalibration",
                "_repCounter",
                "_muscleGrowth");

            var serializedRegistry =
                new SerializedObject(registry);
            var registeredAvatars =
                serializedRegistry.FindProperty("_avatars");
            Assert.That(registeredAvatars, Is.Not.Null);
            Assert.That(
                registeredAvatars.arraySize,
                Is.EqualTo(2));
            Assert.That(
                serializedRegistry.FindProperty(
                        "_initialIndex")
                    .intValue,
                Is.Zero);

            var definitions =
                GetComponents<AvatarDefinition>(scene);
            Assert.That(definitions, Has.Length.EqualTo(2));
            var male = definitions.Single(
                definition =>
                    AssetDatabase.GetAssetPath(
                        definition.TrackedBodyRenderer
                            .sharedMesh) ==
                    maleModelPath);
            var female = definitions.Single(
                definition =>
                    AssetDatabase.GetAssetPath(
                        definition.TrackedBodyRenderer
                            .sharedMesh) ==
                    femaleModelPath);
            Assert.That(
                registeredAvatars
                    .GetArrayElementAtIndex(0)
                    .objectReferenceValue,
                Is.SameAs(male));
            Assert.That(
                registeredAvatars
                    .GetArrayElementAtIndex(1)
                    .objectReferenceValue,
                Is.SameAs(female));

            Assert.That(male.gameObject.activeSelf, Is.True);
            Assert.That(male.MirrorInstance.activeSelf, Is.True);
            Assert.That(male.GrowthOutput.enabled, Is.True);
            Assert.That(female.gameObject.activeSelf, Is.False);
            Assert.That(
                female.MirrorInstance.activeSelf,
                Is.False);
            Assert.That(
                female.GrowthOutput.enabled,
                Is.False);

            AssertAvatarRig(
                male,
                maleModelPath,
                maleConfigPath,
                true);
            AssertAvatarRig(
                female,
                femaleModelPath,
                femaleConfigPath,
                false);

            var metrics = GetSingle<BodyPoseMetrics>(scene);
            var serializedMetrics =
                new SerializedObject(metrics);
            var animator =
                serializedMetrics.FindProperty("_animator")
                    .objectReferenceValue as Animator;
            var retargeter =
                serializedMetrics.FindProperty("_retargeter")
                    .objectReferenceValue
                    as Meta.XR.Movement.Retargeting
                        .CharacterRetargeter;

            Assert.That(animator, Is.Not.Null);
            Assert.That(retargeter, Is.Not.Null);
            Assert.That(
                animator.gameObject,
                Is.SameAs(retargeter.gameObject));
            Assert.That(
                animator,
                Is.SameAs(male.Animator));
            Assert.That(
                retargeter,
                Is.SameAs(male.Retargeter));

            AssertCalibrationTargetsRetargeter<
                AvatarCalibrationController>(
                    scene,
                    retargeter);
            AssertCalibrationTargetsRetargeter<
                HeightCalibrationController>(
                    scene,
                    retargeter);

            var blendOutputs =
                GetComponents<
                    BlendShapeMuscleGrowthOutput>(scene);
            Assert.That(blendOutputs, Has.Length.EqualTo(2));
            Assert.That(
                blendOutputs,
                Does.Contain(male.GrowthOutput));
            Assert.That(
                blendOutputs,
                Does.Contain(female.GrowthOutput));
            Assert.That(
                blendOutputs.Count(output => output.enabled),
                Is.EqualTo(1));

            AssertGrowthBindings(
                male.GrowthOutput,
                male.TrackedBodyRenderer,
                male.MirrorBodyRenderer);
            AssertGrowthBindings(
                female.GrowthOutput,
                female.TrackedBodyRenderer,
                female.MirrorBodyRenderer);

            var outputs =
                new SerializedObject(muscleGrowth)
                    .FindProperty("_outputBehaviours");
            Assert.That(
                Enumerable
                    .Range(0, outputs.arraySize)
                    .Select(index =>
                        outputs
                            .GetArrayElementAtIndex(index)
                            .objectReferenceValue)
                    .Contains(male.GrowthOutput),
                Is.True);
            Assert.That(
                Enumerable
                    .Range(0, outputs.arraySize)
                    .Select(index =>
                        outputs
                            .GetArrayElementAtIndex(index)
                            .objectReferenceValue)
                    .Contains(female.GrowthOutput),
                Is.True);
        }

        private static void AssertHumanoidModel(
            string modelPath)
        {
            var model =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    modelPath);
            Assert.That(model, Is.Not.Null);
            var importer =
                AssetImporter.GetAtPath(modelPath)
                    as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.animationType,
                Is.EqualTo(
                    ModelImporterAnimationType.Human));
            Assert.That(importer.importBlendShapes, Is.True);
        }

        private static void AssertAvatarRig(
            AvatarDefinition definition,
            string modelPath,
            string configPath,
            bool expectedActive)
        {
            Assert.That(
                PrefabUtility
                    .GetCorrespondingObjectFromSource(
                        definition.gameObject),
                Is.Not.Null,
                $"{definition.DisplayName} is not linked " +
                $"to {modelPath}.");
            Assert.That(
                AssetDatabase.GetAssetPath(
                    PrefabUtility
                        .GetCorrespondingObjectFromSource(
                            definition.gameObject)),
                Is.EqualTo(modelPath));
            Assert.That(
                definition.gameObject.activeSelf,
                Is.EqualTo(expectedActive));
            Assert.That(definition.Animator, Is.Not.Null);
            Assert.That(
                definition.Animator.gameObject,
                Is.SameAs(definition.gameObject));
            Assert.That(definition.Animator.enabled, Is.False);
            Assert.That(
                definition.Animator.avatar,
                Is.Not.Null);
            Assert.That(
                definition.Animator.avatar.isHuman,
                Is.True);
            Assert.That(
                definition.Animator.avatar.isValid,
                Is.True);

            var retargeter = definition.Retargeter;
            Assert.That(retargeter, Is.Not.Null);
            Assert.That(
                retargeter.gameObject,
                Is.SameAs(definition.gameObject));
            Assert.That(
                retargeter.GetComponent<
                    Meta.XR.Movement.Retargeting
                        .MetaSourceDataProvider>(),
                Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(
                    retargeter.ConfigAsset),
                Is.EqualTo(configPath));
            Assert.That(
                retargeter.NumberOfJoints,
                Is.EqualTo(65));
            Assert.That(
                retargeter.JointPairs.All(
                    pair => pair.Joint != null),
                Is.True);
            Assert.That(
                retargeter.SkeletonRetargeter
                    .RetargetingBehavior,
                Is.EqualTo(
                    Meta.XR.Movement.MSDKUtility
                        .RetargetingBehavior
                        .RotationsAndPositionsHandsRotationOnly),
                "Avatar-native hand proportions must be " +
                "preserved.");

            Assert.That(
                definition.TrackedBodyRenderer,
                Is.Not.Null);
            Assert.That(
                definition.MirrorBodyRenderer,
                Is.Not.Null);
            Assert.That(
                definition.CinematicPrefab,
                Is.SameAs(
                    AssetDatabase.LoadAssetAtPath<GameObject>(
                        modelPath)));
            Assert.That(
                definition.ValidateCinematicPrefab(
                    out var cinematicError),
                Is.True,
                cinematicError);
            Assert.That(
                definition.CinematicPrefab
                    .GetComponentInChildren<
                        Meta.XR.Movement.Retargeting
                            .CharacterRetargeter>(true),
                Is.Null);
            var characterLayer =
                LayerMask.NameToLayer("Character");
            var mirroredCharacterLayer =
                LayerMask.NameToLayer(
                    "MirroredCharacter");
            Assert.That(
                definition.gameObject
                    .GetComponentsInChildren<Renderer>(true)
                    .All(renderer =>
                        renderer.gameObject.layer ==
                        characterLayer),
                Is.True,
                $"{definition.DisplayName} tracked renderers " +
                "must stay on Character.");
            Assert.That(
                definition.MirrorInstance
                    .GetComponentsInChildren<Renderer>(true)
                    .All(renderer =>
                        renderer.gameObject.layer ==
                        mirroredCharacterLayer),
                Is.True,
                $"{definition.DisplayName} mirror renderers " +
                "must stay on MirroredCharacter.");
            Assert.That(
                definition.GrowthOutput,
                Is.Not.Null);

            Assert.That(
                definition.MirrorInstance,
                Is.Not.Null);
            var mirrorAnimator =
                definition.MirrorInstance
                    .GetComponent<Animator>();
            Assert.That(mirrorAnimator, Is.Not.Null);
            Assert.That(mirrorAnimator.enabled, Is.False);
            var mirror =
                definition.MirrorInstance.GetComponent<
                    Meta.XR.Movement.Utils
                        .MirrorTransforms>();
            Assert.That(mirror, Is.Not.Null);
            var serializedMirror =
                new SerializedObject(mirror);
            Assert.That(
                serializedMirror.FindProperty("_target")
                    .objectReferenceValue,
                Is.SameAs(definition.transform));
            var bonePairs =
                serializedMirror.FindProperty("_bonePairs");
            Assert.That(bonePairs.arraySize, Is.EqualTo(70));
            for (var index = 0;
                 index < bonePairs.arraySize;
                 index++)
            {
                var pair =
                    bonePairs.GetArrayElementAtIndex(index);
                Assert.That(
                    pair.FindPropertyRelative("Joint")
                        .objectReferenceValue,
                    Is.Not.Null);
                Assert.That(
                    pair.FindPropertyRelative(
                            "ParentJoint")
                        .objectReferenceValue,
                    Is.Not.Null);
            }

            Assert.That(
                definition.Validate(out var error),
                Is.True,
                error);
        }

        private static void AssertGrowthBindings(
            BlendShapeMuscleGrowthOutput output,
            SkinnedMeshRenderer trackedRenderer,
            SkinnedMeshRenderer mirrorRenderer)
        {
            // Only the explicitly configured per-exercise shapes are driven.
            // Weights run 100 (untrained) to 0 (grown).
            var expectedShapes = new[]
            {
                (Name: "UpperArm.L",
                    Channel: MuscleGrowthChannel.ElbowFlexionLeft),
                (Name: "UpperArm.R",
                    Channel: MuscleGrowthChannel.ElbowFlexionRight),
                (Name: "Shoulder.L",
                    Channel: MuscleGrowthChannel.ArmRaiseLeft),
                (Name: "Shoulder.R",
                    Channel: MuscleGrowthChannel.ArmRaiseRight),
                (Name: "Chest.L",
                    Channel: MuscleGrowthChannel.ChestLeft),
                (Name: "Chest.R",
                    Channel: MuscleGrowthChannel.ChestRight)
            };
            var expectedRenderers =
                new[] { trackedRenderer, mirrorRenderer };

            var bindings = new SerializedObject(output)
                .FindProperty("_bindings");
            Assert.That(
                bindings.arraySize,
                Is.EqualTo(
                    expectedRenderers.Length *
                    expectedShapes.Length));

            for (var index = 0;
                 index < bindings.arraySize;
                 index++)
            {
                var binding =
                    bindings.GetArrayElementAtIndex(index);
                var expectedRenderer = expectedRenderers[
                    index / expectedShapes.Length];
                var expectedShape = expectedShapes[
                    index % expectedShapes.Length];

                var renderer = binding
                    .FindPropertyRelative("_renderer")
                    .objectReferenceValue
                    as SkinnedMeshRenderer;
                Assert.That(
                    renderer,
                    Is.SameAs(expectedRenderer));
                Assert.That(
                    binding
                        .FindPropertyRelative(
                            "_blendShapeName")
                        .stringValue,
                    Is.EqualTo(expectedShape.Name));
                Assert.That(
                    (MuscleGrowthChannel)binding
                        .FindPropertyRelative("_channel")
                        .enumValueIndex,
                    Is.EqualTo(expectedShape.Channel),
                    $"{expectedShape.Name} must follow the " +
                    "exercise that trains it.");

                var shapeIndex = renderer.sharedMesh
                    .GetBlendShapeIndex(expectedShape.Name);
                Assert.That(
                    shapeIndex,
                    Is.GreaterThanOrEqualTo(0));
                Assert.That(
                    binding
                        .FindPropertyRelative(
                            "_useCurrentWeightAsBaseline")
                        .boolValue,
                    Is.False);
                Assert.That(
                    binding
                        .FindPropertyRelative(
                            "_configuredBaselineWeight")
                        .floatValue,
                    Is.EqualTo(100f),
                    "Untrained muscles start fully weighted.");
                Assert.That(
                    binding
                        .FindPropertyRelative(
                            "_targetWeight")
                        .floatValue,
                    Is.Zero,
                    "Growth drives the weight down to zero.");

                AssertBlendShapeHasDeformation(
                    renderer.sharedMesh,
                    shapeIndex);
            }
        }

        private static void AssertBlendShapeHasDeformation(
            Mesh mesh,
            int blendShapeIndex)
        {
            Assert.That(
                mesh.GetBlendShapeFrameCount(
                    blendShapeIndex),
                Is.GreaterThan(0));
            var frameIndex =
                mesh.GetBlendShapeFrameCount(
                    blendShapeIndex) - 1;
            var vertices = new Vector3[mesh.vertexCount];
            var normals = new Vector3[mesh.vertexCount];
            var tangents = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(
                blendShapeIndex,
                frameIndex,
                vertices,
                normals,
                tangents);
            Assert.That(
                vertices.Any(delta =>
                    delta.sqrMagnitude > 0.00000001f),
                Is.True,
                "The muscle BlendShape exists but has no " +
                "vertex deformation.");
        }

        private static void
            AssertCalibrationTargetsRetargeter<T>(
                Scene scene,
                Meta.XR.Movement.Retargeting
                    .CharacterRetargeter expected)
            where T : Component
        {
            var calibration = GetSingle<T>(scene);
            var retargeters =
                new SerializedObject(calibration)
                    .FindProperty(
                        "_characterRetargeters");
            Assert.That(retargeters, Is.Not.Null);
            Assert.That(
                retargeters.arraySize,
                Is.EqualTo(1));
            Assert.That(
                retargeters.GetArrayElementAtIndex(0)
                    .objectReferenceValue,
                Is.SameAs(expected));
        }

        private static void AssertNoMissingScripts(
            Scene scene)
        {
            var objects = new List<GameObject>();
            foreach (var root in scene.GetRootGameObjects())
            {
                objects.AddRange(
                    root
                        .GetComponentsInChildren<Transform>(true)
                        .Select(transform =>
                            transform.gameObject));
            }

            foreach (var gameObject in objects)
            {
                Assert.That(
                    GameObjectUtility
                        .GetMonoBehavioursWithMissingScriptCount(
                            gameObject),
                    Is.Zero,
                    $"Missing script on {GetPath(gameObject)}.");
            }
        }

        private static string GetPath(
            GameObject gameObject)
        {
            var path = gameObject.name;
            var current = gameObject.transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
