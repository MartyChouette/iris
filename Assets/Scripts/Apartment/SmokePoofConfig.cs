using UnityEngine;

/// <summary>
/// Drop-in config for SmokePoof particle art. Place on any always-active
/// GameObject (e.g. ApartmentManager). Assigns the sprite on Awake.
/// Falls back to procedural blob if no sprite is set.
/// </summary>
public class SmokePoofConfig : MonoBehaviour
{
    [Tooltip("Custom smoke particle sprite (PNG, transparent bg). Leave empty for procedural fallback.")]
    [SerializeField] private Texture2D _smokeParticleSprite;

    private void Awake()
    {
        if (_smokeParticleSprite != null)
            SmokePoof.ParticleSprite = _smokeParticleSprite;
    }
}
