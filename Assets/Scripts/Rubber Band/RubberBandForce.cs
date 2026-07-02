using Obi;

using Unity.Netcode;

using UnityEngine;

// Optional hard max-stretch end-stop for the rubber band, driven entirely by Obi's own simulated rope
// state. No raycasts and no "wrapped / not wrapped" classification (those caused flicker and wildly
// different behaviour every time). Off by default; RubberBandSpawner only arms it when its
// useEndStopBackstop flag is set.
//
// How it reads the rope:
//   Tension = rope.CalculateLength() / restLength, the rope's real strain from the particle positions
//   Obi already solved. It rises the same way whether the rope is straight, draped, or wrapped several
//   times, because wrapping just uses up more length.
//   Direction = the rope's actual tangent at each player's end particle. When wrapped, that tangent
//   points toward the obstacle, so resisting outward motion along it stops the player from pulling the
//   wrap tighter. Real geometry, not a re-derived guess.
//
// The wall: below restLength * brakeStretchFactor we do nothing, Obi owns the stretchy feel. From there
// up to restLength * wallStretchFactor we bleed off each owned player's outward velocity (the part that
// would stretch the band further); at the wall all of it is removed. Since we never add a big force,
// nothing explodes and movement below the cap is untouched.
//
// Why it stops tunnelling: the players are the only things that can lengthen the rope (the obstacle is
// static). Capping outward velocity at both ends caps total strain, so the stretch constraint can never
// build enough tension to out-muscle the collision constraint and slip through the pole. Around a pole
// the cap engages at a smaller player separation, automatically, because the wrap already used up length.
//
// Netcode: each peer only ever edits its own Rigidbody.
[DisallowMultipleComponent]
public class RubberBandForce : MonoBehaviour
{
    [Header("Master switch")]
    [Tooltip("Off = pure Obi, this component is inert. On = the max-stretch end-stop is armed.")]
    public bool enableEndStop = true;

    [Tooltip("Grace period (s) after Bind()/respawn during which the end-stop stays inert, so the rope " +
             "and players can settle. 0 = armed immediately.")]
    public float warmupSeconds = 0.5f;

    [Header("Rope")]
    [Tooltip("Natural rope length. Set automatically from rope.restLength via Bind().")]
    public float restLength = 3.24f;

    [Tooltip("Hard wall as a multiple of restLength: the measured length can never exceed restLength * " +
             "this. Pick the largest stretch that still holds on your thinnest pole without tunnelling " +
             "(find it with logStrain on). 1.6 = up to 60% stretch.")]
    [Range(1.05f, 4f)] public float wallStretchFactor = 1.6f;

    [Tooltip("Where braking begins, as a multiple of restLength. Between here and the wall, outward " +
             "speed is bled off so a fast player is caught before the wall. Must be < wallStretchFactor; " +
             "the gap is your give.")]
    [Range(1f, 4f)] public float brakeStretchFactor = 1.45f;

    [Header("End-stop tuning")]
    [Tooltip("How many particles in from each end to sample the tangent. 1 = the immediate neighbour " +
             "(can jitter on a slack rope); 2-3 = a steadier pull direction.")]
    [Range(1, 6)] public int tangentSamples = 2;

    [Tooltip("If the rope is somehow already past the wall, the most we shove a player back inward per " +
             "second to recover, in m/s. Keep modest so it can't fling.")]
    public float maxCorrectionSpeed = 6f;

    [Tooltip("Optional active inward assist (m/s^2) in the brake zone, on top of Obi's own pull. Leave " +
             "at 0 to let Obi do the yank-back; raise only if the band feels limp at the wall.")]
    public float wallAssist = 0f;

    [Header("Safety net (should never trigger in normal play)")]
    [Tooltip("Zero non-finite velocities and cap any speed above this (m/s) on owned players. Pure guard " +
             "against an Obi/controller explosion; set well above legitimate jump speed.")]
    public float maxPlayerSpeed = 60f;
    public bool enableSpeedClamp = true;

    [Header("Debug")]
    [Tooltip("Log the live strain / length / wall each FixedUpdate.")]
    public bool logStrain = false;
    [Tooltip("On-screen readout of strain and state.")]
    public bool showDebugGUI = false;

    private Transform pointA, pointB;
    private Rigidbody rbA, rbB;
    private NetworkObject netA, netB;
    private ObiRope rope;

    private float bindTime = -999f;

    // Live debug state.
    private float dbgLength, dbgStrain, dbgBrake, dbgWall;
    private Vector3 dbgInA, dbgInB;

    // Wire the band to the two player anchors, the rope's rest length, and the ObiRope (required, all
    // signals are read from it).
    public void Bind(Transform a, Transform b, float ropeRestLength, ObiRope obiRope = null)
    {
        pointA = a;
        pointB = b;
        restLength = ropeRestLength;
        rope = obiRope;
        bindTime = Time.time;
        rbA = a.GetComponentInParent<Rigidbody>();
        rbB = b.GetComponentInParent<Rigidbody>();
        netA = rbA != null ? rbA.GetComponent<NetworkObject>() : null;
        netB = rbB != null ? rbB.GetComponent<NetworkObject>() : null;

        if (rbA == null || rbB == null)
            Debug.LogError("[RubberBandForce] Couldn't find Rigidbody on one or both players.");
        if (rope == null)
            Debug.LogError("[RubberBandForce] No ObiRope passed to Bind(); the end-stop needs it to read strain.");
    }

    private void FixedUpdate()
    {
        if (!enableEndStop) return;
        if (pointA == null || pointB == null || rbA == null || rbB == null) return;
        if (rope == null || !rope.isLoaded) return;
        if (restLength < 1e-4f) return;

        bool ownsA = netA != null && netA.IsOwner;
        bool ownsB = netB != null && netB.IsOwner;

        if (enableSpeedClamp)
        {
            if (ownsA) ClampSpeed(rbA);
            if (ownsB) ClampSpeed(rbB);
        }

        // Tension straight from Obi: the rope's real strain.
        dbgLength = rope.CalculateLength();
        dbgStrain = dbgLength / restLength;
        dbgWall = wallStretchFactor;
        dbgBrake = Mathf.Min(brakeStretchFactor, wallStretchFactor - 0.001f);

        if (logStrain)
            Debug.Log($"[RubberBandForce] length={dbgLength:F2} strain={dbgStrain:F3} " +
                      $"brake={dbgBrake:F3} wall={dbgWall:F3}");

        // Below the brake zone the band is still Obi's job.
        if (dbgStrain <= dbgBrake) return;

        // Let things settle after spawn/respawn before clamping.
        if (Time.time - bindTime < warmupSeconds) return;

        if (!TryGetEndTangents(out Vector3 inwardA, out Vector3 inwardB)) return;
        dbgInA = inwardA; dbgInB = inwardB;

        // 0 at the brake line, 1 at the wall and beyond.
        float t = Mathf.Clamp01(Mathf.InverseLerp(dbgBrake, dbgWall, dbgStrain));

        if (ownsA && inwardA != Vector3.zero) ApplyEndStop(rbA, inwardA, dbgStrain, t);
        if (ownsB && inwardB != Vector3.zero) ApplyEndStop(rbB, inwardB, dbgStrain, t);
    }

    // Remove the part of this player's velocity that would stretch the rope further (the outward
    // component along its tangent), gentle at the brake line and total at the wall. Nudge back inward if
    // already overshot. Never adds outward energy.
    private void ApplyEndStop(Rigidbody rb, Vector3 inwardDir, float strain, float t)
    {
        Vector3 outwardDir = -inwardDir;
        Vector3 v = rb.linearVelocity;

        float outwardSpeed = Vector3.Dot(v, outwardDir);
        if (outwardSpeed > 0f)
            v -= outwardDir * (outwardSpeed * t);

        // Overshoot past the wall (in metres of rope) -> bounded inward nudge.
        float over = (strain - wallStretchFactor) * restLength;
        if (over > 0f)
        {
            float correct = Mathf.Min(over / Time.fixedDeltaTime, maxCorrectionSpeed);
            v += inwardDir * correct;
        }

        rb.linearVelocity = v;

        if (wallAssist > 0f)
            rb.AddForce(inwardDir * (wallAssist * t), ForceMode.Acceleration);
    }

    // World-space inward tangents (anchor -> a few particles into the rope) for each player, from Obi's
    // live particle positions. Ends are matched to players by proximity. False if the rope has no usable
    // elements.
    private bool TryGetEndTangents(out Vector3 inwardA, out Vector3 inwardB)
    {
        inwardA = Vector3.zero;
        inwardB = Vector3.zero;

        int n = rope.elements.Count;
        if (n < 1) return false;

        int s = Mathf.Clamp(tangentSamples, 1, n);

        // Ordered chain: p0 = elements[0].particle1 ... pN = elements[n-1].particle2.
        int startAnchor = rope.elements[0].particle1;
        int startInward = rope.elements[s - 1].particle2;       // s particles in from the start
        int finishAnchor = rope.elements[n - 1].particle2;
        int finishInward = rope.elements[n - s].particle1;      // s particles in from the finish

        Vector3 startAnchorPos = ParticleWorldPos(startAnchor);
        Vector3 startInwardPos = ParticleWorldPos(startInward);
        Vector3 finishAnchorPos = ParticleWorldPos(finishAnchor);
        Vector3 finishInwardPos = ParticleWorldPos(finishInward);

        // Which physical end is player A's? Whichever anchor is closer to A.
        bool startIsA = (startAnchorPos - pointA.position).sqrMagnitude
                      <= (finishAnchorPos - pointA.position).sqrMagnitude;

        Vector3 aAnchor = startIsA ? startAnchorPos : finishAnchorPos;
        Vector3 aInward = startIsA ? startInwardPos : finishInwardPos;
        Vector3 bAnchor = startIsA ? finishAnchorPos : startAnchorPos;
        Vector3 bInward = startIsA ? finishInwardPos : startInwardPos;

        Vector3 da = aInward - aAnchor;
        Vector3 db = bInward - bAnchor;
        if (da.sqrMagnitude > 1e-8f) inwardA = da.normalized;
        if (db.sqrMagnitude > 1e-8f) inwardB = db.normalized;
        return inwardA != Vector3.zero || inwardB != Vector3.zero;
    }

    // Solver-local particle position -> world.
    private Vector3 ParticleWorldPos(int solverIndex)
    {
        return rope.solver.transform.TransformPoint((Vector3)rope.solver.positions[solverIndex]);
    }

    // NaN guard + speed cap. Should never act in normal play.
    private void ClampSpeed(Rigidbody rb)
    {
        Vector3 v = rb.linearVelocity;
        if (!IsFinite(v)) { rb.linearVelocity = Vector3.zero; return; }

        float speed = v.magnitude;
        if (speed > maxPlayerSpeed && speed > 1e-4f)
            rb.linearVelocity = v * (maxPlayerSpeed / speed);
    }

    private static bool IsFinite(Vector3 v)
    {
        return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                 float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }

    private void OnDrawGizmos()
    {
        if (pointA == null || pointB == null) return;

        Gizmos.color = dbgStrain >= dbgWall ? Color.red
                     : dbgStrain > dbgBrake ? new Color(1f, 0.6f, 0f) : Color.cyan;
        Gizmos.DrawLine(pointA.position, pointB.position);

        // Inward (relieve-stretch) tangents.
        Gizmos.color = Color.green;
        Gizmos.DrawRay(pointA.position, dbgInA * 0.6f);
        Gizmos.DrawRay(pointB.position, dbgInB * 0.6f);
    }

    private void OnGUI()
    {
        if (!showDebugGUI) return;

        const float w = 260f, h = 120f;
        GUI.Box(new Rect(10, 10, w, h), "RubberBandForce (max-stretch)");
        GUILayout.BeginArea(new Rect(20, 34, w - 20, h - 28));
        GUILayout.Label($"rope length: {dbgLength:F2} m");
        GUILayout.Label($"strain:      {dbgStrain:F3}  (rest = 1.0)");
        GUILayout.Label($"brake @:     {dbgBrake:F3}");
        GUILayout.Label($"wall  @:     {dbgWall:F3}");
        GUILayout.Label(dbgStrain >= dbgWall ? "STATE: WALL" :
                        dbgStrain > dbgBrake ? "STATE: braking" : "STATE: free (Obi)");
        GUILayout.EndArea();
    }
}
