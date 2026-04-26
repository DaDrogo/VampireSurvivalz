using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// In-game pause menu. ESC toggles pause (skips if BuildingManager is in placement mode).
/// Place on any GameObject in SampleScene.
/// </summary>
public class PauseMenuManager : MonoBehaviour
{
    public static PauseMenuManager Instance { get; private set; }

    private bool       _isPaused;
    private GameObject _pauseCanvas;
    private GameObject _pausePanel;
    private GameObject _settingsPanel;

    private TMP_FontAsset _font;

    [Header("UI Theme")]
    [SerializeField] private UITheme _theme;

    private static readonly Color BgDark    = new Color(0.08f, 0.08f, 0.10f, 0.97f);
    private static readonly Color BtnGreen  = new Color(0.15f, 0.52f, 0.15f);
    private static readonly Color BtnBlue   = new Color(0.15f, 0.32f, 0.62f);
    private static readonly Color BtnRed    = new Color(0.52f, 0.12f, 0.12f);
    private static readonly Color BtnGray   = new Color(0.28f, 0.28f, 0.30f);

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _font = _theme?.font != null
            ? _theme.font
            : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        BuildPauseUI();
    }

    private void Update()
    {
        if (GameManager.Instance?.CurrentState == GameManager.GameState.GameOver) return;
        if (!Keyboard.current.escapeKey.wasPressedThisFrame) return;

        // Let BuildingManager consume ESC first to cancel placement
        if (BuildingManager.Instance != null && BuildingManager.Instance.IsPlacing) return;

        if (_settingsPanel != null && _settingsPanel.activeSelf)
            ShowPausePanel();
        else
            TogglePause();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public bool IsPaused => _isPaused;

    public void Pause()
    {
        _isPaused = true;
        Time.timeScale = 0f;
        BuildingManager.Instance?.CancelPlacement();
        ShowPausePanel();
        _pauseCanvas.SetActive(true);
    }

    public void Resume()
    {
        _isPaused = false;
        Time.timeScale = 1f;
        _pauseCanvas.SetActive(false);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void TogglePause()
    {
        if (_isPaused) Resume();
        else           Pause();
    }

    private void ShowPausePanel()
    {
        _pausePanel.SetActive(true);
        _settingsPanel.SetActive(false);
    }

    private void ShowSettingsPanel()
    {
        _pausePanel.SetActive(false);
        _settingsPanel.SetActive(true);
    }

    private void ReturnToMainMenu()
    {
        _isPaused = false;
        SceneTransitionManager.Instance?.LoadScene("MainMenuScene");
    }

    // ── UI construction ───────────────────────────────────────────────────────

    private void BuildPauseUI()
    {
        EnsureEventSystem();

        _pauseCanvas = new GameObject("PauseCanvas");
        Canvas canvas        = _pauseCanvas.AddComponent<Canvas>();
        canvas.renderMode    = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder  = 100;

        CanvasScaler scaler        = _pauseCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 1f;
        _pauseCanvas.AddComponent<GraphicRaycaster>();

        // Full-screen dim
        GameObject dim        = new GameObject("Dim");
        dim.transform.SetParent(_pauseCanvas.transform, false);
        RectTransform dimRT   = dim.AddComponent<RectTransform>();
        dimRT.anchorMin       = Vector2.zero;
        dimRT.anchorMax       = Vector2.one;
        dimRT.offsetMin       = Vector2.zero;
        dimRT.offsetMax       = Vector2.zero;

        _pausePanel    = BuildPanel("PausePanel", new Vector2(360f, 320f), _pauseCanvas.transform,
            p => {
                AddTitle(p,  "PAUSED", 46f, new Color(0.9f, 0.9f, 0.9f));
                AddButton(p, "Resume",      "Resume",      BtnGreen, Resume,            _theme?.buttonNav);
                AddButton(p, "Settings",    "Settings",    BtnBlue,  ShowSettingsPanel,  _theme?.buttonNav);
                AddButton(p, "MainMenu",    "Main Menu",   BtnRed,   ReturnToMainMenu,   _theme?.buttonDanger);
            });

        _settingsPanel = BuildPanel("SettingsPanel", new Vector2(460f, 340f), _pauseCanvas.transform,
            p => {
                AddTitle(p, "SETTINGS", 38f, Color.white);
                AddVolumeSlider(p, "Music Volume",
                    PersistentDataManager.Instance?.MusicVolume ?? 0.8f,
                    v => PersistentDataManager.Instance?.SetMusicVolume(v));
                AddVolumeSlider(p, "SFX Volume",
                    PersistentDataManager.Instance?.SFXVolume ?? 0.8f,
                    v => PersistentDataManager.Instance?.SetSFXVolume(v));
                AddButton(p, "Back", "Back", BtnGray, ShowPausePanel, _theme?.buttonNav);
            });

        _settingsPanel.SetActive(false);
        _pauseCanvas.SetActive(false);
    }

    // ── Panel factory ─────────────────────────────────────────────────────────

    private GameObject BuildPanel(string name, Vector2 size, Transform parent,
                                   System.Action<Transform> populate)
    {
        GameObject panel      = new GameObject(name);
        panel.transform.SetParent(parent, false);
        RectTransform rt      = panel.AddComponent<RectTransform>();
        rt.anchorMin          = new Vector2(0.5f, 0.5f);
        rt.anchorMax          = new Vector2(0.5f, 0.5f);
        rt.pivot              = new Vector2(0.5f, 0.5f);
        rt.sizeDelta          = size;
        Image pauseImg        = panel.AddComponent<Image>();
        UIHelper.ApplyImage(pauseImg, _theme?.menuBackground, new Color(0.18f, 0.55f, 0.18f));

        VerticalLayoutGroup vl  = panel.AddComponent<VerticalLayoutGroup>();
        vl.padding              = new RectOffset(30, 30, 28, 28);
        vl.spacing              = 14f;
        vl.childControlHeight   = true;
        vl.childControlWidth    = true;
        vl.childForceExpandHeight = true;
        vl.childForceExpandWidth  = true;

        populate(panel.transform);
        return panel;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void AddTitle(Transform parent, string text, float size, Color color)
    {
        var lbl         = MakeLabel(parent, "Title", text, size);
        lbl.color       = color;
        lbl.fontStyle   = FontStyles.Bold;
    }

    private void AddButton(Transform parent, string goName, string label, Color color, UnityAction action, Sprite sprite = null)
    {
        GameObject go   = new GameObject(goName);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        Image img       = go.AddComponent<Image>();
        UIHelper.ApplyImage(img, sprite, color);

        Button btn      = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.colors = UIHelper.BtnColors(sprite, color, color * 1.3f, color * 0.65f);
        btn.onClick.AddListener(action);

        var lbl         = MakeLabel(go.transform, "Label", label, 28f);
        var lblRT       = lbl.GetComponent<RectTransform>();
        lblRT.anchorMin = Vector2.zero;
        lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = Vector2.zero;
        lblRT.offsetMax = Vector2.zero;
    }

    private void AddVolumeSlider(Transform parent, string label, float initial,
                                  System.Action<float> onChange)
    {
        // Label row
        var lbl       = MakeLabel(parent, label + "Lbl", label, 22f);
        lbl.alignment = TextAlignmentOptions.Left;
        lbl.color     = new Color(0.8f, 0.8f, 0.8f);

        // Slider + value label row
        GameObject row  = new GameObject(label + "Row");
        row.transform.SetParent(parent, false);
        row.AddComponent<RectTransform>();
        HorizontalLayoutGroup hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.childControlHeight    = true;
        hl.spacing               = 10f;

        // Value label
        var valLbl      = MakeLabel(row.transform, "Val", $"{initial:P0}", 20f);
        valLbl.color    = new Color(0.55f, 0.85f, 1f);
        var valLE       = valLbl.gameObject.AddComponent<LayoutElement>();
        valLE.preferredWidth = 52f;

        // Slider
        Slider slider   = BuildSlider(row.transform, initial, _theme, v =>
        {
            onChange?.Invoke(v);
            valLbl.text = $"{v:P0}";
        });
        slider.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
    }

    private static Slider BuildSlider(Transform parent, float initial, UITheme theme, UnityAction<float> onChange)
    {
        GameObject go   = new GameObject("Slider");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();

        // Background
        GameObject bg   = new GameObject("BG");
        bg.transform.SetParent(go.transform, false);
        RectTransform bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin  = new Vector2(0f, 0.3f);
        bgRT.anchorMax  = new Vector2(1f, 0.7f);
        bgRT.offsetMin  = Vector2.zero;
        bgRT.offsetMax  = Vector2.zero;
        Image bgImg     = bg.AddComponent<Image>();
        UIHelper.ApplyImage(bgImg, theme?.sliderBackground, new Color(0.18f, 0.18f, 0.18f));

        // Fill area
        GameObject fillArea = new GameObject("FillArea");
        fillArea.transform.SetParent(go.transform, false);
        RectTransform faRT  = fillArea.AddComponent<RectTransform>();
        faRT.anchorMin      = new Vector2(0f, 0.3f);
        faRT.anchorMax      = new Vector2(1f, 0.7f);
        faRT.offsetMin      = new Vector2(5f, 0f);
        faRT.offsetMax      = new Vector2(-15f, 0f);

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fillRT = fill.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = new Vector2(1f, 1f);
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        Image fillImg    = fill.AddComponent<Image>();
        UIHelper.ApplyImage(fillImg, theme?.sliderFill, new Color(0.2f, 0.6f, 1f));

        // Handle
        GameObject ha   = new GameObject("HandleArea");
        ha.transform.SetParent(go.transform, false);
        RectTransform haRT = ha.AddComponent<RectTransform>();
        haRT.anchorMin  = Vector2.zero;
        haRT.anchorMax  = Vector2.one;
        haRT.offsetMin  = new Vector2(10f, 0f);
        haRT.offsetMax  = new Vector2(-10f, 0f);

        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(ha.transform, false);
        RectTransform handleRT = handle.AddComponent<RectTransform>();
        handleRT.sizeDelta = new Vector2(20f, 0f);
        handleRT.anchorMin = Vector2.zero;
        handleRT.anchorMax = new Vector2(0f, 1f);
        Image handleImg    = handle.AddComponent<Image>();
        UIHelper.ApplyImage(handleImg, theme?.sliderHandle, Color.white);

        Slider slider       = go.AddComponent<Slider>();
        slider.minValue     = 0f;
        slider.maxValue     = 1f;
        slider.value        = initial;
        slider.fillRect     = fillRT;
        slider.handleRect   = handleRT;
        slider.targetGraphic = handleImg;
        slider.onValueChanged.AddListener(onChange);

        return slider;
    }

    private TextMeshProUGUI MakeLabel(Transform parent, string name, string text, float size)
    {
        var go    = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var tmp        = go.AddComponent<TextMeshProUGUI>();
        tmp.text       = text;
        tmp.fontSize   = size;
        tmp.alignment  = TextAlignmentOptions.Center;
        tmp.color      = Color.white;
        tmp.enableWordWrapping = false;
        if (_font != null) tmp.font = _font;
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
