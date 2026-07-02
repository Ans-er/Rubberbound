using Obi;

using Unity.Netcode;

using UnityEngine;

// Client only. Renders the host's streamed rope shape on a non-simulating puppet. Added to the puppet
// by RubberBandSpawner. Keeps the two newest world-space snapshots and, in ObiSolver.OnInterpolate,
// overwrites every particle's renderablePositions with the time-interpolated shape (the same mechanism
// RopeVisualPin uses for the ends, extended to the whole rope). The puppet's particles are pinned by
// the spawner, so its solver never moves them; this only touches the drawn positions.
[DisallowMultipleComponent]
public class RopeStreamReceiver : MonoBehaviour
{
    [Tooltip("Render this many seconds behind the newest snapshot so consecutive snapshots can be " +
             "blended smoothly. Roughly one send interval (~1/sendRate).")]
    public float interpolationDelay = 0.06f;

    private ObiRope rope;

    // The two newest snapshots (world positions in actor-particle order) and when they arrived.
    private Vector3[] prev, latest;
    private float prevTime, latestTime;
    private int particleCount;
    private bool hasPrev, hasLatest;
    private bool registered;

    // Bind to the loaded puppet and start listening for the host's stream.
    public void Bind(ObiRope obiRope)
    {
        Unbind();
        rope = obiRope;
        if (rope != null && rope.solver != null)
            rope.solver.OnInterpolate += HandleInterpolate;
        Register();
    }

    private void Register()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || registered) return;
        nm.CustomMessagingManager.RegisterNamedMessageHandler(RopeStreamProtocol.MessageName, OnMessage);
        registered = true;
    }

    private void Unregister()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && registered)
            nm.CustomMessagingManager.UnregisterNamedMessageHandler(RopeStreamProtocol.MessageName);
        registered = false;
    }

    private void Unbind()
    {
        if (rope != null && rope.solver != null)
            rope.solver.OnInterpolate -= HandleInterpolate;
    }

    private void OnDestroy()
    {
        Unbind();
        Unregister();
    }

    // New snapshot arrived: shift latest into prev, read the new one as latest.
    private void OnMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ushort count);
        if (count == 0) return;

        if (latest == null || latest.Length != count)
        {
            prev = new Vector3[count];
            latest = new Vector3[count];
            particleCount = count;
            hasPrev = false;
            hasLatest = false;
        }

        if (hasLatest)
        {
            System.Array.Copy(latest, prev, count);
            prevTime = latestTime;
            hasPrev = true;
        }

        for (int i = 0; i < count; i++)
            latest[i] = RopeStreamProtocol.ReadPos(reader);

        latestTime = Time.unscaledTime;
        hasLatest = true;
    }

    // Every render frame: write the blended snapshot into the puppet's drawn positions.
    private void HandleInterpolate(ObiSolver solver, float timeToSimulate, float substepTime)
    {
        if (rope == null || !rope.isLoaded || !hasLatest) return;
        if (rope.activeParticleCount != particleCount) return; // blueprint mismatch guard

        // Render slightly in the past and blend prev -> latest, reaching latest just as the next
        // snapshot is due. If one was dropped we hold at latest (t clamps to 1).
        float t = 1f;
        if (hasPrev && latestTime > prevTime)
            t = Mathf.Clamp01((Time.unscaledTime - interpolationDelay - prevTime) / (latestTime - prevTime));

        Transform st = solver.transform;
        var rp = solver.renderablePositions;
        for (int i = 0; i < particleCount; i++)
        {
            int idx = rope.solverIndices[i];
            if (idx < 0 || idx >= rp.count) continue;

            Vector3 world = hasPrev ? Vector3.Lerp(prev[i], latest[i], t) : latest[i];
            Vector3 local = st.InverseTransformPoint(world);

            // This frame's crisp drawn shape.
            Vector4 cur = rp[idx];
            rp[idx] = new Vector4(local.x, local.y, local.z, cur.w);

            // Also move the simulated position: Obi's cull/shadow bounds come from positions, not
            // renderablePositions. Without this they stay at the spawn point and the rope vanishes when
            // the camera looks away. The particle is pinned, so this only relocates it.
            rope.TeleportParticle(i, local);
        }
    }
}
