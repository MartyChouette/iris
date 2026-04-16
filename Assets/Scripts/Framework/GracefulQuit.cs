using UnityEngine;

/// <summary>
/// Shared quit-to-desktop helper. Stops all audio and minimizes the window
/// before calling Application.Quit so the player doesn't hear lingering
/// sounds during the slow Unity shutdown.
/// </summary>
public static class GracefulQuit
{
    public static void Execute()
    {
        // Stop all audio immediately
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.StopMusic();
            AudioManager.Instance.StopAmbience();
            AudioManager.Instance.StopWeather();

            // Mute any remaining one-shot SFX by zeroing the listener
            AudioListener.volume = 0f;
        }
        else
        {
            AudioListener.volume = 0f;
        }

        // Minimize the window so the player isn't staring at a frozen frame
#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
        MinimizeWindow();
#endif

        Debug.Log("[GracefulQuit] Audio stopped, window minimized. Quitting.");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern System.IntPtr GetActiveWindow();

    private const int SW_MINIMIZE = 6;

    private static void MinimizeWindow()
    {
        try
        {
            var hwnd = GetActiveWindow();
            if (hwnd != System.IntPtr.Zero)
                ShowWindow(hwnd, SW_MINIMIZE);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[GracefulQuit] Could not minimize window: {e.Message}");
        }
    }
#endif
}
