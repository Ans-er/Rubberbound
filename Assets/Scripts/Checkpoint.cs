using UnityEngine;

// Attach to a trigger collider (Is Trigger = on) to mark a checkpoint. When the owning
// player passes through, it reports this checkpoint to the server so future respawns
// return here. Checkpoints only count if their number increases (see RBGameManager.SetCheckpoint).
public class Checkpoint : MonoBehaviour
{
    [SerializeField] private int checkpointNumber = 1;

    private void OnTriggerEnter(Collider other)
    {
        var respawn = other.GetComponent<PlayerRespawn>() ?? other.GetComponentInParent<PlayerRespawn>();
        if (respawn == null) return;

        // Only the owner reports its own checkpoint; the server stores it authoritatively.
        if (respawn.IsOwner)
            respawn.SetCheckpointServerRpc(transform.position, checkpointNumber);
    }
}
