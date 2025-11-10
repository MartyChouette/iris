using UnityEngine;

public interface LeafTarget
{
    void BeginExternalGrab(Vector3 anchorWorld, Vector3 planeNormal);
    void SetExternalHand(Vector3 handWorld);
    void EndExternalGrab();
    void TearOff();
    Vector3 GetPosition();
}