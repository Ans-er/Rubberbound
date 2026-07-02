using Unity.Netcode;

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A floating-capsule physics character controller (Very Very Valet style), trimmed to
/// exactly what this game needs and built around an explicit jump state machine.
///
/// Multiplayer model: client-authoritative. Each peer owns one player. The owner reads
/// input and runs all the physics. Non-owners are kinematic and driven by NetworkTransform.
///
/// STATE MACHINE (owner only, FixedUpdate):
///   GROUNDED — a one-sided "solid floor" holds the capsule at ride height (no spring, so no
///              bob/bounce). Movement is a target-velocity force whose budget scales with
///              input (held = snappy walking; idle = easily dragged by the rope). The body
///              yaws to face the move direction. Pressing jump begins a charge.
///   CHARGING — translation is fully frozen by PhysX (X/Y/Z): the player is a hard anchor, so
///              a partner's rope yank reflects entirely back onto the puller ("glue"). The
///              body can still YAW to aim. Release (or hitting max charge) launches.
///   AIRBORNE — no floor; gravity, drag and the rope dominate. A small input acceleration lets
///              you nudge the trajectory but never lift against gravity. Holding jump while
///              airborne queues a charge that fires the instant you land.
///
/// JUMP — Jump King-style charge. Hold to charge, release to launch in the facing direction
/// with an impulse that scales with charge time. Because you cannot move while charging, the
/// launch is fully deterministic (no inherited horizontal velocity, no float-spring phase).
/// </summary>
public class PhysicsBasedCharacterController : NetworkBehaviour
{
    private enum MoveState { Grounded, Charging, Airborne }

    private Rigidbody _rb;
    private Vector3 _gravitationalForce;
    private Vector2 _moveContext;   // server-side: the owner's resolved WORLD-space move vector
    private Vector2 _ownerStick;    // owner-side: latest raw stick, before camera adjustment
    private Vector3 _goalVel = Vector3.zero;
    private ParticleSystem.EmissionModule _emission;

    private MoveState _state = MoveState.Airborne;
    private float _chargeStartTime;
    private bool _jumpHeld;
    private float _timeSinceLaunch = 999f;

    // ---- Replicated input (owner writes, server reads) ----
    // SERVER-AUTHORITATIVE model: the owning client samples its input devices and writes them here;
    // the SERVER (the single Obi authority) reads them every FixedUpdate and drives the body. The
    // host writes+reads its own input in the same frame; a remote client's input arrives over the
    // wire. Unified code path: every player is always driven from this network state.
    private readonly NetworkVariable<Vector2> _netMove = new NetworkVariable<Vector2>(
        Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // ---- Replicated charge meter (server writes, owner reads) ----
    // Charge state now lives on the server, but the jump meter must show on the OWNER's screen, so
    // the server publishes the live charge here and the owner drives its local UI from it (Update).
    private readonly NetworkVariable<float> _netChargePercent = new NetworkVariable<float>(0f);
    private readonly NetworkVariable<bool> _netCharging = new NetworkVariable<bool>(false);

    // ---- Animation parameter hashes ----
    // The owner writes these onto the Animator each FixedUpdate; the transitions you build in the
    // Animator window decide which clip plays. Create matching parameters on the controller:
    //   Grounded (Bool), Charging (Bool), Speed (Float), VerticalSpeed (Float),
    //   Release (Trigger), Land (Trigger).
    private static readonly int _hashGrounded = Animator.StringToHash("Grounded");
    private static readonly int _hashCharging = Animator.StringToHash("Charging");
    private static readonly int _hashSpeed = Animator.StringToHash("Speed");
    private static readonly int _hashVerticalSpeed = Animator.StringToHash("VerticalSpeed");
    private static readonly int _hashRelease = Animator.StringToHash("Release");
    private static readonly int _hashLand = Animator.StringToHash("Land");

    [Header("Other:")]
    [SerializeField] private bool _adjustInputsToCameraAngle = false;
    [SerializeField] private LayerMask _terrainLayer;
    [SerializeField] private ParticleSystem _dustParticleSystem;
    [SerializeField] private JumpChargeUI _jumpChargeUI;

    [Header("Ride height (solid floor):")]
    [Tooltip("How high the capsule floats above the ground it's standing on.")]
    [SerializeField] private float _rideHeight = 2f;
    [Tooltip("Downward ground-probe length. Must be a bit longer than ride height.")]
    [SerializeField] private float _rayToGroundLength = 3f;
    [Tooltip("Max speed (m/s) used to lift the capsule back to ride height after it dips below " +
             "(fast landing / step up). Higher = snappier, but too high pops.")]
    [SerializeField] private float _solidSnapSpeed = 12f;
    [Tooltip("Small band above ride height within which the capsule still rests, so it sits " +
             "dead-still instead of micro-jittering on the line.")]
    [SerializeField] private float _solidFloatSkin = 0.05f;

    [Header("Grounded check:")]
    [Tooltip("How far above ride height still counts as 'on the ground' for landing / staying " +
             "grounded. Keep tight — this is NOT a coyote-time forgiveness margin.")]
    [SerializeField] private float _landSkin = 0.15f;
    [Tooltip("After launching, ignore the grounded check for this long (s) so the upward arc " +
             "can't be mistaken for a landing and snap you back down.")]
    [SerializeField] private float _postLaunchLockout = 0.1f;

    [Header("Turning:")]
    [Tooltip("Yaw time constant (s). SMALLER = SNAPPIER. ~0.05 turns in ~0.1s.")]
    [SerializeField] private float _turnSmoothTime = 0.05f;
    [Tooltip("Max turn rate (deg/s) so a 180 flip doesn't snap in a single frame.")]
    [SerializeField] private float _maxTurnSpeed = 1080f;

    [Header("Movement (grounded):")]
    [SerializeField] private float _maxSpeed = 8f;
    [SerializeField] private float _acceleration = 200f;
    [Tooltip("Per-step force budget when input is fully held. Higher = snappier walking, more " +
             "resistance to being dragged while moving.")]
    [SerializeField] private float _walkingMaxAccelForce = 150f;
    [Tooltip("Per-step force budget when no input is held. Lower = more easily dragged by the " +
             "rope while standing still. In between: lerped by input magnitude.")]
    [SerializeField] private float _idleMaxAccelForce = 20f;
    [Tooltip("Acceleration multiplier vs (input · current goal-velocity direction). >1 at -1 " +
             "snaps direction changes; 1 elsewhere.")]
    [SerializeField] private AnimationCurve _accelerationFactorFromDot =
        new AnimationCurve(new Keyframe(-1f, 2f), new Keyframe(0f, 1f), new Keyframe(1f, 1f));
    [Tooltip("Force-budget multiplier vs (input · current goal-velocity direction). LOW at +1 " +
             "means you push only weakly in the direction you're ALREADY moving — this is what " +
             "stops you from out-muscling / over-tensioning the rubber band when pulling it taut " +
             "(e.g. wrapped around a pole). Full at -1 so braking/reversing stays snappy.")]
    [SerializeField] private AnimationCurve _maxAccelerationForceFactorFromDot =
        new AnimationCurve(new Keyframe(-1f, 1f), new Keyframe(1f, 0.2f));

    [Header("Movement (airborne):")]
    [Tooltip("Acceleration applied in the input direction while airborne (m/s²). Subtle: lets " +
             "you correct a trajectory or pump a swing. Vertical contribution is always zero.")]
    [SerializeField] private float _airInputAcceleration = 6f;

    [Header("Charged Jump (Jump King-style):")]
    [Tooltip("Master switch for the charged jump. Off = no jump at all.")]
    [SerializeField] private bool _useChargedJump = true;
    [Tooltip("Seconds to reach full charge. Jump King is ~1s.")]
    [SerializeField] private float _maxChargeTime = 1.0f;
    [Tooltip("Smallest launch impulse (N·s). Effective launch speed = impulse / mass.")]
    [SerializeField] private float _minLaunchImpulse = 5f;
    [Tooltip("Fully-charged launch impulse (N·s).")]
    [SerializeField] private float _maxLaunchImpulse = 14f;
    [Tooltip("Reshape the charge percent -> impulse mapping. Default linear.")]
    [SerializeField] private AnimationCurve _chargeCurve = AnimationCurve.Linear(0, 0, 1, 1);
    [Tooltip("Launch angle from horizontal (deg). 90 = pure vertical, 45 = max range.")]
    [Range(30f, 89f)]
    [SerializeField] private float _launchAngleDegrees = 75f;

    [Header("Animation:")]
    [Tooltip("Animator on the kangaroo mesh. If left empty, the first Animator found in children " +
             "is used at runtime. Its parameters are driven from this script; build the state " +
             "transitions in the Animator window. Add a NetworkAnimator to replicate to remotes.")]
    [SerializeField] private Animator _animator;
    [Tooltip("Stairs/slopes briefly un-ground the body for a frame or two; this delay keeps the " +
             "Animator 'Grounded' (no Fall clip) through those blips. A real fall lasts longer.")]
    [SerializeField] private float _animFallDelay = 0.15f;
    private float _animUngroundedTime;

    // Position-only freeze applied while charging; OR'd onto the rigidbody's base rotation
    // constraints so the body still cannot tilt but also cannot be translated by the rope.
    private const RigidbodyConstraints CHARGE_FREEZE_MASK =
        RigidbodyConstraints.FreezePositionX |
        RigidbodyConstraints.FreezePositionY |
        RigidbodyConstraints.FreezePositionZ;

    /// <summary> True while the player is holding to charge a jump. </summary>
    public bool IsCharging => _state == MoveState.Charging;

    /// <summary> 0..1 charge progress, for a UI meter. </summary>
    public float ChargePercent =>
        IsCharging ? Mathf.Clamp01((Time.time - _chargeStartTime) / _maxChargeTime) : 0f;

    private void Awake()
    {
        // One Animator lives on the kangaroo mesh under Renderers; grab it if not wired by hand.
        if (_animator == null) _animator = GetComponentInChildren<Animator>(true);
    }

    public override void OnNetworkSpawn()
    {
        // SERVER-AUTHORITATIVE bodies: the host runs the real physics for BOTH players (so its single
        // Obi solver genuinely pulls both), and every client is a kinematic puppet driven by
        // NetworkTransform. NetworkRigidbody.AutoUpdateKinematicState also manages this once the
        // NetworkTransform is Server authority; we set it explicitly so the intent is local & obvious.
        var rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = !IsServer;

        if (!IsOwner)
        {
            // Only the local player reads input devices and renders a camera.
            var playerInput = GetComponent<PlayerInput>();
            if (playerInput != null) playerInput.enabled = false;

            var cam = GetComponentInChildren<Camera>();
            if (cam != null) cam.gameObject.SetActive(false);
        }
        else
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
    }

    private void Start()
    {
        _rb = GetComponent<Rigidbody>();
        _gravitationalForce = Physics.gravity * _rb.mass;

        if (_dustParticleSystem)
        {
            _emission = _dustParticleSystem.emission;
            _emission.enabled = false;
        }
    }

    // Owner-side per-frame work: publish the camera-relative move vector and draw the jump meter.
    private void Update()
    {
        if (!IsOwner) return;

        // Camera-relative input must be resolved where the camera actually lives — the OWNER. The
        // server simulates every body but has no access to the owning client's camera, so we send a
        // ready-to-use WORLD-space move vector. Recomputed every frame so that holding a direction
        // while turning the camera keeps steering (the raw stick alone doesn't change then).
        Vector2 world = _ownerStick;
        if (_adjustInputsToCameraAngle && world.sqrMagnitude > 1e-6f)
        {
            Vector3 dir = AdjustInputToFaceCamera(new Vector3(world.x, 0f, world.y));
            world = new Vector2(dir.x, dir.z);
        }
        if ((world - _netMove.Value).sqrMagnitude > 1e-6f) _netMove.Value = world;

        // Owner renders its OWN jump meter from the server-published charge state. (The host, being
        // owner of its own player, is covered too.)
        if (_jumpChargeUI != null)
            _jumpChargeUI.SetCharge01(_netChargePercent.Value, _netCharging.Value);
    }

    private void FixedUpdate()
    {
        // SERVER-AUTHORITATIVE: the host simulates EVERY player so its one Obi solver can pull both.
        // Clients are kinematic puppets driven by NetworkTransform and never run this body.
        if (!IsServer) return;

        // Drive this player from its replicated input (host: same-frame; client: over the wire).
        _moveContext = _netMove.Value;

        _timeSinceLaunch += Time.fixedDeltaTime;

        (bool rayHitGround, RaycastHit rayHit) = RaycastToGround();
        Vector3 input = ReadMoveInput();
        bool descending = _rb.linearVelocity.y <= 0f;

        switch (_state)
        {
            case MoveState.Grounded:
                SetDust(true);
                MaintainHeightSolid(rayHitGround, rayHit);
                CharacterMove(input, rayHit);
                FaceDirection(input);
                if (!IsGroundedForState(rayHitGround, rayHit))
                    _state = MoveState.Airborne;
                break;

            case MoveState.Charging:
                SetDust(false);
                _rb.linearVelocity = Vector3.zero;   // belt-and-suspenders; constraints already pin us
                FaceDirection(input);                // steer to aim
                // Charge caps at 100% and HOLDS — launch only happens on release (JumpInputAction).
                break;

            case MoveState.Airborne:
                SetDust(false);
                _rb.angularVelocity = Vector3.zero;  // no self-turn in the air; also stops launch-spin
                CharacterAirInfluence(input);
                if (IsLanding(rayHitGround, rayHit, descending))
                {
                    if (_jumpHeld && _useChargedJump)
                    {
                        BeginCharge();
                    }
                    else
                    {
                        _state = MoveState.Grounded;
                        if (_animator != null) _animator.SetTrigger(_hashLand);
                    }
                }
                break;
        }

        UpdateAnimatorParams();

        // Charge lives on the server; publish it so the OWNER can draw its own jump meter (Update).
        // Guarded writes so we only send when the value actually changes.
        float pct = ChargePercent;
        bool charging = IsCharging;
        if (!Mathf.Approximately(_netChargePercent.Value, pct)) _netChargePercent.Value = pct;
        if (_netCharging.Value != charging) _netCharging.Value = charging;
    }

    // ====================================================================
    // Ground probing
    // ====================================================================

    private (bool, RaycastHit) RaycastToGround()
    {
        Ray ray = new Ray(transform.position, Vector3.down);
        // Ignore triggers: checkpoint / finish / area volumes must never be treated as ground, or
        // the float catches you on them mid-jump (feels like an invisible ceiling).
        bool hit = Physics.Raycast(ray, out RaycastHit rayHit, _rayToGroundLength,
                                   _terrainLayer.value, QueryTriggerInteraction.Ignore);
        return (hit, rayHit);
    }

    /// <summary> Tight grounded test used to STAY grounded (walked off an edge -> false). </summary>
    private bool IsGroundedForState(bool rayHitGround, RaycastHit rayHit)
    {
        return rayHitGround && rayHit.distance <= _rideHeight + _landSkin;
    }

    /// <summary>
    /// Strict landing test: real ground within the tight margin, actually coming down, and past
    /// the post-launch lockout so the upward arc of a jump is never mistaken for a landing.
    /// </summary>
    private bool IsLanding(bool rayHitGround, RaycastHit rayHit, bool descending)
    {
        return rayHitGround
            && rayHit.distance <= _rideHeight + _landSkin
            && descending
            && _timeSinceLaunch >= _postLaunchLockout;
    }

    /// <summary>
    /// One-sided "solid floor" at ride height — no spring, so no bob and it never fights a jump.
    ///   • Above the line (currHeight > skin)  -> do nothing.
    ///   • Below the line                       -> rise back at exactly the needed speed (capped).
    ///   • Resting on the line                  -> kill vertical drift and hold against gravity.
    /// </summary>
    private void MaintainHeightSolid(bool rayHitGround, RaycastHit rayHit)
    {
        if (!rayHitGround) return;

        float currHeight = rayHit.distance - _rideHeight;
        if (currHeight > _solidFloatSkin) return; // airborne above the floor: leave it alone

        Vector3 v = _rb.linearVelocity;
        if (currHeight < 0f)
            // Set (not Max) so once we reach the line there's no leftover upward velocity to hop.
            v.y = Mathf.Min(-currHeight / Time.fixedDeltaTime, _solidSnapSpeed);
        else
            v.y = 0f; // resting on the line: sit dead-still
        _rb.linearVelocity = v;

        _rb.AddForce(-_gravitationalForce); // hold against gravity so it sits still at ride height
    }

    // ====================================================================
    // Movement
    // ====================================================================

    private Vector3 ReadMoveInput()
    {
        // _moveContext is the owner's already-resolved WORLD-space move vector (camera adjustment, if
        // enabled, was applied on the owner in Update — the server has no camera to do it with).
        Vector3 mi = new Vector3(_moveContext.x, 0f, _moveContext.y);
        // Diagonal input from a 2D composite has magnitude ~1.41; clamp so diagonal == cardinal.
        return Vector3.ClampMagnitude(mi, 1f);
    }

    private Vector3 AdjustInputToFaceCamera(Vector3 moveInput)
    {
        var cam = GetComponentInChildren<Camera>();
        if (cam == null) return moveInput;
        float facing = cam.transform.eulerAngles.y;
        return Quaternion.Euler(0f, facing, 0f) * moveInput;
    }

    /// <summary>
    /// Grounded locomotion: drive toward a target velocity, with the per-step force budget
    /// interpolated between idle and walking by input magnitude. Plus simulated ground friction
    /// (the floating capsule never really touches the ground, so PhysX won't apply it for us).
    /// </summary>
    private void CharacterMove(Vector3 moveInput, RaycastHit rayHit)
    {
        // How aligned the push is with where we're already heading (-1 = opposite, +1 = same).
        float velDot = Vector3.Dot(moveInput, _goalVel.normalized);

        Vector3 goalVel = moveInput * _maxSpeed;
        float accel = _acceleration * _accelerationFactorFromDot.Evaluate(velDot);
        _goalVel = Vector3.MoveTowards(_goalVel, goalVel, accel * Time.fixedDeltaTime);

        Vector3 neededAccel = (_goalVel - _rb.linearVelocity) / Time.fixedDeltaTime;
        neededAccel.y = 0f; // horizontal only — never push the capsule off the floor

        // Budget scales with input magnitude (idle->walking) AND with direction: pushing along
        // your current motion is throttled, so you can't out-muscle a taut rubber band.
        float inputMag = Mathf.Clamp01(moveInput.magnitude);
        float budget = Mathf.Lerp(_idleMaxAccelForce, _walkingMaxAccelForce, inputMag);
        float maxAccel = budget * _maxAccelerationForceFactorFromDot.Evaluate(velDot);
        neededAccel = Vector3.ClampMagnitude(neededAccel, maxAccel);

        _rb.AddForce(neededAccel * _rb.mass);

        // Simulated friction: read dynamicFriction off the hit PhysicMaterial, apply F = μ·g.
        Collider hitCol = rayHit.collider;
        float frictionCoef = (hitCol != null && hitCol.sharedMaterial != null)
            ? hitCol.sharedMaterial.dynamicFriction : 0f;

        if (frictionCoef > 0f)
        {
            Vector3 horizVel = _rb.linearVelocity;
            horizVel.y = 0f;
            if (horizVel.sqrMagnitude > 0.0001f)
            {
                float frictionDecel = frictionCoef * Mathf.Abs(Physics.gravity.y);
                float dv = Mathf.Min(frictionDecel * Time.fixedDeltaTime, horizVel.magnitude);
                _rb.linearVelocity -= horizVel.normalized * dv;
            }
        }
    }

    /// <summary>
    /// Subtle airborne input: a small horizontal acceleration in the input direction. No target
    /// velocity — gravity, drag and the rope dominate; this only nudges. (Vertical is always 0.)
    /// </summary>
    private void CharacterAirInfluence(Vector3 moveInput)
    {
        Vector3 dir = moveInput;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();
        _rb.AddForce(dir * _airInputAcceleration, ForceMode.Acceleration); // m/s², ignores mass
    }

    /// <summary>
    /// Yaw the body toward <paramref name="dir"/> by driving angular velocity (eased in, capped,
    /// overshoot-free). Rotation X/Z are frozen by the rigidbody constraints, so only yaw moves
    /// and the body can never tilt.
    /// </summary>
    private void FaceDirection(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) { _rb.angularVelocity = Vector3.zero; return; }

        float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float deltaYaw = Mathf.DeltaAngle(_rb.rotation.eulerAngles.y, targetYaw);
        if (Mathf.Abs(deltaYaw) < 0.01f) { _rb.angularVelocity = Vector3.zero; return; }

        float speedDeg = Mathf.Min(Mathf.Abs(deltaYaw) / Mathf.Max(_turnSmoothTime, 1e-4f), _maxTurnSpeed);
        _rb.angularVelocity = new Vector3(0f, Mathf.Sign(deltaYaw) * speedDeg * Mathf.Deg2Rad, 0f);
    }

    private void SetDust(bool on)
    {
        if (_dustParticleSystem && _emission.enabled != on) _emission.enabled = on;
    }

    // ====================================================================
    // Animation
    // ====================================================================
    // The script only feeds the Animator the gameplay facts; the state graph (which clip plays
    // and how clips blend) is built in the Animator window. Owner-only — remote players are
    // kinematic and should be synced with a NetworkAnimator on the kangaroo.
    //
    // Parameters written here (create these on the "Kangaroo" controller):
    //   Grounded (Bool)       — in the grounded state            → drives Idle / Hop
    //   Charging (Bool)       — charging a jump                   → drives Charge
    //   Speed (Float)         — horizontal speed (m/s)            → Idle ⇄ Hop threshold
    //   VerticalSpeed (Float) — rigidbody y velocity (m/s)        → Sustain (rising) / Fall (falling)
    //   Release (Trigger)     — fired once at launch              → Release  (see Launch)
    //   Land (Trigger)        — fired once at touchdown           → Land     (see FixedUpdate)

    private void UpdateAnimatorParams()
    {
        if (_animator == null) return;
        Vector3 v = _rb.linearVelocity;

        // "Grounded for animation" rides over the brief, normal un-groundings on stairs/slopes so
        // the Fall clip doesn't flash. Charging counts as planted (you're glued to the ground).
        bool planted = _state == MoveState.Grounded || _state == MoveState.Charging;
        _animUngroundedTime = planted ? 0f : _animUngroundedTime + Time.fixedDeltaTime;
        bool animGrounded = _animUngroundedTime < _animFallDelay;

        _animator.SetBool(_hashGrounded, animGrounded);
        _animator.SetBool(_hashCharging, _state == MoveState.Charging);
        _animator.SetFloat(_hashSpeed, new Vector2(v.x, v.z).magnitude);
        _animator.SetFloat(_hashVerticalSpeed, v.y);
    }

    // ====================================================================
    // Charged jump
    // ====================================================================

    private void BeginCharge()
    {
        if (!_useChargedJump || _state == MoveState.Charging) return;

        // Snap to exact ride height so the launch starts from a deterministic position.
        (bool hit, RaycastHit rayHit) = RaycastToGround();
        if (hit) _rb.position += Vector3.down * (rayHit.distance - _rideHeight);

        _rb.linearVelocity = Vector3.zero;
        _rb.constraints |= CHARGE_FREEZE_MASK;

        _state = MoveState.Charging;
        _chargeStartTime = Time.time;
    }

    private void Launch()
    {
        float pct = Mathf.Clamp01((Time.time - _chargeStartTime) / _maxChargeTime);
        float mag = Mathf.Lerp(_minLaunchImpulse, _maxLaunchImpulse, _chargeCurve.Evaluate(pct));

        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
        fwd.Normalize();

        float angleRad = _launchAngleDegrees * Mathf.Deg2Rad;
        Vector3 impulse = fwd * (mag * Mathf.Cos(angleRad)) + Vector3.up * (mag * Mathf.Sin(angleRad));

        _rb.constraints &= ~CHARGE_FREEZE_MASK; // release the position freeze, keep rotation freeze
        _rb.linearVelocity = Vector3.zero;      // deterministic arc: no inherited velocity
        _rb.AddForce(impulse, ForceMode.Impulse);

        _state = MoveState.Airborne;
        _timeSinceLaunch = 0f;

        if (_animator != null) _animator.SetTrigger(_hashRelease);
    }

    // ====================================================================
    // Input (PlayerInput events — keep these signatures)
    // ====================================================================

    public void MoveInputAction(InputAction.CallbackContext context)
    {
        // Owner just records the raw stick; Update() resolves it against the camera every frame and
        // publishes the world-space result to the server-read NetworkVariable.
        if (!IsOwner) return;
        _ownerStick = context.ReadValue<Vector2>();
    }

    public void JumpInputAction(InputAction.CallbackContext context)
    {
        if (!IsOwner || !_useChargedJump) return;

        // Jump is edge-triggered and the SERVER owns the state machine, so forward exact press/release
        // edges to the server via (reliable) RPCs — a sampled bool could miss a fast tap.
        if (context.started) JumpPressedServerRpc();
        else if (context.canceled) JumpReleasedServerRpc();
    }

    [ServerRpc]
    private void JumpPressedServerRpc()
    {
        if (!_useChargedJump) return;
        _jumpHeld = true;
        if (_state == MoveState.Grounded) BeginCharge();
    }

    [ServerRpc]
    private void JumpReleasedServerRpc()
    {
        if (!_useChargedJump) return;
        _jumpHeld = false;
        if (_state == MoveState.Charging) Launch();
    }
}
