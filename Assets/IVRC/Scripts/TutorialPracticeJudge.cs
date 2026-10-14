using System;
using System.Collections.Generic;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Repeating tutorial notes follow the guide's UP=0, DOWN=UpToDownBeats
    /// schedule. BodyRepCounter supplies the same pose/angle gates as Training;
    /// TrainingRhythmMath and TrainingRepTimingResult own the success rules.
    /// No practice result is sent to the training score or growth system.
    /// </summary>
    public sealed class TutorialPracticeJudge
    {
        public const string EarlyMessage = "はやい！";
        public const string LateMessage = "おそい！";
        public const string PoseMessage = "ポーズをガイドに合わせて！";

        private sealed class Attempt
        {
            public readonly bool[,] Resolved = new bool[2, 2];
            public readonly bool[,] Hit = new bool[2, 2];
            public readonly float[,] Accuracy = new float[2, 2];
            public string Failure;
        }

        private readonly Dictionary<int, Attempt> _attempts = new();
        private TrainingRhythmBlock _block;
        private float _secondsPerBeat;
        private float _perfectWindow;
        private float _maximumWindow;
        private int _nextCycle;
        public bool IsRunning => _block != null;
        public event Action<BodyRepKind> RepetitionSucceeded;
        public event Action<string> FeedbackReady;

        public void Begin(TrainingRhythmBlock block, float beat,
            float secondsPerBeat, float perfectWindow, float maximumWindow)
        {
            Reset();
            if (block == null || secondsPerBeat <= 0f ||
                float.IsNaN(beat) || float.IsInfinity(beat)) return;
            _block = block;
            _secondsPerBeat = secondsPerBeat;
            _perfectWindow = perfectWindow;
            _maximumWindow = Mathf.Max(0.001f, maximumWindow);
            // Start with a complete upcoming window, never a half-finished rep
            // inherited from a message, previous exercise, or tracking loss.
            _nextCycle = Mathf.CeilToInt(
                (beat + _maximumWindow / secondsPerBeat) / block.BeatsPerRep);
        }

        public void Reset()
        {
            _block = null;
            _attempts.Clear();
        }

        private double TargetSeconds(int cycle, int point) =>
            (cycle * (double)_block.BeatsPerRep +
             (point == 0 ? 0 : _block.UpToDownBeats)) * _secondsPerBeat;

        private bool Requires(int side) =>
            (_block.RequiredSides & (side == 0
                ? TrainingSideMask.Left : TrainingSideMask.Right)) != 0;

        private Attempt GetAttempt(int cycle)
        {
            if (!_attempts.TryGetValue(cycle, out var attempt))
            {
                attempt = new Attempt();
                _attempts.Add(cycle, attempt);
            }
            return attempt;
        }

        public void Report(BodyRepKind kind, BodySide side,
            RepTimingPoint point, float beat)
        {
            if (!IsRunning || kind != _block.Kind) return;
            var s = side == BodySide.Left ? 0 : 1;
            if (!Requires(s)) return;
            var p = point == RepTimingPoint.Up ? 0 : 1;
            var now = beat * (double)_secondsPerBeat;
            var lastCandidate = Mathf.FloorToInt(
                (beat + _maximumWindow / _secondsPerBeat) / _block.BeatsPerRep);
            for (var cycle = _nextCycle; cycle <= lastCandidate; cycle++)
            {
                var target = TargetSeconds(cycle, p);
                if (now < target - _maximumWindow) break;
                if (now > target + _maximumWindow) continue;
                var attempt = GetAttempt(cycle);
                if (attempt.Resolved[s, p]) continue;
                // Match the chart's ordering: a DOWN cannot consume a note
                // before that side's UP has resolved or its window has closed.
                if (p == 1 && !attempt.Resolved[s, 0] &&
                    now <= TargetSeconds(cycle, 0) + _maximumWindow) return;
                var offset = (float)(now - target);
                var accuracy = TrainingRhythmMath.CalculateNormalizedAccuracy(
                    Mathf.Abs(offset), _perfectWindow, _maximumWindow);
                attempt.Resolved[s, p] = true;
                attempt.Hit[s, p] = true;
                attempt.Accuracy[s, p] = accuracy;
                if (accuracy < TrainingSuccessFeedbackRules.MinimumSuccessAccuracy)
                {
                    attempt.Failure = offset < 0f ? EarlyMessage : LateMessage;
                    FeedbackReady?.Invoke(attempt.Failure);
                }
                Tick(beat);
                return;
            }

            // An out-of-window movement is useful feedback, but never a hit.
            var nearestCycle = Mathf.Max(_nextCycle, Mathf.RoundToInt(
                (beat - (p == 0 ? 0 : _block.UpToDownBeats)) / _block.BeatsPerRep));
            FeedbackReady?.Invoke(now < TargetSeconds(nearestCycle, p)
                ? EarlyMessage : LateMessage);
        }

        public void Tick(float beat)
        {
            if (!IsRunning) return;
            var now = beat * (double)_secondsPerBeat;
            // Bound work after a suspended application. The controller resets
            // the judge on tracking loss and music/phase changes.
            for (var count = 0; count < 64; count++)
            {
                var cycle = _nextCycle;
                if (now < TargetSeconds(cycle, 0) - _maximumWindow) return;
                var attempt = GetAttempt(cycle);
                for (var p = 0; p < 2; p++)
                {
                    for (var s = 0; s < 2; s++)
                    {
                        if (!Requires(s) || attempt.Resolved[s, p] ||
                            now <= TargetSeconds(cycle, p) + _maximumWindow) continue;
                        attempt.Resolved[s, p] = true;
                        if (attempt.Failure == null)
                        {
                            attempt.Failure = PoseMessage;
                            FeedbackReady?.Invoke(PoseMessage);
                        }
                    }
                }
                if (!AllResolved(attempt)) return;
                var kind = _block.Kind;
                var result = new TrainingRepTimingResult(0, cycle, cycle + 1,
                    kind, _block.RequiredSides,
                    Feedback(attempt, cycle, 0), Feedback(attempt, cycle, 1),
                    TrainingRepFinalizationReason.Completed);
                _attempts.Remove(cycle);
                _nextCycle++;
                if (TrainingSuccessFeedbackRules.IsRhythmClear(result))
                {
                    RepetitionSucceeded?.Invoke(kind);
                }
                if (!IsRunning) return;
            }
        }

        private bool AllResolved(Attempt attempt)
        {
            for (var s = 0; s < 2; s++)
                if (Requires(s) && (!attempt.Resolved[s, 0] || !attempt.Resolved[s, 1]))
                    return false;
            return true;
        }

        private TrainingTimingFeedback Feedback(Attempt attempt, int cycle, int point)
        {
            var hit = true;
            var accuracy = 1f;
            for (var s = 0; s < 2; s++)
            {
                if (!Requires(s)) continue;
                hit &= attempt.Hit[s, point];
                accuracy = Mathf.Min(accuracy, attempt.Accuracy[s, point]);
            }
            return new TrainingTimingFeedback(cycle, cycle + 1, _block.Kind,
                point == 0 ? RepTimingPoint.Up : RepTimingPoint.Down, accuracy, hit);
        }
    }
}
