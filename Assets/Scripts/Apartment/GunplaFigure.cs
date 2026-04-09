using UnityEngine;

/// <summary>
/// One-shot rigidifier for the Gunpla model. The figure was originally built
/// from a tree of cube parts wired together by HingeJoints + per-limb
/// Rigidbodies, which made it flop under physics. This component runs in both
/// Awake and Start (belt-and-suspenders against script execution order) and
/// completely destroys every child HingeJoint AND every child Rigidbody, so
/// the figure becomes a single rigid object owned entirely by the root
/// PlaceableObject. Animator components on any descendant are also disabled.
/// </summary>
public class GunplaFigure : MonoBehaviour
{
    private void Awake() => Rigidify();
    private void Start() => Rigidify();

    private void Rigidify()
    {
        // 1. Disable any Animator on descendants — most important guard against
        //    an unexpected animation component.
        var animators = GetComponentsInChildren<Animator>(includeInactive: true);
        for (int i = 0; i < animators.Length; i++)
            if (animators[i] != null) animators[i].enabled = false;

        // 2. Destroy every HingeJoint (and any other joint type) on descendants.
        //    Joints between two kinematic bodies are inert, but destroying them
        //    avoids PhysX warnings and makes the intent clear.
        var hinges = GetComponentsInChildren<HingeJoint>(includeInactive: true);
        for (int i = 0; i < hinges.Length; i++)
            if (hinges[i] != null) Destroy(hinges[i]);

        var anyJoints = GetComponentsInChildren<Joint>(includeInactive: true);
        for (int i = 0; i < anyJoints.Length; i++)
            if (anyJoints[i] != null) Destroy(anyJoints[i]);

        // 3. Destroy every child Rigidbody. Only the root Rigidbody survives —
        //    it's owned by PlaceableObject and drives pickup/place. Child
        //    bodies being destroyed also cascades any remaining joints away.
        var bodies = GetComponentsInChildren<Rigidbody>(includeInactive: true);
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] == null) continue;
            if (bodies[i].gameObject == gameObject) continue;
            Destroy(bodies[i]);
        }
    }
}
