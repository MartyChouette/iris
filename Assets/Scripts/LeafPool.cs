using System.Collections.Generic;
using UnityEngine;
using Obi;

public class LeafPool : MonoBehaviour
{
    public GameObject leafPrefab;
    public int maxPool = 10;

    readonly Queue<GameObject> _pool = new();

    public GameObject Get()
    {
        if (_pool.Count > 0) { var go = _pool.Dequeue(); go.SetActive(true); return go; }
        if (_pool.Count + 1 > maxPool) return null;
        return Instantiate(leafPrefab);
    }

    public void Return(GameObject go)
    {
        if (!go) return;
        go.SetActive(false);
        go.transform.SetParent(transform, true);
        _pool.Enqueue(go);
    }
}