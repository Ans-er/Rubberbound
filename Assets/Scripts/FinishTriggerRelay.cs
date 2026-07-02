using UnityEngine;

// Auto-attached at runtime by RBGameManager onto whatever collider is assigned to its finishTrigger.
// Unity's OnTriggerEnter only fires on the GameObject that owns the collider, so this lives there and
// forwards a player crossing the finish line back to the manager.
[DisallowMultipleComponent]
public class FinishTriggerRelay : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<PlayerRespawn>() == null) return;

        if (RBGameManager.Instance != null)
            RBGameManager.Instance.ReachFinish(); // server-guarded + idempotent
    }
}
