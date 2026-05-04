/**
 * @file VirtualStemCutter.cs
 * @brief Non-destructive stem cutting that preserves the original GameObject.
 *
 * @details
 * Intent:
 * - Perform stem cuts WITHOUT destroying the original stem GameObject.
 * - The original Rigidbody, joints, and component references all survive the cut.
 * - Eliminates the need for joint rebinding, suppression windows, or grace timers.
 *
 * How it works:
 * 1. Uses DMC's MeshCutting to compute the two mesh halves (geometry only).
 * 2. Identifies which half is the "keeper" (connected to the crown/anchor).
 * 3. Swaps the original stem's MeshFilter.mesh with the keeper half.
 * 4. Updates the original stem's MeshCollider to match.
 * 5. Spawns a cosmetic-only falling piece from the other half.
 * 6. The original stem retains ALL joints, Rigidbody, and game logic.
 *
 * What this eliminates:
 * - FlowerJointRebinder (no joints to rebind)
 * - XYTetherJoint.SetCutBreakSuppressed (no physics disruption)
 * - JointCutSuppressor (no Unity joints at risk)
 * - session.suppressDetachEvents race conditions
 * - The entire "keeper vs falling piece" selection + component copy pipeline
 *
 * @ingroup flowers_runtime
 */

using UnityEngine;
using DynamicMeshCutter;

public class VirtualStemCutter : MonoBehaviour
{
    [Header("Cut Face")]
    [Tooltip("Material applied to the exposed cross-section of the cut. If null, uses DMC default.")]
    public Material cutFaceMaterial;

    [Header("Falling Piece")]
    [Tooltip("Separation offset (world units) between kept and falling pieces to avoid z-fighting.")]
    public float separation = 0.02f;

    [Tooltip("Time (seconds) before falling pieces auto-destroy. 0 = rely on OffScreenDespawner only.")]
    public float fallingPieceLifetime = 10f;

    [Header("Debug")]
    public bool debugLogs = true;

    // ----------------------------------------------------------------
    // Public API
    // ----------------------------------------------------------------

    /// <summary>
    /// Perform a virtual cut on the given stem MeshTarget.
    /// The original GameObject is kept alive; its mesh is replaced with the "keeper" half.
    /// A cosmetic falling piece is spawned from the other half.
    /// Returns true if the cut succeeded.
    /// </summary>
    public bool PerformVirtualCut(
        MeshTarget target,
        FlowerStemRuntime stem,
        Vector3 worldPoint,
        Vector3 worldNormal,
        Material defaultMaterial = null)
    {
        if (target == null || stem == null)
        {
            if (debugLogs) Debug.LogWarning("[VirtualStemCutter] Null target or stem.", this);
            return false;
        }

        // ----- 1. Build DMC plane in the target's local space -----
        Matrix4x4 w2l = target.transform.worldToLocalMatrix;

        // Handle RequireLocal (skinned mesh scale correction)
        if (target.RequireLocal)
        {
            Matrix4x4 scalingMatrix = Matrix4x4.Scale(target.transform.lossyScale);
            w2l = scalingMatrix * w2l;
        }

        Vector3 localP = w2l.MultiplyPoint3x4(worldPoint);

        // Normal needs the inverse-transpose for non-uniform scale
        Matrix4x4 w2lNormal = new Matrix4x4();
        for (int i = 0; i < 4; i++)
        {
            var column = w2l.GetColumn(i);
            if (i == 3) column = new Vector4(0, 0, 0, 1f);
            w2lNormal.SetColumn(i, column);
        }
        w2lNormal = w2lNormal.inverse.transpose;
        Vector3 localN = ((Vector3)(w2lNormal * (Vector4)worldNormal)).normalized;

        VirtualPlane plane = new VirtualPlane(localP, localN, worldPoint, worldNormal);

        // ----- 2. Run DMC mesh cutting (synchronous, geometry only) -----
        Info info = new Info(target, plane, null, null, null);

        MeshCutting meshCutting = new MeshCutting();
        VirtualMesh[] halves = meshCutting.Cut(ref info);

        if (halves == null || halves.Length < 2)
        {
            if (debugLogs) Debug.LogWarning("[VirtualStemCutter] Mesh cut produced fewer than 2 halves.", this);
            return false;
        }

        info.CreatedMeshes = halves;

        // ----- 3. Determine keeper vs falling -----
        // Keeper = the half whose center is closest to the stem anchor (crown end).
        // This is the piece that stays in the player's hand.
        Vector3 anchorPos = stem.StemAnchor != null
            ? stem.StemAnchor.position
            : stem.transform.position;

        int keeperIdx = 0;
        float bestDist = float.MaxValue;

        for (int i = 0; i < halves.Length; i++)
        {
            Vector3 localCenter = ComputeMeshCenter(halves[i]);
            Vector3 worldCenter = target.transform.TransformPoint(localCenter);
            float dist = (worldCenter - anchorPos).sqrMagnitude;

            if (dist < bestDist)
            {
                bestDist = dist;
                keeperIdx = i;
            }

            if (debugLogs)
                Debug.Log($"[VirtualStemCutter] Half[{i}] worldCenter={worldCenter}, distToAnchor={Mathf.Sqrt(dist):F3}", this);
        }

        if (debugLogs)
            Debug.Log($"[VirtualStemCutter] Keeper = half[{keeperIdx}]", this);

        // ----- 4. Build Unity meshes -----
        Material[] originalMaterials = GetMaterials(target);
        Material faceMat = cutFaceMaterial != null ? cutFaceMaterial
                         : (target.FaceMaterial != null ? target.FaceMaterial : defaultMaterial);

        // Include the face material as the last submesh material
        Material[] materialsWithFace = new Material[originalMaterials.Length + 1];
        originalMaterials.CopyTo(materialsWithFace, 0);
        materialsWithFace[materialsWithFace.Length - 1] = faceMat;

        Mesh keeperMesh = VirtualMeshToUnityMesh(halves[keeperIdx], "VirtualCut_Keeper");

        // ----- 5. Swap original stem's mesh with the keeper half -----
        MeshFilter mf = target.GetComponent<MeshFilter>();
        if (mf != null)
        {
            mf.mesh = keeperMesh;
        }

        MeshRenderer mr = target.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.materials = materialsWithFace;
        }

        // ----- 6. Update collider on the original -----
        RebuildCollider(target.gameObject, keeperMesh);

        // If there's a separate GameobjectRoot (physics root), update its colliders too
        if (target.GameobjectRoot != null && target.GameobjectRoot != target.gameObject)
        {
            // The root may have its own MeshCollider that needs updating
            RebuildCollider(target.GameobjectRoot, keeperMesh);
        }

        // ----- 7. Spawn falling piece(s) -----
        for (int i = 0; i < halves.Length; i++)
        {
            if (i == keeperIdx) continue;

            Mesh fallingMesh = VirtualMeshToUnityMesh(halves[i], $"VirtualCut_Falling_{i}");
            SpawnFallingPiece(fallingMesh, materialsWithFace, target.transform, worldPoint, worldNormal);
        }

        // ----- 8. Notify stem runtime -----
        stem.ApplyCutFromPlane(worldPoint, worldNormal);

        // ----- 9. Release leaves on the falling side of the cut -----
        ReleaseLeavesBelowCut(stem, worldPoint, worldNormal);

        // ----- 10. Juice: camera nudge + vignette pulse -----
        CameraNudge.Nudge(0.4f);
        VignettePulse.Pulse();

        if (debugLogs)
        {
            float angle = stem.GetCurrentCutAngleDeg(Vector3.up);
            float len = stem.CurrentLength;
            Debug.Log($"[VirtualStemCutter] Virtual cut complete. angle={angle:F1}, length={len:F3}. Original stem preserved.", stem);
        }

        return true;
    }

    /// <summary>
    /// After a virtual stem cut, release any leaves whose attachment points
    /// are on the falling side of the cut plane.
    /// </summary>
    private void ReleaseLeavesBelowCut(FlowerStemRuntime stem, Vector3 cutPoint, Vector3 cutNormal)
    {
        Vector3 anchorPos = stem.StemAnchor != null
            ? stem.StemAnchor.position : stem.transform.position;
        float anchorSide = Vector3.Dot(anchorPos - cutPoint, cutNormal);

        // Degenerate cut right at the anchor — skip
        if (Mathf.Abs(anchorSide) < 0.001f) return;

        var markers = stem.transform.root.GetComponentsInChildren<LeafAttachmentMarker>();

        foreach (var marker in markers)
        {
            if (marker.owningLeaf == null || !marker.owningLeaf.isAttached) continue;

            float leafSide = Vector3.Dot(marker.transform.position - cutPoint, cutNormal);

            // Same side as crown anchor → keep attached
            if ((anchorSide * leafSide) >= 0f) continue;

            // Leaf is on the falling side → release it
            var tether = marker.owningLeaf.xyJoint;
            if (tether != null)
                tether.ForceBreak("Stem cut below attachment", isAuthoredPhysics: true);
            else
                marker.owningLeaf.MarkDetached("Stem cut below attachment",
                    FlowerPartRuntime.DetachReason.PlayerCut, permanent: true);

            if (debugLogs)
                Debug.Log($"[VirtualStemCutter] Released leaf '{marker.owningLeaf.PartId}' — below cut plane", marker);
        }
    }

    // ----------------------------------------------------------------
    // Internal helpers
    // ----------------------------------------------------------------

    private static Vector3 ComputeMeshCenter(VirtualMesh vm)
    {
        if (vm.Vertices == null || vm.Vertices.Length == 0)
            return Vector3.zero;

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < vm.Vertices.Length; i++)
            sum += vm.Vertices[i];

        return sum / vm.Vertices.Length;
    }

    private static Mesh VirtualMeshToUnityMesh(VirtualMesh vm, string meshName = "VirtualCutMesh")
    {
        Mesh mesh = new Mesh();
        mesh.name = meshName;
        mesh.vertices = vm.Vertices;
        mesh.triangles = vm.Triangles;
        mesh.normals = vm.Normals;
        mesh.uv = vm.UVs;
        mesh.subMeshCount = vm.SubMeshCount;

        for (int j = 0; j < vm.SubMeshCount; j++)
        {
            mesh.SetIndices(vm.GetIndices(j), MeshTopology.Triangles, j);
        }

        mesh.RecalculateBounds();
        return mesh;
    }

    private void RebuildCollider(GameObject go, Mesh newMesh)
    {
        var mc = go.GetComponent<MeshCollider>();
        if (mc != null)
        {
            // Force rebuild by clearing first
            mc.sharedMesh = null;
            mc.sharedMesh = newMesh;
            // Ensure convex for Rigidbody compatibility
            mc.convex = true;
            return;
        }

        // Fallback: update BoxCollider if present
        var bc = go.GetComponent<BoxCollider>();
        if (bc != null)
        {
            bc.center = newMesh.bounds.center;
            bc.size = newMesh.bounds.size;
            return;
        }

        // Fallback: update CapsuleCollider if present
        var cc = go.GetComponent<CapsuleCollider>();
        if (cc != null)
        {
            cc.center = newMesh.bounds.center;
            cc.height = newMesh.bounds.size.y;
            cc.radius = Mathf.Max(newMesh.bounds.size.x, newMesh.bounds.size.z) * 0.5f;
        }
    }

    private void SpawnFallingPiece(
        Mesh mesh,
        Material[] materials,
        Transform originalTransform,
        Vector3 cutPoint,
        Vector3 cutNormal)
    {
        var go = new GameObject("FallingStemPiece");
        go.transform.position = originalTransform.position;
        go.transform.rotation = originalTransform.rotation;
        go.transform.localScale = originalTransform.lossyScale;

        // Offset slightly along cut normal for visual separation
        go.transform.position -= cutNormal.normalized * separation;

        // Visual
        var mf = go.AddComponent<MeshFilter>();
        mf.mesh = mesh;

        var mr = go.AddComponent<MeshRenderer>();
        mr.materials = materials;

        // Collider (convex for Rigidbody)
        var mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.convex = true;

        // Physics
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // Auto-cleanup
        var despawner = go.AddComponent<OffScreenDespawner>();
        if (fallingPieceLifetime > 0f)
        {
            Destroy(go, fallingPieceLifetime);
        }

        if (debugLogs)
            Debug.Log($"[VirtualStemCutter] Spawned falling piece at {go.transform.position}", go);
    }

    private static Material[] GetMaterials(MeshTarget target)
    {
        var renderer = target.GetComponent<MeshRenderer>();
        if (renderer != null) return renderer.sharedMaterials;

        var skinnedRenderer = target.GetComponent<SkinnedMeshRenderer>();
        if (skinnedRenderer != null) return skinnedRenderer.sharedMaterials;

        return new Material[0];
    }
}
