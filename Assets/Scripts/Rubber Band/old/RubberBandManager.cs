using CMF;
using UnityEngine;

namespace RubberBand
{
    /// <summary>
    /// Gameplay bridge between player transforms and the rubber band physics.
    /// Owns pin logic: writes player positions into pinned particles each substep.
    /// 
    /// Auto-finds players via FindObjectsOfType, matching the old prototype pattern.
    /// 
    /// Multiplayer note:
    /// - In singleplayer: reads directly from player transforms.
    /// - In multiplayer: swap _endpointA/_endpointB for network-interpolated transforms.
    ///   The RubberBand and Solver never need to know the difference.
    /// </summary>
    public class RubberBandManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RubberBand _band;
        [SerializeField] private RubberBandSolver _solver;

        [Header("Player Detection")]
        [Tooltip("If left empty, players are auto-detected via FindObjectsOfType.")]
        [SerializeField] private Transform _endpointA;
        [SerializeField] private Transform _endpointB;

        [Header("Debug")]
        [SerializeField] private bool _showDebugInfo = false;

        private float _attachHeightA;
        private float _attachHeightB;
        private bool _initialized = false;

        public bool isInitialized => _initialized;

        private void OnEnable()
        {
            if (_solver != null)
                _solver.onPreConstraintSubstep += WritePinPositions;
        }

        private void OnDisable()
        {
            if (_solver != null)
                _solver.onPreConstraintSubstep -= WritePinPositions;
        }

        private void Update()
        {
            if (!_initialized)
            {
                TryFindPlayers();
                return;
            }

            // Handle players being destroyed mid-game
            if (_endpointA == null || _endpointB == null)
            {
                _initialized = false;
            }
        }

        /// <summary>
        /// Auto-finds players in the scene. Replace SimpleTestMover with
        /// whatever component your player prefab has (e.g. AdvancedWalkerController).
        /// Mirrors the old prototype's TryFindPlayers() pattern.
        /// </summary>
        private void TryFindPlayers()
        {
            // If manually assigned in inspector, use those directly
            if (_endpointA != null && _endpointB != null)
            {
                AttachPlayers(_endpointA, _endpointB);
                return;
            }

            // Auto-detect — replace SimpleTestMover with your actual player component:
            // var players = FindObjectsOfType<AdvancedWalkerController>();
            var players = FindObjectsOfType<AdvancedWalkerController>();

            if (players.Length >= 2)
            {
                AttachPlayers(players[0].transform, players[1].transform);
            }
        }

        /// <summary>
        /// Attaches two players to the rubber band.
        /// Derives attach height from collider bounds, matching the old prototype:
        ///   _attachHeightA = a.GetComponent&lt;Collider&gt;().bounds.size.y / 2f;
        /// </summary>
        public void AttachPlayers(Transform a, Transform b)
        {
            _endpointA = a;
            _endpointB = b;

            Collider colA = a.GetComponent<Collider>();
            Collider colB = b.GetComponent<Collider>();
            _attachHeightA = colA != null ? colA.bounds.size.y / 2f : 1f;
            _attachHeightB = colB != null ? colB.bounds.size.y / 2f : 1f;

            Vector3 startPos = GetAttachPosition(_endpointA, _attachHeightA);
            Vector3 endPos = GetAttachPosition(_endpointB, _attachHeightB);

            _band.Initialize(startPos, endPos);
            _initialized = true;
        }

        private Vector3 GetAttachPosition(Transform endpoint, float attachHeight)
        {
            return endpoint.position + Vector3.up * attachHeight;
        }

        /// <summary>
        /// Writes pinned particle positions each substep.
        /// Called via solver.onPreConstraintSubstep — after PredictPositions, before constraints.
        /// </summary>
        private void WritePinPositions()
        {
            if (!_initialized) return;
            if (_endpointA == null || _endpointB == null) return;
            if (_band.solverIndices == null) return;

            var particles = _solver.particles;
            int first = _band.solverIndices[0];
            int last = _band.solverIndices[_band.particleCount - 1];

            Vector3 posA = GetAttachPosition(_endpointA, _attachHeightA);
            Vector3 posB = GetAttachPosition(_endpointB, _attachHeightB);

            // Write endpoint positions (invMass=0 means constraints won't move them)
            particles.positions[first] = posA;
            particles.positions[last] = posB;

            // Also update prevPositions to prevent velocity explosion
            particles.prevPositions[first] = posA;
            particles.prevPositions[last] = posB;
        }

        // --- Gameplay API ---

        /// <summary>
        /// How stretched the band is. 1.0 = rest, >1 = stretched.
        /// In multiplayer, the server should compute this from authoritative player positions
        /// for gameplay effects, NOT from the client-side particle sim.
        /// </summary>
        public float GetStretchRatio()
        {
            if (!_initialized) return 1f;
            return _band.GetStretchRatio();
        }

        /// <summary>
        /// Tension direction pulling player A toward the band.
        /// </summary>
        public Vector3 GetTensionDirectionA()
        {
            if (!_initialized) return Vector3.zero;

            var particles = _solver.particles;
            int first = _band.solverIndices[0];
            int second = _band.solverIndices[1];
            return (particles.positions[second] - particles.positions[first]).normalized;
        }

        /// <summary>
        /// Tension direction pulling player B toward the band.
        /// </summary>
        public Vector3 GetTensionDirectionB()
        {
            if (!_initialized) return Vector3.zero;

            var particles = _solver.particles;
            int last = _band.solverIndices[_band.particleCount - 1];
            int secondLast = _band.solverIndices[_band.particleCount - 2];
            return (particles.positions[secondLast] - particles.positions[last]).normalized;
        }

        private void OnGUI()
        {
            if (!_showDebugInfo || !_initialized) return;

            GUILayout.BeginArea(new Rect(10, 10, 300, 100));
            GUILayout.Label($"Stretch Ratio: {GetStretchRatio():F3}");
            GUILayout.Label($"Current Length: {_band.CalculateLength():F2}");
            GUILayout.Label($"Rest Length: {_band.restLength:F2}");
            GUILayout.EndArea();
        }
    }
}
