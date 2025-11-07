using UnityEngine;

[DisallowMultipleComponent]
public class SapBurstOneShot : MonoBehaviour
{
    public float lifetime = 8f;   // will be overridden from Leaf
    void OnEnable()
    {
        var ps = GetComponent<ParticleSystem>();
        if (ps)
        {
            var main = ps.main;
            main.loop = false;
            ps.Play(true);
        }
       // if (lifetime > 0) Destroy(gameObject, lifetime);
    }
}