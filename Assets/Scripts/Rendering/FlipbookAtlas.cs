using UnityEngine;

/// <summary>Builds a horizontal texture atlas from individual frame Texture2Ds at runtime.</summary>
public static class FlipbookAtlas
{
    public static Texture2D Build(Texture2D[] frames)
    {
        if (frames == null || frames.Length == 0) return null;

        int frameW = frames[0].width;
        int frameH = frames[0].height;
        int count = frames.Length;

        var atlas = new Texture2D(frameW * count, frameH, TextureFormat.RGBA32, false);
        atlas.filterMode = FilterMode.Bilinear;

        var rt = RenderTexture.GetTemporary(frameW, frameH, 0, RenderTextureFormat.ARGB32);
        var prev = RenderTexture.active;

        for (int i = 0; i < count; i++)
        {
            Graphics.Blit(frames[i], rt);
            RenderTexture.active = rt;

            var tmp = new Texture2D(frameW, frameH, TextureFormat.RGBA32, false);
            tmp.ReadPixels(new Rect(0, 0, frameW, frameH), 0, 0);
            tmp.Apply();

            atlas.SetPixels(i * frameW, 0, frameW, frameH, tmp.GetPixels());
            Object.Destroy(tmp);
        }

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        atlas.Apply();
        return atlas;
    }

    public static Texture2D[] LoadFrames(string resourcePath, string prefix, int count)
    {
        var list = new System.Collections.Generic.List<Texture2D>(count);
        for (int i = 1; i <= count; i++)
        {
            var tex = Resources.Load<Texture2D>($"{resourcePath}/{prefix}{i}");
            if (tex != null) list.Add(tex);
        }
        return list.ToArray();
    }
}
