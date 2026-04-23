using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Suppresses Unity's built-in joint break forces during cuts to prevent joints from breaking due to physics jolt.
/// Works with FixedJoint, ConfigurableJoint, HingeJoint, SpringJoint, etc.
/// PERF: Optimized to avoid repeated dictionary allocation and reduce GetComponent calls.
/// </summary>
public static class JointCutSuppressor
{
    // PERF: Pre-allocate dictionaries with reasonable capacity to avoid resizing
    private static Dictionary<Joint, float> _savedBreakForces = new Dictionary<Joint, float>(64);
    private static Dictionary<Joint, float> _savedBreakTorques = new Dictionary<Joint, float>(64);
    private static bool _isSuppressed = false;

    // PERF: Reusable list to avoid allocation in foreach
    private static List<Joint> _tempJointsList = new List<Joint>(64);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _savedBreakForces.Clear();
        _savedBreakTorques.Clear();
        _isSuppressed = false;
        _tempJointsList.Clear();
    }

    /// <summary>
    /// Suppress all joint breaks by setting breakForce/breakTorque to infinity.
    /// Call this BEFORE cutting.
    /// PERF: Reuses internal list and avoids redundant operations if already suppressed.
    /// </summary>
    public static void SuppressAllJoints(GameObject rootObject)
    {
        if (_isSuppressed) return; // Already suppressed

        // PERF: Don't clear if this is an incremental call (multiple roots)
        // Only clear on first call of a suppression cycle
        if (_savedBreakForces.Count == 0)
        {
            // First call - dictionaries are ready
        }

        // Find all joints under the root
        var joints = rootObject.GetComponentsInChildren<Joint>(true);

        int count = 0;
        foreach (var joint in joints)
        {
            if (joint == null) continue;

            // Skip if already suppressed (from another root object)
            if (_savedBreakForces.ContainsKey(joint)) continue;

            // Save original values
            _savedBreakForces[joint] = joint.breakForce;
            _savedBreakTorques[joint] = joint.breakTorque;

            // Set to infinity to prevent breaking
            joint.breakForce = Mathf.Infinity;
            joint.breakTorque = Mathf.Infinity;
            count++;
        }

        _isSuppressed = true;

        if (count > 0)
            Debug.Log($"[JointCutSuppressor] Suppressed {count} joints under '{rootObject.name}'");
    }
    
    /// <summary>
    /// Suppress joints globally (all joints in scene).
    /// Use this if you don't have a specific root object.
    /// PERF: Uses FindObjectsByType which is more efficient than FindObjectsOfType.
    /// </summary>
    public static void SuppressAllJointsGlobal()
    {
        if (_isSuppressed) return;

        _savedBreakForces.Clear();
        _savedBreakTorques.Clear();

        // Find ALL joints in the scene
        var joints = Object.FindObjectsByType<Joint>(FindObjectsSortMode.None);

        foreach (var joint in joints)
        {
            if (joint == null) continue;

            // Save original values
            _savedBreakForces[joint] = joint.breakForce;
            _savedBreakTorques[joint] = joint.breakTorque;

            // Set to infinity to prevent breaking
            joint.breakForce = Mathf.Infinity;
            joint.breakTorque = Mathf.Infinity;
        }

        _isSuppressed = true;

        Debug.Log($"[JointCutSuppressor] Suppressed {joints.Length} joints globally");
    }

    /// <summary>
    /// Call this before multiple SuppressAllJoints calls to clear state for a new suppression cycle.
    /// PERF: Allows incremental suppression across multiple root objects.
    /// </summary>
    public static void BeginSuppressionCycle()
    {
        _savedBreakForces.Clear();
        _savedBreakTorques.Clear();
        _isSuppressed = false;
    }

    /// <summary>
    /// Marks the suppression cycle as complete.
    /// Call after all SuppressAllJoints calls are done.
    /// </summary>
    public static void EndSuppressionCycle()
    {
        _isSuppressed = true;
    }
    
    /// <summary>
    /// Restore all joint break forces to their original values.
    /// Call this AFTER cutting is complete.
    /// PERF: Batches force and torque restoration in a single pass through saved keys.
    /// </summary>
    public static void RestoreAllJoints()
    {
        if (!_isSuppressed) return;

        int restored = 0;

        // PERF: Iterate once and restore both force and torque
        foreach (var kvp in _savedBreakForces)
        {
            var joint = kvp.Key;
            if (joint == null) continue; // Joint was destroyed

            try
            {
                joint.breakForce = kvp.Value;

                // Also restore torque if we have it
                if (_savedBreakTorques.TryGetValue(joint, out float savedTorque))
                {
                    joint.breakTorque = savedTorque;
                }
                restored++;
            }
            catch (System.Exception)
            {
                // Silently ignore destroyed joints - common during gameplay
            }
        }

        _savedBreakForces.Clear();
        _savedBreakTorques.Clear();
        _isSuppressed = false;

        if (restored > 0)
            Debug.Log($"[JointCutSuppressor] Restored {restored} joints");
    }
}
