using Unity.Cinemachine;
using UnityEngine;

// Drives a CameraTarget transform that the CinemachineCamera follows.
//
// Lag:   MoveTowards at a fixed cap so the boat can pull away visibly.
// Bob:   world-space Y sine wave scaled by speed.
// Zoom:  orthographic size grows with speed, eases back when slowing.
// Acceleration pulse: brief extra zoom-out when the boat surges (wind catches sail,
//        good heading reached). Decays smoothly via SmoothDamp.
public class CameraController : MonoBehaviour
{
    // The boat the camera follows.
    [SerializeField] Transform         boatTransform;
    // Rigidbody read for current speed and acceleration.
    [SerializeField] Rigidbody2D       boatRb;
    // BoatController read for normalised speed-based effects.
    [SerializeField] BoatController    boatController;
    // Intermediate follow target that Cinemachine tracks.
    [SerializeField] Transform         cameraTarget;
    // Cinemachine camera whose orthographic size this script drives.
    [SerializeField] CinemachineCamera cinemachineCamera;

    [Header("Lag")]
    // Normal follow speed while the boat remains comfortably on-screen.
    [SerializeField] float followSpeed = 3f;
    // Emergency follow speed used when the boat nears the viewport edge.
    [SerializeField] float edgeCatchupSpeed = 12f;
    // Half-size of the safe rectangle around viewport centre.
    // 0.25 means catch-up begins once the boat reaches about halfway from
    // screen centre toward an edge (x/y outside 0.25..0.75).
    [SerializeField] [Range(0.2f, 0.49f)] float safeViewportExtent = 0.25f;
    // Width of the ramp zone between normal lag and full emergency catch-up.
    [SerializeField] [Range(0.01f, 0.2f)] float edgeCatchupBand = 0.08f;

    [Header("Bob")]
    [SerializeField] float bobAmplitude = 0.05f;
    [SerializeField] float bobFrequency = 0.8f;
    [SerializeField] float bobFullSpeed = 5f;

    [Header("Zoom")]
    [SerializeField] float zoomBase      = 5f;
    [SerializeField] float zoomMax       = 8f;
    [SerializeField] float zoomFullSpeed = 8f;
    [SerializeField] float zoomSmoothing = 2f;

    [Header("Acceleration Pulse")]
    // Minimum acceleration (units/s²) needed to fire a pulse.
    [SerializeField] float pulseAccelThreshold   = 5f;
    // Boat must be above this speed fraction before a pulse can fire.
    [SerializeField] [Range(0f, 1f)] float pulseMinSpeedFraction = 0.25f;
    // How many extra orthographic units the pulse adds to the zoom.
    [SerializeField] float pulseZoomOut  = 1.5f;
    // How quickly the pulse decays back to zero.
    [SerializeField] float pulseDuration = 0.4f;

    Vector3 smoothedPos;
    float   previousSpeed;
    float   currentPulse;
    float   pulseVelocity;
    bool    warnedInvalidLensSize;

    void Awake()
    {
        EnsureValidCameraLensSize();
    }

    void OnEnable()
    {
        EnsureValidCameraLensSize();
    }

    void Start()
    {
        EnsureValidCameraLensSize();
        SnapToBoat();
    }

    void Update()
    {
        EnsureValidCameraLensSize();
        if (boatTransform == null || cameraTarget == null) return;

        float speed = 0f;
        if (boatRb != null)
            speed = boatRb.linearVelocity.magnitude;
        float speedFactor = Mathf.Clamp01(speed / Mathf.Max(bobFullSpeed, 0.01f));

        // Lag.
        Vector3 boatPos = boatTransform.position;
        boatPos.z       = smoothedPos.z;
        float followStep = GetCurrentFollowSpeed() * Time.deltaTime;
        smoothedPos      = Vector3.MoveTowards(smoothedPos, boatPos, followStep);

        // Bob: world-space Y, fades to zero at rest.
        float   bob = Mathf.Sin(Time.time * bobFrequency) * bobAmplitude * speedFactor;
        cameraTarget.position = smoothedPos + new Vector3(0f, bob, 0f);

        // Acceleration pulse: fires when the boat surges and is already moving.
        float accel = (speed - previousSpeed) / Time.deltaTime;
        previousSpeed = speed;
        float fraction = 0f;
        if (boatController != null)
            fraction = boatController.SpeedFraction;

        if (accel > pulseAccelThreshold && fraction > pulseMinSpeedFraction)
            currentPulse = Mathf.Max(currentPulse, pulseZoomOut);
        currentPulse = Mathf.SmoothDamp(currentPulse, 0f, ref pulseVelocity, pulseDuration);

        // Zoom: sustained speed zoom + transient pulse.
        if (cinemachineCamera != null)
        {
            float targetSize      = Mathf.Lerp(zoomBase, zoomMax, Mathf.Clamp01(speed / zoomFullSpeed))
                                  + currentPulse;
            var   lens            = cinemachineCamera.Lens;
            lens.OrthographicSize = Mathf.Lerp(lens.OrthographicSize, targetSize, zoomSmoothing * Time.deltaTime);
            cinemachineCamera.Lens = lens;
        }
    }

    void SnapToBoat()
    {
        if (boatTransform == null) return;

        // Start locked exactly to the boat so there is no visible settling on frame 1.
        float targetZ = -10f;
        if (cameraTarget != null)
            targetZ = cameraTarget.position.z;

        smoothedPos = boatTransform.position;
        smoothedPos.z = targetZ;

        if (cameraTarget != null)
            cameraTarget.position = smoothedPos;

        float speed = 0f;
        if (boatRb != null)
            speed = boatRb.linearVelocity.magnitude;
        previousSpeed = speed;
        currentPulse = 0f;
        pulseVelocity = 0f;

        if (cinemachineCamera != null)
        {
            // Initialise the lens directly instead of easing from an inherited scene value.
            float targetSize      = Mathf.Lerp(zoomBase, zoomMax, Mathf.Clamp01(speed / zoomFullSpeed));
            var   lens            = cinemachineCamera.Lens;
            lens.OrthographicSize = targetSize;
            cinemachineCamera.Lens = lens;
        }
    }

    void EnsureValidCameraLensSize()
    {
        float safeOrthographicSize = Mathf.Max(0.1f, zoomBase);

        Camera mainCamera = Camera.main;
        if (mainCamera != null && mainCamera.orthographic && mainCamera.orthographicSize <= 0.01f)
        {
            mainCamera.orthographicSize = safeOrthographicSize;
            WarnInvalidLensRepairOnce();
        }

        if (cinemachineCamera != null)
        {
            var lens = cinemachineCamera.Lens;
            if (lens.OrthographicSize <= 0.01f)
            {
                lens.OrthographicSize = safeOrthographicSize;
                cinemachineCamera.Lens = lens;
                WarnInvalidLensRepairOnce();
            }
        }
    }

    void WarnInvalidLensRepairOnce()
    {
        if (warnedInvalidLensSize)
            return;

        warnedInvalidLensSize = true;
        Debug.LogWarning("[CameraController] Repaired an invalid orthographic camera size at runtime.", this);
    }

    float GetCurrentFollowSpeed()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
            return followSpeed;

        // Measure how far the boat has drifted from viewport centre.
        Vector3 viewport = mainCamera.WorldToViewportPoint(boatTransform.position);
        if (!IsFinite(viewport))
            return edgeCatchupSpeed;
        if (viewport.z <= 0f)
            return edgeCatchupSpeed;

        float extent = Mathf.Clamp(safeViewportExtent, 0.01f, 0.49f);
        float band   = Mathf.Max(edgeCatchupBand, 0.001f);

        // "Over" is how far the boat sits outside the safe rectangle.
        float xOver = Mathf.Max(Mathf.Abs(viewport.x - 0.5f) - extent, 0f);
        float yOver = Mathf.Max(Mathf.Abs(viewport.y - 0.5f) - extent, 0f);
        float over  = Mathf.Max(xOver, yOver);
        if (over <= 0f)
            return followSpeed;

        // As the boat approaches the screen edge, ramp smoothly up to the catch-up speed.
        float urgency = Mathf.Clamp01(over / band);
        return Mathf.Lerp(followSpeed, edgeCatchupSpeed, urgency);
    }

    static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
