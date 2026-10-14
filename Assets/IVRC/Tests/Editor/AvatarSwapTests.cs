using System.Collections.Generic;
using System.Reflection;
using Meta.XR.Movement.Retargeting;
using NUnit.Framework;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class AvatarSwapTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<GameObject> _spawned =
            new List<GameObject>();
        private readonly Dictionary<
            AvatarDefinition,
            GameObject> _mirrors =
            new Dictionary<AvatarDefinition, GameObject>();
        private readonly Dictionary<
            AvatarDefinition,
            BlendShapeMuscleGrowthOutput> _outputs =
            new Dictionary<
                AvatarDefinition,
                BlendShapeMuscleGrowthOutput>();

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
            _mirrors.Clear();
            _outputs.Clear();
        }

        [Test]
        public void SwapTo_ActivatesOnlyTargetPresentation()
        {
            var male = NewAvatar("Male");
            var female = NewAvatar("Female");
            var registry = NewRegistry(male, female);

            registry.SwapTo(male);
            registry.SwapTo(female);

            AssertPresentationState(male, false);
            AssertPresentationState(female, true);
            Assert.That(registry.Active, Is.SameAs(female));
        }

        [Test]
        public void SwapNext_CyclesWrapsAndRaisesOneEvent()
        {
            var male = NewAvatar("Male");
            var female = NewAvatar("Female");
            var registry = NewRegistry(male, female);
            registry.SwapTo(male);

            var changes = 0;
            registry.OnAvatarChanged += _ => changes++;

            registry.SwapNext();
            Assert.That(registry.Active, Is.SameAs(female));
            registry.SwapNext();
            Assert.That(registry.Active, Is.SameAs(male));
            registry.SwapTo(male);

            Assert.That(
                changes,
                Is.EqualTo(2),
                "Selecting the already-active avatar must be a no-op.");
        }

        [TestCase(ExperiencePhase.Setup, true)]
        [TestCase(ExperiencePhase.Ready, true)]
        [TestCase(ExperiencePhase.Countdown, false)]
        [TestCase(ExperiencePhase.Training, false)]
        [TestCase(ExperiencePhase.Transformation, false)]
        [TestCase(ExperiencePhase.StageTransition, false)]
        [TestCase(ExperiencePhase.StageEntrance, false)]
        [TestCase(ExperiencePhase.Posing, false)]
        [TestCase(ExperiencePhase.Results, false)]
        public void SwapPolicy_AllowsOnlySetupAndReady(
            ExperiencePhase phase,
            bool expected)
        {
            Assert.That(
                AvatarSwapInput.AllowsSwapping(phase),
                Is.EqualTo(expected));
        }

        [Test]
        public void RebindingRetargeters_InvalidatesBothCalibrations()
        {
            var host = NewGameObject("CalibrationHost");
            host.SetActive(false);
            var retargeter =
                host.AddComponent<CharacterRetargeter>();
            var alignment =
                host.AddComponent<
                    AvatarCalibrationController>();
            var height =
                host.AddComponent<
                    HeightCalibrationController>();

            SetProperty(alignment, "HasCalibrated", true);
            SetProperty(
                alignment,
                "HasAttemptedCalibration",
                true);
            SetProperty(
                height,
                "HasAttemptedCalibration",
                true);
            SetProperty(
                height,
                "LastCalibrationSucceeded",
                true);
            SetProperty(
                height,
                "LastCalibratedRetargeterCount",
                1);

            alignment.SetRetargeters(retargeter);
            height.SetRetargeters(retargeter);

            Assert.That(alignment.HasCalibrated, Is.False);
            Assert.That(
                alignment.HasAttemptedCalibration,
                Is.False);
            Assert.That(
                height.HasAttemptedCalibration,
                Is.False);
            Assert.That(
                height.LastCalibrationSucceeded,
                Is.False);
            Assert.That(
                height.LastCalibratedRetargeterCount,
                Is.Zero);
            AssertRetargeterArray(
                alignment,
                retargeter);
            AssertRetargeterArray(
                height,
                retargeter);
        }

        private AvatarDefinition NewAvatar(string name)
        {
            var tracked = NewGameObject(name);
            var mirror = NewGameObject(name + "Mirror");
            var outputObject =
                NewGameObject(name + "GrowthOutput");
            var output =
                outputObject.AddComponent<
                    BlendShapeMuscleGrowthOutput>();
            var definition =
                tracked.AddComponent<AvatarDefinition>();

            SetField(
                definition,
                "_mirrorInstance",
                mirror);
            SetField(
                definition,
                "_growthOutput",
                output);
            _mirrors.Add(definition, mirror);
            _outputs.Add(definition, output);
            definition.SetActive(false);
            return definition;
        }

        private AvatarRegistry NewRegistry(
            params AvatarDefinition[] avatars)
        {
            var host = NewGameObject("AvatarRegistry");
            host.SetActive(false);
            var registry =
                host.AddComponent<AvatarRegistry>();
            SetField(
                registry,
                "_avatars",
                new List<AvatarDefinition>(avatars));
            return registry;
        }

        private GameObject NewGameObject(string name)
        {
            var gameObject = new GameObject(name);
            _spawned.Add(gameObject);
            return gameObject;
        }

        private void AssertPresentationState(
            AvatarDefinition definition,
            bool expected)
        {
            Assert.That(
                definition.gameObject.activeSelf,
                Is.EqualTo(expected));
            Assert.That(
                _mirrors[definition].activeSelf,
                Is.EqualTo(expected));
            Assert.That(
                _outputs[definition].enabled,
                Is.EqualTo(expected));
        }

        private static void AssertRetargeterArray(
            Object controller,
            CharacterRetargeter expected)
        {
            var field = controller.GetType().GetField(
                "_characterRetargeters",
                PrivateInstance);
            Assert.That(field, Is.Not.Null);
            var values =
                field.GetValue(controller)
                    as CharacterRetargeter[];
            Assert.That(values, Has.Length.EqualTo(1));
            Assert.That(values[0], Is.SameAs(expected));
        }

        private static void SetField(
            object target,
            string name,
            object value)
        {
            var field = target.GetType().GetField(
                name,
                PrivateInstance);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private static void SetProperty(
            object target,
            string name,
            object value)
        {
            var property = target.GetType().GetProperty(
                name,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null);
            property.SetValue(target, value);
        }
    }
}
