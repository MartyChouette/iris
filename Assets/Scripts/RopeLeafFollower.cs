using UnityEngine;
using Obi;

[DefaultExecutionOrder(32000)]
public class RopeLeafFollower : MonoBehaviour
{
    public ObiRopeBase rope;
    public int actorIndex = 1;
    public Camera cam;

    [Header("Flower anchors")]
    public Transform flowerHead;        // set to your blossom/head transform in the hierarchy

    [Header("Placement")]
    public float side = +1f;            // +1 right of camera, -1 left
    public float posLerp = 30f;

    [Header("Leaf local axes")]
    public Vector3 localOut = Vector3.right;     // leaf “out from stem”
    public Vector3 localUp = Vector3.forward;   // leaf blade axis base direction

    // cache the unmodified localUp so we don't mutate the serialized value
    private Vector3 _baseLocalUp;

    void Awake()
    {
        if (!cam) cam = Camera.main;
        _baseLocalUp = localUp;
    }

    void LateUpdate()
    {
        if (rope == null || rope.solver == null) return;

        var s = rope.solver;
        var siL = rope.solverIndices;     // actor->solver index list
        var rp = s.renderablePositions;  // solver-space particle positions
        int n = rope.activeParticleCount;

        if (siL == null || rp == null || n < 2) return;

        // --- Find the head-most particle (closest to flowerHead), once per frame (cheap for short ropes) ---
        int headSi = -1;
        if (flowerHead != null)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                int si = siL[i];
                if (si < 0 || si >= rp.count) continue;
                Vector3 p = s.transform.TransformPoint((Vector3)rp[si]);
                float d = (p - flowerHead.position).sqrMagnitude;
                if (d < best) { best = d; headSi = i; }
            }
        }

        // Choose actorIndex but NEVER use the segment that touches the head end.
        // We use segments as (i, i+1). If headSi is known, disallow i == headSi-1.
        actorIndex = Mathf.Clamp(actorIndex, 0, n - 2);
        if (headSi >= 0)
        {
            // If our chosen segment would include the head end, push it away toward the tail.
            if (actorIndex == headSi - 1) actorIndex = Mathf.Clamp(actorIndex - 1, 0, n - 2);
            if (actorIndex >= headSi) actorIndex = Mathf.Clamp(headSi - 2, 0, n - 2);
        }

        if (siL.count <= actorIndex + 1) return;

        int sindex = siL[actorIndex];
        int sj = siL[actorIndex + 1];
        if (sindex < 0 || sj < 0 || sindex >= rp.count || sj >= rp.count) return;

        Vector3 pi = s.transform.TransformPoint((Vector3)rp[sindex]);
        Vector3 pj = s.transform.TransformPoint((Vector3)rp[sj]);

        // Decide which endpoint is "head side" for this segment, then make tangent point AWAY from head.
        // Default: use pj - pi. If we know the head index, flip if needed so tangent points tail-ward.
        Vector3 tangent = (pj - pi);
        if (headSi >= 0)
        {
            // If the head lies closer to pj than pi, then pj is the head side -> we want tangent from head -> tail, so use pi - pj.
            float dHead_pi = (pi - flowerHead.position).sqrMagnitude;
            float dHead_pj = (pj - flowerHead.position).sqrMagnitude;
            bool pjIsHead = dHead_pj < dHead_pi;
            tangent = pjIsHead ? (pi - pj) : (pj - pi);
        }
        tangent = tangent.sqrMagnitude > 1e-6f ? tangent.normalized : Vector3.up;

        // Camera-oriented right projected on the stem plane
        Vector3 viewRight = cam ? cam.transform.right : Vector3.right;
        Vector3 rightProj = Vector3.ProjectOnPlane(viewRight, tangent).normalized;
        if (rightProj.sqrMagnitude < 1e-6f) rightProj = Vector3.Cross(Vector3.up, tangent).normalized;

        Vector3 outDir = rightProj * Mathf.Sign(side);

        // Radial is always 0.5f
        const float radial = 0.5f;
        Vector3 center = (pi + pj) * 0.5f;
        Vector3 targetPos = center + outDir * radial;

        transform.position = Vector3.Lerp(
            transform.position,
            targetPos,
            1f - Mathf.Exp(-posLerp * Time.deltaTime)
        );

        // Flip localUp.y based on side (without mutating serialized localUp)
        float ySign = Mathf.Sign(side) >= 0f ? 1f : -1f;
        Vector3 signedLocalUp = new Vector3(_baseLocalUp.x, Mathf.Abs(_baseLocalUp.y) * ySign, _baseLocalUp.z);

        // Map leaf local axes to (tangent, outDir) while applying the Y-sign flip
        Quaternion baseMap = Quaternion.Inverse(Quaternion.LookRotation(_baseLocalUp, localOut));
        Quaternion world = Quaternion.LookRotation(tangent, outDir);
        Quaternion flipY = Quaternion.Inverse(Quaternion.LookRotation(_baseLocalUp, localOut))
                            * Quaternion.LookRotation(signedLocalUp, localOut);

        transform.rotation = world * baseMap * flipY;
    }
}
