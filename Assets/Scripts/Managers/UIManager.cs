using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the in-game HUD. Subscribes to GameManager and ResourceManager events.
/// Assign the fields in the Inspector, or leave them all empty to auto-build at runtime.
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("HUD Elements (leave empty to auto-build)")]
    [SerializeField] private TextMeshProUGUI woodText;
    [SerializeField] private TextMeshProUGUI metalText;
    [SerializeField] private TextMeshProUGUI stateText;   // "PREPARATION" / "WAVE 2"
    [SerializeField] private TextMeshProUGUI timerText;   // "60s remaining" / "8 enemies left"

    [Header("Health Bar")]
    [SerializeField] private Slider healthBar;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        // If any critical HUD label is missing, build a complete fallback HUD.
        if (woodText == null || metalText == null || stateText == null || timerText == null)
            BuildHUD();
    }

    private void Start()
    {
        SubscribeToEvents();
        BindHealthBar();
        RefreshAll();
    }

    private void OnDestroy()
    {
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.OnWoodChanged  -= HandleWoodChanged;
            ResourceManager.Instance.OnMetalChanged -= HandleMetalChanged;
        }
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnStateChanged            -= HandleStateChanged;
            GameManager.Instance.OnWaveNumberChanged       -= HandleWaveNumberChanged;
            GameManager.Instance.OnTimerChanged            -= HandleTimerChanged;
            GameManager.Instance.OnEnemiesRemainingChanged -= HandleEnemiesRemainingChanged;
        }
    }

    // ── Event subscription ────────────────────────────────────────────────────

    private void SubscribeToEvents()
    {
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.OnWoodChanged  += HandleWoodChanged;
            ResourceManager.Instance.OnMetalChanged += HandleMetalChanged;
        }
        else Debug.LogWarning("UIManager: ResourceManager not found in scene.");

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnStateChanged            += HandleStateChanged;
            GameManager.Instance.OnWaveNumberChanged       += HandleWaveNumberChanged;
            GameManager.Instance.OnTimerChanged            += HandleTimerChanged;
            GameManager.Instance.OnEnemiesRemainingChanged += HandleEnemiesRemainingChanged;
        }
        else Debug.LogWarning("UIManager: GameManager not found in scene.");
    }

    /// <summary>Populates all labels immediately without waiting for the first event.</summary>
    private void RefreshAll()
    {
        if (ResourceManager.Instance != null)
        {
            HandleWoodChanged(ResourceManager.Instance.Wood);
            HandleMetalChanged(ResourceManager.Instance.Metal);
        }

        if (GameManager.Instance != null)
        {
            HandleStateChanged(GameManager.Instance.CurrentState);

            if (GameManager.Instance.CurrentState == GameManager.GameState.Preparation)
                HandleTimerChanged(GameManager.Instance.TimeRemaining);
            else if (GameManager.Instance.CurrentState == GameManager.GameState.Wave)
                HandleEnemiesRemainingChanged(GameManager.Instance.EnemiesRemaining);
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void HandleWoodChanged(int amount)  => woodText.SetText("Wood:  {0}", amount);
    private void HandleMetalChanged(int amount) => metalText.SetText("Metal: {0}", amount);

    private void HandleWaveNumberChanged(int wave)
    {
        // stateText already set by HandleStateChanged which fires just before this
    }

    private void HandleStateChanged(GameManager.GameState state)
    {
        switch (state)
        {
            case GameManager.GameState.Preparation:
                stateText.SetText("PREPARATION");
                stateText.color = new Color(0.4f, 0.9f, 0.4f);   // green
                HandleTimerChanged(GameManager.Instance.TimeRemaining);
                break;

            case GameManager.GameState.Wave:
                stateText.SetText("WAVE {0}", GameManager.Instance.WaveNumber);
                stateText.color = new Color(1f, 0.35f, 0.2f);    // orange-red
                HandleEnemiesRemainingChanged(GameManager.Instance.EnemiesRemaining);
                break;

            case GameManager.GameState.GameOver:
                stateText.SetText("GAME OVER");
                stateText.color = new Color(0.9f, 0.15f, 0.15f); // red
                timerText.SetText("");
                break;
        }
    }

    private void HandleTimerChanged(float remaining)
    {
        // CeilToInt so the display reads "1s" right up until the wave starts, never "0s"
        timerText.SetText("{0}s remaining", Mathf.CeilToInt(remaining));
        timerText.color = remaining <= 10f
            ? new Color(1f, 0.35f, 0.2f)   // urgent orange when ≤10 s
            : new Color(1f, 0.85f, 0.3f);  // normal yellow
    }

    private void HandleEnemiesRemainingChanged(int count)
    {
        timerText.SetText(count == 1 ? "1 enemy left" : "{0} enemies left", count);
        timerText.color = new Color(1f, 0.85f, 0.3f);
    }

    // ── Health bar ────────────────────────────────────────────────────────────

    private void BindHealthBar()
    {
        if (healthBar == null) return;

        PlayerController player = FindAnyObjectByType<PlayerController>();
        if (player == null) { Debug.LogWarning("UIManager: PlayerController not found for health bar."); return; }

        healthBar.minValue = 0f;
        healthBar.maxValue = player.MaxHealth;
        healthBar.value    = player.CurrentHealth;

        player.OnHealthChanged += (current, max) =>
        {
            healthBar.maxValue = max;
            healthBar.value    = current;
        };
    }

    // ── Procedural HUD builder ────────────────────────────────────────────────

    private void BuildHUD()
    {
        EnsureEventSystem();

        GameObject canvasGO        = new GameObject("HUD Canvas");
        Canvas canvas              = canvasGO.AddComponent<Canvas>();
        canvas.renderMode          = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder        = 100;

        CanvasScaler scaler        = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // Resource panel — top left
        GameObject panel           = new GameObject("HUD Panel");
        panel.transform.SetParent(canvasGO.transform, false);

        RectTransform panelRect    = panel.AddComponent<RectTransform>();
        panelRect.anchorMin        = new Vector2(0f, 1f);
        panelRect.anchorMax        = new Vector2(0f, 1f);
        panelRect.pivot            = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(20f, -20f);
        panelRect.sizeDelta        = new Vector2(280f, 172f);

        panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding                = new RectOffset(14, 14, 10, 10);
        layout.spacing                = 4f;
        layout.childControlHeight     = true;
        layout.childControlWidth      = true;
        layout.childForceExpandHeight = true;
        layout.childForceExpandWidth  = true;

        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font == null)
            Debug.LogWarning("UIManager: TMP font not found — import TMP Essential Resources.");

        woodText  = CreateLabel(panel.transform, "WoodText",  "Wood:  0",         font);
        metalText = CreateLabel(panel.transform, "MetalText", "Metal: 0",         font);
        stateText = CreateLabel(panel.transform, "StateText", "PREPARATION",      font, new Color(0.4f, 0.9f, 0.4f));
        timerText = CreateLabel(panel.transform, "TimerText", "60s remaining",    font, new Color(1f, 0.85f, 0.3f));
    }

    private static TextMeshProUGUI CreateLabel(Transform parent, string goName,
        string defaultText, TMP_FontAsset font, Color? color = null)
    {
        GameObject go = new GameObject(goName);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text     = defaultText;
        tmp.fontSize = 26f;
        tmp.color    = color ?? Color.white;
        if (font != null) tmp.font = font;
        return tmp;
    }

    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }
}
