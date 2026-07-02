namespace RubberBand
{
    /// <summary>
    /// Interface for constraint batches.
    /// Mirrors Obi's IConstraintsBatchImpl with Evaluate() + Apply() split.
    /// Evaluate accumulates deltas; Apply writes them to positions.
    /// </summary>
    public interface IConstraintBatch
    {
        void Evaluate(ParticleData particles, float substepTime);
        void Apply(ParticleData particles, float sorFactor);
        void ResetLambdas();
    }
}
