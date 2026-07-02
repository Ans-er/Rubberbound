using UnityEngine;

namespace RubberBand
{
    /// <summary>
    /// Shared solver configuration. Both clients must reference the same asset
    /// for deterministic simulation in multiplayer.
    /// Mirrors Obi's Oni.SolverParameters.
    /// </summary>
    [CreateAssetMenu(fileName = "RubberBandSolverSettings", menuName = "RubberBand/Solver Settings")]
    public class RubberBandSolverSettings : ScriptableObject
    {
        [Header("Simulation")]
        [Tooltip("Number of substeps per FixedUpdate. More = more stable. Obi defaults to 4.")]
        [Min(1)] public int substeps = 4;

        [Tooltip("Constraint iterations per substep.")]
        [Min(1)] public int iterations = 4;

        [Header("Forces")]
        public Vector3 gravity = new Vector3(0f, -9.81f, 0f);

        [Header("Damping & Limits")]
        [Tooltip("Velocity damping per second. Obi: velocityScale = pow(1 - damping, substepTime)")]
        [Range(0f, 1f)] public float damping = 0.05f;

        [Tooltip("Maximum particle velocity magnitude.")]
        public float maxVelocity = 50f;

        [Tooltip("Kinetic energy threshold below which particles sleep. Matches Obi's sleepThreshold.")]
        public float sleepThreshold = 0.001f;

        [Header("Solver")]
        [Tooltip("Successive Over-Relaxation factor. 1.0 = standard, >1 = over-relax for faster convergence.")]
        [Range(0.1f, 2f)] public float sorFactor = 1f;
    }
}
