using UnityEngine;

/// <summary>
/// Manages amber gaze highlight on whatever ReactableTag the date NPC is
/// currently investigating. Lives on the same GO as DateCharacterController.
/// Highlight is active during the Investigating state.
/// </summary>
public class NPCGazeHighlight : MonoBehaviour
{
    private DateCharacterController _controller;
    private ItemHighlight _currentHighlight;

    private void Awake()
    {
        _controller = GetComponent<DateCharacterController>();
    }

    private void Update()
    {
        if (_controller == null) return;

        var state = _controller.CurrentState;
        bool shouldHighlight = state == DateCharacterController.CharState.Investigating;

        ReactableTag target = shouldHighlight ? _controller.CurrentTarget : null;
        ItemHighlight desired = target != null
            ? target.GetComponentInChildren<ItemHighlight>()
            : null;

        if (desired == _currentHighlight) return;

        // Clear old
        if (_currentHighlight != null)
            _currentHighlight.SetGazeHighlighted(false);

        _currentHighlight = desired;

        // Set new
        if (_currentHighlight != null)
            _currentHighlight.SetGazeHighlighted(true);
    }

    private void OnDisable()
    {
        ClearHighlight();
    }

    private void OnDestroy()
    {
        ClearHighlight();
    }

    private void ClearHighlight()
    {
        if (_currentHighlight != null)
        {
            _currentHighlight.SetGazeHighlighted(false);
            _currentHighlight = null;
        }
    }
}
