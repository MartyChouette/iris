using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Creates an overlay camera at runtime that renders only the HeldItem layer
/// on top of the main camera. This replaces shader-based ZTest hacks with
/// proper URP camera stacking — held items keep their authored materials
/// and lighting while always appearing above the scene.
///
/// Auto-spawns via RuntimeInitializeOnLoadMethod. No scene setup required.
/// </summary>
public class HeldItemOverlayCamera : MonoBehaviour
{
    private static HeldItemOverlayCamera s_instance;
    private Camera _overlayCam;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (s_instance != null) return;

        var go = new GameObject("[HeldItemOverlayCamera]");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<HeldItemOverlayCamera>();
    }

    private void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Destroy(gameObject);
            return;
        }
        s_instance = this;
    }

    private void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
    }

    private void LateUpdate()
    {
        var mainCam = Camera.main;
        if (mainCam == null)
        {
            if (_overlayCam != null) _overlayCam.enabled = false;
            return;
        }

        EnsureOverlayCamera(mainCam);
        SyncToMainCamera(mainCam);
    }

    private void EnsureOverlayCamera(Camera mainCam)
    {
        if (_overlayCam != null) return;

        // Create overlay camera as a child so it follows main camera transforms
        var camGO = new GameObject("HeldItemOverlay");
        camGO.transform.SetParent(transform, false);
        _overlayCam = camGO.AddComponent<Camera>();

        // Configure as overlay
        _overlayCam.clearFlags = CameraClearFlags.Depth;
        _overlayCam.cullingMask = 1 << LayerMask.NameToLayer("HeldItem");
        _overlayCam.depth = mainCam.depth + 1;

        // URP camera data — mark as Overlay type
        var overlayData = _overlayCam.GetUniversalAdditionalCameraData();
        overlayData.renderType = CameraRenderType.Overlay;
        overlayData.renderPostProcessing = false;
        overlayData.requiresColorOption = CameraOverrideOption.Off;
        overlayData.requiresDepthOption = CameraOverrideOption.Off;

        // Exclude HeldItem layer from main camera so items don't double-render
        int heldLayer = LayerMask.NameToLayer("HeldItem");
        if (heldLayer >= 0)
            mainCam.cullingMask &= ~(1 << heldLayer);

        // Add to main camera's stack
        var mainData = mainCam.GetUniversalAdditionalCameraData();
        mainData.renderType = CameraRenderType.Base;
        if (!mainData.cameraStack.Contains(_overlayCam))
            mainData.cameraStack.Add(_overlayCam);

        _overlayCam.enabled = true;

        Debug.Log("[HeldItemOverlayCamera] Overlay camera created and stacked.");
    }

    private void SyncToMainCamera(Camera mainCam)
    {
        if (_overlayCam == null) return;

        // Match projection settings so held items render at correct positions
        _overlayCam.orthographic = mainCam.orthographic;
        _overlayCam.orthographicSize = mainCam.orthographicSize;
        _overlayCam.fieldOfView = mainCam.fieldOfView;
        _overlayCam.nearClipPlane = mainCam.nearClipPlane;
        _overlayCam.farClipPlane = mainCam.farClipPlane;

        // Match transform exactly
        _overlayCam.transform.position = mainCam.transform.position;
        _overlayCam.transform.rotation = mainCam.transform.rotation;
    }
}
