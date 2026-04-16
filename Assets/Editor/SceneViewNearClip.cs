using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
static class SceneViewNearClip
{
    static SceneViewNearClip()
    {
        SceneView.beforeSceneGui += sv =>
        {
            if (sv.camera.orthographic)
            {
                sv.camera.nearClipPlane = -500f;
                sv.camera.farClipPlane = 500f;
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
