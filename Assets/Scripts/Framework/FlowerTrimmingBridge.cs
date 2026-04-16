using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Cinemachine;
using TMPro;

/// <summary>
/// Scene-scoped singleton that manages the transition into and out of a
/// flower trimming scene. Lives in the apartment scene, loads the flower
/// scene additively (which must contain a FlowerSessionController),
/// captures results, snapshots the trimmed flower, and unloads it.
/// </summary>
public class FlowerTrimmingBridge : MonoBehaviour
{
    public static FlowerTrimmingBridge Instance { get; private set; }

    [Header("Scene")]
    [Tooltip("Default flower trimming scene. Overridden per-date via DatePersonalDefinition.flowerSceneName.")]
    [SerializeField] private string _flowerSceneName = "Daisy_Flower_Scene";

    [Tooltip("Vertical offset applied to the flower scene so it doesn't overlap the apartment.")]
    [SerializeField] private float _sceneYOffset = 50f;

    [Header("Debug")]
    [Tooltip("Always treat flower trimming as a success (high score, max days alive, no game over).")]
    [SerializeField] private bool _alwaysSucceed;

    private Action<int, int, bool> _onComplete;
    private bool _waitingForResult;

    // Cached apartment camera references — disabled during trimming so
    // the flower scene's own Camera takes over rendering.
    private Camera _apartmentCamera;
    private CinemachineBrain _apartmentBrain;
    private AudioListener _apartmentListener;

    /// <summary>
    /// True once the flower scene has been loaded, offset, and the session controller found.
    /// DayPhaseManager polls this to know when it's safe to fade in from black.
    /// </summary>
    public bool IsSceneReady { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[FlowerTrimmingBridge] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Load the flower trimming scene additively, find the FlowerSessionController,
    /// wait for the player to trim, and invoke onComplete with results.
    /// </summary>
    /// <param name="onComplete">Callback: (score, daysAlive, isGameOver)</param>
    public void BeginTrimming(Action<int, int, bool> onComplete)
    {
        if (_waitingForResult)
        {
            Debug.LogWarning("[FlowerTrimmingBridge] Already waiting for a trimming result.");
            return;
        }

        _onComplete = onComplete;
        _waitingForResult = true;
        IsSceneReady = false;
        StartCoroutine(TrimmingSequence());
    }

    /// <summary>
    /// Resolve which flower scene to load: prefer the current date's flowerSceneName,
    /// fall back to the serialized default.
    /// </summary>
    private string ResolveSceneName()
    {
        if (DateSessionManager.Instance != null &&
            DateSessionManager.Instance.CurrentDate != null &&
            !string.IsNullOrEmpty(DateSessionManager.Instance.CurrentDate.flowerSceneName))
        {
            return DateSessionManager.Instance.CurrentDate.flowerSceneName;
        }

        return _flowerSceneName;
    }

    private IEnumerator TrimmingSequence()
    {
        // Resolve per-date scene name, falling back to default
        string sceneName = ResolveSceneName();

        // Cache the apartment camera BEFORE loading — the flower scene has its own
        // MainCamera-tagged Camera (not Cinemachine), so we must disable the apartment's
        // to avoid two cameras rendering simultaneously.
        _apartmentCamera = Camera.main;
        if (_apartmentCamera != null)
        {
            _apartmentBrain = _apartmentCamera.GetComponent<CinemachineBrain>();
            _apartmentListener = _apartmentCamera.GetComponent<AudioListener>();
        }

        // ── PHYSICS-SAFE SCENE OFFSET ──────────────────────────────────
        // Pause physics entirely so no simulation runs at the original (0,0,0)
        // positions. Without this, joints and rigidbodies simulate at wrong
        // positions during/after the offset move, causing the flower to rip
        // apart — especially in builds where frame timing differs from editor.
        var prevSimMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        // Load the flower trimming scene additively
        var loadOp = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        if (loadOp == null)
        {
            Debug.LogError($"[FlowerTrimmingBridge] Failed to load scene '{sceneName}'. " +
                           "Is it added to Build Settings?");
            Physics.simulationMode = prevSimMode;
            _waitingForResult = false;
            _onComplete?.Invoke(0, 0, true);
            yield break;
        }

        yield return loadOp;

        var flowerScene = SceneManager.GetSceneByName(sceneName);
        if (!flowerScene.IsValid())
        {
            Debug.LogError("[FlowerTrimmingBridge] Flower scene not valid after load.");
            Physics.simulationMode = prevSimMode;
            _waitingForResult = false;
            _onComplete?.Invoke(0, 0, true);
            yield break;
        }

        // Wait one frame so all Start() methods run and joints are created.
        yield return null;

        Vector3 offset = new Vector3(0f, _sceneYOffset, 0f);
        var roots = flowerScene.GetRootGameObjects();
        Debug.Log($"[FlowerTrimmingBridge] Scene '{sceneName}' loaded with {roots.Length} root objects, offsetting by Y={_sceneYOffset}.");

        // Suppress joint breaks during the move
        XYTetherJoint.SetCutBreakSuppressed(true);

        // 1) Move all root objects
        foreach (var root in roots)
        {
            root.transform.position += offset;
            Debug.Log($"[FlowerTrimmingBridge]   root: '{root.name}' → {root.transform.position}");
        }

        // 2) Sync rigidbody positions to match moved transforms
        foreach (var root in roots)
        {
            foreach (var rb in root.GetComponentsInChildren<Rigidbody>())
            {
                if (rb == null) continue;
                rb.position = rb.transform.position;
                rb.rotation = rb.transform.rotation;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        // 3) Recreate all XYTetherJoint joints at the new positions.
        //    Toggle off→on: OnDisable destroys the joint, OnEnable recreates it
        //    with anchors/rest baseline computed from the moved transforms.
        foreach (var root in roots)
        {
            foreach (var xy in root.GetComponentsInChildren<XYTetherJoint>())
            {
                xy.enabled = false;
                xy.enabled = true;
            }
        }

        // 4) Flush transform changes into the physics engine
        Physics.SyncTransforms();

        // 5) Resume normal physics simulation
        Physics.simulationMode = prevSimMode;

        // 6) Let joints settle for a couple of physics steps before gameplay
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        // 7) Unsuppress joint breaks
        XYTetherJoint.SetCutBreakSuppressed(false);

        // Find the FlowerSessionController in the loaded scene
        FlowerSessionController session = null;
        foreach (var root in roots)
        {
            session = root.GetComponentInChildren<FlowerSessionController>();
            if (session != null) break;
        }

        if (session == null)
        {
            Debug.LogWarning($"[FlowerTrimmingBridge] No FlowerSessionController found in '{sceneName}'. " +
                             "Results will default to 0.");
            _waitingForResult = false;
            _onComplete?.Invoke(0, 0, true);
            yield break;
        }

        // Log flower brain state for diagnostics
        if (session.brain != null)
        {
            var renderers = session.brain.GetComponentsInChildren<Renderer>();
            Debug.Log($"[FlowerTrimmingBridge] FlowerGameBrain found on '{session.brain.name}' with {renderers.Length} renderers.");
        }
        else
        {
            Debug.LogWarning("[FlowerTrimmingBridge] FlowerSessionController.brain is NULL — no flower model!");
        }

        // Log flower scene camera
        Camera flowerCam = null;
        foreach (var root in roots)
        {
            flowerCam = root.GetComponentInChildren<Camera>();
            if (flowerCam != null) break;
        }
        if (flowerCam != null)
            Debug.Log($"[FlowerTrimmingBridge] Flower camera: '{flowerCam.name}' at {flowerCam.transform.position}, enabled={flowerCam.enabled}");
        else
            Debug.LogWarning("[FlowerTrimmingBridge] No camera found in flower scene!");

        // Reset session state in case the scene was previously played standalone
        session.sessionEnded = false;
        session.endRequested = false;

        // Disable scene-baked Quit/Restart buttons — apartment flow uses its own Continue button
        DisableSceneButtons(flowerScene);

        // Wait for the session to produce a result
        int resultScore = 0;
        int resultDays = 0;
        bool resultGameOver = false;
        bool gotResult = false;

        // Check if the current date guarantees flower success (e.g. Paris tutorial)
        bool guarantee = _alwaysSucceed;
        if (!guarantee && DateSessionManager.Instance != null
            && DateSessionManager.Instance.CurrentDate != null)
        {
            guarantee = DateSessionManager.Instance.CurrentDate.guaranteeFlowerSuccess;
        }

        // Named listener so we can cleanly remove it after the session ends
        UnityEngine.Events.UnityAction<FlowerEvaluation, int, int> resultListener = (eval, score, days) =>
        {
            if (guarantee)
            {
                resultScore = Mathf.Max(score, 95);
                resultDays = Mathf.Max(days, 7);
                resultGameOver = false;
                Debug.Log("[FlowerTrimmingBridge] Guaranteed flower success (per-character).");
            }
            else
            {
                resultScore = score;
                resultDays = days;
                resultGameOver = eval.isGameOver;
            }
            gotResult = true;
        };
        session.OnResult.AddListener(resultListener);

        // Enable keyboard evaluate so player can press E to finish
        session.allowKeyboardEvaluate = true;

        // Disable apartment camera so the flower scene's Camera renders exclusively.
        // ScreenFade is ScreenSpaceOverlay so it still works while apartment camera is off.
        DisableApartmentCamera();

        // Create a runtime "Done Trimming" button
        var doneButtonCanvas = CreateDoneButton(session);

        // Force the cutter to re-discover the flower scene's stem/session objects.
        // Without this, the 2-second cache timer might prevent the first cut from finding targets.
        var cutter = UnityEngine.Object.FindFirstObjectByType<DynamicMeshCutter.PlaneBehaviour>();
        if (cutter != null)
            cutter.InvalidateCache();

        // Disable grading UI — we silently judge and fade back to apartment
        var gradingUI = UnityEngine.Object.FindFirstObjectByType<FlowerGradingUI>();
        if (gradingUI != null)
            gradingUI.gameObject.SetActive(false);

        // Signal that the scene is loaded and ready for play
        IsSceneReady = true;

        // Wait until the session produces a result
        while (!gotResult)
            yield return null;

        // Detach listener so repeated trim sessions don't accumulate subscribers
        if (session != null && resultListener != null)
            session.OnResult.RemoveListener(resultListener);

        // Remove the Done button now that we have a result
        if (doneButtonCanvas != null)
            Destroy(doneButtonCanvas);

        // Brief pause then silently continue — no score UI
        yield return new WaitForSecondsRealtime(1f);

        // Snapshot the trimmed flower visuals BEFORE unloading the scene
        GameObject trimmedVisual = null;
        if (session.brain != null)
        {
            trimmedVisual = TrimmedFlowerSnapshot.Capture(session.brain);
            // Move to DontDestroyOnLoad so it survives the scene unload
            UnityEngine.Object.DontDestroyOnLoad(trimmedVisual);
            trimmedVisual.SetActive(false); // hide until placed
        }

        // Capture results in data pipeline
        DateOutcomeCapture.CaptureFlowerResult(resultScore, resultDays, resultGameOver);
        string grade = DateOutcomeCapture.LastOutcome.flowerGrade;
        DateHistory.UpdateFlowerResult(resultScore, resultDays, grade);

        // Scale down the trimmed flower to apartment pot size
        if (trimmedVisual != null)
        {
            // Normalize to a consistent small size (0.15m tall fits a pot)
            var bounds = CalculateBounds(trimmedVisual);
            float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (maxDim > 0.01f)
            {
                float targetSize = 0.15f;
                float scale = targetSize / maxDim;
                trimmedVisual.transform.localScale = Vector3.one * scale;
            }
        }

        // Spawn living plant in apartment
        if (resultDays > 0 && LivingFlowerPlantManager.Instance != null)
        {
            string charName = DateOutcomeCapture.LastOutcome.characterName;
            LivingFlowerPlantManager.Instance.SpawnPlant(charName, resultDays, trimmedVisual);
        }
        else if (trimmedVisual != null)
        {
            UnityEngine.Object.Destroy(trimmedVisual);
        }

        // NOTE: Apartment camera is restored by DayPhaseManager after its fade-to-black,
        // not here — restoring here would cause a brief flash before the DPM fade starts.

        // Clean up
        DateSessionManager.PendingFlowerTrim = false;
        _waitingForResult = false;
        IsSceneReady = false;

        // Invoke callback before unloading
        _onComplete?.Invoke(resultScore, resultDays, resultGameOver);
        _onComplete = null;

        // Unload the flower scene
        var unloadOp = SceneManager.UnloadSceneAsync(flowerScene);
        if (unloadOp != null)
            yield return unloadOp;

        Debug.Log($"[FlowerTrimmingBridge] Trimming complete. Scene={sceneName}, " +
                  $"Score={resultScore}, Days={resultDays}, GameOver={resultGameOver}");
    }

    private static Bounds CalculateBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);

        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    // ── Runtime UI helpers ─────────────────────────────────────────────

    private GameObject CreateDoneButton(FlowerSessionController session)
    {
        var canvasGO = new GameObject("FlowerDoneButtonCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        // Button background
        var btnGO = new GameObject("DoneButton");
        btnGO.transform.SetParent(canvasGO.transform, false);
        var btnImage = btnGO.AddComponent<Image>();
        btnImage.color = new Color(0.1f, 0.1f, 0.1f, 0.75f);
        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImage;
        btn.onClick.AddListener(() => session.EvaluateCurrentFlower());

        var btnRect = btnGO.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.5f, 0f);
        btnRect.anchorMax = new Vector2(0.5f, 0f);
        btnRect.pivot = new Vector2(0.5f, 0f);
        btnRect.anchoredPosition = new Vector2(0f, 40f);
        btnRect.sizeDelta = new Vector2(260f, 50f);

        // Button text
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(btnGO.transform, false);
        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = "Done Trimming (E)";
        tmp.fontSize = 24;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        var textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return canvasGO;
    }

    private GameObject CreateContinueButton(Action onClick)
    {
        var canvasGO = new GameObject("FlowerContinueCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 101;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        // Button background
        var btnGO = new GameObject("ContinueButton");
        btnGO.transform.SetParent(canvasGO.transform, false);
        var btnImage = btnGO.AddComponent<Image>();
        btnImage.color = new Color(0.22f, 0.20f, 0.18f, 0.88f);
        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImage;
        var btnColors = btn.colors;
        btnColors.highlightedColor = new Color(1f, 0.95f, 0.85f);
        btnColors.pressedColor = new Color(0.85f, 0.8f, 0.7f);
        btn.colors = btnColors;
        btn.interactable = false; // enabled after 1.5s delay
        btn.onClick.AddListener(() => onClick?.Invoke());

        var btnRect = btnGO.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.5f, 0f);
        btnRect.anchorMax = new Vector2(0.5f, 0f);
        btnRect.pivot = new Vector2(0.5f, 0.5f);
        btnRect.anchoredPosition = new Vector2(0f, 100f);
        btnRect.sizeDelta = new Vector2(240f, 56f);

        // Button text
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(btnGO.transform, false);
        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = "Continue";
        tmp.fontSize = 24;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        var textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return canvasGO;
    }

    // ── Camera management ─────────────────────────────────────────────

    /// <summary>
    /// Re-enable the apartment camera after flower trimming.
    /// Called by DayPhaseManager after fade-to-black so there's no visual flash.
    /// Safe to call multiple times (idempotent).
    /// </summary>
    public void RestoreApartmentCamera()
    {
        if (_apartmentBrain != null)
            _apartmentBrain.enabled = true;
        if (_apartmentListener != null)
            _apartmentListener.enabled = true;
        if (_apartmentCamera != null)
        {
            _apartmentCamera.enabled = true;
            _apartmentCamera.gameObject.tag = "MainCamera";
        }

        _apartmentCamera = null;
        _apartmentBrain = null;
        _apartmentListener = null;
        Debug.Log("[FlowerTrimmingBridge] Apartment camera restored.");
    }

    /// <summary>
    /// Find and deactivate Quit/Restart buttons and Game Over UI baked into the flower scene.
    /// These are only relevant when the flower scene runs standalone.
    /// </summary>
    private static void DisableSceneButtons(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.gameObject.name;
                if (n.StartsWith("Quit_Button") || n.StartsWith("Restart_Button"))
                {
                    t.gameObject.SetActive(false);
                    Debug.Log($"[FlowerTrimmingBridge] Disabled scene button: {n}");
                }
            }

            // Disable GameOverUI components so "Game Over" text never appears
            foreach (var goUI in root.GetComponentsInChildren<GameOverUI>(true))
            {
                goUI.enabled = false;
                if (goUI.root != null)
                    goUI.root.gameObject.SetActive(false);
                Debug.Log("[FlowerTrimmingBridge] Disabled GameOverUI.");
            }
        }
    }

    private void DisableApartmentCamera()
    {
        if (_apartmentCamera != null)
        {
            _apartmentCamera.enabled = false;
            _apartmentCamera.gameObject.tag = "Untagged";
        }
        if (_apartmentBrain != null)
            _apartmentBrain.enabled = false;
        if (_apartmentListener != null)
            _apartmentListener.enabled = false;

        Debug.Log("[FlowerTrimmingBridge] Apartment camera disabled for flower scene.");
    }

}
