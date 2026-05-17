/**
 * @file JellyMesh.cs
 * @brief JellyMesh script.
 * @details
 * - Auto-generated Doxygen header. Expand @details with intent, invariants, and perf notes as needed.
*
 * @ingroup tools
 */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
/**
 * @class JellyMesh
 * @brief JellyMesh component.
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

public class JellyMesh : MonoBehaviour
{
    public float Intensity = 1f;
    public float Mass = 1f;
    public float stiffness = 1f;
    public float damping = 0.75f;
    private Mesh OriginalMesh, MeshClone;
    private MeshRenderer renderer;
    private JellyVertex[] jv;
    private Vector3[] vertexArray;
    private Vector3[] _cachedOriginalVerts;

    void Start()
    {
        OriginalMesh = GetComponent<MeshFilter>().sharedMesh;
        MeshClone = Instantiate(OriginalMesh);
        GetComponent<MeshFilter>().sharedMesh = MeshClone;
        renderer = GetComponent<MeshRenderer>();
        _cachedOriginalVerts = OriginalMesh.vertices; // cache once — no per-frame alloc
        vertexArray = new Vector3[_cachedOriginalVerts.Length];
        jv = new JellyVertex[_cachedOriginalVerts.Length];
        for (int i = 0; i < _cachedOriginalVerts.Length; i++)
            jv[i] = new JellyVertex(i, transform.TransformPoint(_cachedOriginalVerts[i]));

    }

    void OnDestroy()
    {
        if (MeshClone != null) Destroy(MeshClone);
    }

    void FixedUpdate()
    {
        System.Array.Copy(_cachedOriginalVerts, vertexArray, _cachedOriginalVerts.Length);
        var bounds = renderer.bounds; // cache bounds once per tick
        float boundsMaxY = bounds.max.y;
        float boundsSizeY = bounds.size.y;
        for (int i = 0; i < jv.Length; i++)
        {
            Vector3 target = transform.TransformPoint(vertexArray[jv[i].ID]);
            float intensity = (1 - (boundsMaxY - target.y) / boundsSizeY) * Intensity;
            jv[i].Shake(target, Mass, stiffness, damping);
            target = transform.InverseTransformPoint(jv[i].Position);
            vertexArray[jv[i].ID] = Vector3.Lerp(vertexArray[jv[i].ID], target, intensity);
        }
        MeshClone.vertices = vertexArray;
    }
    /**
     * @class JellyVertex
     * @brief JellyVertex component.
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

    public class JellyVertex
    {
        public int ID;
        public Vector3 Position;
        public Vector3 velocity, Force;

        public JellyVertex(int _id, Vector3 _pos)
        {
            ID = _id;
            Position = _pos;
        }

        public void Shake(Vector3 target, float m, float s, float d)
        {
            Force = (target - Position) * s;
            velocity = (velocity + Force / m) * d;
            Position += velocity;
            if ((velocity + Force + Force / m).magnitude < 0.001f)
                Position = target;
        }
    }

}
