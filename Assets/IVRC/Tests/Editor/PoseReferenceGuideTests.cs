using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class PoseReferenceGuideTests
    {
        [Test]
        public void LayoutProjectsHandsAndShouldersFromHead()
        {
            var target = new PosingScoreTarget
            {
                LeftHandPositionFromHeadNormalized =
                    new Vector3(-0.3f, 0.2f, 10f),
                RightHandPositionFromHeadNormalized =
                    new Vector3(0.4f, 0.1f, -10f)
            };

            var layout = PoseReferenceGuide.CalculateLayout(
                target,
                new Vector2(10f, 20f),
                200f,
                0.12f,
                0.15f);

            AssertVector2(layout.HeadPosition, 10f, 20f);
            AssertVector2(
                layout.LeftShoulderPosition,
                -14f,
                -10f);
            AssertVector2(
                layout.RightShoulderPosition,
                34f,
                -10f);
            AssertVector2(
                layout.LeftHandPosition,
                -50f,
                60f);
            AssertVector2(
                layout.RightHandPosition,
                90f,
                40f);
        }

        [Test]
        public void LineLayoutProvidesCenterLengthAndRotation()
        {
            var line = PoseReferenceGuide.CalculateLineLayout(
                new Vector2(-2f, -1f),
                new Vector2(2f, 2f),
                6f);

            AssertVector2(line.Position, 0f, 0.5f);
            AssertVector2(line.Size, 5f, 6f);
            Assert.That(
                line.RotationDegrees,
                Is.EqualTo(36.8699f).Within(0.0001f));
        }

        [Test]
        public void InvalidLayoutInputsRemainFiniteAndBounded()
        {
            var target = new PosingScoreTarget
            {
                LeftHandPositionFromHeadNormalized =
                    new Vector3(float.NaN, 0.25f, 0f),
                RightHandPositionFromHeadNormalized =
                    new Vector3(0.5f, float.PositiveInfinity, 0f)
            };

            var layout = PoseReferenceGuide.CalculateLayout(
                target,
                new Vector2(float.NegativeInfinity, 12f),
                -100f,
                float.NaN,
                float.PositiveInfinity);
            var line = PoseReferenceGuide.CalculateLineLayout(
                new Vector2(float.NaN, 4f),
                new Vector2(3f, float.PositiveInfinity),
                float.NaN);

            AssertVector2(layout.HeadPosition, 0f, 12f);
            AssertVector2(
                layout.LeftShoulderPosition,
                0f,
                12f);
            AssertVector2(
                layout.RightShoulderPosition,
                0f,
                12f);
            AssertVector2(layout.LeftHandPosition, 0f, 12f);
            AssertVector2(layout.RightHandPosition, 0f, 12f);
            AssertVector2(line.Position, 1.5f, 2f);
            Assert.That(line.Size.x, Is.EqualTo(5f));
            Assert.That(line.Size.y, Is.EqualTo(0.1f));
            Assert.That(
                float.IsNaN(line.RotationDegrees) ||
                float.IsInfinity(line.RotationDegrees),
                Is.False);
        }

        [TestCase(
            false,
            PoseReferenceDisplayMode.AutomaticFallback,
            false,
            false)]
        [TestCase(
            true,
            PoseReferenceDisplayMode.AutomaticFallback,
            false,
            true)]
        [TestCase(
            true,
            PoseReferenceDisplayMode.AutomaticFallback,
            true,
            false)]
        [TestCase(
            true,
            PoseReferenceDisplayMode.AlwaysShow,
            true,
            true)]
        [TestCase(
            true,
            PoseReferenceDisplayMode.Hidden,
            false,
            false)]
        public void VisibilityPolicyKeepsOnePrimaryReference(
            bool isPosing,
            PoseReferenceDisplayMode displayMode,
            bool primaryGuideVisible,
            bool expected)
        {
            Assert.That(
                PoseReferenceVisibilityPolicy.ShouldShow(
                    isPosing,
                    displayMode,
                    primaryGuideVisible),
                Is.EqualTo(expected));
        }

        [Test]
        public void RefreshAppliesProfileToWiredRectTransforms()
        {
            var root = new GameObject(
                "PoseGuideTest",
                typeof(RectTransform));
            var profile =
                ScriptableObject.CreateInstance<
                    PosingScoreProfile>();

            try
            {
                SetTarget(
                    profile,
                    new Vector3(-0.4f, 0.2f, 0.8f),
                    new Vector3(0.3f, 0.1f, -0.8f));

                var head = CreateChild(root, "Head");
                var leftHand =
                    CreateChild(root, "LeftHand");
                var rightHand =
                    CreateChild(root, "RightHand");
                var leftArm =
                    CreateChild(root, "LeftArm");
                var rightArm =
                    CreateChild(root, "RightArm");
                // The guide no longer owns a profile; it reads the pose
                // the scorer is currently asking for.
                var scorer =
                    root.AddComponent<ThreePointPosingScorer>();
                var serializedScorer =
                    new SerializedObject(scorer);
                var profiles =
                    serializedScorer.FindProperty("_profiles");
                profiles.arraySize = 1;
                profiles
                    .GetArrayElementAtIndex(0)
                    .objectReferenceValue = profile;
                serializedScorer.ApplyModifiedPropertiesWithoutUndo();

                var guide =
                    root.AddComponent<PoseReferenceGuide>();
                var serialized = new SerializedObject(guide);
                SetReference(
                    serialized,
                    "_posingScorer",
                    scorer);
                SetReference(serialized, "_headMarker", head);
                SetReference(
                    serialized,
                    "_leftHandMarker",
                    leftHand);
                SetReference(
                    serialized,
                    "_rightHandMarker",
                    rightHand);
                SetReference(
                    serialized,
                    "_leftArmLine",
                    leftArm);
                SetReference(
                    serialized,
                    "_rightArmLine",
                    rightArm);
                serialized.FindProperty("_headPosition")
                    .vector2Value = new Vector2(5f, 10f);
                serialized.FindProperty(
                        "_pixelsPerNormalizedUnit")
                    .floatValue = 100f;
                serialized.FindProperty(
                        "_shoulderHalfWidthNormalized")
                    .floatValue = 0.2f;
                serialized.FindProperty(
                        "_shoulderDropNormalized")
                    .floatValue = 0.3f;
                serialized.FindProperty("_armLineThickness")
                    .floatValue = 8f;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                guide.RefreshNow();

                AssertVector2(
                    head.anchoredPosition,
                    5f,
                    10f);
                AssertVector2(
                    leftHand.anchoredPosition,
                    -35f,
                    30f);
                AssertVector2(
                    rightHand.anchoredPosition,
                    35f,
                    20f);
                AssertVector2(
                    leftArm.anchoredPosition,
                    -25f,
                    5f);
                AssertVector2(
                    leftArm.sizeDelta,
                    Mathf.Sqrt(2900f),
                    8f);
                Assert.That(
                    leftArm.localEulerAngles.z,
                    Is.EqualTo(111.8014f).Within(0.0001f));
                AssertVector2(
                    rightArm.anchoredPosition,
                    30f,
                    0f);
                AssertVector2(
                    rightArm.sizeDelta,
                    Mathf.Sqrt(1700f),
                    8f);
                Assert.That(
                    rightArm.localEulerAngles.z,
                    Is.EqualTo(75.9638f).Within(0.0001f));
                AssertVector2(
                    leftArm.anchorMin,
                    0.5f,
                    0.5f);
                AssertVector2(
                    leftArm.pivot,
                    0.5f,
                    0.5f);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void VisibilityAndContentWidthFollowFlowPhase()
        {
            var root = new GameObject(
                "PoseGuideVisibilityTest",
                typeof(RectTransform));
            var visual = new GameObject(
                "Visual",
                typeof(RectTransform));
            var content = new GameObject(
                "Content",
                typeof(RectTransform));
            var flowHost = new GameObject(
                "PoseGuideVisibilityFlow");

            try
            {
                visual.transform.SetParent(root.transform, false);
                content.transform.SetParent(root.transform, false);
                var flow =
                    flowHost.AddComponent<GameFlowController>();
                var guide =
                    root.AddComponent<PoseReferenceGuide>();
                guide.enabled = false;
                var serialized = new SerializedObject(guide);
                SetReference(
                    serialized,
                    "_gameFlow",
                    flow);
                SetReference(
                    serialized,
                    "_visualRoot",
                    visual);
                SetReference(
                    serialized,
                    "_contentRect",
                    content.GetComponent<RectTransform>());
                serialized.FindProperty("_posingContentWidth")
                    .floatValue = 430f;
                serialized.FindProperty("_otherContentWidth")
                    .floatValue = 696f;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                SetFlowPhase(flow, ExperiencePhase.Posing);
                guide.enabled = true;

                Assert.That(visual.activeSelf, Is.True);
                Assert.That(
                    content.GetComponent<RectTransform>()
                        .sizeDelta.x,
                    Is.EqualTo(430f));

                SetFlowPhase(flow, ExperiencePhase.Results);
                InvokeRefreshVisibility(guide);

                Assert.That(visual.activeSelf, Is.False);
                Assert.That(
                    content.GetComponent<RectTransform>()
                        .sizeDelta.x,
                    Is.EqualTo(696f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(flowHost);
            }
        }

        [Test]
        public void AutomaticFallbackTracksPrimaryGuideAvailability()
        {
            var root = new GameObject(
                "PoseGuideFallbackTest",
                typeof(RectTransform));
            var visual = new GameObject(
                "Visual",
                typeof(RectTransform));
            var flowHost = new GameObject(
                "PoseGuideFallbackFlow");
            var primaryHost = new GameObject(
                "PoseGuidePrimary");
            var runtimeGhost = new GameObject(
                "RuntimeGhost");

            try
            {
                visual.transform.SetParent(root.transform, false);
                runtimeGhost.transform.SetParent(
                    primaryHost.transform,
                    false);
                var flow =
                    flowHost.AddComponent<GameFlowController>();
                var primary =
                    primaryHost.AddComponent<
                        PoseGhostGuideController>();
                var runtimeGhostField =
                    typeof(PoseGhostGuideController).GetField(
                        "_runtimeGhost",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                Assert.That(runtimeGhostField, Is.Not.Null);
                runtimeGhostField.SetValue(primary, runtimeGhost);

                var guide =
                    root.AddComponent<PoseReferenceGuide>();
                guide.enabled = false;
                var serialized = new SerializedObject(guide);
                SetReference(serialized, "_gameFlow", flow);
                SetReference(
                    serialized,
                    "_visualRoot",
                    visual);
                SetReference(
                    serialized,
                    "_primaryGuide",
                    primary);
                serialized.FindProperty("_displayMode")
                    .enumValueIndex = (int)
                        PoseReferenceDisplayMode
                            .AutomaticFallback;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                SetFlowPhase(flow, ExperiencePhase.Posing);
                guide.enabled = true;

                Assert.That(primary.IsGuideVisible, Is.True);
                Assert.That(
                    visual.activeSelf,
                    Is.False,
                    "The flat guide must not compete with the visible " +
                    "3D guide.");

                runtimeGhost.SetActive(false);
                InvokeRefreshVisibility(guide);

                Assert.That(primary.IsGuideVisible, Is.False);
                Assert.That(
                    visual.activeSelf,
                    Is.True,
                    "The flat guide must recover the instruction when the " +
                    "3D guide becomes unavailable.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(flowHost);
                Object.DestroyImmediate(primaryHost);
            }
        }

        private static RectTransform CreateChild(
            GameObject parent,
            string name)
        {
            var child = new GameObject(
                name,
                typeof(RectTransform));
            child.transform.SetParent(parent.transform, false);
            return child.GetComponent<RectTransform>();
        }

        private static void SetTarget(
            PosingScoreProfile profile,
            Vector3 leftHand,
            Vector3 rightHand)
        {
            var serialized = new SerializedObject(profile);
            var target = serialized.FindProperty("_target");
            target.FindPropertyRelative(
                    "LeftHandPositionFromHeadNormalized")
                .vector3Value = leftHand;
            target.FindPropertyRelative(
                    "RightHandPositionFromHeadNormalized")
                .vector3Value = rightHand;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetReference(
            SerializedObject serialized,
            string propertyName,
            Object value)
        {
            serialized.FindProperty(propertyName)
                .objectReferenceValue = value;
        }

        private static void AssertVector2(
            Vector2 actual,
            float expectedX,
            float expectedY)
        {
            Assert.That(
                actual.x,
                Is.EqualTo(expectedX).Within(0.0001f));
            Assert.That(
                actual.y,
                Is.EqualTo(expectedY).Within(0.0001f));
        }

        private static void SetFlowPhase(
            GameFlowController flow,
            ExperiencePhase phase)
        {
            var property =
                typeof(GameFlowController).GetProperty(
                    nameof(GameFlowController.CurrentPhase),
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null);
            property.SetValue(flow, phase);
        }

        private static void InvokeRefreshVisibility(
            PoseReferenceGuide guide)
        {
            var method =
                typeof(PoseReferenceGuide).GetMethod(
                    "RefreshVisibility",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(guide, null);
        }
    }
}
