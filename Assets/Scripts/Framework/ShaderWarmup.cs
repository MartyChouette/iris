using UnityEngine;

/// <summary>
/// Holds shader references so Unity includes them in builds.
/// Shader.Find only works at runtime if the shader is referenced
/// somewhere in the project. Attach to any always-active GameObject.
/// </summary>
public class ShaderWarmup : MonoBehaviour
{
    [Header("Highlight Shaders")]
    [SerializeField] private Shader highlight;
    [SerializeField] private Shader highlightOutline;
    [SerializeField] private Shader highlightOverlay;
    [SerializeField] private Shader highlightDash;
    [SerializeField] private Shader highlightDouble;
    [SerializeField] private Shader highlightFresnel;
    [SerializeField] private Shader highlightClean;

    [Header("PSX Shaders")]
    [SerializeField] private Shader psxLit;
    [SerializeField] private Shader psxLitGlitch;
    [SerializeField] private Shader psxInteractable;
    [SerializeField] private Shader heldOnTop;

    private void Awake()
    {
        // Warm the shader cache so Shader.Find works reliably in builds.
        // The serialized references guarantee inclusion; touching them here
        // prevents the compiler from stripping them as unused.
        if (highlight == null) highlight = Shader.Find("Iris/Highlight");
        if (highlightOutline == null) highlightOutline = Shader.Find("Iris/HighlightOutline");
        if (highlightOverlay == null) highlightOverlay = Shader.Find("Iris/HighlightOverlay");
        if (highlightDash == null) highlightDash = Shader.Find("Iris/HighlightDash");
        if (highlightDouble == null) highlightDouble = Shader.Find("Iris/HighlightDouble");
        if (highlightFresnel == null) highlightFresnel = Shader.Find("Iris/HighlightFresnel");
        if (highlightClean == null) highlightClean = Shader.Find("Iris/HighlightClean");
        if (psxLit == null) psxLit = Shader.Find("Iris/PSXLit");
        if (psxLitGlitch == null) psxLitGlitch = Shader.Find("Iris/PSXLitGlitch");
        if (psxInteractable == null) psxInteractable = Shader.Find("Iris/PSXInteractable");
        if (heldOnTop == null) heldOnTop = Shader.Find("Iris/HeldOnTop");
    }
}
