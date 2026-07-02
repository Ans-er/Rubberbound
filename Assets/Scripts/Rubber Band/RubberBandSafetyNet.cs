using Unity.Netcode;

using UnityEngine;

// Optional explosion guard. RubberBandForce already caps speed and resets non-finite velocities via
// its own ClampSpeed, so RubberBandSpawner no longer wires this up. It's kept as a second, independent
// velocity cap: add it to the rope GameObject and call Bind() if you want one. No snap-back here (that
// would fight RubberBandForce's progressive barrier); it only bleeds speed above maxPlayerSpeed back
// down, and only on the owned player's Rigidbody. Should never trigger in normal play.
public class RubberBandSafetyNet : MonoBehaviour
{
    [Header("Safety cap (should never trigger in normal play)")]
    [Tooltip("Maximum speed (m/s) we'll let either player reach. Above this we damp velocity. " +
             "Pick something well above max legitimate jump speed but below 'broken physics' territory.")]
    public float maxPlayerSpeed = 40f;

    private Rigidbody rbA;
    private Rigidbody rbB;
    private NetworkObject netA;
    private NetworkObject netB;

    public void Bind(Transform a, Transform b)
    {
        rbA = a.GetComponentInParent<Rigidbody>();
        rbB = b.GetComponentInParent<Rigidbody>();
        netA = rbA != null ? rbA.GetComponent<NetworkObject>() : null;
        netB = rbB != null ? rbB.GetComponent<NetworkObject>() : null;

        if (rbA == null || rbB == null)
            Debug.LogError("[RubberBandSafetyNet] Couldn't find Rigidbody on one or both players.");
    }

    private void FixedUpdate()
    {
        if (rbA == null || rbB == null) return;

        // Each peer only caps its own player, so two peers don't fight over the same velocity.
        bool ownsA = netA != null && netA.IsOwner;
        bool ownsB = netB != null && netB.IsOwner;

        if (ownsA) ApplyVelocityCap(rbA);
        if (ownsB) ApplyVelocityCap(rbB);
    }

    private void ApplyVelocityCap(Rigidbody rb)
    {
        Vector3 v = rb.linearVelocity;
        float speed = v.magnitude;
        if (speed > maxPlayerSpeed)
        {
            // Lerp toward the cap instead of hard-clamping to avoid a visible thud.
            float t = 1f - Mathf.Exp(-10f * Time.fixedDeltaTime);
            float newSpeed = Mathf.Lerp(speed, maxPlayerSpeed, t);
            rb.linearVelocity = v * (newSpeed / speed);
        }
    }
}
