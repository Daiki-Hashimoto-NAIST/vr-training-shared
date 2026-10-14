using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class PoseCompletionFeedbackTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance |
            BindingFlags.NonPublic;

        [Test]
        public void CompletionFiresOnceBeforeTheNextPoseStarts()
        {
            var host = new GameObject("Pose Completion Test");
            host.SetActive(false);
            var profiles = new[]
            {
                CreateProfile("FRONT DOUBLE BICEPS"),
                CreateProfile("SINGLE BICEPS")
            };

            try
            {
                var scorer =
                    host.AddComponent<ThreePointPosingScorer>();
                SetPrivateField(scorer, "_profiles", profiles);
                scorer.BeginRound();

                var order = new List<string>();
                var completions = new List<PoseCompletionResult>();
                scorer.PoseCompleted += result =>
                {
                    completions.Add(result);
                    order.Add($"complete:{result.PoseNumber}");
                };
                scorer.PoseChanged += profile =>
                    order.Add($"changed:{profile.DisplayName}");
                scorer.ScoreChanged += _ => order.Add("score");

                SetBestResult(scorer, CreateScore(86f));
                InvokeCompleteCurrentPose(scorer, true);

                Assert.That(completions, Has.Count.EqualTo(1));
                Assert.That(completions[0].PoseIndex, Is.Zero);
                Assert.That(completions[0].PoseCount, Is.EqualTo(2));
                Assert.That(completions[0].PoseDisplayName,
                    Is.EqualTo("FRONT DOUBLE BICEPS"));
                Assert.That(completions[0].Score,
                    Is.EqualTo(86f).Within(0.001f));
                Assert.That(completions[0].Succeeded, Is.True);
                Assert.That(completions[0].ClearedPoseCount,
                    Is.EqualTo(1));
                Assert.That(completions[0].IsFinalPose, Is.False);
                Assert.That(order, Is.EqualTo(new[]
                {
                    "complete:1",
                    "changed:SINGLE BICEPS",
                    "score"
                }));

                order.Clear();
                SetBestResult(scorer, CreateScore(92f));
                InvokeCompleteCurrentPose(scorer, true);

                Assert.That(completions, Has.Count.EqualTo(2));
                Assert.That(completions[1].PoseIndex, Is.EqualTo(1));
                Assert.That(completions[1].IsFinalPose, Is.True);
                Assert.That(completions[1].ClearedPoseCount,
                    Is.EqualTo(2));
                Assert.That(order, Is.EqualTo(new[]
                {
                    "complete:2",
                    "score"
                }));
                Assert.That(scorer.HasCompletedRoutine, Is.True);
            }
            finally
            {
                DestroyProfiles(profiles);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TimeoutIsReportedButPhaseBackstopDoesNotFloodEvents()
        {
            var host = new GameObject("Pose Timeout Test");
            host.SetActive(false);
            var profiles = new[]
            {
                CreateProfile("POSE ONE"),
                CreateProfile("POSE TWO"),
                CreateProfile("POSE THREE")
            };

            try
            {
                var scorer =
                    host.AddComponent<ThreePointPosingScorer>();
                SetPrivateField(scorer, "_profiles", profiles);
                scorer.BeginRound();

                var completions = new List<PoseCompletionResult>();
                scorer.PoseCompleted += completions.Add;
                SetBestResult(scorer, CreateScore(43f));
                InvokeCompleteCurrentPose(scorer, false);

                Assert.That(completions, Has.Count.EqualTo(1));
                Assert.That(completions[0].Succeeded, Is.False);
                Assert.That(completions[0].PoseIndex, Is.Zero);
                Assert.That(completions[0].ClearedPoseCount, Is.Zero);

                scorer.FinishRound();

                Assert.That(completions, Has.Count.EqualTo(1),
                    "The phase backstop must not emit synthetic feedback " +
                    "for every unfinished pose.");
                Assert.That(scorer.HasCompletedRoutine, Is.True);
            }
            finally
            {
                DestroyProfiles(profiles);
                Object.DestroyImmediate(host);
            }
        }

        private static PosingScoreProfile CreateProfile(string name)
        {
            var profile =
                ScriptableObject.CreateInstance<PosingScoreProfile>();
            SetPrivateField(profile, "_displayName", name);
            return profile;
        }

        private static PoseScoreResult CreateScore(float totalScore)
        {
            return new PoseScoreResult(
                true,
                PoseScaleBasis.HmdHeight,
                1.7f,
                totalScore,
                totalScore,
                totalScore,
                100f,
                totalScore,
                totalScore,
                2f,
                true);
        }

        private static void SetBestResult(
            ThreePointPosingScorer scorer,
            PoseScoreResult result)
        {
            var property = typeof(ThreePointPosingScorer).GetProperty(
                nameof(ThreePointPosingScorer.BestResult),
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null);
            property.SetValue(scorer, result);
        }

        private static void InvokeCompleteCurrentPose(
            ThreePointPosingScorer scorer,
            bool succeeded)
        {
            var method = typeof(ThreePointPosingScorer).GetMethod(
                "CompleteCurrentPose",
                PrivateInstance);
            Assert.That(method, Is.Not.Null);
            method.Invoke(scorer, new object[] { succeeded });
        }

        private static void SetPrivateField<T>(
            object target,
            string fieldName,
            T value)
        {
            var field = target.GetType().GetField(
                fieldName,
                PrivateInstance);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private static void DestroyProfiles(
            IEnumerable<PosingScoreProfile> profiles)
        {
            foreach (var profile in profiles)
            {
                Object.DestroyImmediate(profile);
            }
        }
    }
}
