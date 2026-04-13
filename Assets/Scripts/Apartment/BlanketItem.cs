using System.Collections;
using UnityEngine;

/// <summary>
/// A messy blanket on the floor. When picked up it plays a folding animation
/// (squishes flat then shrinks) and pops out a hidden item (gunpla model).
/// After folding, the blanket becomes a small folded object the player carries.
/// </summary>
public class BlanketItem : MonoBehaviour
{
    [Header("Hidden Item")]
    [Tooltip("Prefab that pops out when the blanket is picked up (e.g. gunpla).")]
    [SerializeField] private GameObject _hiddenItemPrefab;

    [Tooltip("Description shown when the hidden item appears.")]
    [SerializeField] private string _hiddenItemCaption = "Something was under the blanket...";

    [Tooltip("Minimum day before the hidden item can appear (0 = always).")]
    [SerializeField] private int _hiddenItemMinDay;

    [Header("Fold Animation")]
    [Tooltip("Duration of the fold squish (seconds).")]
    [SerializeField] private float _foldDuration = 0.4f;

    [Tooltip("Final folded scale relative to original.")]
    [SerializeField] private Vector3 _foldedScale = new Vector3(0.3f, 0.1f, 0.3f);

    [Header("Audio")]
    [Tooltip("Sound when the blanket folds up.")]
    [SerializeField] private AudioClip _foldSFX;

    [Tooltip("Sound when the hidden item pops out.")]
    [SerializeField] private AudioClip _popSFX;

    private bool _hasBeenPickedUp;
    private Vector3 _originalScale;
    private Coroutine _foldRoutine;

    private void Awake()
    {
        _originalScale = transform.localScale;
    }

    /// <summary>
    /// Called by ObjectGrabber after PlaceableObject.OnPickedUp().
    /// Triggers the fold animation and hidden item spawn.
    /// </summary>
    public void OnBlanketPickedUp()
    {
        if (_hasBeenPickedUp) return;
        _hasBeenPickedUp = true;

        if (_foldRoutine != null) StopCoroutine(_foldRoutine);
        _foldRoutine = StartCoroutine(FoldAndReveal());
    }

    private IEnumerator FoldAndReveal()
    {
        // Play fold SFX
        if (_foldSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(_foldSFX);

        // Fold animation: squish from messy spread to compact folded shape
        Vector3 startScale = _originalScale;
        Vector3 endScale = new Vector3(
            _originalScale.x * _foldedScale.x,
            _originalScale.y * _foldedScale.y,
            _originalScale.z * _foldedScale.z);

        float elapsed = 0f;
        while (elapsed < _foldDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / _foldDuration);
            transform.localScale = Vector3.Lerp(startScale, endScale, t);
            yield return null;
        }
        transform.localScale = endScale;

        // Spawn hidden item
        if (_hiddenItemPrefab != null)
        {
            // Day check
            if (_hiddenItemMinDay > 0 && GameClock.Instance != null
                && GameClock.Instance.CurrentDay < _hiddenItemMinDay)
            {
                _foldRoutine = null;
                yield break;
            }

            // Pop SFX
            if (_popSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(_popSFX);

            // Spawn at blanket's position
            Vector3 spawnPos = transform.position + Vector3.up * 0.05f;
            var item = Instantiate(_hiddenItemPrefab, spawnPos, Quaternion.identity);

            // Frame the discovery (if moments enabled)
            if (ApartmentManager.Instance == null || ApartmentManager.Instance.ItemDiscoveryMoments)
                MomentCamera.FrameTarget(spawnPos, 2f);

            // Caption
            if (!string.IsNullOrEmpty(_hiddenItemCaption))
                CaptionDisplay.Show(_hiddenItemCaption, 3f);

            Debug.Log($"[BlanketItem] Hidden item revealed: {_hiddenItemPrefab.name}");
        }

        _foldRoutine = null;
    }

    /// <summary>Restore to messy state (for new day / reset).</summary>
    public void Unfold()
    {
        _hasBeenPickedUp = false;
        transform.localScale = _originalScale;
    }
}
