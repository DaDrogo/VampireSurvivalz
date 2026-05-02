using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the entire in-game HUD, built fully at runtime — no prefab needed.
///
/// Layout (1920×1080 reference):
///   • Top bar      — full width, 90 px: resources (left) | state+timer (centre) | HP+coins (right) | pause
///   • Action bar   — full width, 80 px, bottom: hammer button + context area
///   • Build menu   — full width, slides up above action bar when hammer is pressed
///   • Choice panel — slides up above action bar when upgrade choices are shown
///   • Boss bar     — 500 px wide, below top bar, centre
///   • Toast        — centre-screen notification
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("UI Theme")]
    [SerializeField] private UITheme _theme;

    // ── Top bar labels ────────────────────────────────────────────────────────
    private TextMeshProUGUI _woodText;
    private TextMeshProUGUI _metalText;
    private TextMeshProUGUI _stateText;
    private TextMeshProUGUI _timerText;
    private TextMeshProUGUI _hpText;
    private TextMeshProUGUI _currencyText;
    private Image           _stateIcon;

    // ── Boss health bar ───────────────────────────────────────────────────────
    private GameObject      _bossPanel;
    private Slider          _bossSlider;
    private TextMeshProUGUI _bossNameText;
    private TextMeshProUGUI _bossLevelText;
    private Image           _bossVulnGlow;
    private VampireEnemy    _boundVampire;

    // ── Build menu overlay ────────────────────────────────────────────────────
    private GameObject      _buildMenuPanel;
    private RectTransform   _buildMenuRT;
    private GameObject      _buildMenuBackdrop;
    private Transform       _buildMenuContent;   // parent of icon buttons
    private readonly List<Image>  _buildMenuIcons = new();
    private readonly List<Image>  _buildMenuLockOverlays = new();
    private readonly List<Button> _buildMenuButtons = new();
    private bool            _buildMenuOpen;
    private Coroutine       _buildMenuAnim;
    private const float     BuildMenuHeight = 160f;
    private const float     ActionBarHeight = 80f;

    // ── Upgrade choice panel ──────────────────────────────────────────────────
    private GameObject      _choicePanel;
    private RectTransform   _choicePanelRT;
    private Transform       _choicePanelContent;
    private TextMeshProUGUI _choicePanelHeader;
    private bool            _choicePanelOpen;
    private Coroutine       _choicePanelAnim;
    private const float     ChoicePanelHeight = 130f;

    // ── Action bar ────────────────────────────────────────────────────────────
    private GameObject      _actionBar;
    private Image           _buildBtnImage;      // hammer button background (tint when active)

    // Placing page
    private GameObject      _placingPage;
    private Image           _placingIcon;
    private TextMeshProUGUI _placingLabel;

    // Building-selected page
    private GameObject      _infoPage;
    private Image           _infoBuildingIcon;
    private TextMeshProUGUI _infoBuildingName;
    private Image           _infoHpFill;
    private TextMeshProUGUI _infoHpText;
    private Button          _repairBtn;
    private Button          _upgradeBtn;
    private Button          _destroyBtn;

    // ── Player reference ──────────────────────────────────────────────────────
    private PlayerController _player;

    // ── Selection state ───────────────────────────────────────────────────────
    private PlacedBuilding _selectedPlaced;
    private Building       _selectedBuildingHpWatcher;

    // ── Toast ─────────────────────────────────────────────────────────────────
    private TextMeshProUGUI _toastText;
    private CanvasGroup     _toastGroup;
    private Coroutine       _toastCoroutine;

    // ── HUD root (SafeArea transform — shared with DayNightManager widget) ────
    private Transform _hudRoot;

    // ── Colours ───────────────────────────────────────────────────────────────
    private static readonly Color ColAffordable   = Color.white;
    private static readonly Color ColUnaffordable = new Color(0.35f, 0.35f, 0.35f, 1f);
    private static readonly Color ColHpHigh       = new Color(0.18f, 0.78f, 0.18f);
    private static readonly Color ColHpMid        = new Color(0.95f, 0.75f, 0.10f);
    private static readonly Color ColHpLow        = new Color(0.90f, 0.18f, 0.18f);

    // ─────────────────────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        BuildHUD();
    }

    private void Start()
    {
        SubscribeToEvents();
        PopulateBuildMenu();
        RefreshAll();

        // Inject the day/night widget into the HUD canvas so it shares the same
        // CanvasScaler and SafeArea as the top bar, keeping alignment consistent.
        if (_hudRoot != null)
        {
            TMP_FontAsset font = _theme?.font != null
                ? _theme.font
                : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            DayNightManager.Instance?.BuildWidget(_hudRoot, font);
        }
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

        BuildingManager.OnSelectionChanged -= HandleBuildingManagerSelectionChanged;
        BuildingManager.OnBuildingPlaced   -= OnAnyBuildingPlaced;
        PlacedBuilding.OnSelected          -= HandlePlacedBuildingSelected;

        if (_boundVampire != null)
        {
            _boundVampire.OnHealthChanged -= OnVampireHealthChanged;
            _boundVampire.OnLevelChanged  -= OnVampireLevelChanged;
        }
        if (_selectedBuildingHpWatcher != null)
            _selectedBuildingHpWatcher.OnHealthChanged -= OnSelectedBuildingHealthChanged;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Subscriptions
    // ─────────────────────────────────────────────────────────────────────────

    private void SubscribeToEvents()
    {
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.OnWoodChanged  += HandleWoodChanged;
            ResourceManager.Instance.OnMetalChanged += HandleMetalChanged;
        }
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnStateChanged            += HandleStateChanged;
            GameManager.Instance.OnTimerChanged            += HandleTimerChanged;
            GameManager.Instance.OnEnemiesRemainingChanged += HandleEnemiesRemainingChanged;
            GameManager.Instance.OnPlayerSpawned           += BindHPLabel;
        }
        if (PersistentDataManager.Instance != null)
        {
            PersistentDataManager.Instance.OnCurrencyChanged += HandleCurrencyChanged;
            HandleCurrencyChanged(PersistentDataManager.Instance.TotalCurrency);
        }
        BuildingManager.OnSelectionChanged += HandleBuildingManagerSelectionChanged;
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
        RefreshBuildMenuAffordability();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Event handlers
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleWoodChanged(int amount)
    {
        _woodText?.SetText("{0}", amount);
        RefreshBuildMenuAffordability();
    }

    private void HandleMetalChanged(int amount)
    {
        _metalText?.SetText("{0}", amount);
        RefreshBuildMenuAffordability();
    }

    private void HandleStateChanged(GameManager.GameState state)
    {
        switch (state)
        {
            case GameManager.GameState.Preparation:
                if (_stateText != null) { _stateText.SetText("PREPARATION"); _stateText.color = new Color(0.4f, 0.9f, 0.4f); }
                if (_stateIcon != null) ApplyIconOrColor(_stateIcon, _theme?.iconPrep, new Color(0.4f, 0.9f, 0.4f, 0.9f));
                HandleTimerChanged(GameManager.Instance.TimeRemaining);
                break;
            case GameManager.GameState.Wave:
                if (_stateText != null) { _stateText.SetText("WAVE {0}", GameManager.Instance.WaveNumber); _stateText.color = new Color(1f, 0.35f, 0.2f); }
                if (_stateIcon != null) ApplyIconOrColor(_stateIcon, _theme?.iconWave, new Color(1f, 0.35f, 0.2f, 0.9f));
                HandleEnemiesRemainingChanged(GameManager.Instance.EnemiesRemaining);
                break;
            case GameManager.GameState.GameOver:
                if (_stateText != null) { _stateText.SetText("GAME OVER"); _stateText.color = new Color(0.9f, 0.15f, 0.15f); }
                _timerText?.SetText("");
                break;
        }
    }

    private void HandleTimerChanged(float remaining)
    {
        _timerText?.SetText("{0}s", Mathf.CeilToInt(remaining));
        if (_timerText != null)
            _timerText.color = remaining <= 10f ? new Color(1f, 0.35f, 0.2f) : new Color(1f, 0.85f, 0.3f);
    }

    private void HandleCurrencyChanged(int coins) => _currencyText?.SetText("{0}", coins);

    private void HandleEnemiesRemainingChanged(int count)
    {
        _timerText?.SetText(count == 1 ? "1 left" : "{0} left", count);
        if (_timerText != null) _timerText.color = new Color(1f, 0.85f, 0.3f);
    }

    private void HandleBuildingManagerSelectionChanged(int index)
    {
        if (index >= 0)
        {
            // Entered placement mode
            HideBuildMenu();
            HideChoicePanel();
            ShowPlacingPage(index);
            DeselectBuilding();
        }
        else
        {
            // Cancelled placement
            ShowDefaultPage();
        }
        HighlightBuildButton(_buildMenuOpen);
    }

    private void HandlePlacedBuildingSelected(PlacedBuilding pb)
    {
        if (_selectedBuildingHpWatcher != null)
            _selectedBuildingHpWatcher.OnHealthChanged -= OnSelectedBuildingHealthChanged;

        _selectedPlaced = pb;
        _selectedBuildingHpWatcher = pb != null ? pb.GetComponent<Building>() : null;

        if (_selectedBuildingHpWatcher != null)
            _selectedBuildingHpWatcher.OnHealthChanged += OnSelectedBuildingHealthChanged;

        if (pb != null)
        {
            // Close build menu if open; cancel placement if active
            HideBuildMenu();
            HideChoicePanel();
            if (BuildingManager.Instance != null && BuildingManager.Instance.IsPlacing)
                BuildingManager.Instance.CancelPlacement();
            ShowInfoPage();
        }
        else
        {
            HideChoicePanel();
            ShowDefaultPage();
        }
    }

    private void OnSelectedBuildingHealthChanged(float _, float __)
    {
        if (_infoPage != null && _infoPage.activeSelf)
            RefreshInfoHP();
    }

    private void OnAnyBuildingPlaced(PlacedBuilding _) => RefreshBuildMenuLocks();

    // ─────────────────────────────────────────────────────────────────────────
    //  HP label binding
    // ─────────────────────────────────────────────────────────────────────────

    private void BindHPLabel(PlayerController player)
    {
        if (player == null) return;
        _player = player;
        player.OnHealthChanged -= UpdateHPText;
        player.OnHealthChanged += UpdateHPText;
        UpdateHPText(player.CurrentHealth, player.MaxHealth);
    }

    private void UpdateHPText(float current, float max)
        => _hpText?.SetText("{0:0}/{1:0}", current, max);

    // ─────────────────────────────────────────────────────────────────────────
    //  Build menu
    // ─────────────────────────────────────────────────────────────────────────

    private void PopulateBuildMenu()
    {
        if (_buildMenuContent == null || BuildingManager.Instance == null) return;

        // Clear any previous children
        foreach (Transform child in _buildMenuContent)
            Destroy(child.gameObject);
        _buildMenuIcons.Clear();
        _buildMenuLockOverlays.Clear();
        _buildMenuButtons.Clear();

        TMP_FontAsset font = _theme?.font != null
            ? _theme.font
            : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        for (int i = 0; i < BuildingManager.Instance.BuildingCount; i++)
        {
            int idx = i;
            BuildingDefinition def = BuildingManager.Instance.GetDefinition(i);
            GameObject slot = BuildBuildMenuSlot(def, font, () => OnBuildMenuSlotClicked(idx));
            slot.transform.SetParent(_buildMenuContent, false);
        }

        RefreshBuildMenuAffordability();
        RefreshBuildMenuLocks();
    }

    private GameObject BuildBuildMenuSlot(BuildingDefinition def, TMP_FontAsset font, UnityEngine.Events.UnityAction onClick)
    {
        const float slotW = 110f;
        const float slotH = 140f;
        const float iconSize = 80f;

        GameObject slot = new GameObject("BuildSlot_" + def.buildingName);
        RectTransform slotRT = slot.AddComponent<RectTransform>();
        slotRT.sizeDelta = new Vector2(slotW, slotH);

        Image slotBg = slot.AddComponent<Image>();
        UIHelper.ApplyImage(slotBg, _theme?.buildSlotBackground, new Color(0.08f, 0.08f, 0.14f, 0.95f), Image.Type.Tiled);

        Button btn = slot.AddComponent<Button>();
        btn.targetGraphic = slotBg;
        btn.colors = UIHelper.BtnColors(_theme?.buildSlotBackground,
            new Color(0.08f, 0.08f, 0.14f), new Color(0.18f, 0.40f, 0.65f), new Color(0.05f, 0.05f, 0.10f));
        btn.onClick.AddListener(onClick);
        _buildMenuButtons.Add(btn);

        // Icon
        GameObject iconGO = new GameObject("Icon");
        iconGO.transform.SetParent(slot.transform, false);
        RectTransform iconRT = iconGO.AddComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0.5f, 1f);
        iconRT.anchorMax = new Vector2(0.5f, 1f);
        iconRT.pivot     = new Vector2(0.5f, 1f);
        iconRT.anchoredPosition = new Vector2(0f, -8f);
        iconRT.sizeDelta = new Vector2(iconSize, iconSize);

        Image iconImg = iconGO.AddComponent<Image>();
        iconImg.raycastTarget = false;
        Sprite defIcon = GetBuildingIcon(def);
        if (defIcon != null) { iconImg.sprite = defIcon; iconImg.color = Color.white; }
        else                 { iconImg.color = new Color(0.5f, 0.7f, 1f); }
        _buildMenuIcons.Add(iconImg);

        // Cost row
        GameObject costRow = new GameObject("CostRow");
        costRow.transform.SetParent(slot.transform, false);
        RectTransform costRT = costRow.AddComponent<RectTransform>();
        costRT.anchorMin = new Vector2(0f, 0f);
        costRT.anchorMax = new Vector2(1f, 0f);
        costRT.pivot     = new Vector2(0.5f, 0f);
        costRT.anchoredPosition = new Vector2(0f, 8f);
        costRT.sizeDelta = new Vector2(0f, 40f);

        HorizontalLayoutGroup costHLG = costRow.AddComponent<HorizontalLayoutGroup>();
        costHLG.childAlignment       = TextAnchor.MiddleCenter;
        costHLG.spacing              = 4f;
        costHLG.childControlHeight   = true;
        costHLG.childControlWidth    = true;
        costHLG.childForceExpandHeight = false;
        costHLG.childForceExpandWidth  = false;

        if (def.woodCost > 0)  AddCostChip(costRow.transform, _theme?.iconWood,  new Color(0.5f, 0.85f, 0.3f), def.woodCost,  font);
        if (def.metalCost > 0) AddCostChip(costRow.transform, _theme?.iconMetal, new Color(0.6f, 0.85f, 1.0f), def.metalCost, font);

        // Lock overlay (citadel required)
        GameObject lockGO = new GameObject("LockOverlay");
        lockGO.transform.SetParent(slot.transform, false);
        RectTransform lockRT = lockGO.AddComponent<RectTransform>();
        lockRT.anchorMin = Vector2.zero;
        lockRT.anchorMax = Vector2.one;
        lockRT.offsetMin = lockRT.offsetMax = Vector2.zero;
        Image lockImg = lockGO.AddComponent<Image>();
        lockImg.color = new Color(0f, 0f, 0f, 0.60f);
        lockImg.raycastTarget = false;
        lockGO.SetActive(false);
        _buildMenuLockOverlays.Add(lockImg);

        LayoutElement le = slot.AddComponent<LayoutElement>();
        le.minWidth = slotW;
        le.preferredWidth = slotW;
        le.flexibleWidth = 0f;

        return slot;
    }

    private void AddCostChip(Transform parent, Sprite icon, Color fallbackColor, int amount, TMP_FontAsset font)
    {
        GameObject chip = new GameObject("Chip");
        chip.transform.SetParent(parent, false);
        HorizontalLayoutGroup hlg = chip.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 2f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlHeight = true;
        hlg.childControlWidth  = true;
        hlg.childForceExpandHeight = false;
        hlg.childForceExpandWidth  = false;
        chip.AddComponent<RectTransform>();

        if (icon != null)
        {
            GameObject iconGO = new GameObject("CostIcon");
            iconGO.transform.SetParent(chip.transform, false);
            RectTransform irt = iconGO.AddComponent<RectTransform>();
            irt.sizeDelta = new Vector2(18f, 18f);
            Image img = iconGO.AddComponent<Image>();
            img.sprite = icon;
            img.color  = Color.white;
            img.raycastTarget = false;
            LayoutElement le = iconGO.AddComponent<LayoutElement>();
            le.minWidth = 18f; le.preferredWidth = 18f; le.minHeight = 18f; le.preferredHeight = 18f;
        }

        GameObject txtGO = new GameObject("CostText");
        txtGO.transform.SetParent(chip.transform, false);
        txtGO.AddComponent<RectTransform>();
        TextMeshProUGUI txt = txtGO.AddComponent<TextMeshProUGUI>();
        txt.SetText("{0}", amount);
        txt.fontSize  = 15f;
        txt.color     = fallbackColor;
        txt.alignment = TextAlignmentOptions.MidlineLeft;
        txt.raycastTarget = false;
        if (font != null) txt.font = font;
    }

    private void OnBuildMenuSlotClicked(int index)
    {
        HideBuildMenu();
        BuildingManager.Instance?.SelectBuilding(index);
    }

    public void ShowBuildMenu()
    {
        if (_buildMenuPanel == null) return;
        _buildMenuOpen = true;
        _buildMenuPanel.SetActive(true);

        // ContentSizeFitter doesn't run on inactive objects, so the content width
        // stays 0 until we force a rebuild now that the panel is active.
        if (_buildMenuContent != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                _buildMenuContent.GetComponent<RectTransform>());

        if (_buildMenuBackdrop != null) _buildMenuBackdrop.SetActive(true);
        HighlightBuildButton(true);
        AnimateBuildMenu(ActionBarHeight, false);
    }

    public void HideBuildMenu()
    {
        if (!_buildMenuOpen) return;
        _buildMenuOpen = false;
        HighlightBuildButton(false);
        if (_buildMenuBackdrop != null) _buildMenuBackdrop.SetActive(false);
        AnimateBuildMenu(-BuildMenuHeight, true);
    }

    private void AnimateBuildMenu(float targetY, bool deactivateAfter)
    {
        if (_buildMenuAnim != null) StopCoroutine(_buildMenuAnim);
        _buildMenuAnim = StartCoroutine(SlidePanel(_buildMenuRT, targetY, deactivateAfter, 0.18f,
            () => _buildMenuAnim = null));
    }

    private void HighlightBuildButton(bool on)
    {
        if (_buildBtnImage == null) return;
        _buildBtnImage.color = on
            ? new Color(0.15f, 0.45f, 0.85f, 1f)
            : new Color(0.08f, 0.08f, 0.16f, 1f);
    }

    private void RefreshBuildMenuAffordability()
    {
        if (_buildMenuIcons.Count == 0 || BuildingManager.Instance == null) return;
        bool citadelMissing = Citadel.Instance == null;

        for (int i = 0; i < _buildMenuIcons.Count; i++)
        {
            if (i >= BuildingManager.Instance.BuildingCount) break;
            BuildingDefinition def = BuildingManager.Instance.GetDefinition(i);

            bool canAfford = ResourceManager.Instance != null
                && ResourceManager.Instance.Wood  >= def.woodCost
                && ResourceManager.Instance.Metal >= def.metalCost;

            _buildMenuIcons[i].color = canAfford ? ColAffordable : ColUnaffordable;
        }
    }

    private void RefreshBuildMenuLocks()
    {
        if (_buildMenuLockOverlays.Count == 0 || BuildingManager.Instance == null) return;
        bool citadelMissing = Citadel.Instance == null;

        for (int i = 0; i < _buildMenuLockOverlays.Count; i++)
        {
            if (i >= BuildingManager.Instance.BuildingCount) break;
            BuildingDefinition def = BuildingManager.Instance.GetDefinition(i);
            bool locked = citadelMissing && !def.isCitadel;
            _buildMenuLockOverlays[i].gameObject.SetActive(locked);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Action bar page switching
    // ─────────────────────────────────────────────────────────────────────────

    private void ShowDefaultPage()
    {
        SetPage(null);
    }

    private void ShowPlacingPage(int index)
    {
        if (BuildingManager.Instance == null || index < 0 || index >= BuildingManager.Instance.BuildingCount)
        { ShowDefaultPage(); return; }

        BuildingDefinition def = BuildingManager.Instance.GetDefinition(index);
        if (_placingIcon != null)
        {
            Sprite s = GetBuildingIcon(def);
            if (s != null) { _placingIcon.sprite = s; _placingIcon.color = Color.white; }
            else _placingIcon.color = new Color(0.5f, 0.7f, 1f);
        }
        _placingLabel?.SetText($"Placing: {def.buildingName}\nTap to place");
        SetPage(_placingPage);
    }

    private void ShowInfoPage()
    {
        if (_selectedPlaced == null) { ShowDefaultPage(); return; }

        BuildingDefinition def = _selectedPlaced.Definition;
        if (_infoBuildingIcon != null)
        {
            Sprite s = GetBuildingIcon(def);
            if (s != null) { _infoBuildingIcon.sprite = s; _infoBuildingIcon.color = Color.white; }
            else _infoBuildingIcon.color = new Color(0.5f, 0.7f, 1f);
        }
        _infoBuildingName?.SetText(def?.buildingName ?? "");
        RefreshInfoHP();
        RefreshUpgradeButton();
        RefreshRepairButton();
        SetPage(_infoPage);
    }

    private void SetPage(GameObject page)
    {
        if (_placingPage != null) _placingPage.SetActive(page == _placingPage);
        if (_infoPage    != null) _infoPage.SetActive(page == _infoPage);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Building info refresh
    // ─────────────────────────────────────────────────────────────────────────

    private void RefreshInfoHP()
    {
        if (_selectedPlaced == null) return;
        Building bldg = _selectedBuildingHpWatcher;
        if (bldg == null) return;

        float ratio = bldg.MaxHealth > 0f ? bldg.CurrentHealth / bldg.MaxHealth : 0f;
        if (_infoHpFill != null)
        {
            _infoHpFill.fillAmount = ratio;
            _infoHpFill.color = ratio > 0.6f ? ColHpHigh : ratio > 0.3f ? ColHpMid : ColHpLow;
        }
        _infoHpText?.SetText("{0:0}/{1:0}", bldg.CurrentHealth, bldg.MaxHealth);
    }

    private void RefreshRepairButton()
    {
        if (_repairBtn == null || _selectedPlaced == null) return;
        Building bldg = _selectedBuildingHpWatcher;
        bool canRepair = bldg != null && bldg.CurrentHealth < bldg.MaxHealth;
        _repairBtn.interactable = canRepair;
    }

    private void RefreshUpgradeButton()
    {
        if (_upgradeBtn == null || _selectedPlaced == null) return;

        // Button is enabled whenever there is any upgrade to show in the panel.
        // Affordability is displayed inside the panel itself, not on the button.
        bool hasChoices = _selectedPlaced.Definition?.upgradeChoices != null
                       && _selectedPlaced.Definition.upgradeChoices.Length > 0;
        if (hasChoices) { _upgradeBtn.interactable = true; return; }

        if (_selectedPlaced.TryGetComponent(out Citadel cit))
        {
            _upgradeBtn.interactable = cit.GetNextTierData() != null;
            return;
        }

        int maxLevel = _selectedPlaced.Definition?.upgrades?.Length ?? 0;
        _upgradeBtn.interactable = _selectedPlaced.Level < maxLevel;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Action button callbacks
    // ─────────────────────────────────────────────────────────────────────────

    private void OnRepairClicked()
    {
        if (_selectedPlaced == null || _player == null) return;
        if (_selectedPlaced.TryGetComponent(out IHoldInteractable holdTarget))
            _player.StartRepair(holdTarget, _selectedPlaced.transform);
    }

    private void OnUpgradeClicked()
    {
        if (_selectedPlaced == null) return;
        if (_choicePanelOpen) { HideChoicePanel(); return; }
        ShowUpgradePanel();
    }

    private void OnDestroyClicked()
    {
        if (_selectedPlaced == null) return;
        var toDestroy = _selectedPlaced;  // save before Deselect clears _selectedPlaced
        PlacedBuilding.Deselect();
        Destroy(toDestroy.gameObject);
    }

    private void DeselectBuilding()
    {
        if (_selectedBuildingHpWatcher != null)
            _selectedBuildingHpWatcher.OnHealthChanged -= OnSelectedBuildingHealthChanged;
        _selectedPlaced = null;
        _selectedBuildingHpWatcher = null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Upgrade choice panel
    // ─────────────────────────────────────────────────────────────────────────

    private void ShowChoicePanel()
    {
        if (_choicePanel == null || _selectedPlaced == null) return;
        BuildingUpgradeChoice[] choices = _selectedPlaced.Definition?.upgradeChoices;
        if (choices == null || choices.Length == 0) return;

        PopulateChoicePanel(choices);
        _choicePanelOpen = true;
        _choicePanel.SetActive(true);

        if (_choicePanelContent != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                _choicePanelContent.GetComponent<RectTransform>());

        AnimateChoicePanel(ActionBarHeight, false);
    }

    private void HideChoicePanel()
    {
        if (_choicePanel == null || !_choicePanelOpen) return;
        _choicePanelOpen = false;
        AnimateChoicePanel(-ChoicePanelHeight, true);
    }

    private void ShowUpgradePanel()
    {
        if (_choicePanel == null || _selectedPlaced == null) return;

        TMP_FontAsset font = _theme?.font != null
            ? _theme.font
            : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        if (_choicePanelContent != null)
            foreach (Transform child in _choicePanelContent) Destroy(child.gameObject);

        BuildingUpgradeChoice[] choices = _selectedPlaced.Definition?.upgradeChoices;

        if (choices != null && choices.Length > 0)
        {
            if (_choicePanelHeader != null) _choicePanelHeader.SetText("CHOOSE UPGRADE");
            foreach (BuildingUpgradeChoice choice in choices)
            {
                BuildingUpgradeChoice captured = choice;
                GameObject slot = BuildUpgradeSlot(choice.icon, choice.woodCost, choice.metalCost,
                    0, font,
                    () => OnChoiceClicked(captured));
                slot.transform.SetParent(_choicePanelContent, false);
            }
        }
        else if (_selectedPlaced.TryGetComponent(out Citadel cit))
        {
            if (_choicePanelHeader != null) _choicePanelHeader.SetText("UPGRADE CITADEL");
            CitadelTierData next = cit.GetNextTierData();
            if (next != null)
            {
                Sprite icon = GetBuildingIcon(_selectedPlaced.Definition);
                var captured = _selectedPlaced;
                // targetLevel = the tier the Citadel will become after upgrade
                GameObject slot = BuildUpgradeSlot(icon, next.woodCost, next.metalCost,
                    cit.Tier + 1, font,
                    () => { HideChoicePanel(); captured?.TryUpgrade(); });
                slot.transform.SetParent(_choicePanelContent, false);
            }
        }
        else
        {
            if (_choicePanelHeader != null) _choicePanelHeader.SetText("UPGRADE");
            if (_selectedPlaced.Definition?.upgrades != null
                && _selectedPlaced.Level < _selectedPlaced.Definition.upgrades.Length)
            {
                BuildingUpgradeTier tier = _selectedPlaced.Definition.upgrades[_selectedPlaced.Level];
                Sprite icon = GetBuildingIcon(_selectedPlaced.Definition);
                var captured = _selectedPlaced;
                // targetLevel = the level the building will reach after upgrade
                GameObject slot = BuildUpgradeSlot(icon, tier.woodCost, tier.metalCost,
                    _selectedPlaced.Level + 1, font,
                    () => { HideChoicePanel(); captured?.TryUpgrade(); });
                slot.transform.SetParent(_choicePanelContent, false);
            }
        }

        _choicePanelOpen = true;
        _choicePanel.SetActive(true);

        if (_choicePanelContent != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                _choicePanelContent.GetComponent<RectTransform>());

        AnimateChoicePanel(ActionBarHeight, false);
    }

    private GameObject BuildUpgradeSlot(Sprite icon, int woodCost, int metalCost,
        int targetLevel, TMP_FontAsset font, UnityEngine.Events.UnityAction onClick)
    {
        const float slotW = 120f;
        const float slotH = 110f;
        const float iconS = 72f;

        bool canAfford = ResourceManager.Instance != null
            && ResourceManager.Instance.Wood  >= woodCost
            && ResourceManager.Instance.Metal >= metalCost;

        GameObject slot = new GameObject("UpgradeSlot");
        RectTransform rt = slot.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(slotW, slotH);

        Image bg = slot.AddComponent<Image>();
        UIHelper.ApplyImage(bg, _theme?.buildSlotBackground, new Color(0.10f, 0.18f, 0.30f, 0.95f), Image.Type.Tiled);

        Button btn = slot.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.interactable  = canAfford;
        ColorBlock cb     = UIHelper.BtnColors(_theme?.buildSlotBackground,
            new Color(0.10f, 0.18f, 0.30f), new Color(0.18f, 0.42f, 0.72f), new Color(0.06f, 0.12f, 0.22f));
        cb.disabledColor  = new Color(0.25f, 0.25f, 0.30f, 0.6f);
        btn.colors        = cb;
        btn.onClick.AddListener(onClick);

        // Icon
        GameObject iconGO = new GameObject("Icon");
        iconGO.transform.SetParent(slot.transform, false);
        RectTransform iconRT = iconGO.AddComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0.5f, 1f); iconRT.anchorMax = new Vector2(0.5f, 1f);
        iconRT.pivot     = new Vector2(0.5f, 1f);
        iconRT.anchoredPosition = new Vector2(0f, -6f);
        iconRT.sizeDelta = new Vector2(iconS, iconS);
        Image iconImg = iconGO.AddComponent<Image>();
        iconImg.raycastTarget = false;
        if (icon != null) { iconImg.sprite = icon; iconImg.color = canAfford ? Color.white : ColUnaffordable; }
        else iconImg.color = canAfford ? new Color(0.4f, 0.7f, 1f) : ColUnaffordable;

        // Level badge — top-right corner of icon, shows the resulting level as Roman numeral
        if (targetLevel > 0)
        {
            GameObject badge    = new GameObject("LevelBadge");
            badge.transform.SetParent(iconGO.transform, false);
            RectTransform brt   = badge.AddComponent<RectTransform>();
            brt.anchorMin       = new Vector2(1f, 1f);
            brt.anchorMax       = new Vector2(1f, 1f);
            brt.pivot           = new Vector2(1f, 1f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta       = new Vector2(26f, 18f);
            Image badgeBg       = badge.AddComponent<Image>();
            badgeBg.color       = new Color(0f, 0f, 0f, 0.72f);
            badgeBg.raycastTarget = false;

            GameObject badgeTxtGO = new GameObject("Text");
            badgeTxtGO.transform.SetParent(badge.transform, false);
            RectTransform trt   = badgeTxtGO.AddComponent<RectTransform>();
            trt.anchorMin       = Vector2.zero;
            trt.anchorMax       = Vector2.one;
            trt.offsetMin       = trt.offsetMax = Vector2.zero;
            TextMeshProUGUI tmp = badgeTxtGO.AddComponent<TextMeshProUGUI>();
            tmp.text            = ToRoman(targetLevel);
            tmp.fontSize        = 13f;
            tmp.fontStyle       = FontStyles.Bold;
            tmp.color           = Color.white;
            tmp.alignment       = TextAlignmentOptions.Center;
            tmp.raycastTarget   = false;
            if (font != null) tmp.font = font;
        }

        // Cost row
        GameObject costRow = new GameObject("Cost");
        costRow.transform.SetParent(slot.transform, false);
        RectTransform crt = costRow.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 0f); crt.anchorMax = new Vector2(1f, 0f);
        crt.pivot     = new Vector2(0.5f, 0f);
        crt.anchoredPosition = new Vector2(0f, 4f);
        crt.sizeDelta = new Vector2(0f, 34f);
        HorizontalLayoutGroup hlg = costRow.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter; hlg.spacing = 4f;
        hlg.childControlHeight = true; hlg.childControlWidth = true;
        hlg.childForceExpandHeight = false; hlg.childForceExpandWidth = false;

        if (woodCost  > 0) AddCostChip(costRow.transform, _theme?.iconWood,  new Color(0.5f, 0.85f, 0.3f), woodCost,  font);
        if (metalCost > 0) AddCostChip(costRow.transform, _theme?.iconMetal, new Color(0.6f, 0.85f, 1.0f), metalCost, font);

        LayoutElement le = slot.AddComponent<LayoutElement>();
        le.minWidth = slotW; le.preferredWidth = slotW; le.flexibleWidth = 0f;

        return slot;
    }

    private static string ToRoman(int n)
    {
        if (n <= 0) return "";
        string[] thousands = { "",  "M",  "MM",  "MMM" };
        string[] hundreds  = { "", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM" };
        string[] tens      = { "", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC" };
        string[] ones      = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII",  "IX" };
        return thousands[n / 1000]
             + hundreds[(n % 1000) / 100]
             + tens[(n % 100) / 10]
             + ones[n % 10];
    }

    private void AnimateChoicePanel(float targetY, bool deactivateAfter)
    {
        if (_choicePanelAnim != null) StopCoroutine(_choicePanelAnim);
        _choicePanelAnim = StartCoroutine(SlidePanel(_choicePanelRT, targetY, deactivateAfter, 0.18f,
            () => _choicePanelAnim = null));
    }

    private void PopulateChoicePanel(BuildingUpgradeChoice[] choices)
    {
        if (_choicePanelContent == null) return;
        foreach (Transform child in _choicePanelContent) Destroy(child.gameObject);

        TMP_FontAsset font = _theme?.font != null
            ? _theme.font
            : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        foreach (BuildingUpgradeChoice choice in choices)
        {
            BuildingUpgradeChoice captured = choice;
            GameObject slot = BuildChoiceSlot(choice, font, () => OnChoiceClicked(captured));
            slot.transform.SetParent(_choicePanelContent, false);
        }
    }

    private GameObject BuildChoiceSlot(BuildingUpgradeChoice choice, TMP_FontAsset font, UnityEngine.Events.UnityAction onClick)
    {
        const float slotW = 120f;
        const float slotH = 110f;
        const float iconS = 72f;

        GameObject slot = new GameObject("ChoiceSlot");
        RectTransform rt = slot.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(slotW, slotH);

        Image bg = slot.AddComponent<Image>();
        UIHelper.ApplyImage(bg, _theme?.buildSlotBackground, new Color(0.10f, 0.18f, 0.30f, 0.95f), Image.Type.Tiled);

        Button btn = slot.AddComponent<Button>();
        btn.targetGraphic = bg;

        bool canAfford = ResourceManager.Instance != null
            && ResourceManager.Instance.Wood  >= choice.woodCost
            && ResourceManager.Instance.Metal >= choice.metalCost;
        btn.interactable = canAfford;
        btn.colors = UIHelper.BtnColors(_theme?.buildSlotBackground,
            new Color(0.10f, 0.18f, 0.30f), new Color(0.18f, 0.42f, 0.72f), new Color(0.06f, 0.12f, 0.22f));
        btn.onClick.AddListener(onClick);

        // Icon
        GameObject iconGO = new GameObject("Icon");
        iconGO.transform.SetParent(slot.transform, false);
        RectTransform iconRT = iconGO.AddComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0.5f, 1f); iconRT.anchorMax = new Vector2(0.5f, 1f);
        iconRT.pivot = new Vector2(0.5f, 1f);
        iconRT.anchoredPosition = new Vector2(0f, -6f);
        iconRT.sizeDelta = new Vector2(iconS, iconS);
        Image iconImg = iconGO.AddComponent<Image>();
        iconImg.raycastTarget = false;
        if (choice.icon != null) { iconImg.sprite = choice.icon; iconImg.color = canAfford ? Color.white : ColUnaffordable; }
        else iconImg.color = canAfford ? new Color(0.4f, 0.7f, 1f) : ColUnaffordable;

        // Cost row
        GameObject costRow = new GameObject("Cost");
        costRow.transform.SetParent(slot.transform, false);
        RectTransform crt = costRow.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 0f); crt.anchorMax = new Vector2(1f, 0f);
        crt.pivot = new Vector2(0.5f, 0f);
        crt.anchoredPosition = new Vector2(0f, 4f);
        crt.sizeDelta = new Vector2(0f, 34f);
        HorizontalLayoutGroup hlg = costRow.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter; hlg.spacing = 4f;
        hlg.childControlHeight = true; hlg.childControlWidth = true;
        hlg.childForceExpandHeight = false; hlg.childForceExpandWidth = false;

        if (choice.woodCost > 0)  AddCostChip(costRow.transform, _theme?.iconWood,  new Color(0.5f, 0.85f, 0.3f), choice.woodCost,  font);
        if (choice.metalCost > 0) AddCostChip(costRow.transform, _theme?.iconMetal, new Color(0.6f, 0.85f, 1.0f), choice.metalCost, font);

        LayoutElement le = slot.AddComponent<LayoutElement>();
        le.minWidth = slotW; le.preferredWidth = slotW; le.flexibleWidth = 0f;

        return slot;
    }

    private void OnChoiceClicked(BuildingUpgradeChoice choice)
    {
        if (_selectedPlaced == null) return;
        HideChoicePanel();
        _selectedPlaced.TryUpgradeToChoice(choice);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Toast
    // ─────────────────────────────────────────────────────────────────────────

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
        while (elapsed < 0.5f) { _toastGroup.alpha = 1f - elapsed / 0.5f; elapsed += Time.unscaledDeltaTime; yield return null; }
        _toastGroup.alpha = 0f;
        _toastCoroutine = null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Update — vampire bind + boss bar pulse
    // ─────────────────────────────────────────────────────────────────────────

    private void Update()
    {
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

        // Poll HP every frame so the slider stays in sync regardless of event timing
        if (vampireActive && _bossSlider != null)
            _bossSlider.value = _boundVampire.MaxHealth > 0f
                ? _boundVampire.CurrentHealth / _boundVampire.MaxHealth
                : 0f;

        if (vampireActive && _bossVulnGlow != null)
        {
            float alpha = _boundVampire.IsVulnerable ? 0.25f + 0.15f * Mathf.Sin(Time.time * 6f) : 0f;
            _bossVulnGlow.color = new Color(1f, 1f, 0f, alpha);
        }
    }

    private void OnVampireHealthChanged(float current, float max)
    {
        if (_bossSlider != null)
            _bossSlider.value = max > 0f ? current / max : 0f;
    }

    private void OnVampireLevelChanged(int level)
    {
        if (_bossLevelText != null) _bossLevelText.text = $"Lv {level}";
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  HUD builder entry point
    // ─────────────────────────────────────────────────────────────────────────

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
        scaler.matchWidthOrHeight  = 1f;

        canvasGO.AddComponent<GraphicRaycaster>();

        GameObject safeAreaGO = new GameObject("SafeArea");
        safeAreaGO.transform.SetParent(canvasGO.transform, false);
        RectTransform safeRT  = safeAreaGO.AddComponent<RectTransform>();
        safeRT.anchorMin      = Vector2.zero;
        safeRT.anchorMax      = Vector2.one;
        safeRT.offsetMin      = safeRT.offsetMax = Vector2.zero;
        safeAreaGO.AddComponent<SafeAreaFitter>();
        _hudRoot = safeAreaGO.transform;
        Transform root = _hudRoot;

        BuildTopBar(root, font);
        BuildBossHealthBar(root, font);
        BuildActionBar(root, font);
        BuildBuildMenuOverlay(root, font);
        BuildChoicePanelOverlay(root, font);
        BuildToast(root, font);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Top bar
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildTopBar(Transform root, TMP_FontAsset font)
    {
        const float barH    = 90f;
        const float padH    = 60f;
        const float iconSz  = 30f;

        GameObject bar = new GameObject("TopBar");
        bar.transform.SetParent(root, false);
        RectTransform rt = bar.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 1f);
        rt.anchorMax        = new Vector2(1f, 1f);
        rt.pivot            = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = new Vector2(0f, barH);

        Image barBg = bar.AddComponent<Image>();
        UIHelper.ApplyImage(barBg, _theme?.hudPanelBackground, new Color(0f, 0f, 0f, 0.70f), Image.Type.Tiled);

        HorizontalLayoutGroup hlg = bar.AddComponent<HorizontalLayoutGroup>();
        hlg.padding               = new RectOffset((int)padH, (int)padH, 0, 0);
        hlg.spacing               = 0f;
        hlg.childAlignment        = TextAnchor.MiddleCenter;
        hlg.childControlHeight    = true;
        hlg.childControlWidth     = true;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = false;

        // ── Left: resources ───────────────────────────────────────────────────
        GameObject leftGO = MakeGroup(bar.transform, "Resources");
        leftGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
        HorizontalLayoutGroup leftHLG = leftGO.AddComponent<HorizontalLayoutGroup>();
        leftHLG.childAlignment        = TextAnchor.MiddleLeft;
        leftHLG.spacing               = 20f;
        leftHLG.childControlHeight    = true;
        leftHLG.childControlWidth     = true;
        leftHLG.childForceExpandHeight = true;
        leftHLG.childForceExpandWidth  = false;

        _woodText  = MakeResourceChip(leftGO.transform, "Wood",  _theme?.iconWood,  new Color(0.50f, 0.85f, 0.30f), font, iconSz);
        _metalText = MakeResourceChip(leftGO.transform, "Metal", _theme?.iconMetal, new Color(0.60f, 0.85f, 1.00f), font, iconSz);

        // ── Centre: state icon + state text + timer ───────────────────────────
        GameObject centreGO = MakeGroup(bar.transform, "StateTimer");
        centreGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
        HorizontalLayoutGroup centreHLG = centreGO.AddComponent<HorizontalLayoutGroup>();
        centreHLG.childAlignment        = TextAnchor.MiddleCenter;
        centreHLG.spacing               = 10f;
        centreHLG.childControlHeight    = true;
        centreHLG.childControlWidth     = true;
        centreHLG.childForceExpandHeight = true;
        centreHLG.childForceExpandWidth  = false;

        // State icon
        GameObject stateIconGO = new GameObject("StateIcon");
        stateIconGO.transform.SetParent(centreGO.transform, false);
        LayoutElement siLE = stateIconGO.AddComponent<LayoutElement>();
        siLE.minWidth = iconSz; siLE.preferredWidth = iconSz;
        siLE.minHeight = iconSz; siLE.preferredHeight = iconSz;
        _stateIcon = stateIconGO.AddComponent<Image>();
        ApplyIconOrColor(_stateIcon, _theme?.iconPrep, new Color(0.4f, 0.9f, 0.4f, 0.9f));

        // State + timer stacked vertically
        GameObject stateStack = MakeGroup(centreGO.transform, "StateStack");
        stateStack.AddComponent<LayoutElement>().flexibleWidth = 0f;
        VerticalLayoutGroup stateVLG = stateStack.AddComponent<VerticalLayoutGroup>();
        stateVLG.childAlignment        = TextAnchor.MiddleCenter;
        stateVLG.childControlHeight    = true;
        stateVLG.childControlWidth     = true;
        stateVLG.childForceExpandHeight = true;
        stateVLG.childForceExpandWidth  = false;

        _stateText = MakeLabel(stateStack.transform, "StateText", "PREPARATION", font, 26f,
                               new Color(0.4f, 0.9f, 0.4f), TextAlignmentOptions.Center);
        _timerText = MakeLabel(stateStack.transform, "TimerText", "",            font, 20f,
                               new Color(1f, 0.85f, 0.3f),  TextAlignmentOptions.Center);

        // ── Right: HP + coins ─────────────────────────────────────────────────
        GameObject rightGO = MakeGroup(bar.transform, "HPCoins");
        rightGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
        HorizontalLayoutGroup rightHLG = rightGO.AddComponent<HorizontalLayoutGroup>();
        rightHLG.childAlignment        = TextAnchor.MiddleRight;
        rightHLG.spacing               = 16f;
        rightHLG.childControlHeight    = true;
        rightHLG.childControlWidth     = true;
        rightHLG.childForceExpandHeight = true;
        rightHLG.childForceExpandWidth  = false;

        _hpText       = MakeResourceChip(rightGO.transform, "HP",    _theme?.iconHealth, new Color(0.95f, 0.30f, 0.35f), font, iconSz, "--/--");
        _currencyText = MakeResourceChip(rightGO.transform, "Coins", _theme?.iconCoin,   new Color(1.00f, 0.85f, 0.20f), font, iconSz, "0");

        // ── Pause button ──────────────────────────────────────────────────────
        GameObject pauseGO = new GameObject("PauseBtn");
        pauseGO.transform.SetParent(bar.transform, false);
        LayoutElement pauseLE   = pauseGO.AddComponent<LayoutElement>();
        pauseLE.minWidth        = 90f;
        pauseLE.preferredWidth  = 90f;
        pauseLE.flexibleWidth   = 0f;
        Image pauseImg          = pauseGO.AddComponent<Image>();
        UIHelper.ApplyImage(pauseImg, _theme?.buttonSetting, new Color(0.18f, 0.18f, 0.22f, 0.85f));
        Button pauseBtn         = pauseGO.AddComponent<Button>();
        pauseBtn.targetGraphic  = pauseImg;
        pauseBtn.colors         = UIHelper.BtnColors(_theme?.buttonSetting,
                                    Color.white, new Color(0.7f, 0.85f, 1f), new Color(0.45f, 0.55f, 0.7f));
        pauseBtn.onClick.AddListener(() => PauseMenuManager.Instance?.Pause());
    }

    private TextMeshProUGUI MakeResourceChip(Transform parent, string name, Sprite icon,
        Color iconFallback, TMP_FontAsset font, float iconSz, string defaultText = "0")
    {
        GameObject chip = new GameObject(name + "Chip");
        chip.transform.SetParent(parent, false);
        chip.AddComponent<RectTransform>();
        HorizontalLayoutGroup hlg = chip.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.spacing                = 6f;
        hlg.childControlHeight     = true;
        hlg.childControlWidth      = true;
        hlg.childForceExpandHeight = false;
        hlg.childForceExpandWidth  = false;

        GameObject iconGO = new GameObject("Icon");
        iconGO.transform.SetParent(chip.transform, false);
        LayoutElement ile = iconGO.AddComponent<LayoutElement>();
        ile.minWidth = iconSz; ile.preferredWidth = iconSz;
        ile.minHeight = iconSz; ile.preferredHeight = iconSz;
        Image iconImg = iconGO.AddComponent<Image>();
        iconImg.raycastTarget = false;
        ApplyIconOrColor(iconImg, icon, iconFallback);

        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(chip.transform, false);
        textGO.AddComponent<RectTransform>();
        TextMeshProUGUI tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = defaultText;
        tmp.fontSize  = 26f;
        tmp.color     = Color.white;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.raycastTarget = false;
        if (font != null) tmp.font = font;

        return tmp;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Action bar
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildActionBar(Transform root, TMP_FontAsset font)
    {
        _actionBar = new GameObject("ActionBar");
        _actionBar.transform.SetParent(root, false);

        RectTransform rt    = _actionBar.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 0f);
        rt.anchorMax        = new Vector2(1f, 0f);
        rt.pivot            = new Vector2(0.5f, 0f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = new Vector2(0f, ActionBarHeight);

        Image barBg = _actionBar.AddComponent<Image>();
        UIHelper.ApplyImage(barBg, _theme?.hudPanelBackground, new Color(0f, 0f, 0f, 0.70f), Image.Type.Tiled);

        HorizontalLayoutGroup hlg = _actionBar.AddComponent<HorizontalLayoutGroup>();
        hlg.padding               = new RectOffset(60, 60, 0, 0);
        hlg.spacing               = 0f;
        hlg.childAlignment        = TextAnchor.MiddleLeft;
        hlg.childControlHeight    = true;
        hlg.childControlWidth     = true;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = false;

        // ── Hammer / build button (fixed 80×80) ───────────────────────────────
        GameObject buildBtnGO = new GameObject("BuildBtn");
        buildBtnGO.transform.SetParent(_actionBar.transform, false);
        LayoutElement ble   = buildBtnGO.AddComponent<LayoutElement>();
        ble.minWidth        = ActionBarHeight;
        ble.preferredWidth  = ActionBarHeight;
        ble.flexibleWidth   = 0f;

        _buildBtnImage      = buildBtnGO.AddComponent<Image>();
        UIHelper.ApplyImage(_buildBtnImage, _theme?.hudPanelBackground, new Color(0.08f, 0.08f, 0.16f, 1f));

        Button buildBtn     = buildBtnGO.AddComponent<Button>();
        buildBtn.targetGraphic = _buildBtnImage;
        buildBtn.colors     = UIHelper.BtnColors(_theme?.hudPanelBackground,
            new Color(0.08f, 0.08f, 0.16f), new Color(0.18f, 0.35f, 0.60f), new Color(0.05f, 0.05f, 0.10f));
        buildBtn.onClick.AddListener(OnBuildButtonClicked);

        GameObject hammerIconGO = new GameObject("HammerIcon");
        hammerIconGO.transform.SetParent(buildBtnGO.transform, false);
        RectTransform hammerRT = hammerIconGO.AddComponent<RectTransform>();
        hammerRT.anchorMin = new Vector2(0.5f, 0.5f);
        hammerRT.anchorMax = new Vector2(0.5f, 0.5f);
        hammerRT.pivot     = new Vector2(0.5f, 0.5f);
        hammerRT.sizeDelta = new Vector2(44f, 44f);
        hammerRT.anchoredPosition = Vector2.zero;
        Image hammerImg    = hammerIconGO.AddComponent<Image>();
        hammerImg.raycastTarget = false;
        ApplyIconOrColor(hammerImg, _theme?.iconBuild, new Color(0.7f, 0.7f, 0.8f));

        // ── Context area (fills remaining width) ──────────────────────────────
        GameObject contextGO = new GameObject("ContextArea");
        contextGO.transform.SetParent(_actionBar.transform, false);
        contextGO.AddComponent<RectTransform>();
        contextGO.AddComponent<LayoutElement>().flexibleWidth = 1f;

        BuildPlacingPage(contextGO.transform, font);
        BuildInfoPage(contextGO.transform, font);

        // Start with both pages hidden
        _placingPage?.SetActive(false);
        _infoPage?.SetActive(false);
    }

    private void BuildPlacingPage(Transform parent, TMP_FontAsset font)
    {
        _placingPage = new GameObject("PlacingPage");
        _placingPage.transform.SetParent(parent, false);
        RectTransform rt = _placingPage.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        HorizontalLayoutGroup hlg = _placingPage.AddComponent<HorizontalLayoutGroup>();
        hlg.padding               = new RectOffset(12, 12, 8, 8);
        hlg.spacing               = 16f;
        hlg.childAlignment        = TextAnchor.MiddleLeft;
        hlg.childControlHeight    = true;
        hlg.childControlWidth     = true;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = false;

        // Building icon
        GameObject iconGO = new GameObject("PlacingIcon");
        iconGO.transform.SetParent(_placingPage.transform, false);
        LayoutElement ile = iconGO.AddComponent<LayoutElement>();
        ile.minWidth = 56f; ile.preferredWidth = 56f; ile.flexibleWidth = 0f;
        _placingIcon = iconGO.AddComponent<Image>();
        _placingIcon.color = new Color(0.5f, 0.7f, 1f);

        // Label (flex)
        GameObject labelGO = new GameObject("PlacingLabel");
        labelGO.transform.SetParent(_placingPage.transform, false);
        labelGO.AddComponent<RectTransform>();
        labelGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
        _placingLabel = labelGO.AddComponent<TextMeshProUGUI>();
        _placingLabel.text      = "Tap to place";
        _placingLabel.fontSize  = 22f;
        _placingLabel.color     = new Color(0.8f, 0.9f, 1f);
        _placingLabel.alignment = TextAlignmentOptions.MidlineLeft;
        if (font != null) _placingLabel.font = font;

        // Cancel button (fixed right)
        GameObject cancelGO = new GameObject("CancelBtn");
        cancelGO.transform.SetParent(_placingPage.transform, false);
        LayoutElement cle   = cancelGO.AddComponent<LayoutElement>();
        cle.minWidth        = 80f; cle.preferredWidth = 80f; cle.flexibleWidth = 0f;
        Image cancelImg     = cancelGO.AddComponent<Image>();
        UIHelper.ApplyImage(cancelImg, _theme?.buttonDanger, new Color(0.50f, 0.05f, 0.05f, 1f));
        Button cancelBtn    = cancelGO.AddComponent<Button>();
        cancelBtn.targetGraphic = cancelImg;
        cancelBtn.colors    = UIHelper.BtnColors(_theme?.buttonDanger,
            new Color(0.50f, 0.05f, 0.05f), new Color(0.75f, 0.12f, 0.12f), new Color(0.35f, 0.03f, 0.03f));
        cancelBtn.onClick.AddListener(() => BuildingManager.Instance?.CancelPlacement());
        AddCancelIcon(cancelGO.transform);
    }

    private void BuildInfoPage(Transform parent, TMP_FontAsset font)
    {
        _infoPage = new GameObject("InfoPage");
        _infoPage.transform.SetParent(parent, false);
        RectTransform rt = _infoPage.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        HorizontalLayoutGroup hlg = _infoPage.AddComponent<HorizontalLayoutGroup>();
        hlg.padding               = new RectOffset(12, 12, 6, 6);
        hlg.spacing               = 14f;
        hlg.childAlignment        = TextAnchor.MiddleLeft;
        hlg.childControlHeight    = true;
        hlg.childControlWidth     = true;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = false;

        // Building icon (square, fixed)
        GameObject iconGO = new GameObject("BuildingIcon");
        iconGO.transform.SetParent(_infoPage.transform, false);
        LayoutElement ile = iconGO.AddComponent<LayoutElement>();
        ile.minWidth = 60f; ile.preferredWidth = 60f; ile.flexibleWidth = 0f;
        _infoBuildingIcon = iconGO.AddComponent<Image>();
        _infoBuildingIcon.color = new Color(0.5f, 0.7f, 1f);

        // Name + HP bar column (flex)
        GameObject hpCol = MakeGroup(_infoPage.transform, "HPCol");
        hpCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
        VerticalLayoutGroup hpVLG = hpCol.AddComponent<VerticalLayoutGroup>();
        hpVLG.childAlignment       = TextAnchor.MiddleLeft;
        hpVLG.spacing              = 4f;
        hpVLG.childControlHeight   = true;
        hpVLG.childControlWidth    = true;
        hpVLG.childForceExpandHeight = false;
        hpVLG.childForceExpandWidth  = true;

        _infoBuildingName = MakeLabel(hpCol.transform, "BuildingName", "", font, 18f,
                                      new Color(1f, 0.85f, 0.3f), TextAlignmentOptions.Left);
        SetPrefHeight(_infoBuildingName.gameObject, 24f);

        // HP bar background
        GameObject hpBgGO = new GameObject("HPBarBg");
        hpBgGO.transform.SetParent(hpCol.transform, false);
        hpBgGO.AddComponent<RectTransform>();
        SetPrefHeight(hpBgGO, 20f);
        Image hpBgImg = hpBgGO.AddComponent<Image>();
        hpBgImg.color = new Color(0.12f, 0.12f, 0.12f, 1f);

        // HP fill
        GameObject hpFillGO = new GameObject("HPFill");
        hpFillGO.transform.SetParent(hpBgGO.transform, false);
        RectTransform fillRT = hpFillGO.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = fillRT.offsetMax = Vector2.zero;
        _infoHpFill = hpFillGO.AddComponent<Image>();
        _infoHpFill.color      = ColHpHigh;
        _infoHpFill.type       = Image.Type.Filled;
        _infoHpFill.fillMethod = Image.FillMethod.Horizontal;
        _infoHpFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        _infoHpFill.fillAmount = 1f;

        // HP text overlaid on bar
        GameObject hpTxtGO = new GameObject("HPText");
        hpTxtGO.transform.SetParent(hpBgGO.transform, false);
        RectTransform hpTxtRT = hpTxtGO.AddComponent<RectTransform>();
        hpTxtRT.anchorMin = Vector2.zero; hpTxtRT.anchorMax = Vector2.one;
        hpTxtRT.offsetMin = new Vector2(4f, 0f); hpTxtRT.offsetMax = new Vector2(-4f, 0f);
        _infoHpText = hpTxtGO.AddComponent<TextMeshProUGUI>();
        _infoHpText.fontSize  = 14f;
        _infoHpText.color     = Color.white;
        _infoHpText.fontStyle = FontStyles.Bold;
        _infoHpText.alignment = TextAlignmentOptions.Center;
        _infoHpText.raycastTarget = false;
        if (font != null) _infoHpText.font = font;

        // ── Action buttons ────────────────────────────────────────────────────
        GameObject actionRow = MakeGroup(_infoPage.transform, "ActionRow");
        actionRow.AddComponent<LayoutElement>().flexibleWidth = 0f;
        HorizontalLayoutGroup aHLG = actionRow.AddComponent<HorizontalLayoutGroup>();
        aHLG.childAlignment        = TextAnchor.MiddleRight;
        aHLG.spacing               = 8f;
        aHLG.childControlHeight    = true;
        aHLG.childControlWidth     = true;
        aHLG.childForceExpandHeight = true;
        aHLG.childForceExpandWidth  = false;

        _repairBtn  = BuildActionIconButton(actionRow.transform, "Repair",  _theme?.iconRepair,
                          new Color(0.12f, 0.45f, 0.12f), new Color(0.18f, 0.65f, 0.18f), OnRepairClicked, font);
        _upgradeBtn = BuildActionIconButton(actionRow.transform, "Upgrade", _theme?.iconUpgrade,
                          new Color(0.12f, 0.40f, 0.65f), new Color(0.18f, 0.55f, 0.85f), OnUpgradeClicked, font);
        _destroyBtn = BuildActionIconButton(actionRow.transform, "Destroy", _theme?.iconDestroy,
                          new Color(0.55f, 0.08f, 0.08f), new Color(0.80f, 0.15f, 0.15f), OnDestroyClicked, font);

        // ── Close button ──────────────────────────────────────────────────────
        GameObject closeGO  = new GameObject("CloseBtn");
        closeGO.transform.SetParent(_infoPage.transform, false);
        LayoutElement cle   = closeGO.AddComponent<LayoutElement>();
        cle.minWidth        = 56f; cle.preferredWidth = 56f; cle.flexibleWidth = 0f;
        Image closeImg      = closeGO.AddComponent<Image>();
        UIHelper.ApplyImage(closeImg, _theme?.buttonDanger, new Color(0.28f, 0.06f, 0.06f, 0.9f));
        Button closeBtn     = closeGO.AddComponent<Button>();
        closeBtn.targetGraphic = closeImg;
        closeBtn.colors     = UIHelper.BtnColors(_theme?.buttonDanger,
            new Color(0.28f, 0.06f, 0.06f), new Color(0.50f, 0.12f, 0.12f), new Color(0.18f, 0.04f, 0.04f));
        closeBtn.onClick.AddListener(() => { HideChoicePanel(); PlacedBuilding.Deselect(); });
        AddCancelIcon(closeGO.transform);
    }

    private void AddCancelIcon(Transform parent)
    {
        var go  = new GameObject("Icon");
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.15f, 0.15f);
        rt.anchorMax = new Vector2(0.85f, 0.85f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        ApplyIconOrColor(img, _theme?.iconCancel ?? UIHelper.MakeCancelIconSprite(), Color.white);
    }

    private Button BuildActionIconButton(Transform parent, string goName, Sprite icon,
        Color normalCol, Color hoverCol, UnityEngine.Events.UnityAction onClick, TMP_FontAsset font)
    {
        const float sz = 60f;
        GameObject go  = new GameObject(goName + "Btn");
        go.transform.SetParent(parent, false);
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.minWidth = sz; le.preferredWidth = sz; le.flexibleWidth = 0f;

        Image img       = go.AddComponent<Image>();
        UIHelper.ApplyImage(img, _theme?.buttonNav, normalCol);

        Button btn      = go.AddComponent<Button>();
        btn.targetGraphic = img;
        ColorBlock cb   = UIHelper.BtnColors(_theme?.buttonNav, normalCol, hoverCol, normalCol * 0.7f);
        cb.disabledColor = new Color(0.25f, 0.25f, 0.25f, 0.6f);
        btn.colors      = cb;
        btn.onClick.AddListener(onClick);

        GameObject iconGO  = new GameObject("Icon");
        iconGO.transform.SetParent(go.transform, false);
        RectTransform irt  = iconGO.AddComponent<RectTransform>();
        irt.anchorMin      = new Vector2(0.15f, 0.15f);
        irt.anchorMax      = new Vector2(0.85f, 0.85f);
        irt.offsetMin      = irt.offsetMax = Vector2.zero;
        Image iconImg      = iconGO.AddComponent<Image>();
        iconImg.raycastTarget = false;
        ApplyIconOrColor(iconImg, icon, Color.white);

        return btn;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Build menu overlay
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildBuildMenuOverlay(Transform root, TMP_FontAsset font)
    {
        // Full-screen backdrop — sits behind menu, closes it on tap
        _buildMenuBackdrop = new GameObject("BuildMenuBackdrop");
        _buildMenuBackdrop.transform.SetParent(root, false);
        RectTransform brt = _buildMenuBackdrop.AddComponent<RectTransform>();
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.offsetMin = new Vector2(0f, ActionBarHeight);   // leave action bar uncovered
        brt.offsetMax = Vector2.zero;
        Image backdropImg = _buildMenuBackdrop.AddComponent<Image>();
        backdropImg.color = new Color(0f, 0f, 0f, 0.01f);   // nearly invisible but receives touch
        Button backdropBtn = _buildMenuBackdrop.AddComponent<Button>();
        backdropBtn.targetGraphic = backdropImg;
        backdropBtn.transition    = Selectable.Transition.None;
        backdropBtn.onClick.AddListener(HideBuildMenu);
        _buildMenuBackdrop.SetActive(false);

        // Panel — anchored at bottom, slides up
        _buildMenuPanel = new GameObject("BuildMenuPanel");
        _buildMenuPanel.transform.SetParent(root, false);
        _buildMenuRT = _buildMenuPanel.AddComponent<RectTransform>();
        _buildMenuRT.anchorMin        = new Vector2(0f, 0f);
        _buildMenuRT.anchorMax        = new Vector2(1f, 0f);
        _buildMenuRT.pivot            = new Vector2(0.5f, 0f);
        _buildMenuRT.anchoredPosition = new Vector2(0f, -BuildMenuHeight);   // hidden below
        _buildMenuRT.sizeDelta        = new Vector2(0f, BuildMenuHeight);

        Image panelBg = _buildMenuPanel.AddComponent<Image>();
        UIHelper.ApplyImage(panelBg, _theme?.hudPanelBackground, new Color(0.04f, 0.04f, 0.10f, 0.95f), Image.Type.Tiled);

        // ScrollRect
        ScrollRect scroll   = _buildMenuPanel.AddComponent<ScrollRect>();
        scroll.horizontal   = true;
        scroll.vertical     = false;
        scroll.scrollSensitivity = 30f;

        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(_buildMenuPanel.transform, false);
        RectTransform vpRT  = viewport.AddComponent<RectTransform>();
        vpRT.anchorMin      = Vector2.zero; vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin      = new Vector2(12f, 10f);
        vpRT.offsetMax      = new Vector2(-12f, -10f);
        viewport.AddComponent<RectMask2D>();
        scroll.viewport     = vpRT;

        GameObject content  = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        RectTransform cRT   = content.AddComponent<RectTransform>();
        cRT.anchorMin       = new Vector2(0f, 0f);
        cRT.anchorMax       = new Vector2(0f, 1f);
        cRT.pivot           = new Vector2(0f, 0.5f);
        cRT.anchoredPosition = Vector2.zero;
        cRT.sizeDelta        = new Vector2(0f, 0f);

        HorizontalLayoutGroup cHLG  = content.AddComponent<HorizontalLayoutGroup>();
        cHLG.childAlignment         = TextAnchor.MiddleLeft;
        cHLG.spacing                = 10f;
        cHLG.childControlHeight     = true;
        cHLG.childControlWidth      = true;
        cHLG.childForceExpandHeight = true;
        cHLG.childForceExpandWidth  = false;
        cHLG.padding                = new RectOffset(60, 60, 4, 4);

        ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
        csf.horizontalFit     = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit       = ContentSizeFitter.FitMode.Unconstrained;

        scroll.content = cRT;
        _buildMenuContent = content.transform;
        _buildMenuPanel.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Choice panel overlay
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildChoicePanelOverlay(Transform root, TMP_FontAsset font)
    {
        _choicePanel = new GameObject("ChoicePanel");
        _choicePanel.transform.SetParent(root, false);
        _choicePanelRT = _choicePanel.AddComponent<RectTransform>();
        _choicePanelRT.anchorMin        = new Vector2(0f, 0f);
        _choicePanelRT.anchorMax        = new Vector2(1f, 0f);
        _choicePanelRT.pivot            = new Vector2(0.5f, 0f);
        _choicePanelRT.anchoredPosition = new Vector2(0f, -ChoicePanelHeight);
        _choicePanelRT.sizeDelta        = new Vector2(0f, ChoicePanelHeight);

        Image panelBg = _choicePanel.AddComponent<Image>();
        UIHelper.ApplyImage(panelBg, _theme?.hudPanelBackground, new Color(0.06f, 0.06f, 0.14f, 0.95f), Image.Type.Tiled);

        // Header label
        TMP_FontAsset f = _theme?.font != null
            ? _theme.font
            : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        GameObject headerGO = new GameObject("Header");
        headerGO.transform.SetParent(_choicePanel.transform, false);
        RectTransform hRT   = headerGO.AddComponent<RectTransform>();
        hRT.anchorMin       = new Vector2(0f, 1f); hRT.anchorMax = new Vector2(1f, 1f);
        hRT.pivot           = new Vector2(0.5f, 1f);
        hRT.anchoredPosition = new Vector2(0f, -4f);
        hRT.sizeDelta        = new Vector2(0f, 24f);
        _choicePanelHeader = headerGO.AddComponent<TextMeshProUGUI>();
        _choicePanelHeader.text      = "CHOOSE UPGRADE";
        _choicePanelHeader.fontSize  = 16f;
        _choicePanelHeader.color     = new Color(0.65f, 0.65f, 0.65f);
        _choicePanelHeader.alignment = TextAlignmentOptions.Center;
        _choicePanelHeader.raycastTarget = false;
        if (f != null) _choicePanelHeader.font = f;

        // Scrollable row of choices
        ScrollRect scroll = _choicePanel.AddComponent<ScrollRect>();
        scroll.horizontal = true;
        scroll.vertical   = false;
        scroll.scrollSensitivity = 30f;

        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(_choicePanel.transform, false);
        RectTransform vpRT  = viewport.AddComponent<RectTransform>();
        vpRT.anchorMin      = new Vector2(0f, 0f);
        vpRT.anchorMax      = new Vector2(1f, 1f);
        vpRT.offsetMin      = new Vector2(12f, 8f);
        vpRT.offsetMax      = new Vector2(-12f, -28f);
        viewport.AddComponent<RectMask2D>();
        scroll.viewport     = vpRT;

        GameObject content  = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        RectTransform cRT   = content.AddComponent<RectTransform>();
        cRT.anchorMin       = new Vector2(0f, 0f);
        cRT.anchorMax       = new Vector2(0f, 1f);
        cRT.pivot           = new Vector2(0f, 0.5f);
        cRT.anchoredPosition = Vector2.zero;
        cRT.sizeDelta        = Vector2.zero;

        HorizontalLayoutGroup cHLG  = content.AddComponent<HorizontalLayoutGroup>();
        cHLG.childAlignment         = TextAnchor.MiddleLeft;
        cHLG.spacing                = 10f;
        cHLG.childControlHeight     = true;
        cHLG.childControlWidth      = true;
        cHLG.childForceExpandHeight = true;
        cHLG.childForceExpandWidth  = false;
        cHLG.padding                = new RectOffset(60, 60, 4, 4);

        ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
        csf.horizontalFit     = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit       = ContentSizeFitter.FitMode.Unconstrained;

        scroll.content      = cRT;
        _choicePanelContent = content.transform;
        _choicePanel.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Boss health bar (unchanged)
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildBossHealthBar(Transform canvas, TMP_FontAsset font)
    {
        _bossPanel = new GameObject("BossHealthBar");
        _bossPanel.transform.SetParent(canvas, false);

        RectTransform rt  = _bossPanel.AddComponent<RectTransform>();
        rt.anchorMin      = new Vector2(0.5f, 1f);
        rt.anchorMax      = new Vector2(0.5f, 1f);
        rt.pivot          = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -96f);
        rt.sizeDelta      = new Vector2(500f, 60f);

        _bossPanel.AddComponent<Image>().color = new Color(0.05f, 0f, 0.1f, 0.85f);

        GameObject glowGO  = new GameObject("VulnGlow");
        glowGO.transform.SetParent(_bossPanel.transform, false);
        RectTransform grt  = glowGO.AddComponent<RectTransform>();
        grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
        grt.offsetMin = grt.offsetMax = Vector2.zero;
        _bossVulnGlow = glowGO.AddComponent<Image>();
        _bossVulnGlow.color = new Color(1f, 1f, 0f, 0f);

        GameObject nameGO = new GameObject("BossName");
        nameGO.transform.SetParent(_bossPanel.transform, false);
        RectTransform nrt  = nameGO.AddComponent<RectTransform>();
        nrt.anchorMin = new Vector2(0f, 0.5f); nrt.anchorMax = new Vector2(0.65f, 1f);
        nrt.offsetMin = new Vector2(8f, 0f);   nrt.offsetMax = Vector2.zero;
        _bossNameText = nameGO.AddComponent<TextMeshProUGUI>();
        _bossNameText.font      = font;
        _bossNameText.text      = "The Vampire";
        _bossNameText.fontSize  = 18f;
        _bossNameText.fontStyle = FontStyles.Bold;
        _bossNameText.color     = new Color(0.85f, 0.5f, 1f);
        _bossNameText.alignment = TextAlignmentOptions.MidlineLeft;

        GameObject lvlGO  = new GameObject("BossLevel");
        lvlGO.transform.SetParent(_bossPanel.transform, false);
        RectTransform lrt  = lvlGO.AddComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0.65f, 0.5f); lrt.anchorMax = new Vector2(1f, 1f);
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = new Vector2(-8f, 0f);
        _bossLevelText = lvlGO.AddComponent<TextMeshProUGUI>();
        _bossLevelText.font      = font;
        _bossLevelText.text      = "Lv 1";
        _bossLevelText.fontSize  = 16f;
        _bossLevelText.color     = new Color(1f, 0.9f, 0.4f);
        _bossLevelText.alignment = TextAlignmentOptions.MidlineRight;

        // Slider container (same rect as the old HealthBg)
        GameObject sliderGO = new GameObject("HpSlider");
        sliderGO.transform.SetParent(_bossPanel.transform, false);
        RectTransform sliderRT = sliderGO.AddComponent<RectTransform>();
        sliderRT.anchorMin = new Vector2(0f, 0f); sliderRT.anchorMax = new Vector2(1f, 0.5f);
        sliderRT.offsetMin = new Vector2(8f, 6f); sliderRT.offsetMax = new Vector2(-8f, 0f);

        // Background
        GameObject bgGO = new GameObject("Background");
        bgGO.transform.SetParent(sliderGO.transform, false);
        RectTransform bgRT = bgGO.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;
        Image bgImg = bgGO.AddComponent<Image>();
        UIHelper.ApplyImage(bgImg, _theme?.sliderBackground, new Color(0.15f, 0.05f, 0.2f));

        // Fill Area
        GameObject fillAreaGO = new GameObject("Fill Area");
        fillAreaGO.transform.SetParent(sliderGO.transform, false);
        RectTransform fillAreaRT = fillAreaGO.AddComponent<RectTransform>();
        fillAreaRT.anchorMin = Vector2.zero; fillAreaRT.anchorMax = Vector2.one;
        fillAreaRT.offsetMin = fillAreaRT.offsetMax = Vector2.zero;

        // Fill
        GameObject fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(fillAreaGO.transform, false);
        RectTransform fillRT = fillGO.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = fillRT.offsetMax = Vector2.zero;
        Image fillImg = fillGO.AddComponent<Image>();
        UIHelper.ApplyImage(fillImg, _theme?.sliderFill, new Color(0.7f, 0.1f, 0.9f));

        // Slider (added after children so fillRect assignment resolves correctly)
        _bossSlider              = sliderGO.AddComponent<Slider>();
        _bossSlider.interactable = false;
        _bossSlider.direction    = Slider.Direction.LeftToRight;
        _bossSlider.minValue     = 0f;
        _bossSlider.maxValue     = 1f;
        _bossSlider.value        = 1f;
        _bossSlider.fillRect     = fillRT;
        _bossSlider.handleRect   = null;

        _bossPanel.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Toast
    // ─────────────────────────────────────────────────────────────────────────

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
        textRT.anchorMin     = Vector2.zero; textRT.anchorMax = Vector2.one;
        textRT.offsetMin     = new Vector2(20f, 0f); textRT.offsetMax = new Vector2(-20f, 0f);

        _toastText           = textGO.AddComponent<TextMeshProUGUI>();
        _toastText.fontSize  = 28f;
        _toastText.alignment = TextAlignmentOptions.Center;
        _toastText.fontStyle = FontStyles.Bold;
        _toastText.color     = Color.white;
        if (font != null) _toastText.font = font;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Build button callback
    // ─────────────────────────────────────────────────────────────────────────

    private void OnBuildButtonClicked()
    {
        if (_buildMenuOpen)
        {
            HideBuildMenu();
            return;
        }

        // Cancel any active build placement or building selection
        if (BuildingManager.Instance != null && BuildingManager.Instance.IsPlacing)
            BuildingManager.Instance.CancelPlacement();

        HideChoicePanel();
        ShowBuildMenu();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Generic panel slide coroutine
    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator SlidePanel(RectTransform rt, float targetY, bool deactivateAfter,
                                   float duration, System.Action onDone)
    {
        float startY  = rt.anchoredPosition.y;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t  = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            rt.anchoredPosition = new Vector2(0f, Mathf.LerpUnclamped(startY, targetY, t));
            yield return null;
        }
        rt.anchoredPosition = new Vector2(0f, targetY);
        if (deactivateAfter) rt.gameObject.SetActive(false);
        onDone?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static Sprite GetBuildingIcon(BuildingDefinition def)
    {
        if (def == null) return null;
        if (def.icon != null) return def.icon;
        return def.prefab != null
            ? def.prefab.GetComponentInChildren<SpriteRenderer>()?.sprite
            : null;
    }

    private static void ApplyIconOrColor(Image img, Sprite sprite, Color fallback)
    {
        if (sprite != null) { img.sprite = sprite; img.color = Color.white; }
        else                { img.color  = fallback; }
    }

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

    private static void MakeOverlayLabel(Transform parent, string text, TMP_FontAsset font,
        float fontSize, Color color)
    {
        GameObject go    = new GameObject("Label");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin     = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin     = rt.offsetMax = Vector2.zero;
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = fontSize;
        tmp.color     = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        if (font != null) tmp.font = font;
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
