using UnityEngine;

/// <summary>
/// Spawns a single camera-facing billboard quad that plays the puff flipbook
/// animation once, then self-destructs. One card per poof, not a particle cloud.
/// </summary>
public static class SmokePoof
{
    private static Texture2D s_atlas;
    private static int s_frameCount;
    private static Material s_sharedMat;

    private static void EnsureAtlas()
    {
        if (s_atlas != null) return;
        var frames = FlipbookAtlas.LoadFrames("Particles", "puff_0", 6);
        if (frames != null && frames.Length > 0)
        {
            s_atlas = FlipbookAtlas.Build(frames);
            s_frameCount = frames.Length;

            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Transparent");
            if (shader != null)
            {
                s_sharedMat = new Material(shader);
                s_sharedMat.mainTexture = s_atlas;
                s_sharedMat.SetFloat("_Surface", 1f); // transparent
                s_sharedMat.SetFloat("_Blend", 0f);   // alpha blend
                s_sharedMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                s_sharedMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                s_sharedMat.SetInt("_ZWrite", 0);
                s_sharedMat.renderQueue = 3000;
            }
        }
    }

    public static void Spawn(Vector3 position, float size = 0.15f, Color? color = null)
    {
        EnsureAtlas();
        if (s_atlas == null || s_sharedMat == null) return;

        float smokeScale = VisualScaleSettings.Instance.GetSmokeScale();
        float worldSize = size * smokeScale;
        Color tint = color ?? Color.white;

        var go = new GameObject("SmokePoof");
        go.transform.position = position;

        // Render on the overlay camera so the poof draws on top of everything
        int heldLayer = LayerMask.NameToLayer("HeldItem");
        if (heldLayer >= 0)
            go.layer = heldLayer;

        var player = go.AddComponent<SmokePoofPlayer>();
        player.Init(s_sharedMat, s_atlas, s_frameCount, worldSize, tint);
    }
}

/// <summary>
/// Drives a single billboard quad through the puff flipbook, then destroys itself.
/// Always faces the camera. Attached by SmokePoof.Spawn().
/// </summary>
public class SmokePoofPlayer : MonoBehaviour
{
    private MeshRenderer _renderer;
    private MaterialPropertyBlock _mpb;
    private int _frameCount;
    private float _duration;
    private float _elapsed;
    private float _size;
    private Transform _cam;

    private static Mesh s_quad;

    public void Init(Material sharedMat, Texture2D atlas, int frameCount, float worldSize, Color tint)
    {
        _frameCount = frameCount;
        _size = worldSize;
        _duration = 0.35f; // total playback time
        _elapsed = 0f;

        _mpb = new MaterialPropertyBlock();

        if (s_quad == null)
            s_quad = BuildQuad();

        var mf = gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = s_quad;

        _renderer = gameObject.AddComponent<MeshRenderer>();
        _renderer.sharedMaterial = sharedMat;

        // Set initial UV tile and tint
        _mpb.SetVector("_BaseMap_ST", new Vector4(1f / _frameCount, 1f, 0f, 0f));
        _mpb.SetColor("_BaseColor", tint);
        _renderer.SetPropertyBlock(_mpb);

        transform.localScale = Vector3.one * _size;

        _cam = Camera.main != null ? Camera.main.transform : null;
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        if (_elapsed >= _duration)
        {
            Destroy(gameObject);
            return;
        }

        float t = _elapsed / _duration;

        // Pick frame
        int frame = Mathf.Min(Mathf.FloorToInt(t * _frameCount), _frameCount - 1);
        float offsetX = (float)frame / _frameCount;
        _mpb.SetVector("_BaseMap_ST", new Vector4(1f / _frameCount, 1f, offsetX, 0f));

        // Fade out over last 30%
        float alpha = t > 0.7f ? Mathf.InverseLerp(1f, 0.7f, t) : 1f;
        var col = _mpb.GetColor("_BaseColor");
        col.a = alpha;
        _mpb.SetColor("_BaseColor", col);

        _renderer.SetPropertyBlock(_mpb);

        // Billboard — face camera
        if (_cam != null)
            transform.rotation = _cam.rotation;
    }

    private static Mesh BuildQuad()
    {
        var mesh = new Mesh { name = "PoofQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f),
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        return mesh;
    }
}
