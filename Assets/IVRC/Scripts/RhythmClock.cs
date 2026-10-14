using System;
using UnityEngine;

namespace IVRC
{
    public readonly struct RhythmBeat
    {
        public RhythmBeat(
            int beatIndex,
            int beatInBar,
            int barIndex,
            double dspTime)
        {
            BeatIndex = beatIndex;
            BeatInBar = beatInBar;
            BarIndex = barIndex;
            DspTime = dspTime;
        }

        public int BeatIndex { get; }
        public int BeatInBar { get; }
        public int BarIndex { get; }
        public double DspTime { get; }
    }

    /// <summary>
    /// Provides one DSP-time-based beat source for visuals, haptics, scoring,
    /// audio, and hardware output adapters.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class RhythmClock : MonoBehaviour
    {
        [Header("Tempo")]
        [SerializeField, Range(30f, 240f)] private float _beatsPerMinute = 80f;
        [SerializeField, Min(1)] private int _beatsPerBar = 4;
        [SerializeField, Min(0f)] private float _startDelaySeconds = 0.1f;

        private const int MaximumCatchUpBeatsPerFrame = 8;

        private double _clockOriginDspTime;
        private double _nextBeatDspTime;
        private double _pausedAtDspTime;
        private int _nextBeatIndex;
        private int _clockSessionId;
        private bool _hasStarted;

        public event Action<RhythmBeat> BeatOccurred;

        public float BeatsPerMinute => _beatsPerMinute;
        public int BeatsPerBar => _beatsPerBar;
        public float SecondsPerBeat => 60f / Mathf.Max(1f, _beatsPerMinute);
        public bool HasStarted => _hasStarted;
        public int ClockSessionId => _clockSessionId;
        public bool IsRunning { get; private set; }
        public int LastBeatIndex { get; private set; } = -1;
        public int LastBeatInBar =>
            LastBeatIndex < 0 ? -1 : LastBeatIndex % _beatsPerBar;
        public double LastBeatDspTime { get; private set; }

        private void Update()
        {
            if (!IsRunning)
            {
                return;
            }

            var currentDspTime = AudioSettings.dspTime;
            var emittedThisFrame = 0;
            while (currentDspTime >= _nextBeatDspTime &&
                   emittedThisFrame < MaximumCatchUpBeatsPerFrame)
            {
                EmitBeat(_nextBeatIndex, _nextBeatDspTime);
                _nextBeatIndex++;
                _nextBeatDspTime =
                    _clockOriginDspTime + _nextBeatIndex * SecondsPerBeat;
                emittedThisFrame++;
            }

            if (currentDspTime < _nextBeatDspTime)
            {
                return;
            }

            // Do not flood listeners after a long frame or debugger pause.
            var beatsBehind = Mathf.FloorToInt(
                (float)((currentDspTime - _nextBeatDspTime) / SecondsPerBeat)) + 1;
            _nextBeatIndex += Mathf.Max(1, beatsBehind);
            _nextBeatDspTime =
                _clockOriginDspTime + _nextBeatIndex * SecondsPerBeat;
        }

        [ContextMenu("Start Rhythm Clock")]
        public void StartClock()
        {
            _clockOriginDspTime =
                AudioSettings.dspTime + Mathf.Max(0f, _startDelaySeconds);
            _nextBeatDspTime = _clockOriginDspTime;
            _pausedAtDspTime = 0d;
            _nextBeatIndex = 0;
            LastBeatIndex = -1;
            LastBeatDspTime = 0d;
            _hasStarted = true;
            unchecked
            {
                _clockSessionId++;
            }

            IsRunning = true;
        }

        [ContextMenu("Stop Rhythm Clock")]
        public void StopClock()
        {
            if (IsRunning)
            {
                _pausedAtDspTime = AudioSettings.dspTime;
            }

            IsRunning = false;
        }

        [ContextMenu("Resume Rhythm Clock")]
        public void ResumeClock()
        {
            if (IsRunning)
            {
                return;
            }

            if (!_hasStarted)
            {
                StartClock();
                return;
            }

            var currentDspTime = AudioSettings.dspTime;
            var pausedDuration = Math.Max(
                0d,
                currentDspTime - _pausedAtDspTime);
            _clockOriginDspTime += pausedDuration;
            _nextBeatDspTime += pausedDuration;
            _pausedAtDspTime = 0d;
            IsRunning = true;
        }

        [ContextMenu("Reset Rhythm Clock")]
        public void ResetClock()
        {
            IsRunning = false;
            _hasStarted = false;
            _clockOriginDspTime = 0d;
            _nextBeatDspTime = 0d;
            _pausedAtDspTime = 0d;
            _nextBeatIndex = 0;
            LastBeatIndex = -1;
            LastBeatDspTime = 0d;
        }

        public void SetTempo(float beatsPerMinute)
        {
            _beatsPerMinute = Mathf.Clamp(beatsPerMinute, 30f, 240f);
            if (IsRunning)
            {
                StartClock();
            }
        }

        /// <summary>
        /// Re-anchors beat zero to a chosen DSP time at a new tempo, and opens
        /// a new session.
        ///
        /// Note times are derived from beat indices against the origin, so
        /// changing tempo re-times every beat; restarting from a known instant
        /// is what makes that safe. Passing the instant in, rather than
        /// reading the clock at the moment of the call, is what lets the music
        /// be scheduled to the same instant — AudioSource.PlayScheduled takes
        /// a DSP time, and a beat clock anchored to "now" instead would sit a
        /// frame or two away from it.
        /// </summary>
        public void StartClockAt(
            double originDspTime,
            float beatsPerMinute)
        {
            _beatsPerMinute = Mathf.Clamp(
                beatsPerMinute,
                30f,
                240f);
            _clockOriginDspTime =
                double.IsNaN(originDspTime) ||
                double.IsInfinity(originDspTime)
                    ? AudioSettings.dspTime
                    : originDspTime;
            _nextBeatDspTime = _clockOriginDspTime;
            _pausedAtDspTime = 0d;
            _nextBeatIndex = 0;
            LastBeatIndex = -1;
            LastBeatDspTime = 0d;
            _hasStarted = true;
            unchecked
            {
                _clockSessionId++;
            }

            IsRunning = true;
        }

        public double GetDistanceToNearestBeatSeconds(double eventDspTime)
        {
            if (!_hasStarted)
            {
                return double.PositiveInfinity;
            }

            var secondsPerBeat = SecondsPerBeat;
            var beatPosition =
                (eventDspTime - _clockOriginDspTime) / secondsPerBeat;
            var nearestBeatPosition = Math.Round(beatPosition);
            var nearestBeatTime =
                _clockOriginDspTime + nearestBeatPosition * secondsPerBeat;
            return Math.Abs(eventDspTime - nearestBeatTime);
        }

        public double GetBeatDspTime(int beatIndex)
        {
            return !_hasStarted
                ? double.NaN
                : _clockOriginDspTime +
                  beatIndex * SecondsPerBeat;
        }

        public double GetBeatPosition(double dspTime)
        {
            if (!_hasStarted)
            {
                return double.NegativeInfinity;
            }

            var sampledDspTime =
                !IsRunning && _pausedAtDspTime > 0d
                    ? _pausedAtDspTime
                    : dspTime;
            return (sampledDspTime - _clockOriginDspTime) /
                   SecondsPerBeat;
        }

        public float GetNormalizedTimingAccuracy(
            double eventDspTime,
            float perfectWindowSeconds = 0.06f,
            float maximumWindowSeconds = 0.30f)
        {
            var distance = GetDistanceToNearestBeatSeconds(eventDspTime);
            if (double.IsInfinity(distance))
            {
                return 0f;
            }

            maximumWindowSeconds = Mathf.Max(
                perfectWindowSeconds + 0.001f,
                maximumWindowSeconds);
            return 1f - Mathf.InverseLerp(
                perfectWindowSeconds,
                maximumWindowSeconds,
                (float)distance);
        }

        private void EmitBeat(int beatIndex, double dspTime)
        {
            LastBeatIndex = beatIndex;
            LastBeatDspTime = dspTime;
            var beatInBar = beatIndex % _beatsPerBar;
            var barIndex = beatIndex / _beatsPerBar;
            BeatOccurred?.Invoke(
                new RhythmBeat(beatIndex, beatInBar, barIndex, dspTime));
        }

        private void OnDisable()
        {
            StopClock();
        }

        private void OnValidate()
        {
            _beatsPerMinute = Mathf.Clamp(_beatsPerMinute, 30f, 240f);
            _beatsPerBar = Mathf.Max(1, _beatsPerBar);
            _startDelaySeconds = Mathf.Max(0f, _startDelaySeconds);
        }
    }
}
