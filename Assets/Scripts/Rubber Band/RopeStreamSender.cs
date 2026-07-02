using System.Collections.Generic;

using Obi;

using Unity.Collections;
using Unity.Netcode;

using UnityEngine;

// Host only. Streams the authoritative rope's drawn shape to every client so their puppets render the
// identical rope. Added to the rope by RubberBandSpawner after wire-up. Reads renderablePositions in
// ObiSolver.OnInterpolate (bound after RopeVisualPin, so the ends are the pinned on-player ones).
// Rate-limited and sent UnreliableSequenced: old snapshots are dropped rather than resent, which is
// what a position stream wants.
[DisallowMultipleComponent]
public class RopeStreamSender : MonoBehaviour
{
    [Tooltip("Snapshots per second. ~20 keeps bandwidth in single-digit KB/s while staying smooth " +
             "(clients interpolate between snapshots).")]
    public float sendRate = 20f;

    private ObiRope rope;
    private float nextSendTime;
    private readonly List<ulong> targetClients = new List<ulong>();

    // Bind to the loaded rope. Call after RopeVisualPin so we read the pinned ends.
    public void Bind(ObiRope obiRope)
    {
        Unbind();
        rope = obiRope;
        if (rope != null && rope.solver != null)
            rope.solver.OnInterpolate += HandleInterpolate;
    }

    private void Unbind()
    {
        if (rope != null && rope.solver != null)
            rope.solver.OnInterpolate -= HandleInterpolate;
    }

    private void OnDestroy() => Unbind();

    // Every render frame after interpolation (and after the pin), just before the mesh is uploaded, so
    // renderablePositions are the final drawn shape.
    private void HandleInterpolate(ObiSolver solver, float timeToSimulate, float substepTime)
    {
        if (rope == null || !rope.isLoaded) return;

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        if (Time.unscaledTime < nextSendTime) return;
        nextSendTime = Time.unscaledTime + (sendRate > 0f ? 1f / sendRate : 0.05f);

        // Send to every client except the host itself: the host owns the real rope, has no puppet, and
        // sending to a peer with no registered handler just spams warnings.
        targetClients.Clear();
        foreach (ulong id in nm.ConnectedClientsIds)
            if (id != nm.LocalClientId) targetClients.Add(id);
        if (targetClients.Count == 0) return;

        int count = rope.activeParticleCount;
        if (count <= 0) return;

        var writer = new FastBufferWriter(RopeStreamProtocol.ByteSize(count), Allocator.Temp);
        try
        {
            writer.WriteValueSafe((ushort)count);
            Transform st = solver.transform;
            for (int i = 0; i < count; i++)
            {
                int idx = rope.solverIndices[i];
                Vector3 world = st.TransformPoint((Vector3)solver.renderablePositions[idx]);
                RopeStreamProtocol.WritePos(writer, world);
            }

            nm.CustomMessagingManager.SendNamedMessage(
                RopeStreamProtocol.MessageName, targetClients, writer, NetworkDelivery.UnreliableSequenced);
        }
        finally
        {
            writer.Dispose();
        }
    }
}
