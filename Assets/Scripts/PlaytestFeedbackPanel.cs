// File: PlaytestFeedbackPanel.cs
// Add this to a Canvas object in your test scene.
// Wire up: inputField, submitButton, (optional) nameField, statusText.
// Optionally list any MonoBehaviours under "captureTargets" to include their public fields as JSON ("juice settings").
//
// Saves to Desktop/Playtest Feedback/<tester>_<timestamp>_feedback.txt
// Falls back to Application.persistentDataPath if Desktop write fails.

using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class PlaytestFeedbackPanel : MonoBehaviour
{
    [Header("UI")]
    public TMP_InputField inputField;       // big text box for thoughts
    public Button submitButton;             // "Submit Thoughts"
    public TMP_InputField nameField;        // optional tester name (can be null)
    public TMP_Text statusText;             // optional tiny status/“Saved to …”
    [TextArea(1, 3)] public string promptHeader = "Tell us what you felt, what was confusing, what you tried:";

    [Header("Capture Extra Settings (optional)")]
    [Tooltip("Drag any components here (e.g., your RopeSweepCutJuicy) to include their public fields as JSON.")]
    public MonoBehaviour[] captureTargets;

    [Header("Filename Options")]
    public string folderName = "Playtest Feedback";  // created on Desktop
    public bool alsoWriteJson = false;               // optional .json alongside the .txt

    void Awake()
    {
        if (submitButton != null)
            submitButton.onClick.AddListener(Submit);
    }

    void OnDestroy()
    {
        if (submitButton != null)
            submitButton.onClick.RemoveListener(Submit);
    }

    public void Submit()
    {
        string thoughts = inputField != null ? inputField.text : "";
        string tester = (nameField != null && !string.IsNullOrWhiteSpace(nameField.text)) ? nameField.text.Trim() : "anonymous";

        if (string.IsNullOrWhiteSpace(thoughts))
        {
            SetStatus("Please write something before submitting.");
            return;
        }

        // --- Build metadata ---
        DateTime now = DateTime.Now; // local time
        string timeLocal = now.ToString("yyyy-MM-dd HH:mm:ss zzz");
        string scene = SceneManager.GetActiveScene().name;
        string platform = Application.platform.ToString();
        Vector2Int res = new Vector2Int(Screen.width, Screen.height);

        // Build header text
        var sb = new StringBuilder();
        sb.AppendLine("=== PLAYTEST FEEDBACK ===");
        sb.AppendLine($"Tester: {tester}");
        sb.AppendLine($"Local Time: {timeLocal}");
        sb.AppendLine($"Scene: {scene}");
        sb.AppendLine($"Platform: {platform}");
        sb.AppendLine($"Resolution: {res.x}x{res.y}");
        sb.AppendLine($"Unity: {Application.unityVersion}");
        sb.AppendLine($"Build GUID: {Application.buildGUID}");
        sb.AppendLine($"GPU: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), VRAM: {SystemInfo.graphicsMemorySize} MB");
        sb.AppendLine($"CPU: {SystemInfo.processorType}, RAM: {SystemInfo.systemMemorySize} MB");
        sb.AppendLine();
        sb.AppendLine("--- Prompt ---");
        sb.AppendLine(promptHeader);
        sb.AppendLine();
        sb.AppendLine("--- Thoughts ---");
        sb.AppendLine(thoughts.Trim());
        sb.AppendLine();

        // Optional: capture “juice/settings” as JSON snapshots
        if (captureTargets != null && captureTargets.Length > 0)
        {
            sb.AppendLine("--- Captured Settings (JSON) ---");
            for (int i = 0; i < captureTargets.Length; i++)
            {
                var mb = captureTargets[i];
                if (mb == null) continue;

                try
                {
                    // Dump public fields/properties Unity can serialize
                    string json = JsonUtility.ToJson(mb, true);
                    sb.AppendLine($"[{i}] {mb.GetType().Name}");
                    sb.AppendLine(json);
                    sb.AppendLine();
                }
                catch (Exception e)
                {
                    sb.AppendLine($"[{i}] {mb.GetType().Name} <JSON ERROR> {e.Message}");
                }
            }
        }

        // --- Write to Desktop/Playtest Feedback ---
        string safeTester = Sanitize(tester);
        string fileStamp = now.ToString("yyyy-MM-dd_HH-mm-ss");
        string baseName = $"{(string.IsNullOrEmpty(safeTester) ? "anon" : safeTester)}_{fileStamp}_feedback";

        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string targetDir = Path.Combine(desktop, folderName);

        string txtPath = Path.Combine(targetDir, baseName + ".txt");
        string jsonPath = Path.Combine(targetDir, baseName + ".json");

        bool ok = TryWrite(targetDir, txtPath, sb.ToString(), out string finalPath, out string error);

        // Optional JSON mirror (more machine-friendly)
        if (ok && alsoWriteJson)
        {
            try
            {
                var mirror = new FeedbackMirror
                {
                    tester = tester,
                    localTime = timeLocal,
                    scene = scene,
                    platform = platform,
                    resolution = $"{res.x}x{res.y}",
                    unity = Application.unityVersion,
                    buildGUID = Application.buildGUID,
                    gpu = SystemInfo.graphicsDeviceName,
                    gpuType = SystemInfo.graphicsDeviceType.ToString(),
                    vramMB = SystemInfo.graphicsMemorySize,
                    cpu = SystemInfo.processorType,
                    ramMB = SystemInfo.systemMemorySize,
                    prompt = promptHeader,
                    thoughts = thoughts.Trim(),
                    captured = CaptureJsonBlocks()
                };

                Directory.CreateDirectory(targetDir);
                File.WriteAllText(jsonPath, JsonUtility.ToJson(mirror, true));
            }
            catch (Exception e)
            {
                SetStatus($"Saved TXT, JSON failed: {e.Message}");
            }
        }

        if (ok)
        {
            SetStatus($"Saved feedback → {finalPath}");
            if (inputField != null) inputField.text = "";
        }
        else
        {
            // Fallback to persistentDataPath if Desktop failed (permissions, sandbox, etc.)
            string fallbackDir = Path.Combine(Application.persistentDataPath, folderName);
            string fallbackTxt = Path.Combine(fallbackDir, baseName + ".txt");

            bool ok2 = TryWrite(fallbackDir, fallbackTxt, sb.ToString(), out string final2, out string err2);
            if (ok2)
                SetStatus($"Desktop failed ({error}). Saved to: {final2}");
            else
                SetStatus($"Save failed:\nDesktop: {error}\nFallback: {err2}");
        }
    }

    bool TryWrite(string dir, string path, string contents, out string finalPath, out string error)
    {
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, contents, Encoding.UTF8);
            finalPath = path;
            error = null;
            return true;
        }
        catch (Exception e)
        {
            finalPath = null;
            error = e.Message;
            return false;
        }
    }

    string Sanitize(string raw)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            raw = raw.Replace(c.ToString(), "");
        return raw.Trim();
    }

    void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        Debug.Log(msg);
    }

    [Serializable]
    class FeedbackMirror
    {
        public string tester;
        public string localTime;
        public string scene;
        public string platform;
        public string resolution;
        public string unity;
        public string buildGUID;
        public string gpu;
        public string gpuType;
        public int vramMB;
        public string cpu;
        public int ramMB;
        public string prompt;
        public string thoughts;
        public CapturedBlock[] captured;
    }

    [Serializable]
    class CapturedBlock
    {
        public string typeName;
        public string json;
    }

    CapturedBlock[] CaptureJsonBlocks()
    {
        if (captureTargets == null) return Array.Empty<CapturedBlock>();
        var list = new System.Collections.Generic.List<CapturedBlock>();
        foreach (var mb in captureTargets)
        {
            if (mb == null) continue;
            try
            {
                list.Add(new CapturedBlock
                {
                    typeName = mb.GetType().Name,
                    json = JsonUtility.ToJson(mb, true)
                });
            }
            catch { /* ignore */ }
        }
        return list.ToArray();
    }
}
