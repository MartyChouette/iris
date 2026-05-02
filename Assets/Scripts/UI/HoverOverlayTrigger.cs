using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public class HoverOverlayTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Overlay")]
    [SerializeField] private CanvasGroup overlayGroup;
    [SerializeField] private TMP_Text overlayText;

    [TextArea]
    [SerializeField] private string messageToShow;

    public void OnPointerEnter(PointerEventData eventData)
    {
        overlayText.text = messageToShow;
        ShowOverlay();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        HideOverlay();
    }

    private void ShowOverlay()
    {
        overlayGroup.alpha = 1f;
        overlayGroup.interactable = false;
        overlayGroup.blocksRaycasts = false;
    }

    private void HideOverlay()
    {
        overlayGroup.alpha = 0f;
        overlayGroup.interactable = false;
        overlayGroup.blocksRaycasts = false;
    }
}