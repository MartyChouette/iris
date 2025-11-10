using UnityEngine;

[ExecuteAlways]
public class LeafGrabViz : MonoBehaviour
{
    public Leaf3D_PullBreak leaf;
    public Color socketColor = new Color(0.2f, 1f, 0.4f, 0.9f);
    public Color handColor = new Color(1f, 0.2f, 0.8f, 0.9f);

    void Reset() { leaf = GetComponent<Leaf3D_PullBreak>(); }

    void OnDrawGizmos()
    {
        if (!leaf) leaf = GetComponent<Leaf3D_PullBreak>();
        if (!leaf) return;

        // socket
        if (leaf.attachSocket)
        {
            Gizmos.color = socketColor;
            Gizmos.DrawWireSphere(leaf.attachSocket.position, 0.2f);
            Gizmos.DrawLine(leaf.attachSocket.position, leaf.attachSocket.position + leaf.attachSocket.up * 0.05f);
        }

        // draw current transform (hand target when grabbed)
        Gizmos.color = handColor;
        Gizmos.DrawWireSphere(leaf.transform.position, 0.01f);
    }
}