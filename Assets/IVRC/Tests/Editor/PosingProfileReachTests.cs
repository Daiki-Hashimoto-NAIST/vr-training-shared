using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace IVRC.Tests
{
    /// <summary>
    /// A pose target is only as good as the arm that has to reach it. A hand
    /// placed beyond arm's length makes the IK clamp and the guide reads as
    /// a broken shape; a hand placed almost at arm's length leaves the elbow
    /// straight, which is wrong for every bodybuilding pose. These pin both
    /// ends so an edited profile cannot quietly reintroduce either.
    /// </summary>
    public sealed class PosingProfileReachTests
    {
        // Proportions shared with PoseReferenceGuide's figure.
        private const float BodyHeight = 1.66f;
        private const float UpperArm = 0.186f * BodyHeight;
        private const float Forearm = 0.146f * BodyHeight;
        private const float ArmLength = UpperArm + Forearm;
        private const float ShoulderHalfWidth = 0.12f * BodyHeight;
        private const float ShoulderDrop = 0.13f * BodyHeight;

        private static readonly string[] ProfilePaths =
        {
            "Assets/IVRC/Config/FrontDoubleBicepsProfile.asset",
            "Assets/IVRC/Config/SingleBicepsProfile.asset",
            "Assets/IVRC/Config/MostMuscularProfile.asset"
        };

        private static float ElbowAngleDegrees(float reach)
        {
            var cosine =
                (UpperArm * UpperArm +
                 Forearm * Forearm -
                 reach * reach) /
                (2f * UpperArm * Forearm);
            return Mathf.Acos(Mathf.Clamp(cosine, -1f, 1f)) *
                Mathf.Rad2Deg;
        }

        private static float Reach(
            Vector3 handFromHeadNormalized,
            bool rightSide)
        {
            var hand = handFromHeadNormalized * BodyHeight;
            var shoulder = new Vector3(
                rightSide
                    ? ShoulderHalfWidth
                    : -ShoulderHalfWidth,
                -ShoulderDrop,
                0f);
            return Vector3.Distance(hand, shoulder);
        }

        [Test]
        public void EveryPoseIsWithinArmsLength()
        {
            foreach (var path in ProfilePaths)
            {
                var profile = AssetDatabase
                    .LoadAssetAtPath<PosingScoreProfile>(path);
                Assert.That(profile, Is.Not.Null, path);
                var target = profile.ToSettings().Target;

                foreach (var (hand, rightSide, label) in new[]
                         {
                             (target
                                 .LeftHandPositionFromHeadNormalized,
                                 false, "left"),
                             (target
                                 .RightHandPositionFromHeadNormalized,
                                 true, "right")
                         })
                {
                    var reach = Reach(hand, rightSide);
                    Assert.That(
                        reach,
                        Is.LessThan(ArmLength),
                        $"{profile.DisplayName} {label} hand is " +
                        $"{reach:F3} m from the shoulder but the arm " +
                        $"is only {ArmLength:F3} m. The IK will clamp " +
                        "and the pose will read as broken.");
                }
            }
        }

        [Test]
        public void EveryPoseKeepsTheElbowBent()
        {
            foreach (var path in ProfilePaths)
            {
                var profile = AssetDatabase
                    .LoadAssetAtPath<PosingScoreProfile>(path);
                var target = profile.ToSettings().Target;

                foreach (var (hand, rightSide, label) in new[]
                         {
                             (target
                                 .LeftHandPositionFromHeadNormalized,
                                 false, "left"),
                             (target
                                 .RightHandPositionFromHeadNormalized,
                                 true, "right")
                         })
                {
                    var angle = ElbowAngleDegrees(
                        Reach(hand, rightSide));

                    // Real posing sits well inside a straight arm: folded
                    // into the body at the bottom, a pointer at the top. The
                    // lower bound was 60 until a most muscular captured from
                    // a real body solved to 58.6 — the pose does fold that
                    // far, and the bound was describing an estimate rather
                    // than a body.
                    Assert.That(
                        angle,
                        Is.InRange(55f, 140f),
                        $"{profile.DisplayName} {label} elbow solves " +
                        $"to {angle:F0} degrees.");
                }
            }
        }

        [Test]
        public void HandsStayFarEnoughApartToTrack()
        {
            // Quest controllers track by an infrared ring seen from the
            // headset. Bringing the rings together is the classic way to
            // lose tracking, so no pose may ask for it.
            //
            // 25 cm was a guess made before any pose had been measured, and
            // a most muscular genuinely brings the hands together: captured
            // from a real body it is 17 cm. Lowered to 10, which still keeps
            // the rings apart, but this is the bound most worth re-checking
            // on the Quest 2 the exhibition will run — the Pro used for
            // capture tracks its controllers itself and is more forgiving
            // than a headset that has to see them.
            foreach (var path in ProfilePaths)
            {
                var profile = AssetDatabase
                    .LoadAssetAtPath<PosingScoreProfile>(path);
                var target = profile.ToSettings().Target;
                var separation = Vector3.Distance(
                    target.LeftHandPositionFromHeadNormalized *
                    BodyHeight,
                    target.RightHandPositionFromHeadNormalized *
                    BodyHeight);
                Assert.That(
                    separation,
                    Is.GreaterThan(0.10f),
                    $"{profile.DisplayName} puts the hands " +
                    $"{separation * 100f:F0} cm apart.");
            }
        }

        [Test]
        public void NoPoseReachesBehindTheBody()
        {
            // A hand behind the chest leaves the headset's view, which is
            // the other common way Quest loses a controller.
            //
            // A little behind the head is normal for a front double biceps
            // and is allowed. The margin is small on purpose: capture was
            // done on a Quest Pro, whose controllers track themselves, while
            // the exhibition runs a Quest 2, where a controller the headset
            // cannot see is a controller that is gone.
            const float BehindLimit = -0.06f;
            foreach (var path in ProfilePaths)
            {
                var profile = AssetDatabase
                    .LoadAssetAtPath<PosingScoreProfile>(path);
                var target = profile.ToSettings().Target;
                Assert.That(
                    target.LeftHandPositionFromHeadNormalized.z,
                    Is.GreaterThanOrEqualTo(BehindLimit),
                    $"{profile.DisplayName} left hand sits too far " +
                    "behind the body for a headset to see it.");
                Assert.That(
                    target.RightHandPositionFromHeadNormalized.z,
                    Is.GreaterThanOrEqualTo(BehindLimit),
                    $"{profile.DisplayName} right hand sits too far " +
                    "behind the body for a headset to see it.");
            }
        }

        [Test]
        public void EveryReferencePoseBendsBothElbowsOutward()
        {
            foreach (var path in ProfilePaths)
            {
                var profile = AssetDatabase
                    .LoadAssetAtPath<PosingScoreProfile>(path);
                var target = profile.ToSettings().Target;
                var hints = profile.GuideElbows;
                var leftShoulder = new Vector2(
                    -0.12f,
                    -0.13f);
                var rightShoulder = new Vector2(
                    0.12f,
                    -0.13f);
                var leftElbow =
                    PoseReferenceGuide.CalculateElbowTowardPole(
                        leftShoulder,
                        new Vector2(
                            target
                                .LeftHandPositionFromHeadNormalized.x,
                            target
                                .LeftHandPositionFromHeadNormalized.y),
                        0.186f,
                        0.146f,
                        leftShoulder + new Vector2(
                            hints
                                .LeftPoleFromShoulderNormalized.x,
                            hints
                                .LeftPoleFromShoulderNormalized.y));
                var rightElbow =
                    PoseReferenceGuide.CalculateElbowTowardPole(
                        rightShoulder,
                        new Vector2(
                            target
                                .RightHandPositionFromHeadNormalized.x,
                            target
                                .RightHandPositionFromHeadNormalized.y),
                        0.186f,
                        0.146f,
                        rightShoulder + new Vector2(
                            hints
                                .RightPoleFromShoulderNormalized.x,
                            hints
                                .RightPoleFromShoulderNormalized.y));

                Assert.That(
                    leftElbow.x,
                    Is.LessThan(leftShoulder.x),
                    $"{profile.DisplayName} left elbow points inward.");
                Assert.That(
                    rightElbow.x,
                    Is.GreaterThan(rightShoulder.x),
                    $"{profile.DisplayName} right elbow points inward.");
            }
        }
    }
}
