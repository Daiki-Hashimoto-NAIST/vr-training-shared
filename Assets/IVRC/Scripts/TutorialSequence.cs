using System;
using System.Collections.Generic;
using UnityEngine;

namespace IVRC
{
    public enum TutorialStepKind
    {
        Message,
        Practice
    }

    /// <summary>
    /// One beat of the introduction: either a line to read, or a movement to
    /// try. Practice steps name the exercise the rep counter must report.
    /// </summary>
    [Serializable]
    public struct TutorialStep
    {
        public TutorialStepKind Kind;
        [TextArea(1, 3)] public string Message;
        public BodyRepKind Exercise;
        [Min(1)] public int RequiredReps;

        [Tooltip(
            "Message display duration. Practice steps keep running until " +
            "their required successful repetitions are completed.")]
        [Min(0.5f)] public float TimeLimitSeconds;
    }

    /// <summary>
    /// Progression through the introduction, with no Unity objects and no
    /// clock of its own, so every path can be driven from tests.
    ///
    /// Message steps advance on time. Practice steps only advance after the
    /// required successful repetitions, so missed attempts never end the
    /// tutorial and the participant can immediately try the next rhythm loop.
    /// </summary>
    public sealed class TutorialSequence
    {
        private const int SideLeft = 1 << 0;
        private const int SideRight = 1 << 1;

        private readonly List<TutorialStep> _steps;
        private float _elapsedInStep;
        private int _repsInStep;

        // Which arms have reported the repetition being counted right now.
        private int _sidesInRound;

        public TutorialSequence(IReadOnlyList<TutorialStep> steps)
        {
            _steps = new List<TutorialStep>();
            if (steps != null)
            {
                for (var index = 0; index < steps.Count; index++)
                {
                    _steps.Add(steps[index]);
                }
            }
        }

        public int StepCount => _steps.Count;
        public int CurrentIndex { get; private set; }
        public bool IsComplete => CurrentIndex >= _steps.Count;

        public TutorialStep CurrentStep =>
            IsComplete || _steps.Count <= 0
                ? default
                : _steps[CurrentIndex];

        public float ElapsedInStep => _elapsedInStep;
        public int RepsInStep => _repsInStep;

        /// <summary>
        /// How much of the current step's time limit has run, for a progress
        /// indicator. 1 means the step is about to advance on its own.
        /// </summary>
        public float StepTimeProgress
        {
            get
            {
                if (IsComplete)
                {
                    return 1f;
                }

                if (CurrentStep.Kind == TutorialStepKind.Practice)
                {
                    return RepProgress;
                }

                var limit = Mathf.Max(
                    0.5f,
                    CurrentStep.TimeLimitSeconds);
                return Mathf.Clamp01(_elapsedInStep / limit);
            }
        }

        public float RepProgress
        {
            get
            {
                var step = CurrentStep;
                if (IsComplete ||
                    step.Kind != TutorialStepKind.Practice)
                {
                    return 0f;
                }

                return Mathf.Clamp01(
                    _repsInStep /
                    (float)Mathf.Max(1, step.RequiredReps));
            }
        }

        public event Action<int> StepEntered;
        public event Action Completed;

        public void Restart()
        {
            CurrentIndex = 0;
            _elapsedInStep = 0f;
            _repsInStep = 0;
            _sidesInRound = 0;
            if (!IsComplete)
            {
                StepEntered?.Invoke(CurrentIndex);
            }
            else
            {
                Completed?.Invoke();
            }
        }

        /// <summary>
        /// Advances the clock. Returns true when the step changed, so the
        /// caller can refresh the display without polling.
        /// </summary>
        public bool Tick(float deltaTime)
        {
            if (IsComplete ||
                !IsFinite(deltaTime) ||
                deltaTime <= 0f)
            {
                return false;
            }

            _elapsedInStep += deltaTime;
            if (CurrentStep.Kind == TutorialStepKind.Practice)
            {
                return false;
            }

            var limit = Mathf.Max(
                0.5f,
                CurrentStep.TimeLimitSeconds);
            if (_elapsedInStep < limit)
            {
                return false;
            }

            return Advance();
        }

        /// <summary>
        /// The participant pressed the confirm button. Only messages can be
        /// skipped this way; practice waits for its required successes.
        /// </summary>
        public bool Confirm()
        {
            if (IsComplete ||
                CurrentStep.Kind != TutorialStepKind.Message)
            {
                return false;
            }

            return Advance();
        }

        /// <summary>
        /// One completed repetition was recognised, for one arm. Reps for a
        /// different exercise than the current step asks for are ignored
        /// rather than counted, so flailing cannot skip the step being taught.
        ///
        /// The counter reports each arm separately, but a participant curling
        /// both arms has done one repetition, not two. A side is credited only
        /// when it opens a new round; the other arm arriving straight after
        /// joins that round silently. Moving one arm alone still counts,
        /// because that side opens every round by itself, which is what keeps
        /// the promise that either arm will do.
        /// </summary>
        public bool ReportRep(
            BodyRepKind exercise,
            BodySide side)
        {
            if (IsComplete ||
                CurrentStep.Kind != TutorialStepKind.Practice ||
                CurrentStep.Exercise != exercise)
            {
                return false;
            }

            var sideFlag = side == BodySide.Left
                ? SideLeft
                : SideRight;
            if (_sidesInRound == 0)
            {
                // First arm of a new repetition.
                _sidesInRound = sideFlag;
            }
            else if ((_sidesInRound & sideFlag) == 0)
            {
                // The other arm finishing the repetition already counted.
                _sidesInRound |= sideFlag;
                return false;
            }
            else
            {
                // This arm has been counted once, so a new repetition began.
                _sidesInRound = sideFlag;
            }

            _repsInStep++;
            if (_repsInStep <
                Mathf.Max(1, CurrentStep.RequiredReps))
            {
                return false;
            }

            return Advance();
        }

        /// <summary>One already-aggregated rhythm success, including all
        /// required arms and both UP and DOWN. No per-arm deduplication.</summary>
        public bool ReportSuccessfulRep(BodyRepKind exercise)
        {
            if (IsComplete || CurrentStep.Kind != TutorialStepKind.Practice ||
                CurrentStep.Exercise != exercise) return false;
            _repsInStep++;
            return _repsInStep >= Mathf.Max(1, CurrentStep.RequiredReps) && Advance();
        }

        /// <summary>
        /// Ends the whole introduction, for a staff shortcut.
        /// </summary>
        public void SkipAll()
        {
            if (IsComplete)
            {
                return;
            }

            CurrentIndex = _steps.Count;
            _elapsedInStep = 0f;
            _repsInStep = 0;
            _sidesInRound = 0;
            Completed?.Invoke();
        }

        /// <summary>
        /// Configured phase-duration estimate used by the flow HUD. Practice
        /// completion is not controlled by this estimate; it remains gated by
        /// successful repetitions.
        /// </summary>
        public float WorstCaseSeconds()
        {
            var total = 0f;
            for (var index = 0; index < _steps.Count; index++)
            {
                total += Mathf.Max(
                    0.5f,
                    _steps[index].TimeLimitSeconds);
            }

            return total;
        }

        private bool Advance()
        {
            CurrentIndex++;
            _elapsedInStep = 0f;
            _repsInStep = 0;
            _sidesInRound = 0;
            if (IsComplete)
            {
                Completed?.Invoke();
            }
            else
            {
                StepEntered?.Invoke(CurrentIndex);
            }

            return true;
        }

        private static bool IsFinite(float value)
        {
            return
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }
    }
}
