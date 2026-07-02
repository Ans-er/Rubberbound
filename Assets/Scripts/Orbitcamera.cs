using Unity.Netcode;

using UnityEngine;
using UnityEngine.InputSystem;

// Simple third-person orbit camera. Drop on the Main Camera and drag the player into 'target'.
// Movement input becomes camera-relative when 'Adjust Inputs To Camera Angle' is on the controller.
public class OrbitCamera : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The transform to orbit around. Drag your player here.")]
    [SerializeField] private Transform target;
    [Tooltip("Where on the target the camera looks at, relative to its origin. Roughly head/chest height.")]
    [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.5f, 0f);

    [Header("Distance")]
    [SerializeField] private float distance = 5f;

    [Header("Sensitivity (degrees per pixel of mouse delta)")]
    [SerializeField] private float sensitivityX = 0.15f;
    [SerializeField] private float sensitivityY = 0.15f;
    [SerializeField] private bool invertY = false;

    [Header("Vertical clamp (degrees)")]
    [SerializeField] private float minPitch = -30f;
    [SerializeField] private float maxPitch = 70f;

    [Header("Smoothing")]
    [Tooltip("Higher = camera follows player position more tightly. 20 is a good default.")]
    [SerializeField] private float positionLerp = 20f;

    [Header("Wall avoidance (optional)")]
    [SerializeField] private bool avoidWalls = true;
    [SerializeField] private LayerMask wallMask = ~0;
    [Tooltip("Sphere radius used for the cast, keeps the camera off walls.")]
    [SerializeField] private float wallSkin = 0.2f;

    [Header("Cursor")]
    [SerializeField] private bool lockCursor = true;

    private float _yaw;
    private float _pitch;

    private void Start()
    {
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // Initialise yaw/pitch from however the camera is rotated in the scene.
        Vector3 e = transform.eulerAngles;
        _yaw = e.y;
        _pitch = NormalizePitch(e.x);
    }

    private void LateUpdate()
    {
        if (target == null) return;

        // Only steer while the cursor is locked to the game. When a menu (e.g. the pause screen) frees
        // the cursor, feed zero look input so the camera holds still while the mouse moves over the UI.
        // It still follows the player's position below.
        Vector2 mouseDelta = (Cursor.lockState == CursorLockMode.Locked && Mouse.current != null)
            ? Mouse.current.delta.ReadValue()
            : Vector2.zero;

        _yaw += mouseDelta.x * sensitivityX;
        _pitch += (invertY ? mouseDelta.y : -mouseDelta.y) * sensitivityY;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 pivot = target.position + targetOffset;

        // Wall avoidance: if there's geometry between the pivot and where we want the camera,
        // pull the camera in to that hit point.
        float currentDist = distance;
        if (avoidWalls)
        {
            Vector3 castDir = rot * Vector3.back;
            if (Physics.SphereCast(pivot, wallSkin, castDir, out RaycastHit hit,
                                   distance, wallMask, QueryTriggerInteraction.Ignore))
            {
                currentDist = hit.distance;
            }
        }

        Vector3 desiredPos = pivot + rot * (Vector3.back * currentDist);

        transform.position = desiredPos;
        transform.rotation = rot;
    }

    // Convert a 0..360 euler angle into the -180..180 range so clamping behaves.
    private static float NormalizePitch(float a)
    {
        a %= 360f;
        if (a > 180f) a -= 360f;
        return a;
    }
}