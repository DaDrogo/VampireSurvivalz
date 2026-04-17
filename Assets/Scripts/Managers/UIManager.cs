using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the entire in-game HUD, built fully at runtime — no prefab needed.
///
/// Layout (1920×1080 reference):
///   • Top bar    — full width, 68 px:  resources (left) | state + timer (centre) | HP (right)
///   • Hotbar     — full width, 108 px, bottom: one clickable slot per building definition
///   • Info panel — 280 px wide, right edge, vertically centred: stats + upgrade button
///     visible whenever a hotbar slot is selected OR a placed building is clicked
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    // ── Runtime-built label references ────────────────────────────────────────

    private TextMeshProUGUI _woodText;
    private TextMeshProUGUI _metalText;
    private TextMeshProUGUI _stateText;
    private TextMeshProUGUI _timerText;
    private TextMeshProUGUI _hpText;

    // ── Hotbar ────────────────────────────────────────────────────────────────

    private Image[]           _hotbarBgs;
    private TextMeshProUGUI[] _hotbarNames;
    private TextMeshProUGUI[] _hotbarCosts;

    // ── Info / upgrade panel ──────────────────────────────────────────────────

    private GameObject      _infoPanel;
    private TextMeshProUGUI _infoBuildingName;
    private TextMeshProUGUI _infoDescription;
    private TextMeshProUGUI _infoStats;
    private TextMeshProUGUI _infoLevel;
    private Button          _upgradeButton;
    private TextMeshProUGUI _upgradeButtonText;

    // ── Selection state ───────────────────────────────────────────────────────

    private int            _selectedHotbarIndex = -1;
    private PlacedBuilding _selectedPlaced;

    private static readonly Color SlotNormal   = new Color(0.05f, 0.05f, 0.1f,  0.75f);
    private static readonly Color SlotSelected = new Color(0.15f, 0.50f, 1.00f, 0.85f);

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        BuildHUD();
    }

    private void Start()
    {
        SubscribeToEvents();
        RefreshHotbarContent();
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
            GameManager.Instance.OnTimerChanged            -= HandleTimerChanged;
            GameManager.Instance.OnEnemiesRemainingChanged -= HandleEnemiesRemainingChanged;
            GameManager.Instance.OnPlayerSpawned           -= BindHPLabel;
        }
        BuildingManager.OnSelectionChanged -= HandleHotbarSelectionChanged;
        PlacedBuilding.OnSelected          -= HandlePlacedBuildingSelected;
    }

    // ── Subscription ─────────────────────────────────────────────────────────

    private void SubscribeToEvents()
    {
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.OnWoodChanged  += HandleWoodChanged;
            ResourceManager.Instance.OnMetalChanged += HandleMetalChanged;
        }
        else Debug.LogWarning("UIManager: ResourceManager not found.");

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnStateChanged            += HandleStateChanged;
            GameManager.Instance.OnTimerChanged            += HandleTimerChanged;
            GameManager.Instance.OnEnemiesRemainingChanged += HandleEnemiesRemainingChanged;
            GameManager.Instance.OnPlayerSpawned           += BindHPLabel;
        }
        else Debug.LogWarning("UIManager: GameManager not found.");

        BuildingManager.OnSelectionChanged += HandleHotbarSelectionChanged;
        PlacedBuilding.OnSelected          += HandlePlacedBuildingSelected;
    }

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
        RefreshHotbar();
        RefreshInfoPanel();
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void HandleWoodChanged(int amount)
    {
        _woodText?.SetText("Wood: {0}", amount);
        if (_infoPanel != null && _infoPanel.activeSelf) RefreshInfoPanel();
    }

    private void HandleMetalChanged(int amount)
    {
        _metalText?.SetText("Metal: {0}", amount);
        if (_infoPanel != null && _infoPanel.activeSelf) RefreshInfoPanel();
    }

    private void HandleStateChanged(GameManager.GameState state)
    {
        switch (state)
        {
            case GameManager.GameState.Preparation:
                if (_stateText != null)
                { _stateText.SetText("PREPARATION"); _stateText.color = new Color(0.4f, 0.9f, 0.4f); }
                HandleTimerChanged(GameManager.Instance.TimeRemaining);
                break;

            case GameManager.GameState.Wave:
                if (_stateText != null)
                { _stateText.SetText("WAVE {0}", GameManager.Instance.WaveNumber); _stateText.color = new Color(1f, 0.35f, 0.2f); }
                HandleEnemiesRemainingChanged(GameManager.Instance.EnemiesRemaining);
                break;

            case GameManager.GameState.GameOver:
                if (_stateText != null)
                { _stateText.SetText("GAME OVER"); _stateText.color = new Color(0.9f, 0.15f, 0.15f); }
                _timerText?.SetText("");
                break;
        }
    }

    private void HandleTimerChanged(float remaining)
    {
        _timerText?.SetText("{0}s remaining", Mathf.CeilToInt(remaining));
        if (_timerText != null)
            _timerText.color = remaining <= 10f
                ? new Color(1f, 0.35f, 0.2f)
                : new Color(1f, 0.85f, 0.3f);
    }

    private void HandleEnemiesRemainingChanged(int count)
    {
        _timerText?.SetText(count == 1 ? "1 enemy left" : "{0} enemies left", count);
        if (_timerText != null) _timerText.color = new Color(1f, 0.85f, 0.3f);
    }

    private void HandleHotbarSelectionChanged(int index)
    {
        _selectedHotbarIndex = index;
        if (index >= 0) _selectedPlaced = null;  // new hotbar selection clears placed-building info
        RefreshHotbar();
        RefreshInfoPanel();
    }

    private void HandlePlacedBuildingSelected(PlacedBuilding pb)
    {
        _selectedPlaced      = pb;
        _selectedHotbarIndex = -1;
        RefreshHotbar();
        RefreshInfoPanel();
    }

    // ── Health bar ────────────────────────────────────────────────────────────

    private void BindHPLabel(PlayerController player)
    {
        if (player == null) return;

        // Remove any previous listener to avoid duplicate subscriptions on restart
        player.OnHealthChanged -= UpdateHPText;
        player.OnHealthChanged += UpdateHPText;
        UpdateHPText(player.CurrentHealth, player.MaxHealth);
    }

    private void UpdateHPText(float current, float max) =>
        _hpText?.SetText("{0:0} / {1:0}", current, max);

    // ── Hotbar ────────────────────────────────────────────────────────────────

    private void RefreshHotbar()
    {
        if (_hotbarBgs == null) return;
        for (int i = 0; i < _hotbarBgs.Length; i++)
            if (_hotbarBgs[i] != null)
                _hotbarBgs[i].color = i == _selectedHotbarIndex ? SlotSelected : SlotNormal;
    }

    /// <summary>Fills hotbar slot names and costs from BuildingManager (called in Start).</summary>
    private void RefreshHotbarContent()
    {
        if (_hotbarNames == null || BuildingManager.Instance == null) return;
        for (int i = 0; i < _hotbarNames.Length; i++)
        {
            if (i >= BuildingManager.Instance.BuildingCount) break;
            BuildingDefinition def = BuildingManager.Instance.GetDefinition(i);
            if (_hotbarNames[i] != null) _hotbarNames[i].SetText(def.buildingName);
            if (_hotbarCosts[i] != null) _hotbarCosts[i].SetText(
                $"{def.woodCost}W / {def.metalCost}M");
        }
    }

    // ── Info / upgrade panel ──────────────────────────────────────────────────

    private void RefreshInfoPanel()
    {
        if (_infoPanel == null) return;

        BuildingDefinition def       = null;
        int                level     = 0;
        bool               showStats = false;

        if (_selectedPlaced != null && _selectedPlaced.Definition != null)
        {
            def       = _selectedPlaced.Definition;
            level     = _selectedPlaced.Level;
            showStats = true;
        }
        else if (_selectedHotbarIndex >= 0 && BuildingManager.Instance != null &&
                 _selectedHotbarIndex < BuildingManager.Instance.BuildingCount)
        {
            def = BuildingManager.Instance.GetDefinition(_selectedHotbarIndex);
        }

        if (def == null) { _infoPanel.SetActive(false); return; }

        _infoPanel.SetActive(true);
        _infoBuildingName?.SetText(def.buildingName);
        _infoDescription?.SetText(def.description);

        // Stats
        var sb = new StringBuilder();
        sb.AppendLine($"Cost: {def.woodCost}W / {def.metalCost}M");
        if (showStats && _selectedPlaced != null)
        {
            if (_selectedPlaced.TryGetComponent(out Barricade b))
                sb.AppendLine($"HP: {b.CurrentHealth:0} / {b.MaxHealth:0}");
            if (_selectedPlaced.TryGetComponent(out Turret t))
            {
                sb.AppendLine($"HP:        {t.CurrentHealth:0} / {t.MaxHealth:0}");
                sb.AppendLine($"Range:     {t.DetectionRange:0.0}");
                sb.AppendLine($"Fire rate: {t.FireRate:0.00}/s");
            }
        }
        _infoStats?.SetText(sb.ToString());

        int maxLevel = def.upgrades?.Length ?? 0;
        _infoLevel?.SetText($"Level {level} / {maxLevel}");

        // Upgrade button
        if (_upgradeButton != null)
        {
            if (!showStats)
            {
                _upgradeButton.gameObject.SetActive(false);
            }
            else if (level >= maxLevel)
            {
                _upgradeButton.gameObject.SetActive(true);
                _upgradeButtonText?.SetText("MAX LEVEL");
                _upgradeButton.interactable = false;
            }
            else
            {
                BuildingUpgradeTier tier = def.upgrades[level];
                bool canAfford = ResourceManager.Instance != null
                    && ResourceManager.Instance.Wood  >= tier.woodCost
                    && ResourceManager.Instance.Metal >= tier.metalCost;

                _upgradeButton.gameObject.SetActive(true);
                _upgradeButton.interactable = canAfford;
                _upgradeButtonText?.SetText(
                    $"Upgrade  [{tier.label}]\n{tier.woodCost}W / {tier.metalCost}M");
            }
        }
    }

    private void OnUpgradeClicked()
    {
        _selectedPlaced?.TryUpgrade();
        // TryUpgrade fires PlacedBuilding.OnSelected which calls HandlePlacedBuildingSelected → RefreshInfoPanel
    }

    // ── HUD builder ───────────────────────────────────────────────────────────

    private void BuildHUD()
    {
        EnsureEventSystem();

        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font == null)
            Debug.LogWarning("UIManager: TMP font not found — import TMP Essential Resources via Window > TextMeshPro.");

        GameObject canvasGO        = new GameObject("HUD Canvas");
        Canvas canvas              = canvasGO.AddComponent<Canvas>();
        canvas.renderMode          = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder        = 100;

        CanvasScaler scaler        = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        BuildTopBar(canvasGO.transform, font);
        BuildHotbar(canvasGO.transform, font);
        BuildInfoPanel(canvasGO.transform, font);
    }

    // ── Top bar ───────────────────────────────────────────────────────────────

    private void BuildTopBar(Transform canvas, TMP_FontAsset font)
    {
        GameObject bar   = new GameObject("TopBar");
        bar.transform.SetParent(canvas, false);

        RectTransform rt = bar.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 1f);
        rt.anchorMax        = new Vector2(1f, 1f);
        rt.pivot            = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = new Vector2(0f, 68f);

        bar.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

        HorizontalLayoutGroup hlg  = bar.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(20, 20, 8, 8);
        hlg.spacing                = 0f;
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlHeight     = true;
        hlg.childControlWidth      = true;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = true;

        // Left: resources
        GameObject leftGO  = MakeGroup(bar.transform, "Resources");
        LayoutElement leftLE = leftGO.AddComponent<LayoutElement>();
        leftLE.flexibleWidth = 1f;
        VerticalLayoutGroup leftVLG  = leftGO.AddComponent<VerticalLayoutGroup>();
        leftVLG.childAlignment       = TextAnchor.MiddleLeft;
        leftVLG.childControlHeight   = true;
        leftVLG.childControlWidth    = true;
        leftVLG.childForceExpandHeight = true;
        leftVLG.childForceExpandWidth  = true;

        _woodText  = MakeLabel(leftGO.transform,  "WoodText",  "Wood: 0",  font, 24f);
        _metalText = MakeLabel(leftGO.transform, "MetalText", "Metal: 0", font, 24f);

        // Centre: state + timer
        GameObject centreGO  = MakeGroup(bar.transform, "StateTimer");
        LayoutElement centreLE = centreGO.AddComponent<LayoutElement>();
        centreLE.flexibleWidth = 1f;
        VerticalLayoutGroup centreVLG  = centreGO.AddComponent<VerticalLayoutGroup>();
        centreVLG.childAlignment       = TextAnchor.MiddleCenter;
        centreVLG.childControlHeight   = true;
        centreVLG.childControlWidth    = true;
        centreVLG.childForceExpandHeight = true;
        centreVLG.childForceExpandWidth  = true;

        _stateText = MakeLabel(centreGO.transform, "StateText", "PREPARATION", font, 26f,
                               new Color(0.4f, 0.9f, 0.4f), TextAlignmentOptions.Center);
        _timerText = MakeLabel(centreGO.transform, "TimerText", "",            font, 22f,
                               new Color(1f, 0.85f, 0.3f),  TextAlignmentOptions.Center);

        // Right: HP
        GameObject rightGO  = MakeGroup(bar.transform, "HP");
        LayoutElement rightLE = rightGO.AddComponent<LayoutElement>();
        rightLE.flexibleWidth = 1f;
        VerticalLayoutGroup rightVLG  = rightGO.AddComponent<VerticalLayoutGroup>();
        rightVLG.childAlignment       = TextAnchor.MiddleRight;
        rightVLG.childControlHeight   = true;
        rightVLG.childControlWidth    = true;
        rightVLG.childForceExpandHeight = true;
        rightVLG.childForceExpandWidth  = true;

        MakeLabel(rightGO.transform, "HPCaption", "HEALTH", font, 16f,
                  new Color(0.6f, 0.6f, 0.6f), TextAlignmentOptions.Right);
        _hpText = MakeLabel(rightGO.transform, "HPText", "--/--", font, 22f,
                            Color.white, TextAlignmentOptions.Right);
    }

    // ── Hotbar ────────────────────────────────────────────────────────────────

    private void BuildHotbar(Transform canvas, TMP_FontAsset font)
    {
        const int slots = 5;   // matches BuildingManager.Hotkeys.Length
        _hotbarBgs   = new Image[slots];
        _hotbarNames = new TextMeshProUGUI[slots];
        _hotbarCosts = new TextMeshProUGUI[slots];

        GameObject hotbar   = new GameObject("Hotbar");
        hotbar.transform.SetParent(canvas, false);

        RectTransform rt    = hotbar.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 0f);
        rt.anchorMax        = new Vector2(1f, 0f);
        rt.pivot            = new Vector2(0.5f, 0f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = new Vector2(0f, 108f);

        hotbar.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

        HorizontalLayoutGroup hlg  = hotbar.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(8, 8, 8, 8);
        hlg.spacing                = 4f;
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlHeight     = true;
        hlg.childControlWidth      = true;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = true;

        for (int i = 0; i < slots; i++)
        {
            int idx = i;   // capture for lambda

            GameObject slot   = new GameObject($"Slot_{i}");
            slot.transform.SetParent(hotbar.transform, false);

            Image slotBg      = slot.AddComponent<Image>();
            slotBg.color      = SlotNormal;
            _hotbarBgs[i]     = slotBg;

            Button btn        = slot.AddComponent<Button>();
            btn.targetGraphic = slotBg;
            ColorBlock cb     = btn.colors;
            cb.normalColor    = Color.white;
            cb.highlightedColor = new Color(0.85f, 0.85f, 1f);
            btn.colors        = cb;
            btn.onClick.AddListener(() => BuildingManager.Instance?.SelectBuilding(idx));

            VerticalLayoutGroup vlg    = slot.AddComponent<VerticalLayoutGroup>();
            vlg.padding                = new RectOffset(6, 6, 6, 6);
            vlg.spacing                = 2f;
            vlg.childAlignment         = TextAnchor.MiddleCenter;
            vlg.childControlHeight     = true;
            vlg.childControlWidth      = true;
            vlg.childForceExpandHeight = true;
            vlg.childForceExpandWidth  = true;

            MakeLabel(slot.transform, "Hotkey", $"[{i + 1}]", font, 16f,
                      new Color(0.65f, 0.65f, 0.65f), TextAlignmentOptions.Center);

            _hotbarNames[i] = MakeLabel(slot.transform, "Name", $"—",   font, 20f,
                                        Color.white, TextAlignmentOptions.Center);
            _hotbarCosts[i] = MakeLabel(slot.transform, "Cost", "",      font, 15f,
                                        new Color(0.8f, 0.9f, 0.55f), TextAlignmentOptions.Center);
        }
    }

    // ── Info panel ────────────────────────────────────────────────────────────

    private void BuildInfoPanel(Transform canvas, TMP_FontAsset font)
    {
        GameObject panel   = new GameObject("InfoPanel");
        panel.transform.SetParent(canvas, false);
        _infoPanel = panel;

        RectTransform rt   = panel.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(1f, 0.5f);
        rt.anchorMax        = new Vector2(1f, 0.5f);
        rt.pivot            = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-8f, 0f);
        rt.sizeDelta        = new Vector2(280f, 380f);

        panel.AddComponent<Image>().color = new Color(0.04f, 0.04f, 0.10f, 0.88f);

        VerticalLayoutGroup vlg    = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(14, 14, 14, 14);
        vlg.spacing                = 6f;
        vlg.childAlignment         = TextAnchor.UpperCenter;
        vlg.childControlHeight     = false;
        vlg.childControlWidth      = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth  = true;

        // Title
        _infoBuildingName = MakeLabel(panel.transform, "InfoName", "", font, 24f,
                                     new Color(1f, 0.85f, 0.3f), TextAlignmentOptions.Center);
        SetPrefHeight(_infoBuildingName.gameObject, 32f);

        // Separator
        GameObject sep    = new GameObject("Separator");
        sep.transform.SetParent(panel.transform, false);
        sep.AddComponent<Image>().color = new Color(0.35f, 0.35f, 0.35f);
        sep.AddComponent<LayoutElement>().preferredHeight = 1f;

        // Description
        _infoDescription = MakeLabel(panel.transform, "InfoDesc", "", font, 17f,
                                     new Color(0.72f, 0.72f, 0.72f), TextAlignmentOptions.Left);
        _infoDescription.enableWordWrapping = true;
        SetPrefHeight(_infoDescription.gameObject, 56f);

        // Stats
        _infoStats = MakeLabel(panel.transform, "InfoStats", "", font, 17f,
                               Color.white, TextAlignmentOptions.Left);
        _infoStats.enableWordWrapping = false;
        SetPrefHeight(_infoStats.gameObject, 100f);

        // Level indicator
        _infoLevel = MakeLabel(panel.transform, "InfoLevel", "", font, 18f,
                               new Color(0.45f, 0.85f, 1f), TextAlignmentOptions.Center);
        SetPrefHeight(_infoLevel.gameObject, 26f);

        // Upgrade button
        GameObject btnGO  = new GameObject("UpgradeBtn");
        btnGO.transform.SetParent(panel.transform, false);

        Image btnImg      = btnGO.AddComponent<Image>();
        btnImg.color      = new Color(0.12f, 0.55f, 0.12f, 1f);

        _upgradeButton    = btnGO.AddComponent<Button>();
        _upgradeButton.targetGraphic = btnImg;
        _upgradeButton.onClick.AddListener(OnUpgradeClicked);

        ColorBlock upgCB  = _upgradeButton.colors;
        upgCB.disabledColor = new Color(0.35f, 0.35f, 0.35f);
        _upgradeButton.colors = upgCB;

        btnGO.AddComponent<LayoutElement>().preferredHeight = 56f;

        // Text inside the button — fills the button's rect
        GameObject btnTextGO       = new GameObject("BtnText");
        btnTextGO.transform.SetParent(btnGO.transform, false);
        RectTransform btnTextRT    = btnTextGO.AddComponent<RectTransform>();
        btnTextRT.anchorMin        = Vector2.zero;
        btnTextRT.anchorMax        = Vector2.one;
        btnTextRT.offsetMin        = Vector2.zero;
        btnTextRT.offsetMax        = Vector2.zero;

        _upgradeButtonText         = btnTextGO.AddComponent<TextMeshProUGUI>();
        _upgradeButtonText.text    = "Upgrade";
        _upgradeButtonText.fontSize = 19f;
        _upgradeButtonText.alignment = TextAlignmentOptions.Center;
        _upgradeButtonText.color   = Color.white;
        if (font != null) _upgradeButtonText.font = font;

        panel.SetActive(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GameObject MakeGroup(Transform parent, string goName)
    {
        GameObject go = new GameObject(goName);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    private static TextMeshProUGUI MakeLabel(Transform parent, string goName, string text,
        TMP_FontAsset font, float fontSize = 24f, Color? color = null,
        TextAlignmentOptions alignment = TextAlignmentOptions.Left)
    {
        GameObject go = new GameObject(goName);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = fontSize;
        tmp.color     = color ?? Color.white;
        tmp.alignment = alignment;
        if (font != null) tmp.font = font;
        return tmp;
    }

    private static void SetPrefHeight(GameObject go, float h)
    {
        LayoutElement le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.preferredHeight = h;
    }

    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }
}
