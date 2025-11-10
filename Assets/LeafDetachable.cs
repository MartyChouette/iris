using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class LeafDetachable : MonoBehaviour
{
    [Header("State (readonly)")]
    [SerializeField] bool attached = true;   // starts attached
    [SerializeField] bool carrying = false;  // true while player is holding LMB on this leaf

    [Header("Physics")]
    public Vector3 breakImpulse = new(0, 0.8f, 0);      // small kick when it tears
    public Rigidbody stemRigidbody;                     // optional recoil on stem

    [Header("After release")]
    [Tooltip("Seconds after you release the leaf before it despawns.")]
    public float despawnAfterSeconds = 25f;

    [Header("Splatter")]
    [Tooltip("Which layers count as 'ground' for splatter.")]
    public LayerMask groundMask = ~0;
    [Tooltip("Minimum impact speed to splatter.")]
    public float splatterMinSpeed = 1.5f;
    public UnityEvent onSplatter;                       // hook particles/sfx here

    [Header("Debug")]
    public bool log = false;

    // cache
    Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        // While attached we keep kinematic so dragging/followers can move us exactly:
        rb.isKinematic = true;
        rb.detectCollisions = false;
        rb.interpolation = RigidbodyInterpolation.None;
    }

    // ========== Public API called by the grabber ==========

    /// Called when player first presses LMB on this leaf (even while still attached).
    public void BeginHold()
    {
        carrying = true;
        // remain kinematic while in hand
        rb.isKinematic = true;
        rb.detectCollisions = false;
        if (log) Debug.Log($"[Leaf:{name}] BeginHold()");
    }

    /// While held, the grabber will keep sending target world positions.
    public void SetHeldPosition(Vector3 worldPos)
    {
        if (rb.isKinematic) rb.MovePosition(worldPos);
        else transform.position = worldPos; // (safety) should stay kinematic while carrying
    }

    /// Player released LMB.
    public void EndHold()
    {
        if (log) Debug.Log($"[Leaf:{name}] EndHold()");
        carrying = false;

        // Once released, if we are detached we fall to the ground:
        if (!attached)
        {
            rb.isKinematic = false;
            rb.detectCollisions = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            StartCoroutine(DespawnTimer());
        }
    }

    /// Tells the leaf to detach from the stem (delete pins/followers, give physics impulse),
    /// but KEEP it in-hand until EndHold().
    public void TearOff()
    {
        if (!attached) return;
        attached = false;

        // 1) stop any follower scripts that might snap us back
        var follower = GetComponent<MonoBehaviour>(); // replaced below for clarity
        // If you use a specific follower, disable it explicitly:
        // var bespoke = GetComponent<RopeLeafFollower_Bespoke>();
        // if (bespoke) bespoke.enabled = false;

        // 2) delete Obi attachments/pins that target this leaf (optional)
#if OBI_PRESENT
        TryKillObiAttachments();
#endif

        // 3) physics stays kinematic while carried; we’ll enable on release
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.AddForce(breakImpulse, ForceMode.VelocityChange);

        // tiny recoil on the stem (optional)
        if (stemRigidbody)
            stemRigidbody.AddForce(-breakImpulse, ForceMode.VelocityChange);

        if (log) Debug.Log($"[Leaf:{name}] TORN (still carried).");
    }

    // ========== Collisions (for splatter) ==========

    void OnCollisionEnter(Collision c)
    {
        if (attached || carrying) return; // don’t splatter while attached or still in hand
        if (((1 << c.collider.gameObject.layer) & groundMask.value) == 0) return;

        if (c.relativeVelocity.magnitude >= splatterMinSpeed)
        {
            if (log) Debug.Log($"[Leaf:{name}] Splatter on {c.collider.name}");
            onSplatter?.Invoke();
        }
    }

    IEnumerator DespawnTimer()
    {
        yield return new WaitForSeconds(despawnAfterSeconds);
        if (log) Debug.Log($"[Leaf:{name}] Despawn.");
        Destroy(gameObject);
    }

#if OBI_PRESENT
    // Remove pin/pinhole/attachment components that reference this leaf.
    void TryKillObiAttachments()
    {
        // Keep the ones you actually use. Examples:
        foreach (var pinhole in Object.FindObjectsByType<ObiPinhole>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (pinhole && pinhole.target == transform) Destroy(pinhole);

        foreach (var att in Object.FindObjectsByType<ObiParticleAttachment>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (att && att.target == transform) Destroy(att);

        // If you have custom pin components that expose a 'target', handle them here too.
    }
#endif
}
