using UnityEngine;

namespace RubberBand
{
    /// <summary>
    /// XPBD distance constraint batch.
    /// Math is taken directly from Obi's BurstDistanceConstraintsBatch.DistanceConstraintsBatchJob.Execute().
    /// 
    /// Each constraint connects two particles with a rest length.
    /// Compliance > 0 makes it elastic (rubber band). Compliance = 0 makes it inextensible (rope).
    /// maxCompression limits how much shorter than rest length the constraint can be (0 = no limit).
    /// </summary>
    public class DistanceConstraintBatch : IConstraintBatch
    {
        // Flat pair array: [p0,p1, p0,p1, ...] — mirrors Obi's particleIndices layout
        public int[] particleIndices;
        public float[] restLengths;
        public float[] lambdas;

        // Per-constraint stiffness (mirrors Obi's stiffnesses float2: x=compliance, y=maxCompression)
        public float[] compliance;
        public float[] maxCompression;

        public int constraintCount { get; private set; }

        private const float EPSILON = 1e-6f; // mirrors BurstMath.epsilon

        public DistanceConstraintBatch(int capacity)
        {
            particleIndices = new int[capacity * 2];
            restLengths = new float[capacity];
            lambdas = new float[capacity];
            compliance = new float[capacity];
            maxCompression = new float[capacity];
            constraintCount = 0;
        }

        /// <summary>
        /// Adds a distance constraint between two particles.
        /// </summary>
        public void AddConstraint(int particleA, int particleB, float restLength, float complianceValue, float maxCompressionValue)
        {
            int i = constraintCount;

            // Grow if needed
            if (i >= restLengths.Length)
            {
                int newCap = Mathf.Max(i + 1, restLengths.Length * 2);
                System.Array.Resize(ref particleIndices, newCap * 2);
                System.Array.Resize(ref restLengths, newCap);
                System.Array.Resize(ref lambdas, newCap);
                System.Array.Resize(ref compliance, newCap);
                System.Array.Resize(ref maxCompression, newCap);
            }

            particleIndices[i * 2] = particleA;
            particleIndices[i * 2 + 1] = particleB;
            restLengths[i] = restLength;
            lambdas[i] = 0f;
            compliance[i] = complianceValue;
            maxCompression[i] = maxCompressionValue;
            constraintCount++;
        }

        /// <summary>
        /// Reset lambdas at the start of each physics step (NOT per substep).
        /// Lambdas accumulate across substeps within a step — this is core to XPBD convergence.
        /// </summary>
        public void ResetLambdas()
        {
            for (int i = 0; i < constraintCount; i++)
                lambdas[i] = 0f;
        }

        /// <summary>
        /// Projects distance constraints, accumulating deltas.
        /// This is a direct port of Obi's DistanceConstraintsBatchJob.Execute().
        /// </summary>
        public void Evaluate(ParticleData particles, float substepTime)
        {
            float deltaTimeSqr = substepTime * substepTime;

            for (int i = 0; i < constraintCount; i++)
            {
                int p1 = particleIndices[i * 2];
                int p2 = particleIndices[i * 2 + 1];

                float w1 = particles.invMasses[p1];
                float w2 = particles.invMasses[p2];

                // --- Obi's DistanceConstraintsBatchJob.Execute() begins here ---

                // calculate time adjusted compliance
                float comp = compliance[i] / deltaTimeSqr;

                // calculate position and lambda deltas:
                Vector3 distance = particles.positions[p1] - particles.positions[p2];
                float d = distance.magnitude;

                // calculate constraint value:
                float constraint = d - restLengths[i];
                // maxCompression clamp (Obi: constraint -= max(min(constraint, 0), -stiffnesses[i].y))
                constraint -= Mathf.Max(Mathf.Min(constraint, 0f), -maxCompression[i]);

                // calculate lambda and position deltas:
                float dlambda = (-constraint - comp * lambdas[i]) / (w1 + w2 + comp + EPSILON);
                Vector3 delta = dlambda * distance / (d + EPSILON);

                lambdas[i] += dlambda;

                particles.positionDeltas[p1] += delta * w1;
                particles.positionDeltas[p2] -= delta * w2;

                particles.constraintCounts[p1]++;
                particles.constraintCounts[p2]++;

                // --- Obi's DistanceConstraintsBatchJob.Execute() ends here ---
            }
        }

        /// <summary>
        /// Applies accumulated deltas to positions.
        /// Direct port of Obi's ApplyDistanceConstraintsBatchJob.Execute().
        /// </summary>
        public void Apply(ParticleData particles, float sorFactor)
        {
            for (int i = 0; i < constraintCount; i++)
            {
                int p1 = particleIndices[i * 2];
                int p2 = particleIndices[i * 2 + 1];

                // --- Obi's ApplyDistanceConstraintsBatchJob.Execute() ---

                if (particles.constraintCounts[p1] > 0)
                {
                    particles.positions[p1] += particles.positionDeltas[p1] * sorFactor / particles.constraintCounts[p1];
                    particles.positionDeltas[p1] = Vector3.zero;
                    particles.constraintCounts[p1] = 0;
                }

                if (particles.constraintCounts[p2] > 0)
                {
                    particles.positions[p2] += particles.positionDeltas[p2] * sorFactor / particles.constraintCounts[p2];
                    particles.positionDeltas[p2] = Vector3.zero;
                    particles.constraintCounts[p2] = 0;
                }
            }
        }
    }
}
