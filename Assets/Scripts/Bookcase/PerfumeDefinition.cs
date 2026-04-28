using UnityEngine;

[CreateAssetMenu(menuName = "Iris/Perfume Definition")]
public class PerfumeDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Display name of the perfume.")]
    public string perfumeName = "Unnamed Perfume";

    [TextArea(2, 4)]
    [Tooltip("Short description shown during inspection.")]
    public string description = "";

    [Header("Visuals")]
    [Tooltip("Color of the perfume bottle glass.")]
    public Color bottleColor = new Color(0.8f, 0.6f, 0.9f);

    [Tooltip("Color of the spray particle mist.")]
    public Color sprayColor = new Color(0.9f, 0.8f, 1f, 0.5f);

    [Header("Mood")]
    [Tooltip("Target color for the directional light when this perfume is active.")]
    public Color lightingColor = new Color(1f, 0.95f, 0.85f);

    [Tooltip("Target intensity for the directional light.")]
    [Range(0.5f, 2f)]
    public float lightIntensity = 1f;

    [Tooltip("Tint for ambient mood particles.")]
    public Color moodParticleColor = new Color(1f, 0.9f, 0.8f, 0.3f);

    [Header("Type")]
    [Tooltip("Tag identifying this perfume type (e.g. 'floral', 'woody', 'citrus', 'fresh'). Matched against date preferences.")]
    public string perfumeTag = "";

    [Header("Mood Machine")]
    [Tooltip("Mood value this perfume pushes toward (0 = sunny, 1 = stormy). Drives visual atmosphere only.")]
    [Range(0f, 1f)]
    public float moodValue = 0.5f;

    [Header("Audio")]
    [Tooltip("SFX played on spray (optional).")]
    public AudioClip spraySFX;
}
