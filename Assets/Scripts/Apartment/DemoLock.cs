using System.Collections;
using UnityEngine;

/// <summary>
/// Put on any cabinet door or drawer to lock it in the demo.
/// Player clicks it → door jiggles, plays SFX, shows caption.
/// Disables the DrawerController so it can't actually open.
/// </summary>
public class DemoLock : MonoBehaviour
{
    [Tooltip("SFX played when the player tries to open it.")]
    [SerializeField] private AudioClip _lockedSFX;

    [Tooltip("Caption shown when clicked.")]
    [SerializeField] private string _caption = "Hmm... guess it's locked.";

    [Header("Jiggle")]
    [Tooltip("How far the door jiggles in degrees.")]
    [SerializeField] private float _jiggleAngle = 3f;

    [Tooltip("How many times it jiggles back and forth.")]
    [SerializeField] private int _jiggleCount = 4;

    [Tooltip("Total jiggle duration in seconds.")]
    [SerializeField] private float _jiggleDuration = 0.35f;

    private bool _jiggling;
    private Coroutine _jiggleRoutine;

    private void Awake()
    {
        var drawer = GetComponent<DrawerController>();
        if (drawer != null) drawer.enabled = false;
    }

    /// <summary>Called by ObjectGrabber or click detection when the player clicks this object.</summary>
    public void OnClicked()
    {
        if (_jiggling) return;

        if (_lockedSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(_lockedSFX);

        if (!string.IsNullOrEmpty(_caption))
            CaptionDisplay.Show(_caption, 2f);

        if (_jiggleRoutine != null) StopCoroutine(_jiggleRoutine);
        _jiggleRoutine = StartCoroutine(Jiggle());
    }

    private IEnumerator Jiggle()
    {
        _jiggling = true;
        Quaternion startRot = transform.localRotation;
        float elapsed = 0f;
        float stepDuration = _jiggleDuration / (_jiggleCount * 2f);

        for (int i = 0; i < _jiggleCount * 2; i++)
        {
            float targetAngle = (i % 2 == 0) ? _jiggleAngle : -_jiggleAngle;
            // Decay amplitude each cycle
            float decay = 1f - (float)i / (_jiggleCount * 2f);
            targetAngle *= decay;

            Quaternion targetRot = startRot * Quaternion.Euler(0f, targetAngle, 0f);
            float stepElapsed = 0f;

            while (stepElapsed < stepDuration)
            {
                stepElapsed += Time.deltaTime;
                elapsed += Time.deltaTime;
                float t = stepElapsed / stepDuration;
                transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRot, t);
                yield return null;
            }
        }

        transform.localRotation = startRot;
        _jiggling = false;
        _jiggleRoutine = null;
    }
}
