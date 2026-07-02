using System.Collections.Generic;
using UnityEngine;

namespace RubberBand
{
    /// <summary>
    /// Particle solver. Owns ParticleData and constraint batches, runs the substep loop.
    /// 
    /// Mirrors Obi's BurstSolverImpl.Substep() order:
    ///   1. PredictPositions (store prev, apply gravity, integrate velocity into position)
    ///   2. Project constraints (iterate: Evaluate + Apply for each batch)
    ///   3. UpdateVelocities (derive velocity from position change)
    ///   4. UpdatePositions (damping, velocity clamping, sleep)
    /// 
    /// Lambda reset happens once per FixedUpdate, NOT per substep.
    /// </summary>
    public class RubberBandSolver : MonoBehaviour
    {
        [SerializeField] private RubberBandSolverSettings _settings;

        public RubberBandSolverSettings settings => _settings;
        public ParticleData particles { get; private set; }

        private List<IConstraintBatch> _constraintBatches = new List<IConstraintBatch>();

        // Event for pin updates — Manager subscribes to write pin positions each substep.
        // Called after PredictPositions, before constraints. Mirrors how Obi's
        // ObiActor pins override positions at the start of ApplyConstraints.
        public System.Action onPreConstraintSubstep;

        private void Awake()
        {
            particles = new ParticleData(64);
        }

        public void RegisterBatch(IConstraintBatch batch)
        {
            if (!_constraintBatches.Contains(batch))
                _constraintBatches.Add(batch);
        }

        public void UnregisterBatch(IConstraintBatch batch)
        {
            _constraintBatches.Remove(batch);
        }

        private void FixedUpdate()
        {
            if (particles.count == 0 || _settings == null) return;

            float stepTime = Time.fixedDeltaTime;
            float substepTime = stepTime / _settings.substeps;

            // Reset lambdas once per step (NOT per substep) — core XPBD requirement
            for (int b = 0; b < _constraintBatches.Count; b++)
                _constraintBatches[b].ResetLambdas();

            for (int s = 0; s < _settings.substeps; s++)
            {
                PredictPositions(substepTime);

                // Let manager write pin positions
                onPreConstraintSubstep?.Invoke();

                // Constraint projection iterations
                // Mirrors Obi's ApplyConstraints loop in BurstSolverImpl.Substep()
                for (int iter = 0; iter < _settings.iterations; iter++)
                {
                    for (int b = 0; b < _constraintBatches.Count; b++)
                    {
                        _constraintBatches[b].Evaluate(particles, substepTime);
                        _constraintBatches[b].Apply(particles, _settings.sorFactor);
                    }
                }

                UpdateVelocities(substepTime);
                UpdatePositions(substepTime);
            }
        }

        /// <summary>
        /// Mirrors Obi's PredictPositionsJob.Execute():
        ///   previousPositions[i] = positions[i]
        ///   velocity += (invMass * externalForces + gravity) * dt
        ///   position = IntegrateLinear(position, velocity, dt)
        /// 
        /// We skip external forces for v0.1 — just gravity.
        /// </summary>
        private void PredictPositions(float dt)
        {
            Vector3 gravity = _settings.gravity;

            for (int i = 0; i < particles.count; i++)
            {
                particles.prevPositions[i] = particles.positions[i];

                if (particles.invMasses[i] > 0f)
                {
                    // Apply gravity to velocity (Obi: vel += (invMass * externalForces + gravity) * dt)
                    particles.velocities[i] += gravity * dt;

                    // Integrate velocity into position (Obi: BurstIntegration.IntegrateLinear)
                    particles.positions[i] += particles.velocities[i] * dt;
                }
            }
        }

        /// <summary>
        /// Mirrors Obi's UpdateVelocitiesJob.Execute():
        ///   velocity = (position - previousPosition) / dt
        /// 
        /// This is how XPBD derives velocity — from the position change that constraints produced.
        /// </summary>
        private void UpdateVelocities(float dt)
        {
            float invDt = 1f / dt;

            for (int i = 0; i < particles.count; i++)
            {
                if (particles.invMasses[i] > 0f)
                {
                    // Obi: BurstIntegration.DifferentiateLinear(positions[i], previousPositions[i], deltaTime)
                    particles.velocities[i] = (particles.positions[i] - particles.prevPositions[i]) * invDt;
                }
                else
                {
                    particles.velocities[i] = Vector3.zero;
                }
            }
        }

        /// <summary>
        /// Mirrors Obi's UpdatePositionsJob.Execute():
        ///   velocity *= pow(1 - damping, substepTime)   (damping)
        ///   clamp velocity to maxVelocity
        ///   sleep if kinetic energy below threshold
        /// </summary>
        private void UpdatePositions(float dt)
        {
            // Obi: velocityScale = pow(1 - saturate(damping), substepTime)
            float velocityScale = Mathf.Pow(1f - Mathf.Clamp01(_settings.damping), dt);
            float sleepThreshold = _settings.sleepThreshold;
            float maxVel = _settings.maxVelocity;

            for (int i = 0; i < particles.count; i++)
            {
                Vector3 vel = particles.velocities[i];

                // --- Obi's UpdatePositionsJob.Execute() ---

                // Damp velocity
                vel *= velocityScale;

                // Clamp velocity
                float velMag = vel.magnitude;
                if (velMag > 1e-6f)
                    vel *= Mathf.Min(maxVel, velMag) / velMag;

                // Sleep: if kinetic energy below threshold, revert to previous position
                if (velMag * velMag * 0.5f <= sleepThreshold)
                {
                    particles.positions[i] = particles.prevPositions[i];
                    vel = Vector3.zero;
                }

                particles.velocities[i] = vel;
            }
        }
    }
}
