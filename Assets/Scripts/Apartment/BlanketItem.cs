using System.Collections;
using UnityEngine;

/// <summary>
/// Any object that hides something underneath — blankets, cushions, pillows, rugs.
/// When picked up, optionally plays a fold animation and reveals hidden items.
/// Hidden items can be prefabs (spawned) or scene objects (activated).
/// </summary>
public class BlanketItem : MonoBehaviour
{
    [Header("Hidden Items — Prefabs (spawned on reveal)")]
    [Tooltip("Prefab(s) that pop out when this object is picked up.")]
    [SerializeField] private GameObject[] _hiddenItemPrefabs;

    [Header("Hidden Items — Scene Objects (activated on reveal)")]
    [Tooltip("Existing scene objects to activate when picked up (disabled in scene, enabled on reveal).")]
    [SerializeField] private GameObject[] _hiddenSceneObjects;

    [Header("Reveal Settings")]
    [Tooltip("Description shown when hidden items appear.")]
    [SerializeField] private string _hiddenItemCaption = "Something was underneath...";

    [Tooltip("Minimum day before hidden items can appear (0 = always).")]
    [SerializeField] private int _hiddenItemMinDay;

    [Header("Fold Animation")]
    [Tooltip("Play a fold/squish animation when picked up (blankets). Disable for cushions that just lift.")]
    [SerializeField] private bool _playFoldAnimation = true;

    [Tooltip("Duration of the fold squish (seconds).")]
    [SerializeField] private float _foldDuration = 0.4f;

    [Tooltip("Final folded scale relative to original.")]
    [SerializeField] private Vector3 _foldedScale = new Vector3(0.3f, 0.1f, 0.3f);

    [Header("Audio")]
    [Tooltip("Sound when the object is picked up / folds.")]
    [SerializeField] private AudioClip _foldSFX;

    [Tooltip("Sound when hidden items pop out.")]
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
    /// Triggers the fold animation and hidden item reveal.
    /// </summary>
    public void OnBlanketPickedUp()
    {
        if (_hasBeenPickedUp) return;
        _hasBeenPickedUp = true;

        if (_foldRoutine != null) StopCoroutine(_foldRoutine);
        _foldRoutine = StartCoroutine(FoldAndReveal());
    }

    private bool HasHiddenItems =>
        (_hiddenItemPrefabs != null && _hiddenItemPrefabs.Length > 0)
        || (_hiddenSceneObjects != null && _hiddenSceneObjects.Length > 0);

    private IEnumerator FoldAndReveal()
    {
        if (_foldSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(_foldSFX);

        // Fold animation (blankets squish down; cushions skip this)
        if (_playFoldAnimation)
        {
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
        }

        // Day check
        if (_hiddenItemMinDay > 0 && GameClock.Instance != null
            && GameClock.Instance.CurrentDay < _hiddenItemMinDay)
        {
            _foldRoutine = null;
            yield break;
        }

        if (!HasHiddenItems)
        {
            _foldRoutine = null;
            yield break;
        }

        // Pop SFX
        if (_popSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(_popSFX);

        Vector3 spawnPos = transform.position + Vector3.up * 0.05f;

        SmokePoof.Spawn(spawnPos);

        // Activate scene objects
        if (_hiddenSceneObjects != null)
        {
            for (int i = 0; i < _hiddenSceneObjects.Length; i++)
            {
                if (_hiddenSceneObjects[i] == null) continue;
                _hiddenSceneObjects[i].SetActive(true);
                Debug.Log($"[BlanketItem] Revealed scene object: {_hiddenSceneObjects[i].name}");
            }
        }

        // Spawn prefabs
        if (_hiddenItemPrefabs != null)
        {
            for (int i = 0; i < _hiddenItemPrefabs.Length; i++)
            {
                if (_hiddenItemPrefabs[i] == null) continue;
                Instantiate(_hiddenItemPrefabs[i], spawnPos + Vector3.right * (i * 0.1f), Quaternion.identity);
                Debug.Log($"[BlanketItem] Spawned hidden item: {_hiddenItemPrefabs[i].name}");
            }
        }

        if (!string.IsNullOrEmpty(_hiddenItemCaption))
            CaptionDisplay.Show(_hiddenItemCaption, 3f);

        _foldRoutine = null;
    }

    /// <summary>Restore to messy state (for new day / reset).</summary>
    public void Unfold()
    {
        _hasBeenPickedUp = false;
        transform.localScale = _originalScale;

        // Re-hide scene objects
        if (_hiddenSceneObjects != null)
        {
            for (int i = 0; i < _hiddenSceneObjects.Length; i++)
            {
                if (_hiddenSceneObjects[i] != null)
                    _hiddenSceneObjects[i].SetActive(false);
            }
        }
    }
}
