using System;
using UnityEngine;

namespace IVRC
{
    /// <summary>
    /// Draws the crowd as a field of small lights, one per seat in the
    /// theatre, instead of one body per person.
    ///
    /// The placeholder crowd cost 114 renderers for 55 capsules that were
    /// never going to survive to the exhibition. A full house is 132 seats,
    /// and as points of light it is one draw call, because a single particle
    /// system carries all of them.
    ///
    /// Nothing here scores anything. Brightness reads
    /// <see cref="PoseStageFeedbackController.ReactionNormalized"/>, which is
    /// the same value the stage lights and the cheers already answer to, so
    /// the room cannot disagree with itself about how well it is going.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudienceLightPoints : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField]
        private PoseStageFeedbackController _stageFeedback;
        [SerializeField] private ParticleSystem _points;
        [Tooltip(
            "Seat positions in this object's local space, captured from " +
            "the theatre by IVRC / Capture Audience Seats.")]
        [SerializeField] private Vector3[] _seatPositions =
            Array.Empty<Vector3>();

        [Header("Placement")]
        [Tooltip(
            "Height of a seated person's head above the captured seat. " +
            "Kept out of the capture so it can be retuned without " +
            "capturing the seats again.")]
        [SerializeField, Range(0f, 1.5f)]
        private float _seatedHeadHeight = 0.62f;
        [Tooltip(
            "The stage the wave rolls away from. Falls back to this " +
            "object's own position when empty.")]
        [SerializeField] private Transform _stageOrigin;

        [Tooltip(
            "Sideways scatter per seat. The benches are evenly divided, " +
            "which reads as ruled lines running away from the stage. Zero " +
            "keeps the rows exact.")]
        [SerializeField, Range(0f, 0.4f)]
        private float _lateralJitterMetres = 0.05f;

        [Header("Look")]
        [SerializeField] private Color _restColor =
            new Color(1f, 0.86f, 0.62f, 1f);
        [SerializeField] private Color _peakColor =
            new Color(1f, 0.97f, 0.86f, 1f);
        [SerializeField, Range(0f, 4f)]
        private float _restBrightness = 0.35f;
        [SerializeField, Range(0f, 8f)]
        private float _peakBrightness = 2.4f;
        [SerializeField, Range(0.01f, 0.5f)]
        private float _pointSize = 0.07f;
        [SerializeField, Range(0f, 2f)]
        private float _sizeGain = 0.45f;

        [Header("Wave")]
        [SerializeField, Range(0.1f, 4f)]
        private float _waveFrequencyHz = 0.9f;
        [Tooltip(
            "Metres of seating one full cycle of the wave spans. Larger " +
            "values make the room react more as one.")]
        [SerializeField, Range(1f, 40f)]
        private float _metresPerCycle = 9f;
        [SerializeField, Range(0f, 1f)]
        private float _jitterTurns = 0.12f;

        private ParticleSystem.Particle[] _particles =
            Array.Empty<ParticleSystem.Particle>();
        private float[] _phases = Array.Empty<float>();
        private bool _seeded;

        public int PointCount => _seatPositions.Length;

        private void OnEnable()
        {
            Seed();
        }

        private void OnDisable()
        {
            _seeded = false;
        }

        private void LateUpdate()
        {
            if (!_seeded)
            {
                Seed();
            }

            if (!_seeded || _points == null)
            {
                return;
            }

            var reaction = _stageFeedback == null
                ? 0f
                : _stageFeedback.ReactionNormalized;
            var time = Time.time;

            for (var index = 0;
                 index < _particles.Length;
                 index++)
            {
                var excitement =
                    AudienceLightPointsMath.CalculateExcitement(
                        reaction,
                        time,
                        _phases[index],
                        _waveFrequencyHz);

                var colour = Color.Lerp(
                    _restColor,
                    _peakColor,
                    excitement);
                var brightness = Mathf.Lerp(
                    _restBrightness,
                    _peakBrightness,
                    excitement);
                _particles[index].startColor =
                    colour * brightness;
                _particles[index].startSize =
                    _pointSize *
                    (1f + _sizeGain * excitement);
            }

            _points.SetParticles(
                _particles,
                _particles.Length);
        }

        /// <summary>
        /// Rebuilds the field from the captured seats. Safe to call again;
        /// the capture tool uses it to show the result immediately.
        /// </summary>
        public void Seed()
        {
            _seeded = false;
            if (_points == null || _seatPositions == null)
            {
                return;
            }

            var count = _seatPositions.Length;
            if (count <= 0)
            {
                _points.Clear();
                return;
            }

            ConfigureParticleSystem(count);

            if (_particles.Length != count)
            {
                _particles =
                    new ParticleSystem.Particle[count];
                _phases = new float[count];
            }

            var origin = _stageOrigin == null
                ? Vector3.zero
                : transform.InverseTransformPoint(
                    _stageOrigin.position);

            for (var index = 0; index < count; index++)
            {
                var seat = _seatPositions[index];
                var position =
                    AudienceLightPointsMath.ApplyLateralJitter(
                        AudienceLightPointsMath
                            .CalculatePointPosition(
                                seat,
                                _seatedHeadHeight),
                        _lateralJitterMetres);

                _particles[index] = default;
                _particles[index].position = position;
                _particles[index].startSize = _pointSize;
                _particles[index].startColor =
                    _restColor * _restBrightness;

                // Long enough that the field never expires. Ageing is off
                // as well, so this only has to outlast one visitor.
                _particles[index].startLifetime = 1e7f;
                _particles[index].remainingLifetime = 1e7f;

                _phases[index] =
                    AudienceLightPointsMath.CalculatePhase(
                        Vector3.Distance(position, origin),
                        _metresPerCycle,
                        AudienceLightPointsMath
                            .CalculateJitterTurns(
                                seat,
                                _jitterTurns));
            }

            _points.SetParticles(_particles, count);
            _seeded = true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Shows where the crowd sits while the Editor is not playing.
        ///
        /// The field itself only exists in Play mode, because seeding it
        /// from [ExecuteAlways] would write to the scene on every domain
        /// reload. Gizmos draw without touching anything, so the placement
        /// can still be checked by selecting this object.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (_seatPositions == null)
            {
                return;
            }

            var origin = _stageOrigin == null
                ? transform.position
                : _stageOrigin.position;

            // Same placement the field itself uses, scatter included, so
            // the sliders can be judged without entering Play.
            Gizmos.color = new Color(1f, 0.85f, 0.5f, 0.9f);
            foreach (var seat in _seatPositions)
            {
                var world = transform.TransformPoint(
                    AudienceLightPointsMath.ApplyLateralJitter(
                        AudienceLightPointsMath
                            .CalculatePointPosition(
                                seat,
                                _seatedHeadHeight),
                        _lateralJitterMetres));
                Gizmos.DrawSphere(world, 0.05f);
            }

            // The wave travels outward from the performer, so its crests
            // are rings around them. Drawing a few makes it obvious which
            // way the room reacts.
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.35f);
            for (var ring = 1; ring <= 4; ring++)
            {
                DrawGroundRing(
                    origin,
                    ring * Mathf.Max(_metresPerCycle, 0.1f));
            }
        }

        private static void DrawGroundRing(
            Vector3 centre,
            float radius)
        {
            const int segments = 64;
            var previous = centre +
                           new Vector3(radius, 0f, 0f);
            for (var step = 1; step <= segments; step++)
            {
                var angle = step / (float)segments *
                            Mathf.PI * 2f;
                var next = centre + new Vector3(
                    Mathf.Cos(angle) * radius,
                    0f,
                    Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
#endif

        private void ConfigureParticleSystem(int count)
        {
            var main = _points.main;
            main.maxParticles = Mathf.Max(count, 1);
            main.simulationSpace =
                ParticleSystemSimulationSpace.Local;
            main.playOnAwake = false;
            // The field is placed, not simulated. Leaving the clock
            // running would drift the points off their seats.
            main.simulationSpeed = 0f;

            var emission = _points.emission;
            emission.enabled = false;

            var shape = _points.shape;
            shape.enabled = false;

            if (!_points.isPlaying)
            {
                _points.Play();
            }
        }
    }
}
