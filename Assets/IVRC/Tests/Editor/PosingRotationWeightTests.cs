using NUnit.Framework;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class PosingRotationWeightTests
    {
        private static PosingScoreProfile CreateProfile()
        {
            return ScriptableObject
                .CreateInstance<PosingScoreProfile>();
        }

        [Test]
        public void RotationIsNotScoredUntilTargetsAreCaptured()
        {
            // Every shipped profile leaves the hand rotation targets at
            // identity. Scoring against identity would ask the player to
            // hold the controllers unrotated, which no pose does, so the
            // rotation weight has to be dropped until real values exist.
            var profile = CreateProfile();
            try
            {
                Assert.That(
                    profile.ScoresHandRotation,
                    Is.False,
                    "Rotation scoring must be opt-in.");
                Assert.That(
                    profile.ToSettings().ScoreHandRotation,
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void DroppedRotationLeavesTheOtherWeightsIntact()
        {
            // The calculator normalizes by the total weight, so zeroing
            // rotation must redistribute its share rather than shrink the
            // score. Position and symmetry therefore stay untouched.
            var profile = CreateProfile();
            try
            {
                var weights = profile.ToSettings().Weights;
                Assert.That(
                    weights.Position,
                    Is.GreaterThan(0f));
                Assert.That(
                    weights.Position + weights.Symmetry,
                    Is.GreaterThan(0f),
                    "Something must still be graded spatially.");
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void RotationIsGradedExactlyWhenItWasCaptured()
        {
            foreach (var path in new[]
                     {
                         "Assets/IVRC/Config/" +
                         "FrontDoubleBicepsProfile.asset",
                         "Assets/IVRC/Config/" +
                         "SingleBicepsProfile.asset",
                         "Assets/IVRC/Config/" +
                         "MostMuscularProfile.asset"
                     })
            {
                var profile = UnityEditor.AssetDatabase
                    .LoadAssetAtPath<PosingScoreProfile>(path);
                Assert.That(
                    profile,
                    Is.Not.Null,
                    $"{path} is missing.");

                var target = profile.ToSettings().Target;
                var rotationsAuthored =
                    target.LeftHandEulerDegreesInStageFrame !=
                    Vector3.zero ||
                    target.RightHandEulerDegreesInStageFrame !=
                    Vector3.zero;

                // The two must agree: either real rotations were captured
                // and rotation is graded, or neither is true. Holding a
                // rotation target the scoring never reads is the state that
                // makes a wrist look like it matters when it does not.
                Assert.That(
                    profile.ScoresHandRotation,
                    Is.EqualTo(rotationsAuthored),
                    $"{profile.DisplayName} grades hand rotation " +
                    "without authored rotation targets, or the " +
                    "reverse.");

                Assert.That(
                    profile.ToSettings().ScoreHandRotation,
                    Is.EqualTo(rotationsAuthored));
            }
        }
    }
}
