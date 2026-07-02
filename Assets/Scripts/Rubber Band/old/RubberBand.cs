using UnityEngine;

namespace RubberBand
{
    /// <summary>
    /// The "actor" — creates particles and distance constraints, registers them with the solver.
    /// Mirrors ObiRopeBase + ObiRope: owns structural elements, compliance, rest length.
    /// 
    /// Does NOT handle pin logic (that's the Manager's job).
    /// Does NOT know about player transforms.
    /// </summary>
    public class RubberBand : MonoBehaviour
    {
        [Header("Solver")]
        [SerializeField] private RubberBandSolver _solver;

        [Header("Band Properties")]
        [SerializeField] private int _particleCount = 15;
        [SerializeField] private float _restLength = 5f;
        [SerializeField] private float _particleMass = 0.1f;

        [Header("XPBD Stiffness")]
        [Tooltip("Higher = stretchier. 0 = inextensible rope. Try 0.0001 to start.")]
        [SerializeField] private float _compliance = 0.0001f;

        [Tooltip("0-1. How much shorter than rest length a segment can be. 0 = can compress fully.")]
        [Range(0f, 1f)]
        [SerializeField] private float _maxCompression = 0f;

        public RubberBandSolver solver => _solver;
        public int particleCount => _particleCount;
        public float restLength => _restLength;
        public float compliance => _compliance;
        public float maxCompression => _maxCompression;

        // Solver indices: maps local particle index → solver particle index
        // Mirrors ObiActor.solverIndices
        public int[] solverIndices { get; private set; }

        // The constraint batch this band registered
        public DistanceConstraintBatch constraintBatch { get; private set; }

        private bool _initialized = false;
        public bool isInitialized => _initialized;

        /// <summary>
        /// Initializes particles and constraints between two world positions.
        /// Called by RubberBandManager when both endpoints are known.
        /// 
        /// Mirrors ObiRopeBlueprint.Initialize(): create particles along a line,
        /// then create distance constraints for consecutive pairs.
        /// </summary>
        public void Initialize(Vector3 startPos, Vector3 endPos)
        {
            if (_solver == null)
            {
                Debug.LogError("RubberBand: No solver assigned.", this);
                return;
            }

            // --- Allocate particles (mirrors ObiRopeBlueprint particle generation) ---
            int startIndex = _solver.particles.AllocateParticles(_particleCount);
            solverIndices = new int[_particleCount];

            float segmentRestLength = _restLength / (_particleCount - 1);
            float invMass = 1f / _particleMass;

            for (int i = 0; i < _particleCount; i++)
            {
                int si = startIndex + i;
                solverIndices[i] = si;

                float t = (float)i / (_particleCount - 1);
                Vector3 pos = Vector3.Lerp(startPos, endPos, t);

                _solver.particles.positions[si] = pos;
                _solver.particles.prevPositions[si] = pos;
                _solver.particles.velocities[si] = Vector3.zero;
                _solver.particles.invMasses[si] = invMass;
            }

            // Pin endpoints: invMass = 0 means constraints won't move them.
            // Mirrors Obi's approach for pinned/kinematic particles.
            _solver.particles.invMasses[solverIndices[0]] = 0f;
            _solver.particles.invMasses[solverIndices[_particleCount - 1]] = 0f;

            // --- Create distance constraints (mirrors ObiRopeBlueprint constraint generation) ---
            constraintBatch = new DistanceConstraintBatch(_particleCount - 1);

            for (int i = 0; i < _particleCount - 1; i++)
            {
                constraintBatch.AddConstraint(
                    solverIndices[i],
                    solverIndices[i + 1],
                    segmentRestLength,
                    _compliance,
                    _maxCompression * segmentRestLength // Obi stores maxCompression as absolute distance
                );
            }

            _solver.RegisterBatch(constraintBatch);
            _initialized = true;
        }

        /// <summary>
        /// Current band length (sum of segment distances). Mirrors ObiRopeBase.CalculateLength().
        /// </summary>
        public float CalculateLength()
        {
            if (!_initialized) return 0f;

            float length = 0f;
            for (int i = 0; i < _particleCount - 1; i++)
            {
                Vector3 a = _solver.particles.positions[solverIndices[i]];
                Vector3 b = _solver.particles.positions[solverIndices[i + 1]];
                length += Vector3.Distance(a, b);
            }
            return length;
        }

        /// <summary>
        /// How stretched the band is relative to rest length. 1.0 = at rest, >1 = stretched, <1 = compressed.
        /// </summary>
        public float GetStretchRatio()
        {
            return CalculateLength() / _restLength;
        }

        /// <summary>
        /// Gets world positions for all particles. Used by renderer.
        /// </summary>
        public void GetParticlePositions(Vector3[] output)
        {
            if (!_initialized) return;

            for (int i = 0; i < _particleCount; i++)
                output[i] = _solver.particles.positions[solverIndices[i]];
        }

        /// <summary>
        /// Updates compliance and maxCompression on existing constraints at runtime.
        /// Allows inspector tweaking during play mode.
        /// </summary>
        public void UpdateConstraintParameters()
        {
            if (constraintBatch == null) return;

            float segmentRestLength = _restLength / (_particleCount - 1);
            for (int i = 0; i < constraintBatch.constraintCount; i++)
            {
                constraintBatch.compliance[i] = _compliance;
                constraintBatch.maxCompression[i] = _maxCompression * segmentRestLength;
            }
        }

        private void OnValidate()
        {
            // Live-update constraint parameters when tweaking in inspector during play mode
            if (_initialized && constraintBatch != null)
                UpdateConstraintParameters();
        }

        private void OnDestroy()
        {
            if (_solver != null && constraintBatch != null)
                _solver.UnregisterBatch(constraintBatch);
        }
    }
}
