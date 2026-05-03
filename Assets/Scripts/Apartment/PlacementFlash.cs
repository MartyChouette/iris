using System.Collections;
using UnityEngine;

/// <summary>
/// Spawns a flat disc on the ground that flashes green 3 times to signal
/// "a new pickable item appeared here." Self-destructs after the animation.
/// </summary>
public static class PlacementFlash
{
    public static void Spawn(Vector3 worldPos, float diameter = 0.18f)
    {
        var go = new GameObject("PlacementFlash");
        go.transform.position = worldPos + Vector3.up * 0.005f;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        go.transform.localScale = new Vector3(diameter, diameter, 1f);

        var mf = go.AddComponent<MeshFilter>();
        mf.mesh = CreateQuadMesh();

        var mr = go.AddComponent<MeshRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                  ?? Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        mat.SetFloat("_Surface", 1f); // transparent
        mat.SetFloat("_Blend", 0f);
        mat.color = new Color(0.25f, 0.9f, 0.3f, 0f);
        mat.renderQueue = 3100;
        mr.material = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        var runner = go.AddComponent<PlacementFlashRunner>();
        runner.Init(mat);
    }

    private static Mesh CreateQuadMesh()
    {
        var mesh = new Mesh { name = "FlashQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(1, 1), new Vector2(0, 1)
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        return mesh;
    }
}

/// <summary>MonoBehaviour that drives the 3-flash animation and self-destructs.</summary>
public class PlacementFlashRunner : MonoBehaviour
{
    private Material _mat;

    public void Init(Material mat) => _mat = mat;

    private IEnumerator Start()
    {
        if (_mat == null) { Destroy(gameObject); yield break; }

        Color baseColor = new Color(0.25f, 0.9f, 0.3f, 0.75f);

        for (int flash = 0; flash < 3; flash++)
        {
            // Fade in
            float half = 0.15f;
            for (float t = 0f; t < half; t += Time.deltaTime)
            {
                float a = Mathf.Lerp(0f, baseColor.a, t / half);
                _mat.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
                yield return null;
            }
            // Fade out
            for (float t = 0f; t < half; t += Time.deltaTime)
            {
                float a = Mathf.Lerp(baseColor.a, 0f, t / half);
                _mat.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
                yield return null;
            }
            _mat.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
            yield return new WaitForSeconds(0.08f);
        }

        Destroy(_mat);
        Destroy(gameObject);
    }
}
