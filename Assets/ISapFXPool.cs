using UnityEngine;


public class SapBurstPoolAdapter : MonoBehaviour
{
    public MonoBehaviour poolObject; // drag the component that implements ISapFxPool
    ISapFxPool _pool;

    void Awake() { _pool = poolObject as ISapFxPool; }

    public void Burst(Vector3 pos, Vector3 normal)
    {
        if (_pool == null) return;
        _pool.SpawnBurst(pos, (normal == Vector3.zero ? Vector3.up : normal).normalized);
    }
}


public interface ISapFxPool
{
    void SpawnBurst(Vector3 pos, Vector3 dir); // adapt to your real API
}