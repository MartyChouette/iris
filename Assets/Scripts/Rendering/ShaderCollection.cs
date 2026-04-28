using System.Collections;
using UnityEngine;

/// <summary>
/// Holds references to all custom shaders so Unity includes them in builds.
/// Shader.Find() only works at runtime if the shader is referenced somewhere —
/// this SO lives in Resources/ to guarantee inclusion.
/// </summary>
[CreateAssetMenu(menuName = "Iris/Shader Collection")]
public class ShaderCollection : ScriptableObject
{
    [Tooltip("All custom shaders that must survive build stripping.")]
    public Shader[] shaders;

    private static ShaderCollection s_instance;

    /// <summary>
    /// Loads the collection from Resources on first access.
    /// Just accessing this property forces Unity to load (and therefore include) the shaders.
    /// </summary>
    public static ShaderCollection Instance
    {
        get
        {
            if (s_instance == null)
                s_instance = Resources.Load<ShaderCollection>("ShaderCollection");
            return s_instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Preload()
    {
        var inst = Instance;
        if (inst == null) return;
        Debug.Log($"[ShaderCollection] Loaded {inst.shaders.Length} shaders — warming across frames.");

        // Create a temporary GO to run the warmup coroutine, then self-destruct
        var go = new GameObject("[ShaderWarmup]");
        Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        var runner = go.AddComponent<ShaderWarmupRunner>();
        runner.StartCoroutine(WarmupRoutine(inst, go));
    }

    private static IEnumerator WarmupRoutine(ShaderCollection collection, GameObject runner)
    {
        // Create a throwaway material for each shader to force compilation,
        // spread across frames so we don't hitch.
        for (int i = 0; i < collection.shaders.Length; i++)
        {
            if (collection.shaders[i] == null) continue;
            var mat = new Material(collection.shaders[i]);
            Object.Destroy(mat);
            yield return null;
        }
        Debug.Log("[ShaderCollection] Warmup complete.");
        Object.Destroy(runner);
    }

    private class ShaderWarmupRunner : MonoBehaviour { }
}
