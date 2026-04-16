using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Results screen shown after a date ends. Displays affection score, grade, and summary.
/// Continue button dismisses the screen so the player can clean up in Evening phase.
/// </summary>
public class DateEndScreen : MonoBehaviour
{
    public static DateEndScreen Instance { get; private set; }
    public bool IsShowing => screenRoot != null && screenRoot.activeSelf;

    [Header("References")]
    [SerializeField] private GameObject screenRoot;
    [SerializeField] private TMP_Text dateNameText;
    [SerializeField] private TMP_Text affectionScoreText;
    [SerializeField] private TMP_Text gradeText;
    [SerializeField] private TMP_Text summaryText;
    [SerializeField] private Button continueButton;

    [Header("Audio")]
    [SerializeField] private AudioClip goodDateSFX;
    [SerializeField] private AudioClip badDateSFX;

    /// <summary>Invoked when the Continue button is clicked (after Show).</summary>
    public event System.Action OnDismissed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[DateEndScreen] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (screenRoot != null)
            screenRoot.SetActive(false);

        if (continueButton != null)
            continueButton.onClick.AddListener(OnContinue);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (continueButton != null)
            continueButton.onClick.RemoveListener(OnContinue);
    }

    /// <summary>Show the date end screen with results.</summary>
    public void Show(DatePersonalDefinition date, float affection, bool failed = false)
    {
        if (screenRoot == null) return;

        screenRoot.SetActive(true);

        string grade = failed ? "F" : ComputeGrade(affection);

        if (dateNameText != null)
            dateNameText.text = date != null ? date.characterName : "???";

        if (affectionScoreText != null)
            affectionScoreText.text = $"{affection:F0}%";

        if (gradeText != null)
        {
            gradeText.text = grade;
            gradeText.color = grade == "F"
                ? new Color(1f, 0.3f, 0.3f)
                : grade switch
                {
                    "S" => new Color(1f, 0.84f, 0f),
                    "A" => new Color(0.4f, 1f, 0.4f),
                    "B" => new Color(0.4f, 0.8f, 1f),
                    "C" => new Color(1f, 0.8f, 0.4f),
                    _ => new Color(1f, 0.4f, 0.4f)
                };
        }

        if (summaryText != null)
        {
            summaryText.text = failed
                ? "They left early..."
                : grade switch
                {
                    "S" => "A perfect connection! They'll definitely call back.",
                    "A" => "A wonderful time together. Very promising!",
                    "B" => "A pleasant evening. Room for improvement.",
                    "C" => "An awkward date. Better luck next time.",
                    _ => "That did not go well at all..."
                };
        }

        // Play SFX
        AudioClip clip = failed ? badDateSFX : (affection >= 60f ? goodDateSFX : badDateSFX);
        if (clip != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(clip);

        Debug.Log($"[DateEndScreen] Grade: {grade} (Affection: {affection:F0}%) Failed: {failed}");
    }

    /// <summary>Compute letter grade from affection score.</summary>
    public static string ComputeGrade(float affection)
    {
        if (affection >= 90f) return "S";
        if (affection >= 75f) return "A";
        if (affection >= 60f) return "B";
        if (affection >= 40f) return "C";
        return "D";
    }

    private void OnContinue()
    {
        Dismiss();
    }

    /// <summary>Hide the end screen. Called by Continue button and sleep reset.</summary>
    public void Dismiss()
    {
        if (screenRoot != null && screenRoot.activeSelf)
        {
            screenRoot.SetActive(false);
            Debug.Log("[DateEndScreen] Dismissed.");
            OnDismissed?.Invoke();
        }
    }
}
