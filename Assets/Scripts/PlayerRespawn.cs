using UnityEngine;
using Unity.Netcode;

// Attach this to the player prefab. It detects falling below a threshold and notifies the server to respawn.
[RequireComponent(typeof(NetworkObject))]
public class PlayerRespawn : NetworkBehaviour
{
    [Tooltip("Y position below which the player is considered to have fallen off the map.")]
    public float fallY = -20f;
    
    [Tooltip("Grace period in seconds after respawn during which the player is frozen.")]
    public float respawnFreezeDuration = 0.1f;

    private bool hasRespawned = false;
    private float freezeTimer = 0f;
    private bool isFrozen = false;
    private Rigidbody rb;
    private RigidbodyConstraints originalConstraints;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    // Called by the falling owner to ask the server to respawn. One player falling resets
    // BOTH, since they're tied together by the rope.
    [ServerRpc(RequireOwnership = false)]
    public void RequestRespawnServerRpc(ServerRpcParams rpcParams = default)
    {
        if (RBGameManager.Instance != null)
            RBGameManager.Instance.RespawnBothPlayers();
    }

    private void Update()
    {
        if (!IsOwner) return; // only owner checks their own falling

        // Handle freeze timer
        if (isFrozen)
        {
            freezeTimer -= Time.deltaTime;
            if (freezeTimer <= 0)
            {
                isFrozen = false;
                UnfreezePlayer();
            }
            return; // Skip fall detection during freeze
        }

        if (transform.position.y < fallY && !hasRespawned)
        {
            hasRespawned = true;
            // tell server to respawn both players
            RequestRespawnServerRpc();
        }
    }

    private void FreezePlayer()
    {
        if (rb == null) return;

        isFrozen = true;
        freezeTimer = respawnFreezeDuration;

        // Remember the constraints so we can restore them, then lock the body for the grace period.
        originalConstraints = rb.constraints;
        rb.constraints = RigidbodyConstraints.FreezeAll;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    private void UnfreezePlayer()
    {
        if (rb == null) return;
        rb.constraints = originalConstraints;
    }

    // Owner reports a checkpoint; the server stores it against the sending client.
    [ServerRpc(RequireOwnership = false)]
    public void SetCheckpointServerRpc(Vector3 position, int checkpointNumber, ServerRpcParams rpcParams = default)
    {
        if (RBGameManager.Instance != null)
            RBGameManager.Instance.SetCheckpoint(rpcParams.Receive.SenderClientId, position, checkpointNumber);
    }

    // Win-screen "replay" button: restart the run from the level's initial spawn points.
    [ServerRpc(RequireOwnership = false)]
    public void RequestReplayFromSpawnServerRpc(ServerRpcParams rpcParams = default)
    {
        if (RBGameManager.Instance != null)
            RBGameManager.Instance.RespawnBothPlayersAtStart();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Only the owner manages its own fall detection / spawn state.
        if (!IsOwner) return;

        hasRespawned = false;

        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            // Clear only vertical velocity so we don't fight the jump logic, and keep mass sane.
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            rb.angularVelocity = Vector3.zero;
            rb.mass = Mathf.Max(rb.mass, 0.1f);
        }

        // Briefly freeze on (re)spawn so the body settles before the player can move.
        FreezePlayer();
    }
}
