/**
 * @file GrabPull.cs
 * @brief GrabPull script.
 * @details
 * - Auto-generated Doxygen header. Expand @details with intent, invariants, and perf notes as needed.
*
 * @ingroup tools
 */

using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
/**
 * @class GrabPull
 * @brief GrabPull component.
 * @details
 * Responsibilities:
 * - (Documented) See fields and methods below.
 *
 * Unity lifecycle:
 * - Awake(): cache references / validate setup.
 * - OnEnable()/OnDisable(): hook/unhook events.
 * - Update(): per-frame behavior (if any).
 *
 * Gotchas:
 * - Keep hot paths allocation-free (Update/cuts/spawns).
 * - Prefer event-driven UI updates over per-frame string building.
 *
 * @ingroup tools
 */
public class GrabPull : MonoBehaviour
{
    public Camera cam;
    public KeyCode grabKey = KeyCode.Mouse0;
    public float grabSpring = 120f;      // pull strength
    public float grabDamper = 18f;       // oppose overshoot
    public float maxAccel = 60f;         // safety cap
    public float maxSpeed = 12f;

    [Header("Grab SFX")]
    public AudioClip leafGrabPrimary;
    public AudioClip leafGrabSecondary;
    public float leafGrabSecondaryDelay = 0.06f;

    public AudioClip petalGrabPrimary;
    public AudioClip petalGrabSecondary;
    public float petalGrabSecondaryDelay = 0.06f;

    [Tooltip("Optional generic grab SFX if we can't classify the part.")]
    public AudioClip genericGrabPrimary;
    public AudioClip genericGrabSecondary;
    public float genericGrabSecondaryDelay = 0.05f;

    Rigidbody rb;
    public bool grabbing;
    Vector3 grabWorld;

    // who we mark as "engaged" while grabbing
    private InteractionEngagement currentEngagement;   // optional

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    /// <summary>Returns cached camera, refreshed when disabled/destroyed.</summary>
    private Camera ActiveCam
    {
        get
        {
            if (cam != null && cam.enabled) return cam;
            cam = Camera.main;
            return cam;
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(grabKey))
        {
            if (ActiveCam == null) return;
            var ray = ActiveCam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit) && hit.rigidbody == rb)
            {
                grabbing = true;
                grabWorld = hit.point; // start at hit

                // mark this object (or its parent) as engaged
                currentEngagement = hit.rigidbody.GetComponentInParent<InteractionEngagement>();
                if (currentEngagement != null)
                    currentEngagement.isEngaged = true;

                // play grab SFX based on what kind of part we grabbed
                PlayGrabSFX(hit.collider);
            }
        }

        if (Input.GetKeyUp(grabKey))
        {
            if (grabbing)
            {
                grabbing = false;

                // clear engagement on release
                if (currentEngagement != null)
                {
                    currentEngagement.isEngaged = false;
                    currentEngagement = null;
                }
            }
        }
    }

    void FixedUpdate()
    {
        if (!grabbing) return;
        
        // CRITICAL: Check if Rigidbody still exists (might be destroyed during cut)
        if (rb == null)
        {
            grabbing = false;
            return;
        }

        // project cursor onto object plane for a stable target
        if (ActiveCam == null) { grabbing = false; return; }
        var ray = ActiveCam.ScreenPointToRay(Input.mousePosition);
        var plane = new Plane(-cam.transform.forward, rb.worldCenterOfMass);
        if (plane.Raycast(ray, out float enter))
            grabWorld = ray.GetPoint(enter);

        Vector3 toTarget = grabWorld - rb.worldCenterOfMass;
        Vector3 accel = toTarget * grabSpring - rb.linearVelocity * grabDamper;

        if (accel.sqrMagnitude > maxAccel * maxAccel)
            accel = accel.normalized * maxAccel;

        // physics-friendly pull
        rb.AddForce(accel, ForceMode.Acceleration);

        // optional speed cap to avoid tunneling
        if (rb.linearVelocity.sqrMagnitude > maxSpeed * maxSpeed)
            rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed;
    }

    // ─────────────────────────────────────────────
    // Grab SFX helper
    // ─────────────────────────────────────────────

    void PlayGrabSFX(Collider col)
    {
        if (AudioManager.Instance == null || col == null)
            return;

        // prefer FlowerPartRuntime, since we already have that
        FlowerPartRuntime part = col.GetComponentInParent<FlowerPartRuntime>();
        if (part != null)
        {
            switch (part.kind)
            {
                case FlowerPartKind.Leaf:
                    PlayDual(leafGrabPrimary, leafGrabSecondary, leafGrabSecondaryDelay);
                    return;

                case FlowerPartKind.Petal:
                    PlayDual(petalGrabPrimary, petalGrabSecondary, petalGrabSecondaryDelay);
                    return;

                default:
                    break;
            }
        }

        // fallback: tags if no FlowerPartRuntime or unknown kind
        if (col.CompareTag("Leaf"))
        {
            PlayDual(leafGrabPrimary, leafGrabSecondary, leafGrabSecondaryDelay);
        }
        else if (col.CompareTag("Petal"))
        {
            PlayDual(petalGrabPrimary, petalGrabSecondary, petalGrabSecondaryDelay);
        }
        else
        {
            PlayDual(genericGrabPrimary, genericGrabSecondary, genericGrabSecondaryDelay);
        }
    }

    void PlayDual(AudioClip first, AudioClip second, float delay)
    {
        if (AudioManager.Instance == null) return;
        if (first == null && second == null) return;

        AudioManager.Instance.PlayDualSFX(first, second, delay);
    }
}
