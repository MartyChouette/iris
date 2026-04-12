using UnityEngine;

/// <summary>
/// Minimal bridge between an album sleeve and its RecordDefinition SO.
/// Lives on the album sleeve root alongside AlbumSleeve. Applies album art
/// to the sleeve mesh. The heavy interaction logic is in AlbumSleeve.
/// </summary>
public class RecordItem : MonoBehaviour
{
    [Header("Record Content")]
    [Tooltip("The record definition (title, artist, audio, mood).")]
    [SerializeField] private RecordDefinition _definition;

    public RecordDefinition Definition => _definition;

    private Material _artMat;

    private void Awake()
    {
        ApplyAlbumArt();
    }

    private void OnDestroy()
    {
        if (_artMat != null)
            Destroy(_artMat);
    }

    /// <summary>
    /// Apply album art texture to the sleeve's renderer, scaled to fit.
    /// Creates an instance material so each sleeve can have its own art.
    /// </summary>
    private void ApplyAlbumArt()
    {
        if (_definition == null || _definition.albumArt == null) return;

        var rend = GetComponent<Renderer>();
        if (rend == null) rend = GetComponentInChildren<Renderer>();
        if (rend == null) return;

        _artMat = new Material(rend.sharedMaterial);
        _artMat.mainTexture = _definition.albumArt;
        _artMat.mainTextureScale = Vector2.one;
        _artMat.mainTextureOffset = Vector2.zero;
        _artMat.color = Color.white;
        rend.material = _artMat;
    }
}
