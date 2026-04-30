using UnityEngine;

/// <summary>
/// The Gunpla body — a placeable figure that accepts Sword and Wings attachments.
/// Once all parts are collected, enables the Animator to cycle through hero poses.
/// Periodically turns its head toward Nema when complete and idle.
///
/// Scene setup:
///   GunplaBody (PlaceableObject + Rigidbody + GunplaFigure)
///   ├── BodyMesh (the rigged FBX with Animator)
///   ├── SwordAttach (empty child transform — where sword parents to)
///   └── WingsAttach (empty child transform — where wings parent to)
/// </summary>
public class GunplaFigure : MonoBehaviour
{
    [Header("Attach Points")]
    [Tooltip("Child transform where the sword snaps to.")]
    [SerializeField] private Transform _swordAttach;

    [Tooltip("Child transform where the wings snap to.")]
    [SerializeField] private Transform _wingsAttach;

    [Header("Animator")]
    [Tooltip("Animator on the rigged body mesh (disabled until complete).")]
    [SerializeField] private Animator _animator;

    [Header("Look At Nema")]
    [Tooltip("Head bone for subtle look-at rotation.")]
    [SerializeField] private Transform _headBone;

    [Tooltip("How fast the head turns toward Nema (degrees/sec).")]
    [SerializeField] private float _lookSpeed = 30f;

    [Tooltip("Max head turn angle from forward.")]
    [SerializeField] private float _maxLookAngle = 35f;

    [Tooltip("Seconds between look-at glances.")]
    [SerializeField] private float _glanceInterval = 5f;

    [Tooltip("How long each glance lasts.")]
    [SerializeField] private float _glanceDuration = 2f;

    [Header("System")]
    [Tooltip("Disable to turn off the entire gunpla assembly system.")]
    [SerializeField] private bool _systemEnabled = false;

    [Header("Snap Radius")]
    [Tooltip("World-space distance at which a held part magnetically snaps to this body.")]
    [SerializeField] private float _snapRadius = 0.5f;

    // Runtime
    private GunplaPart _sword;
    private GunplaPart _wings;
    private bool _isComplete;
    private float _glanceTimer;
    private bool _isGlancing;
    private float _glanceElapsed;
    private Quaternion _headBaseRotation;

    // Static registry
    private static readonly System.Collections.Generic.List<GunplaFigure> s_all = new();
    public static System.Collections.Generic.IReadOnlyList<GunplaFigure> All => s_all;

    private void OnEnable() => s_all.Add(this);
    private void OnDisable() => s_all.Remove(this);

    public bool IsComplete => _isComplete;
    public bool HasSword => _sword != null;
    public bool HasWings => _wings != null;
    public float SnapRadius => _snapRadius;

    private void Awake()
    {
        // Start with animator disabled — only plays when all parts attached
        if (_animator != null)
            _animator.enabled = false;

        _glanceTimer = _glanceInterval;
    }

    /// <summary>Can this body accept the given part?</summary>
    public bool SystemEnabled => _systemEnabled;

    public bool CanAccept(GunplaPart part)
    {
        if (!_systemEnabled) return false;
        if (part == null || part.IsAttached) return false;
        return part.Type switch
        {
            GunplaPart.PartType.Sword => _sword == null && _swordAttach != null,
            GunplaPart.PartType.Wings => _wings == null && _wingsAttach != null,
            _ => false
        };
    }

    /// <summary>Get the world-space attach position for a part type.</summary>
    public Vector3 GetAttachPosition(GunplaPart part)
    {
        return part.Type switch
        {
            GunplaPart.PartType.Sword => _swordAttach != null ? _swordAttach.position : transform.position,
            GunplaPart.PartType.Wings => _wingsAttach != null ? _wingsAttach.position : transform.position,
            _ => transform.position
        };
    }

    /// <summary>Attach a part to this body.</summary>
    public void AttachPart(GunplaPart part)
    {
        if (!CanAccept(part)) return;

        Transform attachPoint = part.Type == GunplaPart.PartType.Sword ? _swordAttach : _wingsAttach;
        part.AttachTo(attachPoint);

        if (part.Type == GunplaPart.PartType.Sword)
            _sword = part;
        else
            _wings = part;

        // Play a snap SFX
        AudioManager.Instance?.PlaySFX(null); // placeholder — wire clip later

        CheckComplete();
    }

    private void CheckComplete()
    {
        if (_sword == null || _wings == null) return;

        _isComplete = true;

        // Animator and head-tracking disabled — gunpla stays in its static pose

        Debug.Log("[GunplaFigure] All parts collected! Gunpla is complete.");
    }

    private void LateUpdate()
    {
        // Disabled — gunpla stays stationary, no pose cycling or head tracking
    }

    private void UpdateLookAtNema()
    {
        var nema = NemaController.Instance;
        if (nema == null || nema.ActiveModel == null)
        {
            _headBone.localRotation = _headBaseRotation;
            return;
        }

        // Periodic glance timer
        if (!_isGlancing)
        {
            _glanceTimer -= Time.deltaTime;
            if (_glanceTimer <= 0f)
            {
                _isGlancing = true;
                _glanceElapsed = 0f;
            }
            else
            {
                // Smoothly return to base
                _headBone.localRotation = Quaternion.Slerp(
                    _headBone.localRotation, _headBaseRotation,
                    Time.deltaTime * _lookSpeed * 0.1f);
                return;
            }
        }

        _glanceElapsed += Time.deltaTime;
        if (_glanceElapsed >= _glanceDuration)
        {
            _isGlancing = false;
            _glanceTimer = _glanceInterval + Random.Range(-1f, 1f);
            return;
        }

        // Look toward Nema
        Vector3 nemaPos = nema.ActiveModel.position;
        Vector3 toNema = (nemaPos - _headBone.position).normalized;
        Vector3 localDir = _headBone.parent != null
            ? _headBone.parent.InverseTransformDirection(toNema)
            : toNema;

        // Clamp the look angle
        float angle = Vector3.Angle(Vector3.forward, localDir);
        if (angle > _maxLookAngle)
            localDir = Vector3.Slerp(Vector3.forward, localDir, _maxLookAngle / angle);

        Quaternion targetRot = _headBaseRotation * Quaternion.FromToRotation(Vector3.forward, localDir);
        _headBone.localRotation = Quaternion.Slerp(
            _headBone.localRotation, targetRot,
            Time.deltaTime * _lookSpeed * Mathf.Deg2Rad);
    }
}
