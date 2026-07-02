using Obi;

using UnityEngine;

// Cosmetic only, physics untouched. Pins the rope's two drawn endpoints onto the players' anchors
// every frame, right after Obi computes its interpolated render positions and before it uploads the
// mesh (ObiSolver.OnInterpolate). The rope still simulates and pulls the players; we only override
// where the two ends are drawn.
//
// At very fast motion (the charged launch, ~30 m/s) Obi's rope interpolation and Unity's Rigidbody
// interpolation run on independent phase, so the drawn rope end leads or lags the drawn player by
// velocity*dt and looks detached. Forcing the drawn end onto the player's drawn anchor fixes it at the
// source.
[DisallowMultipleComponent]
public class RopeVisualPin : MonoBehaviour
{
    private ObiRope rope;
    private Transform anchorA, anchorB;
    private int endA = -1, endB = -1;
    private bool resolved;

    // Wire the pin to the loaded rope and the two player anchors.
    public void Bind(ObiRope obiRope, Transform a, Transform b)
    {
        Unbind();
        rope = obiRope;
        anchorA = a;
        anchorB = b;
        resolved = false;
        if (rope != null && rope.solver != null)
            rope.solver.OnInterpolate += HandleInterpolate;
    }

    private void Unbind()
    {
        if (rope != null && rope.solver != null)
            rope.solver.OnInterpolate -= HandleInterpolate;
    }

    private void OnDestroy() { Unbind(); }

    // Every frame after interpolation, just before the mesh is uploaded.
    private void HandleInterpolate(ObiSolver solver, float timeToSimulate, float substepTime)
    {
        if (rope == null || !rope.isLoaded || anchorA == null || anchorB == null) return;

        int n = rope.elements.Count;
        if (n < 1) return;

        // Match each physical rope end to its player once (ends never swap).
        if (!resolved)
        {
            int start = rope.elements[0].particle1;
            int finish = rope.elements[n - 1].particle2;
            Vector3 startW = solver.transform.TransformPoint((Vector3)solver.renderablePositions[start]);
            bool startIsA = (startW - anchorA.position).sqrMagnitude <= (startW - anchorB.position).sqrMagnitude;
            endA = startIsA ? start : finish;
            endB = startIsA ? finish : start;
            resolved = true;
        }

        Pin(solver, endA, anchorA.position);
        Pin(solver, endB, anchorB.position);
    }

    // Overwrite a particle's render position (solver-local) with a world position.
    private void Pin(ObiSolver solver, int solverIndex, Vector3 worldPos)
    {
        if (solverIndex < 0 || solverIndex >= solver.renderablePositions.count) return;
        Vector4 cur = solver.renderablePositions[solverIndex];
        Vector3 local = solver.transform.InverseTransformPoint(worldPos);
        solver.renderablePositions[solverIndex] = new Vector4(local.x, local.y, local.z, cur.w);
    }
}
