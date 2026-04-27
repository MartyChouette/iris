using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
static class SceneViewNearClip
{
    static SceneViewNearClip()
    {
        SceneView.beforeSceneGui += sv =>
        {
            var settings = sv.cameraSettings;
            if (sv.camera.orthographic)
            {
                settings.dynamicClip = false;
                settings.nearClip = -500f;
                settings.farClip = 500f;
                sv.cameraSettings = settings;

                sv.camera.nearClipPlane = -500f;
                sv.camera.farClipPlane = 500f;
            }
            else if (!settings.dynamicClip)
            {
                // Restore dynamic clip for perspective so it doesn't stay grey
                settings.dynamicClip = true;
                sv.cameraSettings = settings;
            }
        };

        Camera.onPreRender += cam =>
        {
            if (cam.name == "SceneCamera" && cam.orthographic)
            {
                cam.nearClipPlane = -500f;
                cam.farClipPlane = 500f;
            }
        };
    }
}
