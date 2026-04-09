using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Controls a date NPC. Stationary per-phase models toggled by DateSessionManager.
/// Evaluates nearby ReactableTags via seated excursions (no walking).
/// </summary>
public class DateCharacterController : MonoBehaviour
{
    public enum CharState { Idle, Sitting, Investigating, Dismissed }

    // ──────────────────────────────────────────────────────────────
    // Configuration
    // ──────────────────────────────────────────────────────────────
    [Header("Investigation")]
    [Tooltip("Seconds spent investigating an object before returning to sitting.")]
    [SerializeField] private float investigateDuration = 3f;

    [Header("Excursions")]
    [Tooltip("Seconds sitting before considering an excursion.")]
    [SerializeField] private float excursionInterval = 4f;

    [Tooltip("Chance of investigating each excursion check (0-1).")]
    [Range(0f, 1f)]
    [SerializeField] private float excursionChance = 0.9f;

    [Header("Audio")]
    [Tooltip("SFX played when the NPC starts investigating an item.")]
    [SerializeField] private AudioClip investigateSFX;

    [Tooltip("SFX played when the NPC reacts to an item.")]
    [SerializeField] private AudioClip reactionSFX;

    // ──────────────────────────────────────────────────────────────
    // Events
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired when the character reacts to a ReactableTag. DateSessionManager subscribes.
    /// Args: (tag, reactionType, displayName)
    /// </summary>
    public event Action<ReactableTag, ReactionType, string> OnReaction;

    // ──────────────────────────────────────────────────────────────
    // Runtime state
    // ──────────────────────────────────────────────────────────────
    private NavMeshAgent _agent;
    private CharState _state = CharState.Idle;
    private ReactableTag _currentTarget;
    private float _sitTimer;
    private float _investigateTimer;
    private bool _excursionsEnabled;

    public CharState CurrentState => _state;
    public ReactableTag CurrentTarget => _currentTarget;

    // ──────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────

    /// <summary>Initialize the character at a position.</summary>
    public void Initialize(Vector3 position)
    {
        _agent = GetComponent<NavMeshAgent>();
        if (_agent != null)
        {
            _agent.enabled = false;
            transform.position = position;
            _agent.enabled = true;

            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                _agent.Warp(hit.position);
        }
        else
        {
            transform.position = position;
        }

        _state = CharState.Idle;
        Debug.Log($"[DateCharacterController] Initialized at {position}");
    }

    /// <summary>Teleport the NPC to a new position.</summary>
    public void WarpTo(Vector3 position)
    {
        if (_agent == null) _agent = GetComponent<NavMeshAgent>();

        if (_agent != null)
        {
            _agent.enabled = false;
            transform.position = position;
            _agent.enabled = true;

            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                _agent.Warp(hit.position);
        }
        else
        {
            transform.position = position;
        }

        _currentTarget = null;
        Debug.Log($"[DateCharacterController] Warped to {position}");
    }

    /// <summary>Set the NPC to sitting/idle state.</summary>
    public void SetSitting()
    {
        _state = CharState.Sitting;
        _sitTimer = 0f;
        _currentTarget = null;
        if (_agent != null && _agent.isOnNavMesh)
            _agent.ResetPath();
    }

    /// <summary>Allow the character to start evaluating ReactableTags from their seat.</summary>
    public void EnableExcursions()
    {
        _excursionsEnabled = true;
        _sitTimer = 0f;
        Debug.Log("[DateCharacterController] Excursions enabled.");
    }

    /// <summary>Stop the character from starting new excursions.</summary>
    public void DisableExcursions()
    {
        _excursionsEnabled = false;
        Debug.Log("[DateCharacterController] Excursions disabled.");
    }

    /// <summary>Force the character to investigate a specific target (e.g. drink delivery).</summary>
    public void InvestigateSpecific(Transform target)
    {
        if (_state == CharState.Dismissed) return;

        _currentTarget = target.GetComponent<ReactableTag>();
        _state = CharState.Investigating;
        _investigateTimer = 0f;

        if (investigateSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(investigateSFX);
    }

    /// <summary>Mark the character as dismissed.</summary>
    public void Dismiss()
    {
        _state = CharState.Dismissed;
        _excursionsEnabled = false;
        _currentTarget = null;
        Debug.Log("[DateCharacterController] Dismissed.");
    }

    // ──────────────────────────────────────────────────────────────
    // Update
    // ──────────────────────────────────────────────────────────────

    private void Update()
    {
        switch (_state)
        {
            case CharState.Sitting:
                if (!_excursionsEnabled) break;
                _sitTimer += Time.deltaTime;
                if (_sitTimer >= excursionInterval)
                {
                    _sitTimer = 0f;
                    TryExcursion();
                }
                break;

            case CharState.Investigating:
                _investigateTimer += Time.deltaTime;

                // Opinion phase
                if (_investigateTimer >= 2.0f && _investigateTimer < 2.0f + Time.deltaTime)
                    EvaluateCurrentTarget();

                // Done investigating — return to sitting
                if (_investigateTimer >= investigateDuration)
                {
                    _currentTarget = null;
                    _state = CharState.Sitting;
                    _sitTimer = 0f;
                }
                break;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Internal
    // ──────────────────────────────────────────────────────────────

    private void TryExcursion()
    {
        if (UnityEngine.Random.value > excursionChance) return;

        // Find a random active, non-private ReactableTag
        var candidates = new List<ReactableTag>();
        foreach (var tag in ReactableTag.All)
        {
            if (!tag.IsActive) continue;
            if (tag.IsPrivate) continue;
            candidates.Add(tag);
        }

        if (candidates.Count == 0) return;

        _currentTarget = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        _state = CharState.Investigating;
        _investigateTimer = 0f;

        if (investigateSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(investigateSFX);

        Debug.Log($"[DateCharacterController] Evaluating {_currentTarget.gameObject.name}");
    }

    private void EvaluateCurrentTarget()
    {
        if (_currentTarget == null) return;

        var dateSession = DateSessionManager.Instance;
        if (dateSession == null || dateSession.CurrentDate == null) return;

        var reaction = ReactionEvaluator.EvaluateReactable(_currentTarget, dateSession.CurrentDate.preferences);

        if (reactionSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(reactionSFX);

        string itemDisplayName = _currentTarget.DisplayName;
        OnReaction?.Invoke(_currentTarget, reaction, itemDisplayName);

        Debug.Log($"[DateCharacterController] Reacted to {itemDisplayName}: {reaction}");
    }
}
