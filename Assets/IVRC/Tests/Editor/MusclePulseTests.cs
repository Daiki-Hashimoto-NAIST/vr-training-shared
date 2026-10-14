using NUnit.Framework;
using UnityEngine;

namespace IVRC.Tests
{
    public sealed class MusclePulseTests
    {
        private static MusclePulseSettings Settings =>
            new MusclePulseSettings
            {
                GlowHalfLifeSeconds = 0.2f,
                OvershootSeconds = 0.4f,
                MissedTimingGlow = 0.2f,
                PerfectGlow = 1f,
                RepOvershoot = 0.04f,
                RepsPerMilestone = 4,
                MilestoneOvershoot = 0.1f,
                MilestoneGlowScale = 0.35f
            };

        [Test]
        public void LightRisesImmediately()
        {
            var pulse = new MuscleGrowthPulse();
            pulse.Strike(0.8f, 0.1f, 0.95f, 0.4f);

            Assert.That(pulse.Glow, Is.EqualTo(0.8f));
            Assert.That(pulse.IsActive, Is.True);
            Assert.That(
                pulse.Grade,
                Is.EqualTo(TrainingTimingGrade.Perfect));
        }

        /// <summary>
        /// The shape does not: it dips first, so the swell has somewhere to
        /// come from. Nothing else in the experience reads the overshoot as
        /// necessarily positive.
        /// </summary>
        [Test]
        public void TheShapeDipsBeforeItSwells()
        {
            var pulse = new MuscleGrowthPulse();
            pulse.Strike(0f, 0.4f, 1f, 0.4f);

            Assert.That(pulse.HasOvershoot, Is.True);
            Assert.That(pulse.Overshoot, Is.Zero.Within(0.0001f));

            // Into the anticipation, before the rise.
            pulse.Advance(0.03f, 0.2f);
            Assert.That(
                pulse.Overshoot,
                Is.LessThan(0f),
                "The body has to get thinner before it gets bigger.");
        }

        [Test]
        public void TheShapeReachesItsPeakAndHoldsThere()
        {
            var pulse = new MuscleGrowthPulse();
            pulse.Strike(0f, 0.4f, 1f, 0.4f);

            // Past the dip and the rise, into the hold.
            pulse.Advance(0.09f, 0.2f);
            var atHoldStart = pulse.Overshoot;
            Assert.That(
                atHoldStart,
                Is.EqualTo(0.4f).Within(0.0001f));

            pulse.Advance(0.05f, 0.2f);
            Assert.That(
                pulse.Overshoot,
                Is.EqualTo(0.4f).Within(0.0001f),
                "A peak of one frame cannot be seen at 72 fps.");
        }

        [Test]
        public void TheShapeSettlesThroughThinnerThanItStarted()
        {
            var pulse = new MuscleGrowthPulse();
            pulse.Strike(0f, 0.4f, 1f, 0.4f);

            var wentBelowResting = false;
            for (var step = 0; step < 40; step++)
            {
                pulse.Advance(0.01f, 0.2f);
                if (pulse.Overshoot < -0.001f &&
                    step > 20)
                {
                    wentBelowResting = true;
                }
            }

            Assert.That(
                wentBelowResting,
                Is.True,
                "Returning past resting is what makes it read as flesh.");
        }

        /// <summary>
        /// Exponential decay never arrived, so the body sat permanently
        /// half-inflated and no milestone ever finished. This one ends.
        /// </summary>
        [Test]
        public void TheShapeMoveEndsBeforeTheNextMilestoneCouldArrive()
        {
            var pulse = new MuscleGrowthPulse();
            pulse.Strike(0f, 0.4f, 1f, 0.4f);
            pulse.Advance(0.41f, 0.2f);

            Assert.That(pulse.HasOvershoot, Is.False);
            Assert.That(pulse.Overshoot, Is.Zero);
            Assert.That(pulse.IsActive, Is.False);
        }

        [Test]
        public void GlowHalvesOverOneHalfLife()
        {
            var pulse = new MuscleGrowthPulse();
            pulse.Strike(1f, 0f, 1f, 0.4f);
            pulse.Advance(0.2f, 0.2f);

            Assert.That(
                pulse.Glow,
                Is.EqualTo(0.5f).Within(0.001f));
        }

        /// <summary>
        /// The headset does not hold a fixed frame time, so one long step and
        /// many short ones covering the same span must land in the same place.
        /// </summary>
        [Test]
        public void NeitherLightNorShapeDependsOnFrameRate()
        {
            var coarse = new MuscleGrowthPulse();
            coarse.Strike(1f, 0.4f, 1f, 0.4f);
            coarse.Advance(0.3f, 0.2f);

            var fine = new MuscleGrowthPulse();
            fine.Strike(1f, 0.4f, 1f, 0.4f);
            for (var step = 0; step < 30; step++)
            {
                fine.Advance(0.01f, 0.2f);
            }

            Assert.That(
                fine.Glow,
                Is.EqualTo(coarse.Glow).Within(0.001f));
            Assert.That(
                fine.Overshoot,
                Is.EqualTo(coarse.Overshoot).Within(0.001f));
        }

        [Test]
        public void WeakerRepBrightensNothingButRecolours()
        {
            var pulse = new MuscleGrowthPulse();
            pulse.Strike(1f, 0f, 1f, 0.4f);
            pulse.Strike(0.2f, 0f, 0.1f, 0.4f);

            Assert.That(pulse.Glow, Is.EqualTo(1f));
            Assert.That(
                pulse.Grade,
                Is.EqualTo(TrainingTimingGrade.Miss));
        }

        /// <summary>
        /// Two swells running at once read as one long wobble, which is what
        /// the shaped move replaced, so a new milestone restarts it.
        /// </summary>
        [Test]
        public void ANewMilestoneRestartsTheMoveRatherThanBlending()
        {
            var pulse = new MuscleGrowthPulse();
            pulse.Strike(0f, 0.4f, 1f, 0.4f);
            pulse.Advance(0.2f, 0.2f);
            pulse.Strike(0f, 0.4f, 1f, 0.4f);

            Assert.That(
                pulse.Overshoot,
                Is.Zero.Within(0.0001f),
                "Restarted, so it is back at the start of the dip.");
        }

        [Test]
        public void PulseSettlesExactlyAtZero()
        {
            var pulse = new MuscleGrowthPulse();
            pulse.Strike(1f, 1f, 1f, 0.4f);
            for (var step = 0; step < 200; step++)
            {
                pulse.Advance(0.05f, 0.2f);
            }

            Assert.That(pulse.Glow, Is.Zero);
            Assert.That(pulse.Overshoot, Is.Zero);
            Assert.That(pulse.IsActive, Is.False);
        }

        [Test]
        public void BadNumbersNeverMoveThePulse()
        {
            var pulse = new MuscleGrowthPulse();
            pulse.Strike(
                float.NaN,
                float.PositiveInfinity,
                float.NaN,
                float.NaN);

            Assert.That(pulse.Glow, Is.Zero);
            Assert.That(pulse.Overshoot, Is.Zero);

            pulse.Strike(0.5f, 0.1f, 0.5f, 0.4f);
            pulse.Advance(0.09f, 0.2f);
            var settledGlow = pulse.Glow;
            var atPeak = pulse.Overshoot;

            pulse.Advance(float.NaN, 0.2f);
            pulse.Advance(-1f, 0.2f);

            Assert.That(pulse.Glow, Is.EqualTo(settledGlow));
            Assert.That(pulse.Overshoot, Is.EqualTo(atPeak));
        }

        [Test]
        public void AnUnstruckShapeStaysStill()
        {
            Assert.That(
                MusclePulseMath.EvaluateOvershoot(0.1f, 0f, 0.4f),
                Is.Zero);
            Assert.That(
                MusclePulseMath.EvaluateOvershoot(-1f, 0.4f, 0.4f),
                Is.Zero);
            Assert.That(
                MusclePulseMath.EvaluateOvershoot(
                    float.NaN,
                    0.4f,
                    0.4f),
                Is.Zero);
            Assert.That(
                MusclePulseMath.EvaluateOvershoot(
                    0.1f,
                    0.4f,
                    float.NaN),
                Is.Not.Zero,
                "A broken duration falls back rather than freezing.");
        }

        [TestCase(0f, 0.2f)]
        [TestCase(0.5f, 0.6f)]
        [TestCase(1f, 1f)]
        public void MistimedRepsStillLightUpFaintly(
            float accuracy,
            float expected)
        {
            Assert.That(
                MusclePulseMath.GlowForAccuracy(
                    accuracy,
                    0.2f,
                    1f),
                Is.EqualTo(expected).Within(0.0001f));
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(3, false)]
        [TestCase(4, true)]
        [TestCase(8, true)]
        public void MilestonesLandOnEveryFourthRep(
            int repCount,
            bool expected)
        {
            Assert.That(
                MusclePulseMath.IsMilestone(repCount, 4),
                Is.EqualTo(expected));
        }

        [Test]
        public void AMilestoneSwellsFurtherThanAnOrdinaryRep()
        {
            var state = new MusclePulseState();
            var settings = Settings;

            for (var rep = 1; rep < 4; rep++)
            {
                Assert.That(
                    state.ReportRep(
                        MuscleGrowthChannel.ElbowFlexionLeft,
                        1f,
                        settings),
                    Is.False,
                    $"Rep {rep} should not be a milestone.");
                state.Advance(0.12f, settings);
                Assert.That(
                    state.GetOvershoot(
                        MuscleGrowthChannel.ElbowFlexionLeft),
                    Is.EqualTo(0.04f).Within(0.0001f),
                    $"Rep {rep} answers, at the ordinary size.");
                Assert.That(
                    state.GetGlow(
                        MuscleGrowthChannel.ElbowFlexionLeft),
                    Is.GreaterThan(0f));
                state.Advance(2f, settings);
            }

            Assert.That(
                state.ReportRep(
                    MuscleGrowthChannel.ElbowFlexionLeft,
                    1f,
                    settings),
                Is.True);

            // The move opens with a dip, so the peak is a moment later.
            state.Advance(0.09f, settings);
            Assert.That(
                state.GetOvershoot(
                    MuscleGrowthChannel.ElbowFlexionLeft),
                Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(
                settings.MilestoneOvershoot,
                Is.GreaterThan(settings.RepOvershoot * 2f),
                "A milestone that is only slightly bigger is not a " +
                "milestone, it is a rep.");
        }

        /// <summary>
        /// The peak is set by the repetition that struck, not inherited. A
        /// milestone must not latch every rep after it at milestone size.
        /// </summary>
        [Test]
        public void AnOrdinaryRepAfterAMilestoneIsOrdinaryAgain()
        {
            var state = new MusclePulseState();
            var settings = Settings;
            for (var rep = 0; rep < 4; rep++)
            {
                state.ReportRep(
                    MuscleGrowthChannel.ArmRaiseRight,
                    1f,
                    settings);
                state.Advance(2f, settings);
            }

            state.ReportRep(
                MuscleGrowthChannel.ArmRaiseRight,
                1f,
                settings);
            state.Advance(0.12f, settings);

            Assert.That(
                state.GetOvershoot(
                    MuscleGrowthChannel.ArmRaiseRight),
                Is.EqualTo(0.04f).Within(0.0001f));
        }

        /// <summary>
        /// Curling must light the arm that curled and nothing else, or the
        /// participant cannot tell which movement was credited.
        /// </summary>
        [Test]
        public void OneGroupLightsWithoutTheOthers()
        {
            var state = new MusclePulseState();
            state.ReportRep(
                MuscleGrowthChannel.ElbowFlexionLeft,
                1f,
                Settings);

            Assert.That(
                state.GetGlow(
                    MuscleGrowthChannel.ElbowFlexionLeft),
                Is.EqualTo(1f));
            Assert.That(
                state.GetGlow(
                    MuscleGrowthChannel.ElbowFlexionRight),
                Is.Zero);
            Assert.That(
                state.GetGlow(
                    MuscleGrowthChannel.ArmRaiseLeft),
                Is.Zero);
        }

        [TestCase(MuscleGrowthChannel.ChestLeft)]
        [TestCase(MuscleGrowthChannel.ChestRight)]
        public void DerivedGroupsAreNeverStruck(
            MuscleGrowthChannel channel)
        {
            var state = new MusclePulseState();

            Assert.That(
                state.ReportRep(channel, 1f, Settings),
                Is.False);
            Assert.That(state.GetGlow(channel), Is.Zero);
            Assert.That(state.GetRepCount(channel), Is.Zero);
        }

        [Test]
        public void StrongestGlowReportsItsOwnTiming()
        {
            var state = new MusclePulseState();
            state.ReportRep(
                MuscleGrowthChannel.ElbowFlexionLeft,
                0.2f,
                Settings);
            state.ReportRep(
                MuscleGrowthChannel.ElbowFlexionRight,
                1f,
                Settings);

            Assert.That(
                state.StrongestGlow,
                Is.EqualTo(1f));
            Assert.That(
                state.StrongestGlowAccuracy,
                Is.EqualTo(1f));
        }

        [Test]
        public void RetryStartsItsMilestonesFromTheBeginning()
        {
            var state = new MusclePulseState();
            var settings = Settings;
            for (var rep = 0; rep < 3; rep++)
            {
                state.ReportRep(
                    MuscleGrowthChannel.ArmRaiseRight,
                    1f,
                    settings);
            }

            state.Clear();

            Assert.That(state.IsActive, Is.False);
            Assert.That(
                state.GetRepCount(
                    MuscleGrowthChannel.ArmRaiseRight),
                Is.Zero);
            Assert.That(
                state.ReportRep(
                    MuscleGrowthChannel.ArmRaiseRight,
                    1f,
                    settings),
                Is.False,
                "The first rep after a retry cannot be a milestone.");
        }

        [Test]
        public void StateGoesQuietOnceEveryGroupHasFaded()
        {
            var state = new MusclePulseState();
            state.ReportRep(
                MuscleGrowthChannel.ElbowFlexionLeft,
                1f,
                Settings);
            Assert.That(state.IsActive, Is.True);

            for (var step = 0; step < 200; step++)
            {
                state.Advance(0.05f, Settings);
            }

            Assert.That(state.IsActive, Is.False);
            Assert.That(state.StrongestGlow, Is.Zero);
        }

        /// <summary>
        /// A component that predates these settings deserializes them as an
        /// empty struct. That must not stall the decay.
        /// </summary>
        [Test]
        public void EmptySettingsAreRepairedIntoUsableOnes()
        {
            var repaired = default(MusclePulseSettings)
                .Sanitized();

            Assert.That(
                repaired.GlowHalfLifeSeconds,
                Is.GreaterThan(0f));
            Assert.That(
                repaired.OvershootSeconds,
                Is.GreaterThan(0f));
            Assert.That(
                repaired.RepsPerMilestone,
                Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void SettingsAreClampedIntoRange()
        {
            var repaired = new MusclePulseSettings
            {
                GlowHalfLifeSeconds = float.NaN,
                OvershootSeconds = -3f,
                MissedTimingGlow = 5f,
                PerfectGlow = float.PositiveInfinity,
                RepsPerMilestone = -2,
                MilestoneOvershoot = 9f
            }.Sanitized();

            Assert.That(
                repaired.GlowHalfLifeSeconds,
                Is.EqualTo(
                    MusclePulseSettings.Default
                        .GlowHalfLifeSeconds));
            Assert.That(
                repaired.OvershootSeconds,
                Is.EqualTo(
                    MusclePulseSettings.Default
                        .OvershootSeconds));
            Assert.That(
                repaired.MissedTimingGlow,
                Is.EqualTo(1f));
            Assert.That(
                repaired.PerfectGlow,
                Is.EqualTo(1f));
            Assert.That(
                repaired.RepsPerMilestone,
                Is.EqualTo(1));
            Assert.That(
                repaired.MilestoneOvershoot,
                Is.EqualTo(1f));
        }

        /// <summary>
        /// The colour and the sound have to name the same verdict, so both
        /// read it from one place.
        /// </summary>
        [TestCase(1f, TrainingTimingGrade.Perfect, "PERFECT")]
        [TestCase(0.9f, TrainingTimingGrade.Perfect, "PERFECT")]
        [TestCase(0.7f, TrainingTimingGrade.Good, "GOOD")]
        [TestCase(0.4f, TrainingTimingGrade.Ok, "OK")]
        [TestCase(0f, TrainingTimingGrade.Miss, "MISS")]
        public void GradeAndLabelAgree(
            float accuracy,
            TrainingTimingGrade expectedGrade,
            string expectedLabel)
        {
            Assert.That(
                TrainingRhythmMath.GetTimingGrade(accuracy),
                Is.EqualTo(expectedGrade));
            Assert.That(
                TrainingRhythmMath.GetTimingLabel(accuracy),
                Is.EqualTo(expectedLabel));
        }

        /// <summary>
        /// A milestone competes with the growth that is already arriving on
        /// the same shape every repetition. On the current chart each group
        /// finishes in 16 reps, so every rep is worth 0.0625 on its own. At
        /// 0.15 the milestone was 2.4 times that and went unnoticed on the
        /// headset twice. It has to be several times the per-rep increment
        /// before it reads as an event rather than as more of the same.
        /// </summary>
        [Test]
        public void MilestoneOvershootOutweighsOneRepetitionOfGrowth()
        {
            const float growthPerRepOnCurrentChart = 1f / 16f;
            var settings = MusclePulseSettings.Default;

            Assert.That(
                settings.MilestoneOvershoot,
                Is.GreaterThan(growthPerRepOnCurrentChart * 4f),
                "A milestone that a participant cannot pick out from the " +
                "steady growth is not a milestone.");
            Assert.That(
                settings.MilestoneOvershoot,
                Is.LessThanOrEqualTo(1f),
                "Growth is normalized, so more than 1 cannot be shown.");
        }

        [Test]
        public void TheShapeOutlastsTheLightButNeitherSmearsIntoTheNextRep()
        {
            var settings = MusclePulseSettings.Default;

            Assert.That(
                settings.GlowHalfLifeSeconds,
                Is.LessThan(0.5f),
                "At 120 BPM a rep lands every 0.5 s; a longer glow " +
                "would smear one rep into the next.");
            Assert.That(
                settings.OvershootSeconds,
                Is.GreaterThan(settings.GlowHalfLifeSeconds),
                "Shape is the heavier statement and has to last long " +
                "enough to register on someone who is moving.");
            Assert.That(
                settings.OvershootSeconds,
                Is.LessThan(4f * 0.5f),
                "Four reps at 120 BPM is when the next milestone lands, " +
                "and this one has to be over by then.");
        }

        [Test]
        public void ASilentBodyEmitsNothing()
        {
            var snapshot = new MuscleSurfaceSnapshot(
                0.8f,
                0.72f,
                1.08f,
                0.14f,
                0.25f,
                0f,
                TrainingTimingGrade.Perfect,
                1.6f,
                0.45f);

            Assert.That(
                snapshot.EvaluateEmission(),
                Is.EqualTo(Color.black));
        }

        /// <summary>
        /// Callers that predate the pulse, such as the stage entrance clone,
        /// must keep producing an unlit body.
        /// </summary>
        [Test]
        public void SurfaceSnapshotsWithoutAPulseStayDark()
        {
            var snapshot = new MuscleSurfaceSnapshot(
                1f,
                0.72f,
                1.08f,
                0.14f,
                0.25f);

            Assert.That(snapshot.PulseGlow, Is.Zero);
            Assert.That(
                snapshot.EvaluateEmission(),
                Is.EqualTo(Color.black));
        }

        [Test]
        public void EmissionCarriesTheVerdictColourAndTheGlow()
        {
            var snapshot = new MuscleSurfaceSnapshot(
                0.5f,
                0.72f,
                1.08f,
                0.14f,
                0.25f,
                0.5f,
                TrainingTimingGrade.Ok,
                2f,
                0f);
            var expected =
                TrainingTimingPalette.GetGradeColor(
                    TrainingTimingGrade.Ok);
            var emission = snapshot.EvaluateEmission();

            Assert.That(
                emission.r,
                Is.EqualTo(expected.r).Within(0.0001f));
            Assert.That(
                emission.g,
                Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(
                emission.b,
                Is.EqualTo(expected.b).Within(0.0001f));
        }

        /// <summary>
        /// On the headset the full-strength colours read as paint on the skin.
        /// Whitening lightens the flash without losing which verdict it is.
        /// </summary>
        [Test]
        public void WhiteningLightensTheFlashButKeepsTheVerdict()
        {
            var plain = TrainingTimingPalette.GetGradeColor(
                TrainingTimingGrade.Perfect);
            var lightened = TrainingTimingPalette.GetGradeColor(
                TrainingTimingGrade.Perfect,
                0.45f);

            Assert.That(
                lightened.r,
                Is.GreaterThan(plain.r));
            Assert.That(
                lightened.b,
                Is.GreaterThan(plain.b));
            Assert.That(
                lightened.g - lightened.r,
                Is.GreaterThan(0f),
                "Perfect must still lean green after whitening.");
            Assert.That(
                TrainingTimingPalette.GetGradeColor(
                    TrainingTimingGrade.Perfect,
                    0f),
                Is.EqualTo(plain));
            Assert.That(
                TrainingTimingPalette.GetGradeColor(
                    TrainingTimingGrade.Perfect,
                    1f),
                Is.EqualTo(Color.white));
        }

        [Test]
        public void VerdictsStayApartOnceWhitened()
        {
            var grades = new[]
            {
                TrainingTimingGrade.Miss,
                TrainingTimingGrade.Ok,
                TrainingTimingGrade.Good,
                TrainingTimingGrade.Perfect
            };

            for (var first = 0;
                 first < grades.Length;
                 first++)
            {
                for (var second = first + 1;
                     second < grades.Length;
                     second++)
                {
                    Assert.That(
                        TrainingTimingPalette.GetGradeColor(
                            grades[first],
                            0.45f),
                        Is.Not.EqualTo(
                            TrainingTimingPalette.GetGradeColor(
                                grades[second],
                                0.45f)),
                        $"{grades[first]} and {grades[second]} " +
                        "cannot converge at the shipped whiteness.");
                }
            }
        }

        [Test]
        public void EveryVerdictGetsItsOwnColour()
        {
            var grades = new[]
            {
                TrainingTimingGrade.Miss,
                TrainingTimingGrade.Ok,
                TrainingTimingGrade.Good,
                TrainingTimingGrade.Perfect
            };

            for (var first = 0;
                 first < grades.Length;
                 first++)
            {
                for (var second = first + 1;
                     second < grades.Length;
                     second++)
                {
                    Assert.That(
                        TrainingTimingPalette.GetGradeColor(
                            grades[first]),
                        Is.Not.EqualTo(
                            TrainingTimingPalette.GetGradeColor(
                                grades[second])),
                        $"{grades[first]} and {grades[second]} " +
                        "cannot share a colour.");
                }
            }
        }

        /// <summary>
        /// The mirror avatar is bound through the same output as the tracked
        /// body, so both renderers must come back lit from one call. A body
        /// that glows while its reflection does not would read as a bug in the
        /// mirror rather than as a reward.
        /// </summary>
        [Test]
        public void TheBodyAndItsMirrorLightTogether()
        {
            var host = new GameObject("MusclePulseEmissionTest");
            var material = new Material(
                Shader.Find(
                    "Universal Render Pipeline/Lit"));
            var normal = new Texture2D(2, 2);
            var mesh = CreatePulseMesh();
            try
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
                material.EnableKeyword("_EMISSION");
                var body = AddRenderer(host, mesh, material);
                var mirror = AddRenderer(host, mesh, material);

                var synchronizer =
                    new MuscleSurfaceStateSynchronizer();
                Assert.That(
                    synchronizer.Rebuild(
                        new Renderer[] { body, mirror }),
                    Is.EqualTo(2),
                    synchronizer.ValidationError);
                Assert.That(
                    synchronizer.EmissionIsUnavailable,
                    Is.False);
                Assert.That(
                    synchronizer.Apply(
                        new MuscleSurfaceSnapshot(
                            0.5f,
                            0.72f,
                            1.08f,
                            0.14f,
                            0.25f,
                            1f,
                            TrainingTimingGrade.Perfect,
                            2f,
                            0f)),
                    Is.EqualTo(2));

                foreach (var renderer in new Renderer[]
                         {
                             body,
                             mirror
                         })
                {
                    var applied = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(applied);
                    Assert.That(
                        applied.GetColor("_EmissionColor").g,
                        Is.GreaterThan(1f),
                        $"{renderer.name} was left dark.");
                }

                synchronizer.Clear();
                var restored = new MaterialPropertyBlock();
                body.GetPropertyBlock(restored);
                Assert.That(
                    restored.GetColor("_EmissionColor"),
                    Is.EqualTo(Color.clear));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(normal);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void MaterialsWithoutEmissionAreReported()
        {
            var host = new GameObject("MusclePulseNoEmissionTest");
            var material = new Material(
                Shader.Find(
                    "Universal Render Pipeline/Lit"));
            var normal = new Texture2D(2, 2);
            var mesh = CreatePulseMesh();
            try
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
                var body = AddRenderer(host, mesh, material);

                var synchronizer =
                    new MuscleSurfaceStateSynchronizer();
                Assert.That(
                    synchronizer.Rebuild(
                        new Renderer[] { body }),
                    Is.EqualTo(1),
                    synchronizer.ValidationError);
                Assert.That(
                    synchronizer.EmissionIsUnavailable,
                    Is.True,
                    "A body that cannot glow has to say so rather than " +
                    "look like a broken pulse.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(normal);
                Object.DestroyImmediate(mesh);
            }
        }

        private static SkinnedMeshRenderer AddRenderer(
            GameObject host,
            Mesh mesh,
            Material material)
        {
            var child = new GameObject("PulseRenderer");
            child.transform.SetParent(host.transform);
            var renderer =
                child.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            return renderer;
        }

        private static Mesh CreatePulseMesh()
        {
            var mesh = new Mesh
            {
                name = "MusclePulseMesh",
                vertices = new[]
                {
                    Vector3.zero,
                    Vector3.right,
                    Vector3.up
                },
                triangles = new[] { 0, 1, 2 }
            };
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>
        /// A group that has finished training still has to be able to show a
        /// milestone. Unity extrapolates a BlendShape past its endpoints, so
        /// the overshoot is carried beside the growth channels rather than
        /// added into them, where clamping to 1 would have swallowed it.
        /// </summary>
        [Test]
        public void AFullyGrownGroupCanStillOvershoot()
        {
            var state = new MusclePulseState();
            for (var rep = 0; rep < 4; rep++)
            {
                state.ReportRep(
                    MuscleGrowthChannel.ArmRaiseLeft,
                    1f,
                    Settings);
            }

            // Into the hold, past the dip that opens the move.
            state.Advance(0.09f, Settings);
            var overshoot = state.GetOvershoot(
                MuscleGrowthChannel.ArmRaiseLeft);
            Assert.That(overshoot, Is.EqualTo(0.1f).Within(0.0001f));

            var captured = state.CaptureOvershoot();
            Assert.That(
                captured.Get(MuscleGrowthChannel.ArmRaiseLeft),
                Is.EqualTo(overshoot).Within(0.0001f));
            Assert.That(
                1f + captured.Get(MuscleGrowthChannel.ArmRaiseLeft),
                Is.GreaterThan(1f),
                "Fully grown plus a milestone has to exceed fully grown.");
        }

        /// <summary>
        /// Overall drives the whole-body shape. A body swelling as one is the
        /// restlessness the milestone was meant to replace.
        /// </summary>
        [Test]
        public void TheWholeBodyNeverOvershootsAsOne()
        {
            var state = new MusclePulseState();
            for (var rep = 0; rep < 4; rep++)
            {
                state.ReportRep(
                    MuscleGrowthChannel.ElbowFlexionLeft,
                    1f,
                    Settings);
            }

            Assert.That(
                state.CaptureOvershoot()
                    .Get(MuscleGrowthChannel.Overall),
                Is.Zero);
        }

        /// <summary>
        /// Light washes out the silhouette, so the one rep that also changes
        /// the body must not hide it behind the brightest flash of the set.
        /// </summary>
        [Test]
        public void AMilestoneDimsItsOwnFlash()
        {
            var settings = Settings;
            settings.MilestoneGlowScale = 0.35f;

            var ordinary = new MusclePulseState();
            ordinary.ReportRep(
                MuscleGrowthChannel.ArmRaiseRight,
                1f,
                settings);
            var ordinaryGlow = ordinary.GetGlow(
                MuscleGrowthChannel.ArmRaiseRight);

            var milestone = new MusclePulseState();
            for (var rep = 0; rep < 3; rep++)
            {
                milestone.ReportRep(
                    MuscleGrowthChannel.ArmRaiseRight,
                    1f,
                    settings);

                // Let each flash die out, so the milestone's own dimmer flash
                // is what is measured rather than the previous rep's tail.
                milestone.Advance(2f, settings);
            }

            milestone.ReportRep(
                MuscleGrowthChannel.ArmRaiseRight,
                1f,
                settings);

            Assert.That(
                milestone.GetGlow(
                    MuscleGrowthChannel.ArmRaiseRight),
                Is.LessThan(ordinaryGlow));
            Assert.That(
                milestone.GetGlow(
                    MuscleGrowthChannel.ArmRaiseRight),
                Is.GreaterThan(0f),
                "Dimmed, not dropped: the rep still happened.");
        }
    }
}
