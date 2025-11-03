using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Obi;

public class SapFxPool : MonoBehaviour
{
    [Header("Choose ONE prefab")]
    public ParticleSystem particlePrefab;
    public ObiEmitter obiEmitterPrefab;

    [Header("Settings")]
    public Transform obiSolverParent;   // required if obiEmitterPrefab is used
    public int maxPool = 10;
    public float defaultLifetime = 1.25f;
    public float obiSpeed = 2.0f;

    readonly Queue<GameObject> _pool = new();

    GameObject GetInstance()
    {
        if (_pool.Count > 0) { var go = _pool.Dequeue(); go.SetActive(true); return go; }

        if (_pool.Count + 1 > maxPool) return null;

        if (particlePrefab)
            return Instantiate(particlePrefab, transform).gameObject;

        if (obiEmitterPrefab)
        {
            if (!obiSolverParent) { Debug.LogError("[SapFxPool] Obi requires solver parent."); return null; }
            var inst = Instantiate(obiEmitterPrefab, obiSolverParent, true);
            inst.enabled = false; // start disabled, we'll enable for a short burst
            return inst.gameObject;
        }

        Debug.LogError("[SapFxPool] Assign a prefab.");
        return null;
    }

    public void Play(Vector3 pos, Vector3 normal, Transform parent = null, float? lifetime = null)
    {
        var go = GetInstance(); if (!go) return;

        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(normal, Vector3.up));
        if (parent) go.transform.SetParent(parent, true);

        if (particlePrefab)
        {
            var ps = go.GetComponent<ParticleSystem>();
            var main = ps.main; main.loop = false;
            ps.Play(true);
            StartCoroutine(ReturnAfter(go, lifetime ?? defaultLifetime));
        }
        else
        {
            var em = go.GetComponent<ObiEmitter>();
            em.transform.SetParent(obiSolverParent, true);
            em.speed = obiSpeed;
            em.enabled = true;           // start emitting
            StartCoroutine(StopObiAfter(em, lifetime ?? defaultLifetime));
        }
    }

    IEnumerator StopObiAfter(ObiEmitter em, float t)
    {
        yield return new WaitForSeconds(t);
        if (em)
        {
            em.enabled = false; // stop emission
            em.gameObject.SetActive(false);
            _pool.Enqueue(em.gameObject);
        }
    }

    IEnumerator ReturnAfter(GameObject go, float t)
    {
        yield return new WaitForSeconds(t);
        if (go)
        {
            go.SetActive(false);
            go.transform.SetParent(transform, true);
            _pool.Enqueue(go);
        }
    }
}
