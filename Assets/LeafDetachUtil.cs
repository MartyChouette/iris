using UnityEngine;

public static class LeafDetachUtil
{
#if OBI_PRESENT
    // Replace these types with the ones you actually use in your project.
    // The screenshot shows "Obi Pinhole". In Obi 7 that’s usually a Pin/Attachment.
    // Try these common ones; keep the ones that exist in your project:
    static void KillPinsTargeting(Transform leaf)
    {
        // Obi "Pinhole"
        foreach (var pin in Object.FindObjectsByType<ObiPinhole>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (pin && pin.target == leaf) Object.Destroy(pin);

        // Obi Particle Attachment
        foreach (var att in Object.FindObjectsByType<ObiParticleAttachment>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (att && att.target == leaf) Object.Destroy(att);

        // Obi Pin Constraints component that points to this leaf
        foreach (var pinAtt in Object.FindObjectsByType<ObiPinConstraints>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (pinAtt && pinAtt.target == leaf) Object.Destroy(pinAtt);
    }
#endif

    public static void DetachAllFor(Transform leaf)
    {
#if OBI_PRESENT
        KillPinsTargeting(leaf);
#endif
    }
}