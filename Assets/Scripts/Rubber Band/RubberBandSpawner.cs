using Obi;

using System.Collections;

using Unity.Netcode;

using UnityEngine;

// Spawns the rubber band between the two players once both exist. The host builds the real
// simulating Obi rope (Dynamic attachments, so it pulls both bodies); each client builds a
// display-only puppet that just renders the host's streamed shape. Rebuilt on respawn via ResetRope().
public class RubberBandSpawner : MonoBehaviour
{
    [Tooltip("Prefab built from the single-player rope: ObiSolver + ObiRope with the chain blueprint " +
             "and two ObiParticleAttachments (their target is set at runtime).")]
    [SerializeField] private GameObject ropePrefab;

    [Tooltip("How often (seconds) to look for both players.")]
    [SerializeField] private float checkInterval = 0.5f;

    [Tooltip("Compliance of the attachment point itself, not rope stretch. 0 = rigid grab. Keep at 0; " +
             "stretchiness should come from the rope blueprint's distance compliance.")]
    [SerializeField] private float attachmentCompliance = 0f;

    [Tooltip("Force at which the attachment breaks. Infinity = unbreakable.")]
    [SerializeField] private float breakThreshold = Mathf.Infinity;

    [Tooltip("Off (default) = pure Obi, the solver drives the rope and collisions win via high Substeps. " +
             "On = also attach RubberBandForce as a max-stretch backstop. Only needed if an extreme pull " +
             "on a razor-thin pole still tunnels after tuning substeps.")]
    [SerializeField] private bool useEndStopBackstop = false;

    [Tooltip("Let the rope pass through the players it's attached to instead of wrapping around their " +
             "capsules. It still collides with poles and level geometry. Recommended on.")]
    [SerializeField] private bool ropeIgnoresPlayers = true;

    private GameObject ropeInstance;
    private float nextCheckTime;

    private void Update()
    {
        if (ropeInstance != null) return;
        if (Time.time < nextCheckTime) return;
        nextCheckTime = Time.time + checkInterval;

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return;

        var players = FindObjectsOfType<PhysicsBasedCharacterController>();
        if (players.Length != 2) return;

        var playerA = players[0].GetComponent<NetworkObject>();
        var playerB = players[1].GetComponent<NetworkObject>();

        if (playerA == null || playerB == null)
        {
            Debug.LogWarning("[RubberBandSpawner] Players missing NetworkObject component");
            return;
        }

        // Sort by NetworkObjectId so both peers pair the players in the same order. Otherwise peer A
        // might pair (you, them) while peer B pairs (them, you).
        System.Array.Sort(players, (a, b) =>
        {
            var na = a.GetComponent<NetworkObject>();
            var nb = b.GetComponent<NetworkObject>();
            ulong ida = na != null ? na.NetworkObjectId : 0;
            ulong idb = nb != null ? nb.NetworkObjectId : 0;
            return ida.CompareTo(idb);
        });

        Transform anchorA = players[0].transform.Find("RopeAnchor") ?? players[0].transform;
        Transform anchorB = players[1].transform.Find("RopeAnchor") ?? players[1].transform;

        // Only the host runs the real solve (it owns both dynamic bodies, so it genuinely pulls both).
        // Clients render a non-simulating puppet driven by the host's streamed shape, so every screen
        // shows the same rope. The client still feels the pull because its body is moved by the host.
        if (nm.IsServer)
            SpawnRubberBand(anchorA, anchorB);
        else
            SpawnDisplayPuppet(anchorA, anchorB);
    }

    private void SpawnRubberBand(Transform endA, Transform endB)
    {
        if (ropePrefab == null)
        {
            Debug.LogError("[RubberBandSpawner] Rope prefab not assigned.");
            enabled = false;
            return;
        }

        // Spawn at the players' midpoint, not the origin. The prefab's particles are authored in
        // solver-local space, so spawning at world-zero leaves the whole body there while only the two
        // ends snap to the players. Obi resolves that huge first-frame stretch by yanking the pinned
        // ends (the players), which is the "knocked back on respawn" jolt. Starting near the players
        // keeps the initial stretch tiny.
        Vector3 midpoint = (endA.position + endB.position) * 0.5f;
        ropeInstance = Instantiate(ropePrefab, midpoint, Quaternion.identity);
        StartCoroutine(WireUpAttachments(endA, endB));
    }

    // Destroys the rope so Update recreates it. Called when players respawn.
    public void ResetRope()
    {
        if (ropeInstance != null)
        {
            Destroy(ropeInstance);
            ropeInstance = null;
        }
        StopAllCoroutines();
        nextCheckTime = Time.time;
    }

    private IEnumerator WireUpAttachments(Transform endA, Transform endB)
    {
        var rope = ropeInstance.GetComponentInChildren<ObiRope>();
        var attachments = ropeInstance.GetComponentsInChildren<ObiParticleAttachment>();

        if (rope == null || attachments.Length < 2)
        {
            Debug.LogError("[RubberBandSpawner] Rope prefab needs ObiRope and >= 2 ObiParticleAttachments.");
            Destroy(ropeInstance);
            ropeInstance = null;
            yield break;
        }

        Transform bindTargetA = endA.GetComponentInParent<Rigidbody>().transform;
        Transform bindTargetB = endB.GetComponentInParent<Rigidbody>().transform;

        // Wait for Obi to finish loading the blueprint before touching particles.
        float timeout = 5f;
        while (!rope.isLoaded && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }
        if (!rope.isLoaded)
        {
            Debug.LogError("[RubberBandSpawner] Rope never finished loading.");
            yield break;
        }

        // Lay the whole rope on the straight A->B line before binding, so it starts near zero stretch
        // and Obi has nothing to snap back (see RelaxRopeAlongLine).
        RelaxRopeAlongLine(rope, attachments[0], endA, endB);

        // Snap endpoints to the anchors, but bind to the collider-bearing bodies.
        SnapAndBind(rope, attachments[0], endA, bindTargetA);
        SnapAndBind(rope, attachments[1], endB, bindTargetB);

        // Cosmetic: pin the drawn rope ends onto the players so they never visually detach during a
        // fast launch. Physics untouched (see RopeVisualPin).
        var pin = ropeInstance.GetComponentInChildren<RopeVisualPin>();
        if (pin == null) pin = ropeInstance.AddComponent<RopeVisualPin>();
        pin.Bind(rope, endA, endB);

        // Make the rope pass through the players instead of wrapping their capsules: put both players'
        // Obi colliders in their own category and clear that bit from the rope's mask. The rope still
        // collides with everything else (poles).
        if (ropeIgnoresPlayers)
        {
            const int playerCategory = 1; // poles/level stay at the default category 0
            SetPlayerCollidersCategory(bindTargetA, playerCategory);
            SetPlayerCollidersCategory(bindTargetB, playerCategory);
            rope.SetFilterMask(ObiUtils.CollideWithEverything & ~(1 << playerCategory));
        }

        // Pure Obi by default. The RubberBandForce backstop is only armed when useEndStopBackstop is on;
        // otherwise any pre-authored instance is disabled so the solver drives the rope alone.
        var force = ropeInstance.GetComponentInChildren<RubberBandForce>();
        if (useEndStopBackstop)
        {
            if (force == null) force = ropeInstance.AddComponent<RubberBandForce>();
            force.enabled = true;
            force.Bind(endA, endB, rope.restLength, rope);
        }
        else if (force != null)
        {
            force.enabled = false;
        }

        // Stream this rope's drawn shape to clients. Bound after RopeVisualPin so the sender reads the
        // pinned (on-player) ends.
        var sender = ropeInstance.GetComponentInChildren<RopeStreamSender>();
        if (sender == null) sender = rope.gameObject.AddComponent<RopeStreamSender>();
        sender.Bind(rope);
    }

    // Client path: a display-only puppet that never solves its own physics (that would diverge from the
    // host). RopeStreamReceiver overwrites its drawn positions from the host's world-space stream, so
    // the ends land on the right players by themselves. Spawned at the midpoint (positions get
    // overwritten immediately anyway).
    private void SpawnDisplayPuppet(Transform endA, Transform endB)
    {
        if (ropePrefab == null)
        {
            Debug.LogError("[RubberBandSpawner] Rope prefab not assigned.");
            enabled = false;
            return;
        }

        Vector3 midpoint = (endA.position + endB.position) * 0.5f;
        ropeInstance = Instantiate(ropePrefab, midpoint, Quaternion.identity);
        StartCoroutine(WireUpPuppet());
    }

    private IEnumerator WireUpPuppet()
    {
        var rope = ropeInstance.GetComponentInChildren<ObiRope>();
        if (rope == null)
        {
            Debug.LogError("[RubberBandSpawner] Puppet rope prefab missing ObiRope.");
            Destroy(ropeInstance);
            ropeInstance = null;
            yield break;
        }

        float timeout = 5f;
        while (!rope.isLoaded && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }
        if (!rope.isLoaded)
        {
            Debug.LogError("[RubberBandSpawner] Puppet rope never finished loading.");
            yield break;
        }

        var solver = rope.solver;

        // Pin every particle (infinite mass) so the puppet's solver leaves them where they are and can
        // never diverge from the host. The receiver only overwrites drawn positions, so the frozen sim
        // underneath is just a guard against drift/NaN.
        for (int i = 0; i < rope.activeParticleCount; i++)
        {
            int idx = rope.solverIndices[i];
            if (idx >= 0 && idx < solver.invMasses.count) solver.invMasses[idx] = 0f;
        }

        // No attachments on a client; disable the authored ones so they stay inert.
        foreach (var att in ropeInstance.GetComponentsInChildren<ObiParticleAttachment>())
            att.enabled = false;

        // Render the host's streamed shape. It carries world-space positions of the host's pinned ends,
        // so the puppet's ends land on the correct players on their own (no client-side RopeVisualPin,
        // which would risk resolving ends before the first snapshot arrives).
        var receiver = rope.gameObject.GetComponent<RopeStreamReceiver>();
        if (receiver == null) receiver = rope.gameObject.AddComponent<RopeStreamReceiver>();
        receiver.Bind(rope);
    }

    // Lays every particle on the straight segment between the anchors so the rope starts near zero
    // stretch. Instantiate() places the body at the prefab origin; if only the two ends snap to the
    // players, the first solver step contracts the origin-to-player stretch and drags both pinned ends
    // (the players) toward the origin. That is the one-direction knockback seen on a respawn far from
    // the origin. Spreading all particles along the line removes the stretch, so no freeze hack is needed.
    private void RelaxRopeAlongLine(ObiRope rope, ObiParticleAttachment aEndAttachment, Transform endA, Transform endB)
    {
        if (rope == null || !rope.isLoaded) return;

        int count = rope.activeParticleCount;
        if (count < 2) return;

        // Particles run 0..count-1 along the rope, but we don't know which physical end is index 0. Ask
        // the A-end attachment's group: if it holds the last index, the A end sits at count-1 and we
        // walk the line backwards so endA still lands on the A end.
        bool aEndAtZero = true;
        if (aEndAttachment != null && aEndAttachment.particleGroup != null)
            aEndAtZero = !aEndAttachment.particleGroup.ContainsParticle(count - 1);

        Transform solverT = rope.solver.transform;
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / (count - 1);   // 0 at index 0 .. 1 at index count-1
            float s = aEndAtZero ? t : 1f - t;  // 0 at the A end .. 1 at the B end
            Vector3 world = Vector3.Lerp(endA.position, endB.position, s);
            rope.TeleportParticle(i, solverT.InverseTransformPoint(world));
        }
    }

    // Puts every Obi collider under a player into a collision category (keeping its mask) so the rope,
    // whose mask clears that category, passes through it while the player still collides with ground
    // and poles.
    private void SetPlayerCollidersCategory(Transform playerRoot, int category)
    {
        if (playerRoot == null) return;
        var cols = playerRoot.GetComponentsInChildren<ObiColliderBase>(true);
        if (cols.Length == 0)
            Debug.LogWarning($"[RubberBandSpawner] No ObiCollider under '{playerRoot.name}'; " +
                             "rope-through-player won't take effect for it.");
        foreach (var col in cols)
            col.Filter = ObiUtils.MakeFilter(ObiUtils.GetMaskFromFilter(col.Filter), category);
    }

    private void SnapAndBind(ObiRope rope, ObiParticleAttachment attachment, Transform snapTo, Transform bindTo)
    {
        if (attachment.particleGroup == null)
        {
            Debug.LogError("[RubberBandSpawner] Attachment is missing its particleGroup. " +
                           "Author it on the rope prefab in the editor.");
            return;
        }

        if (snapTo == null)
        {
            Debug.LogError("[RubberBandSpawner] snapTo is null.");
            return;
        }

        if (bindTo == null)
        {
            Debug.LogError("[RubberBandSpawner] bindTo is null.");
            return;
        }

        // Snap the end particles to the anchor in solver-local space.
        Vector3 targetInSolverSpace = rope.solver.transform.InverseTransformPoint(snapTo.position);
        foreach (int particleIndex in attachment.particleGroup.particleIndices)
        {
            rope.TeleportParticle(particleIndex, targetInSolverSpace);
        }

        attachment.attachmentType = ObiParticleAttachment.AttachmentType.Dynamic;
        attachment.compliance = attachmentCompliance;
        attachment.breakThreshold = breakThreshold;

        // Dynamic attachments only transmit force if the target has an ObiCollider.
        var hasObiCollider = bindTo.GetComponent<ObiColliderBase>() != null;
        if (!hasObiCollider)
        {
            Debug.LogWarning($"[RubberBandSpawner] Bind target '{bindTo.name}' has no ObiColliderBase. " +
                             "Dynamic ObiParticleAttachment will not transmit forces. " +
                             "Add an ObiCollider to the bind target (same GameObject as a Unity Collider).");
        }

        attachment.target = null;
        attachment.target = bindTo;
    }

}
