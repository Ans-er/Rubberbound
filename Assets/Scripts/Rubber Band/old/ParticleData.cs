using UnityEngine;

namespace RubberBand
{
    /// <summary>
    /// Struct-of-arrays particle container.
    /// Mirrors Obi's BurstSolverImpl flat array layout:
    /// positions, prevPositions, velocities, invMasses + XPBD delta accumulators.
    /// </summary>
    public class ParticleData
    {
        public Vector3[] positions;
        public Vector3[] prevPositions;
        public Vector3[] velocities;
        public float[] invMasses;

        // XPBD delta accumulation (mirrors Obi's positionDeltas + positionConstraintCounts)
        public Vector3[] positionDeltas;
        public int[] constraintCounts;

        public int count { get; private set; }

        public ParticleData(int capacity)
        {
            count = 0;
            positions = new Vector3[capacity];
            prevPositions = new Vector3[capacity];
            velocities = new Vector3[capacity];
            invMasses = new float[capacity];
            positionDeltas = new Vector3[capacity];
            constraintCounts = new int[capacity];
        }

        /// <summary>
        /// Allocates a contiguous block of particles. Returns the start index.
        /// Mirrors how ObiActor.solverIndices maps local → solver indices.
        /// </summary>
        public int AllocateParticles(int amount)
        {
            int start = count;
            count += amount;

            // Grow arrays if needed
            if (count > positions.Length)
            {
                int newCapacity = Mathf.Max(count, positions.Length * 2);
                Grow(newCapacity);
            }

            // Zero-initialize the new block
            for (int i = start; i < count; i++)
            {
                positions[i] = Vector3.zero;
                prevPositions[i] = Vector3.zero;
                velocities[i] = Vector3.zero;
                invMasses[i] = 1f;
                positionDeltas[i] = Vector3.zero;
                constraintCounts[i] = 0;
            }

            return start;
        }

        /// <summary>
        /// Clears deltas and counts. Called before each constraint projection pass.
        /// Mirrors Obi's pattern of zeroing positionDeltas/positionConstraintCounts before each batch.
        /// </summary>
        public void ClearDeltas()
        {
            for (int i = 0; i < count; i++)
            {
                positionDeltas[i] = Vector3.zero;
                constraintCounts[i] = 0;
            }
        }

        private void Grow(int newCapacity)
        {
            System.Array.Resize(ref positions, newCapacity);
            System.Array.Resize(ref prevPositions, newCapacity);
            System.Array.Resize(ref velocities, newCapacity);
            System.Array.Resize(ref invMasses, newCapacity);
            System.Array.Resize(ref positionDeltas, newCapacity);
            System.Array.Resize(ref constraintCounts, newCapacity);
        }
    }
}
