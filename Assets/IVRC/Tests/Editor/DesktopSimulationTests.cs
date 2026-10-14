using System.Collections.Generic;
using System.Linq;
using IVRC.Desktop;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEngine;

namespace IVRC.Tests
{
    /// <summary>
    /// Guards the rules that keep desktop simulation out of the VR path.
    /// </summary>
    public sealed class DesktopSimulationTests
    {
        private readonly List<GameObject> _spawned =
            new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var gameObject in _spawned)
            {
                if (gameObject != null)
                {
                    Object.DestroyImmediate(gameObject);
                }
            }

            _spawned.Clear();
        }

        [Test]
        public void ForceDesktop_SelectsDesktopRegardlessOfHardware()
        {
            var useDesktop = SimulationModeResolver.ShouldUseDesktop(
                DesktopSimulationMode.ForceDesktop,
                out var reason);

            Assert.That(useDesktop, Is.True);
            Assert.That(reason, Is.Not.Empty);
        }

        [Test]
        public void OnlyForceDesktopDeclinesToStartXr()
        {
            // Whether a headset is attached cannot be known before startup:
            // OVRPlugin sits on the OpenXR session and reports none until
            // one exists. Auto therefore starts XR and checks afterwards,
            // which is what stopped it being permanently stuck on desktop.
            Assert.That(
                XrStartupController.ShouldAttemptXr(
                    DesktopSimulationMode.ForceDesktop),
                Is.False);
            Assert.That(
                XrStartupController.ShouldAttemptXr(
                    DesktopSimulationMode.Auto),
                Is.True);
            Assert.That(
                XrStartupController.ShouldAttemptXr(
                    DesktopSimulationMode.ForceVR),
                Is.True);
        }

        [Test]
        public void OnlyAutoFoldsBackWhenNoHeadsetTurnsUp()
        {
            Assert.That(
                XrStartupController.ShouldFallBackToDesktop(
                    DesktopSimulationMode.Auto,
                    false),
                Is.True);
            Assert.That(
                XrStartupController.ShouldFallBackToDesktop(
                    DesktopSimulationMode.Auto,
                    true),
                Is.False);

            // Force VR was asked for explicitly. Silently switching what was
            // requested would hide the problem rather than report it.
            Assert.That(
                XrStartupController.ShouldFallBackToDesktop(
                    DesktopSimulationMode.ForceVR,
                    false),
                Is.False);
            Assert.That(
                XrStartupController.ShouldFallBackToDesktop(
                    DesktopSimulationMode.ForceDesktop,
                    false),
                Is.False);
        }

        [Test]
        public void PendingXrStartupCanResolveToDesktopFallback()
        {
            var host = new GameObject("XrStartupFallbackTest");
            try
            {
                var startup =
                    host.AddComponent<XrStartupController>();

                Assert.That(
                    startup.Resolution,
                    Is.EqualTo(XrStartupResolution.Pending));
                startup.CancelPendingStartupForDesktopFallback(
                    "test fallback");

                Assert.That(startup.HasResolved, Is.True);
                Assert.That(
                    startup.Resolution,
                    Is.EqualTo(XrStartupResolution.Desktop));
                Assert.That(
                    startup.StatusReason,
                    Is.EqualTo("test fallback"));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void StandaloneXrIsConfiguredForAutomaticStartup()
        {
            var settings =
                XRGeneralSettingsPerBuildTarget
                    .XRGeneralSettingsForBuildTarget(
                        BuildTargetGroup.Standalone);

            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.InitManagerOnStart, Is.True);
            Assert.That(settings.Manager, Is.Not.Null);
            Assert.That(
                settings.Manager.activeLoaders.Any(
                    loader =>
                        loader != null &&
                        loader.GetType().Name == "OpenXRLoader"),
                Is.True,
                "Quest Link needs the Standalone OpenXR loader " +
                "with XR Management owning startup and shutdown.");
        }

        [Test]
        public void ForceVr_NeverSelectsDesktop()
        {
            var useDesktop = SimulationModeResolver.ShouldUseDesktop(
                DesktopSimulationMode.ForceVR,
                out _);

            Assert.That(
                useDesktop,
                Is.False,
                "ForceVR must never enable desktop simulation, even with " +
                "no headset attached.");
        }

        [Test]
        public void Auto_WithoutHeadset_SelectsDesktop()
        {
            // The Editor test runner has no initialized XR loader, so Auto
            // must fall back to desktop.
            Assume.That(
                SimulationModeResolver.IsVrRuntimeActive(),
                Is.False,
                "This test requires a session with no active HMD.");

            var useDesktop = SimulationModeResolver.ShouldUseDesktop(
                DesktopSimulationMode.Auto,
                out _);

            Assert.That(useDesktop, Is.True);
        }

        [Test]
        public void HeightController_DesktopCalibration_CompletesReadiness()
        {
            var host = NewGameObject("HeightHost");
            var controller =
                host.AddComponent<HeightCalibrationController>();

            controller.CompleteDesktopCalibration(1.7f);

            Assert.That(
                controller.LastCalibrationSucceeded,
                Is.True);
            Assert.That(
                controller.HasAttemptedCalibration,
                Is.True);
            Assert.That(
                controller.LastResolvedHeightMeters,
                Is.EqualTo(1.7f).Within(0.0001f));
            Assert.That(
                controller.HeightMeters,
                Is.EqualTo(1.7f).Within(0.0001f),
                "The existing HeightMeters property must expose the " +
                "desktop value.");
        }

        [Test]
        public void HeightController_DesktopCalibration_ClampsToSafeRange()
        {
            var host = NewGameObject("HeightHost");
            var controller =
                host.AddComponent<HeightCalibrationController>();

            controller.CompleteDesktopCalibration(9f);

            Assert.That(
                controller.LastResolvedHeightMeters,
                Is.LessThanOrEqualTo(2.3f));
        }

        [Test]
        public void AlignmentController_DesktopCalibration_MarksCalibrated()
        {
            var host = NewGameObject("AlignHost");
            var controller =
                host.AddComponent<AvatarCalibrationController>();

            controller.CompleteDesktopCalibration();

            Assert.That(controller.HasCalibrated, Is.True);
            Assert.That(controller.HasAttemptedCalibration, Is.True);
            Assert.That(controller.IsCalibrating, Is.False);
        }

        [Test]
        public void DesktopManager_DefaultsToInactiveBeforeResolve()
        {
            var host = NewGameObject("SimHost");
            host.SetActive(false);
            var manager =
                host.AddComponent<DesktopSimulationManager>();

            Assert.That(
                manager.IsDesktopActive,
                Is.False,
                "Nothing may treat the run as desktop before Awake " +
                "resolves the mode.");
        }

        private GameObject NewGameObject(string name)
        {
            var gameObject = new GameObject(name);
            _spawned.Add(gameObject);
            return gameObject;
        }
    }
}
