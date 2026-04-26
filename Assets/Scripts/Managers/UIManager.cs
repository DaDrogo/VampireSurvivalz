using System.Collections;
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

    // ── Boss health bar ───────────────────────────────────────────────────────

    private GameObject      _bossPanel;
    private Image           _bossHealthFill;
    private TextMeshProUGUI _bossNameText;
    private TextMeshProUGUI _bossLevelText;
    private Image           _bossVulnGlow;

    private VampireEnemy    _boundVampire;

    [Header("UI Theme")]
    [SerializeField] private UITheme _theme;

    // ── Runtime-built label references ────────────────────────────────────────

    private TextMeshProUGUI _woodText;
    private TextMeshProUGUI _metalText;
    private TextMeshProUGUI _stateText;
    private TextMeshProUGUI _timerText;
    private TextMeshProUGUI _hpText;
    private TextMeshProUGUI _currencyText;

    // ── Hotbar ────────────────────────────────────────────────────────────────

    private Image[]           _hotbarBgs;
    private TextMeshProUGUI[] _hotbarNames;
    private TextMeshProUGUI[] _hotbarCosts;
    private Image[]           _hotbarLockOverlays;

    // ── Info / upgrade panel ──────────────────────────────────────────────────

    private GameObject      _infoPanel;
    private TextMeshProUGUI _infoBuildingName;
    private TextMeshProUGUI _infoDescription;
    private TextMeshProUGUI _infoStats;

    // Standard tier-upgrade section
    private GameObject      _upgradeSection;
    private TextMeshProUGUI _infoLevel;
    private Button          _upgradeButton;
    private TextMeshProUGUI _upgradeButtonText;

    // Choice-upgrade section (up to 3 options)
    private const int       MaxChoices = 3;
    private GameObject      _choiceSection;
    private Button[]        _choiceButtons    = new Button[MaxChoices];
    private TextMeshProUGUI[] _choiceNameTexts  = new TextMeshProUGUI[MaxChoices];
    private TextMeshProUGUI[] _choiceCostTexts  = new TextMeshProUGUI[MaxChoices];

    // ── Selection state ───────────────────────────────────────────────────────

    private int            _selectedHotbarIndex = -1;
    private PlacedBuilding _selectedPlaced;

    private TextMeshProUGUI _toastText;
    private CanvasGroup     _toastGroup;
    private Coroutine       _toastCoroutine;

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
        if (PersistentDataManager.Instance != null)
            PersistentDataManager.Instance.OnCurrencyChanged -= HandleCurrencyChanged;
        BuildingManager.OnSelectionChanged -= HandleHotbarSelectionChanged;
        BuildingManager.OnBuildingPlaced   -= OnAnyBuildingPlaced;
        PlacedBuilding.OnSelected          -= HandlePlacedBuildingSelected;

        if (_boundVampire != null)
        {
            _boundVampire.OnHealthChanged -= OnVampireHealthChanged;
            _boundVampire.OnLevelChanged  -= OnVampireLevelChanged;
        }
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

        if (PersistentDataManager.Instance != null)
        {
            PersistentDataManager.Instance.OnCurrencyChanged += HandleCurrencyChanged;
            HandleCurrencyChanged(PersistentDataManager.Instance.TotalCurrency);
        }
        BuildingManager.OnSelectionChanged += HandleHotbarSelectionChanged;
        BuildingManager.OnBuildingPlaced   += OnAnyBuildingPlaced;
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
        RefreshHotbarLocks();
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

    private void HandleCurrencyChanged(int coins) =>
        _currencyText?.SetText("Coins: {0}", coins);

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
    public void RefreshHotbarContent()
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
            if (_selectedPlaced.TryGetComponent(out Citadel citadel))
            {
                sb.AppendLine($"HP:       {citadel.CurrentHealth:0} / {citadel.MaxHealth:0}");
                sb.AppendLine($"Tier:     {citadel.Tier} / {citadel.MaxTier}");
                sb.AppendLine($"Output:   {citadel.ProductionOutput}");
                sb.AppendLine($"Amount:   {citadel.ProductionAmount} / {citadel.ProductionInterval:0.0}s");
            }
            else
            {
                if (_selectedPlaced.TryGetComponent(out Barricade b))
                    sb.AppendLine($"HP: {b.CurrentHealth:0} / {b.MaxHealth:0}");
                if (_selectedPlaced.TryGetComponent(out Turret t))
                {
                    sb.AppendLine($"HP:        {t.CurrentHealth:0} / {t.MaxHealth:0}");
                    sb.AppendLine($"Range:     {t.DetectionRange:0.0}");
                    sb.AppendLine($"Fire rate: {t.FireRate:0.00}/s");
                }
                if (_selectedPlaced.TryGetComponent(out ResourceProducer rp))
                {
                    sb.AppendLine($"HP:       {rp.CurrentHealth:0} / {rp.MaxHealth:0}");
                    sb.AppendLine($"Output:   {rp.OutputType}");
                    sb.AppendLine($"Amount:   {rp.AmountPerCycle} / cycle");
                    sb.AppendLine($"Interval: {rp.ProductionInterval:0.0}s");
                }
            }
        }
        _infoStats?.SetText(sb.ToString());

        // Decide which upgrade section to show
        bool hasChoices = showStats
                       && def.upgradeChoices != null
                       && def.upgradeChoices.Length > 0;

        if (_upgradeSection != null) _upgradeSection.SetActive(!hasChoices);
        if (_choiceSection   != null) _choiceSection.SetActive(hasChoices);

        if (hasChoices)
        {
            // Populate choice buttons
            for (int i = 0; i < MaxChoices; i++)
            {
                if (_choiceButtons[i] == null) continue;

                if (i < def.upgradeChoices.Length)
                {
                    BuildingUpgradeChoice ch = def.upgradeChoices[i];
                    _choiceButtons[i].gameObject.SetActive(true);

                    bool canAfford = ResourceManager.Instance != null
                        && ResourceManager.Instance.Wood  >= ch.woodCost
                        && ResourceManager.Instance.Metal >= ch.metalCost;
                    _choiceButtons[i].interactable = canAfford;

                    string desc = string.IsNullOrEmpty(ch.description) ? "" : $"\n<size=14><color=#aaaaaa>{ch.description}</color></size>";
                    _choiceNameTexts[i]?.SetText($"{ch.label}{desc}");
                    _choiceCostTexts[i]?.SetText($"{ch.woodCost}W / {ch.metalCost}M");
                }
                else
                {
                    _choiceButtons[i].gameObject.SetActive(false);
                }
            }
        }
        else
        {
            // Citadel uses its own tier system, not BuildingDefinition.upgrades
            if (showStats && _selectedPlaced != null &&
                _selectedPlaced.TryGetComponent(out Citadel cit))
            {
                _infoLevel?.SetText($"Tier {cit.Tier} / {cit.MaxTier}");
                if (_upgradeButton != null)
                {
                    CitadelTierData next = cit.GetNextTierData();
                    if (next == null)
                    {
                        _upgradeButton.gameObject.SetActive(true);
                        _upgradeButtonText?.SetText("MAX TIER");
                        _upgradeButton.interactable = false;
                    }
                    else
                    {
                        bool canAfford = ResourceManager.Instance != null
                            && ResourceManager.Instance.Wood  >= next.woodCost
                            && ResourceManager.Instance.Metal >= next.metalCost;
                        _upgradeButton.gameObject.SetActive(true);
                        _upgradeButton.interactable = canAfford;
                        _upgradeButtonText?.SetText(
                            $"Upgrade  [{next.label}]\n{next.woodCost}W / {next.metalCost}M");
                    }
                }
            }
            else
            {
                // Normal tier-upgrade section
                int maxLevel = def.upgrades?.Length ?? 0;
                _infoLevel?.SetText($"Level {level} / {maxLevel}");

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
        }
    }

    private void OnUpgradeClicked()
    {
        _selectedPlaced?.TryUpgrade();
        // TryUpgrade fires PlacedBuilding.OnSelected which calls HandlePlacedBuildingSelected → RefreshInfoPanel
    }

    private void OnChoiceClicked(int index)
    {
        if (_selectedPlaced == null) return;
        BuildingUpgradeChoice[] choices = _selectedPlaced.Definition?.upgradeChoices;
        if (choices == null || index >= choices.Length) return;
        _selectedPlaced.TryUpgradeToChoice(choices[index]);
        // TryUpgradeToChoice destroys the building → OnSelected(null) → panel closes
    }

    // ── Citadel lock ──────────────────────────────────────────────────────────

    private void OnAnyBuildingPlaced(PlacedBuilding _) => RefreshHotbarLocks();

    private void RefreshHotbarLocks()
    {
        if (_hotbarLockOverlays == null || BuildingManager.Instance == null) return;
        bool citadelMissing = Citadel.Instance == null;
        for (int i = 0; i < _hotbarLockOverlays.Length; i++)
        {
            if (_hotbarLockOverlays[i] == null) continue;
            bool locked = citadelMissing
                       && i < BuildingManager.Instance.BuildingCount
                       && !BuildingManager.Instance.GetDefinition(i).isCitadel;
            _hotbarLockOverlays[i].gameObject.SetActive(locked);
        }
    }

    // ── Toast notification ────────────────────────────────────────────────────

    public void ShowToast(string message, Color color)
    {
        if (_toastGroup == null) return;
        if (_toastCoroutine != null) StopCoroutine(_toastCoroutine);
        _toastCoroutine = StartCoroutine(ToastRoutine(message, color));
    }

    private IEnumerator ToastRoutine(string message, Color color)
    {
        _toastText.text   = message;
        _toastText.color  = color;
        _toastGroup.alpha = 1f;

        float elapsed = 0f;
        while (elapsed < 1.8f) { elapsed += Time.unscaledDeltaTime; yield return null; }

        elapsed = 0f;
        while (elapsed < 0.5f)
        {
            _toastGroup.alpha = 1f - elapsed / 0.5f;
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        _toastGroup.alpha = 0f;
        _toastCoroutine   = null;
    }

    private void BuildToast(Transform canvas, TMP_FontAsset font)
    {
        GameObject toastGO  = new GameObject("Toast");
        toastGO.transform.SetParent(canvas, false);

        RectTransform rt    = toastGO.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, 120f);
        rt.sizeDelta        = new Vector2(700f, 64f);

        toastGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.80f);
        _toastGroup       = toastGO.AddComponent<CanvasGroup>();
        _toastGroup.alpha = 0f;

        GameObject textGO    = new GameObject("ToastText");
        textGO.transform.SetParent(toastGO.transform, false);
        RectTransform textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin     = Vector2.zero;
        textRT.anchorMax     = Vector2.one;
        textRT.offsetMin     = new Vector2(20f, 0f);
        textRT.offsetMax     = new Vector2(-20f, 0f);

        _toastText           = textGO.AddComponent<TextMeshProUGUI>();
        _toastText.fontSize  = 28f;
        _toastText.alignment = TextAlignmentOptions.Center;
        _toastText.fontStyle = FontStyles.Bold;
        _toastText.color     = Color.white;
        if (font != null) _toastText.font = font;
    }

    // ── HUD builder ───────────────────────────────────────────────────────────

    private void BuildHUD()
    {
        EnsureEventSystem();

        TMP_FontAsset font = _theme?.font != null
            ? _theme.font
            : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
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
        BuildBossHealthBar(canvasGO.transform, font);
        BuildToast(canvasGO.transform, font);
    }

    // ── Boss health bar ───────────────────────────────────────────────────────

    private void BuildBossHealthBar(Transform canvas, TMP_FontAsset font)
    {
        // Panel — bottom-centre, above hotbar
        _bossPanel = new GameObject("BossHealthBar");
        _bossPanel.transform.SetParent(canvas, false);

        RectTransform rt  = _bossPanel.AddComponent<RectTransform>();
        rt.anchorMin      = new Vector2(0.5f, 0f);
        rt.anchorMax      = new Vector2(0.5f, 0f);
        rt.pivot          = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 120f);   // above hotbar
        rt.sizeDelta      = new Vector2(500f, 60f);

        _bossPanel.AddComponent<Image>().color = new Color(0.05f, 0f, 0.1f, 0.85f);

        // Vulnerability glow (full-panel colour overlay)
        GameObject glowGO = new GameObject("VulnGlow");
        glowGO.transform.SetParent(_bossPanel.transform, false);
        RectTransform grt  = glowGO.AddComponent<RectTransform>();
        grt.anchorMin      = Vector2.zero;
        grt.anchorMax      = Vector2.one;
        grt.offsetMin      = grt.offsetMax = Vector2.zero;
        _bossVulnGlow      = glowGO.AddComponent<Image>();
        _bossVulnGlow.color = new Color(1f, 1f, 0f, 0f);

        // Name label
        GameObject nameGO = new GameObject("BossName");
        nameGO.transform.SetParent(_bossPanel.transform, false);
        RectTransform nrt  = nameGO.AddComponent<RectTransform>();
        nrt.anchorMin      = new Vector2(0f, 0.5f);
        nrt.anchorMax      = new Vector2(0.65f, 1f);
        nrt.offsetMin      = new Vector2(8f, 0f);
        nrt.offsetMax      = Vector2.zero;
        _bossNameText      = nameGO.AddComponent<TextMeshProUGUI>();
        _bossNameText.font = font;
        _bossNameText.text = "The Vampire";
        _bossNameText.fontSize    = 18f;
        _bossNameText.fontStyle   = FontStyles.Bold;
        _bossNameText.color       = new Color(0.85f, 0.5f, 1f);
        _bossNameText.alignment   = TextAlignmentOptions.MidlineLeft;

        // Level badge
        GameObject lvlGO  = new GameObject("BossLevel");
        lvlGO.transform.SetParent(_bossPanel.transform, false);
        RectTransform lrt  = lvlGO.AddComponent<RectTransform>();
        lrt.anchorMin      = new Vector2(0.65f, 0.5f);
        lrt.anchorMax      = new Vector2(1f, 1f);
        lrt.offsetMin      = Vector2.zero;
        lrt.offsetMax      = new Vector2(-8f, 0f);
        _bossLevelText     = lvlGO.AddComponent<TextMeshProUGUI>();
        _bossLevelText.font = font;
        _bossLevelText.text = "Lv 1";
        _bossLevelText.fontSize   = 16f;
        _bossLevelText.color      = new Color(1f, 0.9f, 0.4f);
        _bossLevelText.alignment  = TextAlignmentOptions.MidlineRight;

        // Health bar background
        GameObject barBg = new GameObject("HealthBg");
        barBg.transform.SetParent(_bossPanel.transform, false);
        RectTransform brt  = barBg.AddComponent<RectTransform>();
        brt.anchorMin      = new Vector2(0f, 0f);
        brt.anchorMax      = new Vector2(1f, 0.5f);
        brt.offsetMin      = new Vector2(8f, 6f);
        brt.offsetMax      = new Vector2(-8f, 0f);
        barBg.AddComponent<Image>().color = new Color(0.15f, 0.05f, 0.2f);

        // Health fill
        GameObject fillGO  = new GameObject("HealthFill");
        fillGO.transform.SetParent(barBg.transform, false);
        RectTransform frt   = fillGO.AddComponent<RectTransform>();
        frt.anchorMin       = Vector2.zero;
        frt.anchorMax       = Vector2.one;
        frt.offsetMin       = frt.offsetMax = Vector2.zero;
        _bossHealthFill     = fillGO.AddComponent<Image>();
        _bossHealthFill.color = new Color(0.7f, 0.1f, 0.9f);
        _bossHealthFill.type  = Image.Type.Filled;
        _bossHealthFill.fillMethod  = Image.FillMethod.Horizontal;
        _bossHealthFill.fillOrigin  = (int)Image.OriginHorizontal.Left;
        _bossHealthFill.fillAmount  = 1f;

        _bossPanel.SetActive(false);
    }

    private void Update()
    {
        // Late-bind to VampireEnemy when it first becomes available
        if (_boundVampire == null && VampireEnemy.Instance != null)
        {
            _boundVampire = VampireEnemy.Instance;
            _boundVampire.OnHealthChanged += OnVampireHealthChanged;
            _boundVampire.OnLevelChanged  += OnVampireLevelChanged;
        }

        if (_bossPanel == null) return;

        bool wasVisible    = _bossPanel.activeSelf;
        bool vampireActive = _boundVampire != null && _boundVampire.gameObject.activeSelf;
        _bossPanel.SetActive(vampireActive);

        if (vampireActive && !wasVisible)
            OnVampireHealthChanged(_boundVampire.CurrentHealth, _boundVampire.MaxHealth);

        if (vampireActive && _bossVulnGlow != null)
        {
            float alpha = _boundVampire.IsVulnerable ? 0.25f + 0.15f * Mathf.Sin(Time.time * 6f) : 0f;
            _bossVulnGlow.color = new Color(1f, 1f, 0f, alpha);
        }
    }

    private void OnVampireHealthChanged(float current, float max)
    {
        if (_bossHealthFill != null)
            _bossHealthFill.fillAmount = max > 0f ? current / max : 0f;
    }

    private void OnVampireLevelChanged(int level)
    {
        if (_bossLevelText != null)
            _bossLevelText.text = $"Lv {level}";
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
        hlg.childForceExpandWidth  = false;

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
        _currencyText = MakeLabel(rightGO.transform, "CurrencyText", "Coins: 0", font, 18f,
                                  new Color(1f, 0.85f, 0.2f), TextAlignmentOptions.Right);

        // Pause button — fixed-width slot at the right end of the top bar
        GameObject pauseBtnGO = new GameObject("PauseButton");
        pauseBtnGO.transform.SetParent(bar.transform, false);

        LayoutElement pauseLE   = pauseBtnGO.AddComponent<LayoutElement>();
        pauseLE.minWidth        = 52f;
        pauseLE.preferredWidth  = 52f;
        pauseLE.flexibleWidth   = 0f;

        Image pauseImg          = pauseBtnGO.AddComponent<Image>();
        UIHelper.ApplyImage(pauseImg, _theme?.buttonSetting, new Color(0.18f, 0.18f, 0.22f, 0.85f));

        Button pauseBtn         = pauseBtnGO.AddComponent<Button>();
        pauseBtn.targetGraphic  = pauseImg;
        pauseBtn.colors         = UIHelper.BtnColors(_theme?.buttonSecondary,
                                      Color.white, new Color(0.7f, 0.85f, 1f), new Color(0.45f, 0.55f, 0.7f));
        pauseBtn.onClick.AddListener(() => PauseMenuManager.Instance?.Pause());
    }

    // ── Hotbar ────────────────────────────────────────────────────────────────

    private void BuildHotbar(Transform canvas, TMP_FontAsset font)
    {
        const int slots = 5;   // matches BuildingManager.Hotkeys.Length
        _hotbarBgs          = new Image[slots];
        _hotbarNames        = new TextMeshProUGUI[slots];
        _hotbarCosts        = new TextMeshProUGUI[slots];
        _hotbarLockOverlays = new Image[slots];

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

            // Dark overlay shown when this slot is locked (citadel not yet placed)
            GameObject lockGO    = new GameObject("LockOverlay");
            lockGO.transform.SetParent(slot.transform, false);
            RectTransform lockRT = lockGO.AddComponent<RectTransform>();
            lockRT.anchorMin     = Vector2.zero;
            lockRT.anchorMax     = Vector2.one;
            lockRT.offsetMin     = lockRT.offsetMax = Vector2.zero;
            lockGO.AddComponent<LayoutElement>().ignoreLayout = true;
            Image lockImg         = lockGO.AddComponent<Image>();
            lockImg.color         = new Color(0f, 0f, 0f, 0.60f);
            lockImg.raycastTarget = false;
            _hotbarLockOverlays[i] = lockImg;
            lockGO.SetActive(false);
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
        rt.sizeDelta        = new Vector2(280f, 480f);

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

        // ── Standard tier-upgrade section ─────────────────────────────────────
        _upgradeSection = MakeGroup(panel.transform, "UpgradeSection");
        {
            VerticalLayoutGroup uVLG    = _upgradeSection.AddComponent<VerticalLayoutGroup>();
            uVLG.spacing                = 6f;
            uVLG.childAlignment         = TextAnchor.UpperCenter;
            uVLG.childControlHeight     = false;
            uVLG.childControlWidth      = true;
            uVLG.childForceExpandHeight = false;
            uVLG.childForceExpandWidth  = true;
            SetPrefHeight(_upgradeSection, 88f);   // 26 + 6 + 56

            _infoLevel = MakeLabel(_upgradeSection.transform, "InfoLevel", "", font, 18f,
                                   new Color(0.45f, 0.85f, 1f), TextAlignmentOptions.Center);
            SetPrefHeight(_infoLevel.gameObject, 26f);

            GameObject btnGO = new GameObject("UpgradeBtn");
            btnGO.transform.SetParent(_upgradeSection.transform, false);
            Image btnImg         = btnGO.AddComponent<Image>();
            UIHelper.ApplyImage(btnImg, _theme?.buttonNav, new Color(0.12f, 0.55f, 0.12f, 1f));
            _upgradeButton       = btnGO.AddComponent<Button>();
            _upgradeButton.targetGraphic = btnImg;
            _upgradeButton.onClick.AddListener(OnUpgradeClicked);
            ColorBlock upgCB     = UIHelper.BtnColors(_theme?.buttonNav,
                new Color(0.12f, 0.55f, 0.12f), new Color(0.18f, 0.75f, 0.18f), new Color(0.08f, 0.38f, 0.08f));
            upgCB.disabledColor  = new Color(0.35f, 0.35f, 0.35f);
            _upgradeButton.colors = upgCB;
            btnGO.AddComponent<LayoutElement>().preferredHeight = 56f;

            GameObject btnTextGO    = new GameObject("BtnText");
            btnTextGO.transform.SetParent(btnGO.transform, false);
            RectTransform btnTextRT = btnTextGO.AddComponent<RectTransform>();
            btnTextRT.anchorMin     = Vector2.zero;
            btnTextRT.anchorMax     = Vector2.one;
            btnTextRT.offsetMin     = Vector2.zero;
            btnTextRT.offsetMax     = Vector2.zero;
            _upgradeButtonText      = btnTextGO.AddComponent<TextMeshProUGUI>();
            _upgradeButtonText.text = "Upgrade";
            _upgradeButtonText.fontSize   = 19f;
            _upgradeButtonText.alignment  = TextAlignmentOptions.Center;
            _upgradeButtonText.color      = Color.white;
            if (font != null) _upgradeButtonText.font = font;
        }

        // ── Choice-upgrade section ────────────────────────────────────────────
        _choiceSection = MakeGroup(panel.transform, "ChoiceSection");
        {
            VerticalLayoutGroup cVLG    = _choiceSection.AddComponent<VerticalLayoutGroup>();
            cVLG.spacing                = 5f;
            cVLG.childAlignment         = TextAnchor.UpperCenter;
            cVLG.childControlHeight     = false;
            cVLG.childControlWidth      = true;
            cVLG.childForceExpandHeight = false;
            cVLG.childForceExpandWidth  = true;
            SetPrefHeight(_choiceSection, 24f + MaxChoices * (62f + 5f));

            MakeLabel(_choiceSection.transform, "ChoiceHeader", "Upgrade to:", font, 16f,
                      new Color(0.6f, 0.6f, 0.6f), TextAlignmentOptions.Center);

            for (int i = 0; i < MaxChoices; i++)
            {
                int idx = i;   // capture for lambda

                GameObject cBtn   = new GameObject($"ChoiceBtn_{i}");
                cBtn.transform.SetParent(_choiceSection.transform, false);
                Image cImg        = cBtn.AddComponent<Image>();
                UIHelper.ApplyImage(cImg, _theme?.buttonNav, new Color(0.12f, 0.38f, 0.55f, 1f));
                Button btn        = cBtn.AddComponent<Button>();
                btn.targetGraphic = cImg;
                btn.onClick.AddListener(() => OnChoiceClicked(idx));
                ColorBlock cb     = UIHelper.BtnColors(_theme?.buttonNav,
                    new Color(0.12f, 0.38f, 0.55f), new Color(0.18f, 0.52f, 0.75f), new Color(0.08f, 0.25f, 0.38f));
                cb.disabledColor  = new Color(0.25f, 0.25f, 0.25f);
                btn.colors        = cb;
                cBtn.AddComponent<LayoutElement>().preferredHeight = 62f;
                _choiceButtons[i] = btn;

                VerticalLayoutGroup bVLG    = cBtn.AddComponent<VerticalLayoutGroup>();
                bVLG.padding                = new RectOffset(8, 8, 6, 6);
                bVLG.spacing                = 2f;
                bVLG.childAlignment         = TextAnchor.MiddleLeft;
                bVLG.childControlHeight     = true;
                bVLG.childControlWidth      = true;
                bVLG.childForceExpandHeight = true;
                bVLG.childForceExpandWidth  = true;

                _choiceNameTexts[i] = MakeLabel(cBtn.transform, "ChoiceName", "", font, 16f,
                                                Color.white, TextAlignmentOptions.Left);
                _choiceNameTexts[i].enableWordWrapping = true;

                _choiceCostTexts[i] = MakeLabel(cBtn.transform, "ChoiceCost", "", font, 14f,
                                                new Color(0.8f, 0.9f, 0.55f), TextAlignmentOptions.Left);
            }
        }

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
