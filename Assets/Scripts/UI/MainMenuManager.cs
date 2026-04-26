using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the full main menu UI procedurally at runtime.
/// Attach to the manager object in MainMenuScene alongside
/// PersistentDataManager, AudioManager, and SceneTransitionManager.
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioClip   menuMusic;
    [SerializeField] private AudioClip[] menuPlaylist;

    [Header("UI Theme")]
    [SerializeField] private UITheme _theme;

    [Header("Background")]
    [SerializeField] private Sprite backgroundSprite;
    [Range(0f, 1f)]
    [SerializeField] private float overlayAlpha = 0.55f;

    [Header("Content — assign ScriptableObject arrays in Inspector")]
    [SerializeField] private LexikonEntry[]        lexikonEntries;
    [SerializeField] private MilestoneDefinition[] milestones;

    // ── Panels ────────────────────────────────────────────────────────────────
    private GameObject _homePanel;
    private GameObject _lexikonPanel;
    private GameObject _milestonesPanel;
    private GameObject _settingsPanel;
    private GameObject _activePanel;

    // ── Lexikon live refs ─────────────────────────────────────────────────────
    private TextMeshProUGUI _lexikonCurrencyLabel;
    private TextMeshProUGUI _homeCurrencyLabel;

    // ── Colours ───────────────────────────────────────────────────────────────
    private static readonly Color BgColor        = new Color(0.06f, 0.06f, 0.08f);
    private static readonly Color SidebarColor   = new Color(0.06f, 0.06f, 0.09f, 0.80f);
    private static readonly Color PanelColor     = new Color(0.08f, 0.08f, 0.11f, 0.72f);
    private static readonly Color NavNormal      = new Color(0.12f, 0.12f, 0.15f);
    private static readonly Color NavHover       = new Color(0.18f, 0.22f, 0.30f);
    private static readonly Color CardNormal     = new Color(0.12f, 0.12f, 0.15f);
    private static readonly Color AccentRed      = new Color(0.85f, 0.14f, 0.14f);

    private TMP_FontAsset _font;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Start()
    {
        _font = _theme?.font != null
            ? _theme.font
            : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (lexikonEntries == null || lexikonEntries.Length == 0)
            lexikonEntries = CreateDefaultLexikonEntries();
        EnsureEventSystem();
        BuildUI();
        ShowPanel(_homePanel);
        if (menuPlaylist != null && menuPlaylist.Length > 0)
            AudioManager.Instance?.PlayMenuPlaylist(menuPlaylist);
        else
            AudioManager.Instance?.PlayMusic(menuMusic);
    }

    private static LexikonEntry[] CreateDefaultLexikonEntries()
    {
        LexikonEntry E(string name, LexikonCategory cat, Color col,
                       string desc, bool def = true, int cost = 0)
        {
            var e = ScriptableObject.CreateInstance<LexikonEntry>();
            e.entryName = name; e.category = cat; e.color = col;
            e.description = desc;
            e.isUnlockedByDefault = def; e.unlockCost = cost;
            return e;
        }
        var T = LexikonCategory.Turret;
        var B = LexikonCategory.Building;
        return new[]
        {
            E("Turret Arrow",     T, new Color(0.76f,0.87f,0.55f), "Basic ranged turret. Fires arrows at the nearest enemy."),
            E("Turret Cannon",    T, new Color(0.85f,0.42f,0.18f), "Heavy turret. Slower but deals high damage per shot.",          false, 100),
            E("Turret Chain",     T, new Color(0.40f,0.72f,0.95f), "Chains lightning between nearby enemies after the first hit.",  false, 80),
            E("Turret Frost",     T, new Color(0.55f,0.80f,1.00f), "Slows enemies in range. Great for crowd control.",              false, 80),
            E("Turret Stun",      T, new Color(0.95f,0.90f,0.30f), "Occasionally stuns enemies, leaving them unable to move.",      false, 60),
            E("Turret Poison",    T, new Color(0.42f,0.80f,0.32f), "Applies a damage-over-time poison effect on hit.",              false, 70),
            E("Turret Fire",      T, new Color(1.00f,0.45f,0.10f), "Launches fireballs that deal splash damage on impact.",         false, 90),
            E("Barricade",        B, new Color(0.60f,0.42f,0.22f), "A sturdy wall that blocks enemy movement. Can be damaged."),
            E("Turret (Base)",    B, new Color(0.52f,0.60f,0.70f), "Foundation platform for placing any turret."),
            E("Resource Producer",B, new Color(0.44f,0.72f,0.44f), "Generates Wood or Metal over time. Core economy building."),
            E("Wood Mill",        B, new Color(0.72f,0.54f,0.28f), "Upgrade of the Resource Producer. Produces Wood at an increased rate."),
            E("Metal Smelter",    B, new Color(0.70f,0.70f,0.82f), "Upgrade of the Resource Producer. Smelts ore into metal."),
            E("Trade Post",       B, new Color(0.92f,0.76f,0.10f), "Premium upgrade. Produces both Wood and Metal. Expensive but versatile.", false, 50),
        };
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Root layout
    // ═════════════════════════════════════════════════════════════════════════

    private void BuildUI()
    {
        // ── Root canvas ───────────────────────────────────────────────────────
        GameObject canvasGO     = new GameObject("MainMenuCanvas");
        Canvas canvas           = canvasGO.AddComponent<Canvas>();
        canvas.renderMode       = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder     = 0;
        CanvasScaler scaler     = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode      = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 1f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // ── Background ────────────────────────────────────────────────────────
        // Background image
        GameObject bgGO = MakeBG(canvasGO.transform, "BG", BgColor);
        Image bgImg     = bgGO.GetComponent<Image>();
        if (backgroundSprite != null)
        {
            bgImg.sprite = backgroundSprite;
            bgImg.type   = Image.Type.Simple;
            bgImg.preserveAspect = false;
            bgImg.color  = Color.white;
        }
        Stretch(bgGO, Vector2.zero, Vector2.one);

        // Dark overlay
        GameObject overlayGO = MakeBG(canvasGO.transform, "Overlay", new Color(0f, 0f, 0f, overlayAlpha));
        Stretch(overlayGO, Vector2.zero, Vector2.one);

        // ── Sidebar (left 260 px) ─────────────────────────────────────────────
        GameObject sidebar   = new GameObject("Sidebar");
        sidebar.transform.SetParent(canvasGO.transform, false);
        RectTransform sideRT    = sidebar.AddComponent<RectTransform>();
        sideRT.anchorMin        = new Vector2(0f, 0f);
        sideRT.anchorMax        = new Vector2(0f, 1f);
        sideRT.pivot            = new Vector2(0f, 0.5f);
        sideRT.anchoredPosition = Vector2.zero;
        sideRT.sizeDelta        = new Vector2(260f, 0f);
        sidebar.AddComponent<Image>().color = SidebarColor;

        VerticalLayoutGroup sideVLG    = sidebar.AddComponent<VerticalLayoutGroup>();
        sideVLG.padding                = new RectOffset(0, 0, 16, 16);
        sideVLG.spacing                = 2f;
        sideVLG.childControlHeight     = false;
        sideVLG.childControlWidth      = true;
        sideVLG.childForceExpandWidth  = true;

        // Title
        var titleLbl     = MakeLabel(sidebar.transform, "Title", "VAMPIRE\nSURVIVALZ", 26f);
        titleLbl.color   = AccentRed;
        titleLbl.fontStyle = FontStyles.Bold;
        titleLbl.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 84f);

        // Nav buttons
        AddNavButton(sidebar.transform, "PLAY",        OnPlayClicked, new Color(0.14f, 0.42f, 0.14f), new Color(0.18f, 0.55f, 0.18f));
        AddNavButton(sidebar.transform, "Lexikon",    () => ShowPanel(_lexikonPanel));
        AddNavButton(sidebar.transform, "Milestones", () => ShowPanel(_milestonesPanel));
        AddNavButton(sidebar.transform, "Settings",   () => ShowPanel(_settingsPanel));

        // Flex spacer pushes Quit to bottom
        GameObject spacer   = new GameObject("Spacer");
        spacer.transform.SetParent(sidebar.transform, false);
        spacer.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 0f);
        spacer.AddComponent<LayoutElement>().flexibleHeight = 1f;

        AddNavButton(sidebar.transform, "Quit", QuitGame,
                     new Color(0.50f, 0.10f, 0.10f), new Color(0.65f, 0.14f, 0.14f));

        // ── Content area (right of sidebar) ───────────────────────────────────
        GameObject content    = new GameObject("Content");
        content.transform.SetParent(canvasGO.transform, false);
        RectTransform contentRT = content.AddComponent<RectTransform>();
        contentRT.anchorMin   = Vector2.zero;
        contentRT.anchorMax   = Vector2.one;
        contentRT.offsetMin   = new Vector2(260f, 0f);
        contentRT.offsetMax   = Vector2.zero;

        // Build all panels (hidden by default — ShowPanel activates one)
        _homePanel       = BuildHomePanel(content.transform);
        _lexikonPanel    = BuildLexikonPanel(content.transform);
        _milestonesPanel = BuildMilestonesPanel(content.transform);
        _settingsPanel   = BuildSettingsPanel(content.transform);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Panel: Home
    // ═════════════════════════════════════════════════════════════════════════

    private GameObject BuildHomePanel(Transform parent)
    {
        GameObject panel = MakeFullPanel(parent, "HomePanel");

        int coins = PersistentDataManager.Instance?.TotalCurrency ?? 0;
        _homeCurrencyLabel       = MakeLabel(panel.transform, "Coins", $"Coins:  {coins}", 26f);
        _homeCurrencyLabel.color = new Color(1f, 0.85f, 0.2f);
        _homeCurrencyLabel.alignment = TextAlignmentOptions.Right;

        RectTransform rt = _homeCurrencyLabel.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(1f, 1f);
        rt.anchorMax        = new Vector2(1f, 1f);
        rt.pivot            = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-20f, -16f);
        rt.sizeDelta        = new Vector2(240f, 40f);

        return panel;
    }

    private void OnPlayClicked()
    {
        SceneManager.LoadScene("SetupScene");
    }

    private static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Panel: Lexikon
    // ═════════════════════════════════════════════════════════════════════════

    private GameObject BuildLexikonPanel(Transform parent)
    {
        // No layout group on the panel — explicit anchors give reliable pixel sizes.
        GameObject panel = MakeFullPanel(parent, "LexikonPanel");

        const float SIDE  = 40f;
        const float TOP   = 40f;
        const float HDR_H = 70f;   // header (title + coins)
        const float GAP   = 12f;
        const float FLT_H = 44f;   // filter button row
        const float BOT   = 20f;
        float scrollOffsetTop = TOP + HDR_H + GAP + FLT_H + GAP;

        // ── Header ────────────────────────────────────────────────────────
        GameObject header = new GameObject("Header");
        header.transform.SetParent(panel.transform, false);
        RectTransform hdrRT = header.AddComponent<RectTransform>();
        hdrRT.anchorMin = new Vector2(0f, 1f);
        hdrRT.anchorMax = new Vector2(1f, 1f);
        hdrRT.pivot     = new Vector2(0.5f, 1f);
        hdrRT.offsetMin = new Vector2(SIDE, -(TOP + HDR_H));
        hdrRT.offsetMax = new Vector2(-SIDE, -TOP);

        HorizontalLayoutGroup hdrHL   = header.AddComponent<HorizontalLayoutGroup>();
        hdrHL.childControlHeight      = true;
        hdrHL.childForceExpandHeight  = true;
        hdrHL.childControlWidth       = false;
        hdrHL.childForceExpandWidth   = false;

        var title       = MakeLabel(header.transform, "Title", "LEXIKON", 42f);
        title.color     = Color.white;
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Left;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        int coins                   = PersistentDataManager.Instance?.TotalCurrency ?? 0;
        _lexikonCurrencyLabel       = MakeLabel(header.transform, "Coins", $"Coins: {coins}", 26f);
        _lexikonCurrencyLabel.color = new Color(1f, 0.85f, 0.2f);
        _lexikonCurrencyLabel.alignment = TextAlignmentOptions.Right;
        _lexikonCurrencyLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 200f;

        // ── Filter row ────────────────────────────────────────────────────
        float fltTop = TOP + HDR_H + GAP;
        GameObject filterRow = new GameObject("FilterRow");
        filterRow.transform.SetParent(panel.transform, false);
        RectTransform fltRT = filterRow.AddComponent<RectTransform>();
        fltRT.anchorMin = new Vector2(0f, 1f);
        fltRT.anchorMax = new Vector2(1f, 1f);
        fltRT.pivot     = new Vector2(0.5f, 1f);
        fltRT.offsetMin = new Vector2(SIDE, -(fltTop + FLT_H));
        fltRT.offsetMax = new Vector2(-SIDE, -fltTop);

        HorizontalLayoutGroup fltHL  = filterRow.AddComponent<HorizontalLayoutGroup>();
        fltHL.spacing                = 10f;
        fltHL.childControlHeight     = true;
        fltHL.childForceExpandHeight = true;
        fltHL.childControlWidth      = false;
        fltHL.childForceExpandWidth  = false;

        // ── Scroll view (fills remaining height) ──────────────────────────
        GameObject scrollView = new GameObject("ScrollView");
        scrollView.transform.SetParent(panel.transform, false);
        RectTransform scrRT = scrollView.AddComponent<RectTransform>();
        scrRT.anchorMin = new Vector2(0f, 0f);
        scrRT.anchorMax = new Vector2(1f, 1f);
        scrRT.offsetMin = new Vector2(SIDE, BOT);
        scrRT.offsetMax = new Vector2(-SIDE, -scrollOffsetTop);

        ScrollRect scroll     = scrollView.AddComponent<ScrollRect>();
        scroll.horizontal     = false;
        scroll.vertical       = true;
        scroll.scrollSensitivity = 30f;

        GameObject vp      = new GameObject("Viewport");
        vp.transform.SetParent(scrollView.transform, false);
        RectTransform vpRT = vp.AddComponent<RectTransform>();
        vpRT.anchorMin     = Vector2.zero;
        vpRT.anchorMax     = Vector2.one;
        vpRT.offsetMin     = Vector2.zero;
        vpRT.offsetMax     = Vector2.zero;
        vp.AddComponent<RectMask2D>();

        GameObject content    = new GameObject("Content");
        content.transform.SetParent(vp.transform, false);
        RectTransform cntRT   = content.AddComponent<RectTransform>();
        cntRT.anchorMin       = new Vector2(0f, 1f);
        cntRT.anchorMax       = new Vector2(1f, 1f);
        cntRT.pivot           = new Vector2(0.5f, 1f);
        cntRT.offsetMin       = Vector2.zero;
        cntRT.offsetMax       = Vector2.zero;

        scroll.viewport = vpRT;
        scroll.content  = cntRT;

        if (lexikonEntries == null || lexikonEntries.Length == 0)
        {
            AddEmptyState(panel.transform, "No lexikon entries defined.\nCreate LexikonEntry assets and assign them here.");
            MakeFilterButton(filterRow.transform, "All",        new Color(0.22f, 0.22f, 0.26f), () => {});
            MakeFilterButton(filterRow.transform, "Turrets",    new Color(0.28f, 0.14f, 0.44f), () => {});
            MakeFilterButton(filterRow.transform, "Buildings",  new Color(0.14f, 0.32f, 0.18f), () => {});
            MakeFilterButton(filterRow.transform, "Enemies",    new Color(0.48f, 0.12f, 0.12f), () => {});
            MakeFilterButton(filterRow.transform, "Characters", new Color(0.14f, 0.28f, 0.48f), () => {});
            return panel;
        }

        VerticalLayoutGroup contentVL  = content.AddComponent<VerticalLayoutGroup>();
        contentVL.spacing              = 6f;
        contentVL.padding              = new RectOffset(0, 0, 8, 8);
        contentVL.childControlHeight   = false;
        contentVL.childControlWidth    = true;
        contentVL.childForceExpandWidth = true;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Build rows, collect for filter
        var rows = new List<GameObject>();
        foreach (var entry in lexikonEntries)
        {
            if (entry == null) continue;
            rows.Add(BuildLexikonRow(content.transform, entry));
        }

        // Wire filter buttons
        void SetFilter(LexikonCategory? cat)
        {
            foreach (var row in rows)
            {
                if (row == null) continue;
                var tag = row.GetComponent<LexikonCategoryTag>();
                row.SetActive(cat == null || tag?.Category == cat);
            }
        }

        MakeFilterButton(filterRow.transform, "All",        new Color(0.22f, 0.22f, 0.26f), () => SetFilter(null));
        MakeFilterButton(filterRow.transform, "Turrets",    new Color(0.28f, 0.14f, 0.44f), () => SetFilter(LexikonCategory.Turret));
        MakeFilterButton(filterRow.transform, "Buildings",  new Color(0.14f, 0.32f, 0.18f), () => SetFilter(LexikonCategory.Building));
        MakeFilterButton(filterRow.transform, "Enemies",    new Color(0.48f, 0.12f, 0.12f), () => SetFilter(LexikonCategory.Enemy));
        MakeFilterButton(filterRow.transform, "Characters", new Color(0.14f, 0.28f, 0.48f), () => SetFilter(LexikonCategory.Character));

        return panel;
    }

    private static string GetLiveStats(LexikonEntry entry)
    {
        var lines = new List<StatLine>();
        if (entry.linkedPrefab != null)
            foreach (var src in entry.linkedPrefab.GetComponents<ILexikonSource>())
                lines.AddRange(src.GetLexikonStats());
        if (entry.linkedBuildingCard != null)
            lines.AddRange(entry.linkedBuildingCard.GetLexikonStats());
        if (entry.linkedCharacter != null)
            lines.AddRange(entry.linkedCharacter.GetLexikonStats());

        if (lines.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0) sb.Append("  ");
            sb.Append(lines[i].label).Append(": ").Append(lines[i].value);
        }
        return sb.ToString();
    }

    private GameObject BuildLexikonRow(Transform parent, LexikonEntry entry)
    {
        var pdm       = PersistentDataManager.Instance;
        bool unlocked = pdm?.IsUnlocked(entry) ?? entry.isUnlockedByDefault;
        string stats  = unlocked ? GetLiveStats(entry) : string.Empty;

        GameObject row = new GameObject($"Row_{entry.entryName}");
        row.transform.SetParent(parent, false);
        row.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 84f);
        row.AddComponent<Image>().color = unlocked
            ? new Color(0.12f, 0.12f, 0.15f)
            : new Color(0.08f, 0.08f, 0.10f);

        LexikonCategoryTag tag = row.AddComponent<LexikonCategoryTag>();
        tag.Category           = entry.category;

        HorizontalLayoutGroup hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.padding               = new RectOffset(0, 14, 8, 8);
        hl.spacing               = 10f;
        hl.childControlHeight    = true;

        // Colour badge
        GameObject badge = new GameObject("Badge");
        badge.transform.SetParent(row.transform, false);
        badge.AddComponent<RectTransform>().sizeDelta = new Vector2(8f, 0f);
        badge.AddComponent<Image>().color = unlocked ? entry.color : new Color(0.25f, 0.25f, 0.25f);
        badge.AddComponent<LayoutElement>().preferredWidth = 8f;

        // Sprite image
        GameObject sprGO = new GameObject("Sprite");
        sprGO.transform.SetParent(row.transform, false);
        LayoutElement sprLE       = sprGO.AddComponent<LayoutElement>();
        sprLE.preferredWidth      = 68f;
        sprLE.flexibleHeight      = 1f;
        Image sprImg              = sprGO.AddComponent<Image>();
        sprImg.preserveAspect     = true;
        if (unlocked && entry.sprite != null)
        {
            sprImg.sprite = entry.sprite;
            sprImg.color  = Color.white;
        }
        else
        {
            sprImg.color = unlocked
                ? new Color(0.20f, 0.20f, 0.22f)
                : new Color(0.13f, 0.13f, 0.15f);
        }

        // Text block
        GameObject txt = new GameObject("Text");
        txt.transform.SetParent(row.transform, false);
        txt.AddComponent<RectTransform>();
        txt.AddComponent<LayoutElement>().flexibleWidth = 1f;
        VerticalLayoutGroup tvl   = txt.AddComponent<VerticalLayoutGroup>();
        tvl.childControlHeight    = false;
        tvl.childControlWidth     = true;
        tvl.childForceExpandWidth = true;
        tvl.padding               = new RectOffset(0, 0, 4, 4);

        var nameLbl     = MakeLabel(txt.transform, "Name",
            unlocked ? entry.entryName : $"??? ({entry.category})", 20f);
        nameLbl.color   = unlocked ? Color.white : new Color(0.40f, 0.40f, 0.40f);
        nameLbl.fontStyle  = FontStyles.Bold;
        nameLbl.alignment  = TextAlignmentOptions.Left;
        SetSizeDelta(nameLbl, 0f, 26f);

        var descLbl     = MakeLabel(txt.transform, "Desc",
            unlocked ? entry.description : "Unlock to reveal this entry.", 15f);
        descLbl.color   = new Color(0.60f, 0.60f, 0.60f);
        descLbl.alignment = TextAlignmentOptions.Left;
        SetSizeDelta(descLbl, 0f, 22f);

        // Stats label — always created; hidden when locked or empty
        var statsLbl    = MakeLabel(txt.transform, "Stats", stats, 13f);
        statsLbl.color  = new Color(0.50f, 0.86f, 0.50f);
        statsLbl.alignment = TextAlignmentOptions.Left;
        statsLbl.enableWordWrapping = true;
        SetSizeDelta(statsLbl, 0f, 18f);
        statsLbl.gameObject.SetActive(unlocked && !string.IsNullOrEmpty(stats));

        // Right column: unlock button / unlocked badge / category label
        if (!entry.isUnlockedByDefault && !unlocked)
        {
            GameObject btnGO  = new GameObject("UnlockBtn");
            btnGO.transform.SetParent(row.transform, false);
            btnGO.AddComponent<LayoutElement>().preferredWidth = 130f;
            Image btnImg      = btnGO.AddComponent<Image>();
            UIHelper.ApplyImage(btnImg, _theme?.buttonGold, new Color(0.62f, 0.50f, 0.06f));
            Button btn        = btnGO.AddComponent<Button>();
            btn.targetGraphic = btnImg;

            var btnLbl = MakeLabel(btnGO.transform, "BtnLbl",
                $"Unlock\n{entry.unlockCost} coins", 14f);
            btnLbl.alignment = TextAlignmentOptions.Center;
            btnLbl.color     = Color.white;
            Stretch(btnLbl.gameObject, Vector2.zero, Vector2.one);

            GameObject badge2 = new GameObject("UnlockedBadge");
            badge2.transform.SetParent(row.transform, false);
            badge2.AddComponent<RectTransform>();
            badge2.AddComponent<LayoutElement>().preferredWidth = 130f;
            var badge2Lbl      = MakeLabel(badge2.transform, "Lbl", "✓ UNLOCKED", 16f);
            badge2Lbl.color    = new Color(0.20f, 0.88f, 0.20f);
            badge2Lbl.fontStyle = FontStyles.Bold;
            badge2Lbl.alignment = TextAlignmentOptions.Center;
            Stretch(badge2Lbl.gameObject, Vector2.zero, Vector2.one);
            badge2.SetActive(false);

            btn.onClick.AddListener(() => OnUnlockClicked(entry, row, btnGO, badge2, badge, sprGO));
        }
        else if (!entry.isUnlockedByDefault)
        {
            var unlockBadge      = MakeLabel(row.transform, "UnlockedBadge", "✓ UNLOCKED", 16f);
            unlockBadge.color    = new Color(0.20f, 0.88f, 0.20f);
            unlockBadge.fontStyle = FontStyles.Bold;
            unlockBadge.alignment = TextAlignmentOptions.Center;
            unlockBadge.gameObject.AddComponent<LayoutElement>().preferredWidth = 130f;
        }
        else
        {
            var catLbl  = MakeLabel(row.transform, "Cat", entry.category.ToString(), 14f);
            catLbl.color = new Color(0.45f, 0.45f, 0.50f);
            catLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 88f;
        }

        return row;
    }

    private void OnUnlockClicked(LexikonEntry entry, GameObject row,
                                  GameObject unlockBtn, GameObject unlockedBadge,
                                  GameObject colorBadge, GameObject spriteGO)
    {
        if (PersistentDataManager.Instance == null) return;

        bool success = PersistentDataManager.Instance.UnlockItem(entry);
        if (!success) return;

        unlockBtn.SetActive(false);
        unlockedBadge.SetActive(true);

        colorBadge.GetComponent<Image>().color = entry.color;

        var nameLabel = row.transform.Find("Text/Name")?.GetComponent<TextMeshProUGUI>();
        if (nameLabel != null) { nameLabel.text = entry.entryName; nameLabel.color = Color.white; }

        var descLabel = row.transform.Find("Text/Desc")?.GetComponent<TextMeshProUGUI>();
        if (descLabel != null) descLabel.text = entry.description;

        // Reveal stats
        string liveStats = GetLiveStats(entry);
        var statsLabel   = row.transform.Find("Text/Stats")?.GetComponent<TextMeshProUGUI>();
        if (statsLabel != null && !string.IsNullOrEmpty(liveStats))
        {
            statsLabel.text = liveStats;
            statsLabel.gameObject.SetActive(true);
        }

        // Reveal sprite
        if (spriteGO != null)
        {
            Image sprImg = spriteGO.GetComponent<Image>();
            if (entry.sprite != null)
            {
                sprImg.sprite = entry.sprite;
                sprImg.color  = Color.white;
            }
            else
            {
                sprImg.color = new Color(0.20f, 0.20f, 0.22f);
            }
        }

        row.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.15f);

        int coins = PersistentDataManager.Instance.TotalCurrency;
        if (_lexikonCurrencyLabel != null) _lexikonCurrencyLabel.text = $"Coins: {coins}";
        if (_homeCurrencyLabel    != null) _homeCurrencyLabel.text    = $"Coins:  {coins}";
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Panel: Milestones
    // ═════════════════════════════════════════════════════════════════════════

    private GameObject BuildMilestonesPanel(Transform parent)
    {
        GameObject panel = MakeFullPanel(parent, "MilestonesPanel");

        VerticalLayoutGroup outerVL  = panel.AddComponent<VerticalLayoutGroup>();
        outerVL.padding              = new RectOffset(40, 40, 40, 20);
        outerVL.spacing              = 12f;
        outerVL.childControlHeight   = false;
        outerVL.childControlWidth    = true;
        outerVL.childForceExpandWidth = true;

        var title    = MakeLabel(panel.transform, "Title", "MILESTONES", 42f);
        title.color  = Color.white;
        title.fontStyle = FontStyles.Bold;
        SetSizeDelta(title, 0f, 58f);

        // Stats summary bar
        var pdm      = PersistentDataManager.Instance;
        string summ  = $"Best Wave: {pdm?.BestWave ?? 0}     " +
                       $"Total Kills: {pdm?.TotalEnemiesKilled ?? 0}     " +
                       $"Games Played: {pdm?.TotalGamesPlayed ?? 0}";
        var statLbl  = MakeLabel(panel.transform, "Stats", summ, 22f);
        statLbl.color = new Color(0.60f, 0.60f, 0.65f);
        SetSizeDelta(statLbl, 0f, 34f);

        if (milestones == null || milestones.Length == 0)
        {
            AddEmptyState(panel.transform, "No milestones defined.\nCreate MilestoneDefinition assets and assign them here.");
            return panel;
        }

        GameObject scrollView = BuildScrollView(panel.transform, 0f, vertical: true);
        scrollView.AddComponent<LayoutElement>().flexibleHeight = 1f;

        GameObject content  = scrollView.transform.Find("Viewport/Content").gameObject;
        VerticalLayoutGroup cvl  = content.AddComponent<VerticalLayoutGroup>();
        cvl.spacing              = 5f;
        cvl.padding              = new RectOffset(8, 8, 8, 8);
        cvl.childControlHeight   = false;
        cvl.childControlWidth    = true;
        cvl.childForceExpandWidth = true;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        foreach (var m in milestones)
        {
            if (m == null) continue;
            bool complete = pdm?.IsMilestoneComplete(m) ?? false;
            int  progress = pdm?.GetProgress(m) ?? 0;
            BuildMilestoneRow(content.transform, m, complete, progress);
        }
        return panel;
    }

    private void BuildMilestoneRow(Transform parent, MilestoneDefinition m,
                                    bool complete, int progress)
    {
        GameObject row   = new GameObject($"MS_{m.title}");
        row.transform.SetParent(parent, false);
        row.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 64f);
        row.AddComponent<Image>().color = complete
            ? new Color(0.09f, 0.16f, 0.09f)
            : new Color(0.12f, 0.12f, 0.14f);

        HorizontalLayoutGroup hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.padding               = new RectOffset(16, 16, 10, 10);
        hl.spacing               = 14f;
        hl.childControlHeight    = true;

        // Tick / circle
        var icon         = MakeLabel(row.transform, "Icon", complete ? "✓" : "○", 26f);
        icon.color       = complete ? new Color(0.20f, 0.88f, 0.20f) : new Color(0.38f, 0.38f, 0.38f);
        icon.gameObject.AddComponent<LayoutElement>().preferredWidth = 38f;

        // Text block
        GameObject txt      = new GameObject("Text");
        txt.transform.SetParent(row.transform, false);
        txt.AddComponent<RectTransform>();
        txt.AddComponent<LayoutElement>().flexibleWidth = 1f;
        VerticalLayoutGroup tvl  = txt.AddComponent<VerticalLayoutGroup>();
        tvl.childControlHeight   = false;
        tvl.childControlWidth    = true;
        tvl.childForceExpandWidth = true;

        var titLbl       = MakeLabel(txt.transform, "Title", m.title, 20f);
        titLbl.color     = complete ? new Color(0.88f, 0.82f, 0.18f) : Color.white;
        titLbl.fontStyle = complete ? FontStyles.Bold : FontStyles.Normal;
        titLbl.alignment = TextAlignmentOptions.Left;
        SetSizeDelta(titLbl, 0f, 26f);

        var descLbl      = MakeLabel(txt.transform, "Desc", m.description, 15f);
        descLbl.color    = new Color(0.52f, 0.52f, 0.52f);
        descLbl.alignment = TextAlignmentOptions.Left;
        SetSizeDelta(descLbl, 0f, 22f);

        // Progress "X / Y"
        var progLbl      = MakeLabel(row.transform, "Prog",
            $"{progress} / {m.requiredValue}", 18f);
        progLbl.color    = complete ? new Color(0.20f, 0.88f, 0.20f) : new Color(0.52f, 0.52f, 0.52f);
        progLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 110f;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Panel: Settings
    // ═════════════════════════════════════════════════════════════════════════

    private GameObject BuildSettingsPanel(Transform parent)
    {
        GameObject panel = MakeFullPanel(parent, "SettingsPanel");

        const float SIDE  = 80f;
        const float TOP   = 40f;
        const float HDR_H = 70f;

        // ── Fixed header ──────────────────────────────────────────────────
        GameObject hdr = new GameObject("Header");
        hdr.transform.SetParent(panel.transform, false);
        RectTransform hdrRT = hdr.AddComponent<RectTransform>();
        hdrRT.anchorMin = new Vector2(0f, 1f);
        hdrRT.anchorMax = new Vector2(1f, 1f);
        hdrRT.pivot     = new Vector2(0.5f, 1f);
        hdrRT.offsetMin = new Vector2(SIDE, -(TOP + HDR_H));
        hdrRT.offsetMax = new Vector2(-SIDE, -TOP);

        var titleLbl    = MakeLabel(hdr.transform, "Title", "SETTINGS", 42f);
        titleLbl.color  = Color.white;
        titleLbl.fontStyle = FontStyles.Bold;
        titleLbl.alignment = TextAlignmentOptions.Left;
        Stretch(titleLbl.gameObject, Vector2.zero, Vector2.one);

        // ── Scrollable body ───────────────────────────────────────────────
        GameObject scrollGO = new GameObject("ScrollView");
        scrollGO.transform.SetParent(panel.transform, false);
        RectTransform scrRT = scrollGO.AddComponent<RectTransform>();
        scrRT.anchorMin = new Vector2(0f, 0f);
        scrRT.anchorMax = new Vector2(1f, 1f);
        scrRT.offsetMin = new Vector2(SIDE, 20f);
        scrRT.offsetMax = new Vector2(-SIDE, -(TOP + HDR_H + 8f));

        ScrollRect scroll     = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal     = false;
        scroll.vertical       = true;
        scroll.scrollSensitivity = 30f;

        GameObject vp      = new GameObject("Viewport");
        vp.transform.SetParent(scrollGO.transform, false);
        RectTransform vpRT = vp.AddComponent<RectTransform>();
        vpRT.anchorMin     = Vector2.zero; vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin     = Vector2.zero; vpRT.offsetMax = Vector2.zero;
        vp.AddComponent<RectMask2D>();

        GameObject body    = new GameObject("Content");
        body.transform.SetParent(vp.transform, false);
        RectTransform bodyRT = body.AddComponent<RectTransform>();
        bodyRT.anchorMin   = new Vector2(0f, 1f);
        bodyRT.anchorMax   = new Vector2(1f, 1f);
        bodyRT.pivot       = new Vector2(0.5f, 1f);
        bodyRT.offsetMin   = Vector2.zero;
        bodyRT.offsetMax   = Vector2.zero;

        scroll.viewport    = vpRT;
        scroll.content     = bodyRT;

        VerticalLayoutGroup vl   = body.AddComponent<VerticalLayoutGroup>();
        vl.padding               = new RectOffset(0, 0, 4, 60);
        vl.spacing               = 0f;
        vl.childControlHeight    = false;
        vl.childControlWidth     = true;
        vl.childForceExpandWidth = true;
        body.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var pdm = PersistentDataManager.Instance;

        // ── Audio ─────────────────────────────────────────────────────────
        AddSettingsSectionHeader(body.transform, "AUDIO");
        AddVolumeRow(body.transform, "Music Volume",
            pdm?.MusicVolume ?? 0.8f,
            v => pdm?.SetMusicVolume(v));
        AddSettingsDivider(body.transform);
        AddVolumeRow(body.transform, "SFX Volume",
            pdm?.SFXVolume ?? 0.8f,
            v => pdm?.SetSFXVolume(v));
        AddSettingsSpacer(body.transform, 28f);

        // ── Display ───────────────────────────────────────────────────────
        AddSettingsSectionHeader(body.transform, "DISPLAY");
        AddFullscreenRow(body.transform);
        AddSettingsSpacer(body.transform, 28f);

        // ── Progress ──────────────────────────────────────────────────────
        AddSettingsSectionHeader(body.transform, "PROGRESS");
        AddProgressStatsBlock(body.transform, pdm);
        AddSettingsSpacer(body.transform, 14f);
        AddResetProgressRow(body.transform, pdm);
        AddSettingsSpacer(body.transform, 48f);

        // ── Version ───────────────────────────────────────────────────────
        var ver      = MakeLabel(body.transform, "Version", "VAMPIRE SURVIVALZ  —  v0.1-alpha", 14f);
        ver.color    = new Color(0.28f, 0.28f, 0.30f);
        ver.alignment = TextAlignmentOptions.Left;
        SetSizeDelta(ver, 0f, 28f);

        return panel;
    }

    private void AddSettingsSectionHeader(Transform parent, string text)
    {
        var lbl      = MakeLabel(parent, text + "_Hdr", text, 13f);
        lbl.color    = new Color(0.40f, 0.50f, 0.65f);
        lbl.fontStyle = FontStyles.Bold;
        lbl.alignment = TextAlignmentOptions.Left;
        SetSizeDelta(lbl, 0f, 40f);

        GameObject line = new GameObject("HdrLine");
        line.transform.SetParent(parent, false);
        line.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 1f);
        line.AddComponent<Image>().color = new Color(0.20f, 0.24f, 0.32f);
    }

    private static void AddSettingsDivider(Transform parent)
    {
        GameObject line = new GameObject("Divider");
        line.transform.SetParent(parent, false);
        line.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 1f);
        line.AddComponent<Image>().color = new Color(0.14f, 0.14f, 0.16f);
    }

    private static void AddSettingsSpacer(Transform parent, float height)
    {
        GameObject sp = new GameObject("Spacer");
        sp.transform.SetParent(parent, false);
        sp.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, height);
    }

    private void AddVolumeRow(Transform parent, string label, float initial,
                               Action<float> onChange)
    {
        GameObject row   = new GameObject(label + "Row");
        row.transform.SetParent(parent, false);
        row.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 56f);
        HorizontalLayoutGroup hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.padding               = new RectOffset(0, 0, 12, 12);
        hl.spacing               = 20f;
        hl.childControlHeight    = true;
        hl.childForceExpandHeight = true;
        hl.childControlWidth     = false;
        hl.childForceExpandWidth = false;

        var lbl      = MakeLabel(row.transform, "Lbl", label, 22f);
        lbl.color    = new Color(0.78f, 0.78f, 0.78f);
        lbl.alignment = TextAlignmentOptions.Left;
        lbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 240f;

        GameObject sliderGO = new GameObject("Slider");
        sliderGO.transform.SetParent(row.transform, false);
        sliderGO.AddComponent<RectTransform>();
        sliderGO.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var valLbl   = MakeLabel(row.transform, "Val", $"{initial:P0}", 20f);
        valLbl.color = new Color(0.52f, 0.84f, 1f);
        valLbl.alignment = TextAlignmentOptions.Right;
        valLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 70f;

        BuildSlider(sliderGO.transform, initial, _theme, v =>
        {
            onChange?.Invoke(v);
            valLbl.text = $"{v:P0}";
        });
    }

    private void AddFullscreenRow(Transform parent)
    {
        GameObject row   = new GameObject("FullscreenRow");
        row.transform.SetParent(parent, false);
        row.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 56f);
        HorizontalLayoutGroup hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.padding               = new RectOffset(0, 0, 12, 12);
        hl.spacing               = 20f;
        hl.childControlHeight    = true;
        hl.childForceExpandHeight = true;
        hl.childControlWidth     = false;
        hl.childForceExpandWidth = false;

        var lbl      = MakeLabel(row.transform, "Lbl", "Fullscreen", 22f);
        lbl.color    = new Color(0.78f, 0.78f, 0.78f);
        lbl.alignment = TextAlignmentOptions.Left;
        lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        bool isFS        = Screen.fullScreen;
        GameObject btnGO = new GameObject("ToggleBtn");
        btnGO.transform.SetParent(row.transform, false);
        btnGO.AddComponent<LayoutElement>().preferredWidth = 100f;
        Image btnImg     = btnGO.AddComponent<Image>();
        UIHelper.ApplyImage(btnImg, _theme?.buttonNav,
            isFS ? new Color(0.14f, 0.52f, 0.14f) : new Color(0.36f, 0.14f, 0.14f));
        Button btn       = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImg;

        var btnLbl       = MakeLabel(btnGO.transform, "Lbl", isFS ? "ON" : "OFF", 20f);
        btnLbl.fontStyle = FontStyles.Bold;
        Stretch(btnLbl.gameObject, Vector2.zero, Vector2.one);

        btn.onClick.AddListener(() =>
        {
            bool next        = !Screen.fullScreen;
            Screen.fullScreen = next;
            UIHelper.ApplyImage(btnImg, _theme?.buttonSecondary,
                next ? new Color(0.14f, 0.52f, 0.14f) : new Color(0.36f, 0.14f, 0.14f));
            btnLbl.text      = next ? "ON" : "OFF";
            PlayerPrefs.SetInt("Fullscreen", next ? 1 : 0);
            PlayerPrefs.Save();
        });
    }

    private void AddProgressStatsBlock(Transform parent, PersistentDataManager pdm)
    {
        (string key, string val)[] stats =
        {
            ("Best Wave",     (pdm?.BestWave             ?? 0).ToString()),
            ("Total Kills",   (pdm?.TotalEnemiesKilled   ?? 0).ToString()),
            ("Games Played",  (pdm?.TotalGamesPlayed     ?? 0).ToString()),
            ("Total Coins",   (pdm?.TotalCurrency        ?? 0).ToString()),
        };

        foreach (var (key, val) in stats)
        {
            GameObject row   = new GameObject("Stat_" + key);
            row.transform.SetParent(parent, false);
            row.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 40f);
            HorizontalLayoutGroup hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding               = new RectOffset(0, 0, 8, 8);
            hl.spacing               = 16f;
            hl.childControlHeight    = true;
            hl.childForceExpandHeight = true;
            hl.childControlWidth     = false;
            hl.childForceExpandWidth = false;

            var keyLbl       = MakeLabel(row.transform, "Key", key, 18f);
            keyLbl.color     = new Color(0.52f, 0.52f, 0.58f);
            keyLbl.alignment = TextAlignmentOptions.Left;
            keyLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 220f;

            var valLbl       = MakeLabel(row.transform, "Val", val, 18f);
            valLbl.color     = Color.white;
            valLbl.fontStyle = FontStyles.Bold;
            valLbl.alignment = TextAlignmentOptions.Left;
        }
    }

    private void AddResetProgressRow(Transform parent, PersistentDataManager pdm)
    {
        GameObject container = new GameObject("ResetRow");
        container.transform.SetParent(parent, false);
        container.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 56f);
        HorizontalLayoutGroup hl = container.AddComponent<HorizontalLayoutGroup>();
        hl.padding               = new RectOffset(0, 0, 10, 10);
        hl.spacing               = 18f;
        hl.childControlHeight    = true;
        hl.childForceExpandHeight = true;
        hl.childControlWidth     = false;
        hl.childForceExpandWidth = false;

        var warnLbl      = MakeLabel(container.transform, "Warn",
            "Resets all stats, coins and unlocks.", 15f);
        warnLbl.color    = new Color(0.55f, 0.44f, 0.44f);
        warnLbl.alignment = TextAlignmentOptions.Left;
        warnLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        GameObject btnGO = new GameObject("ResetBtn");
        btnGO.transform.SetParent(container.transform, false);
        btnGO.AddComponent<LayoutElement>().preferredWidth = 180f;
        Image btnImg     = btnGO.AddComponent<Image>();
        Color dangerCol  = new Color(0.44f, 0.10f, 0.10f);
        UIHelper.ApplyImage(btnImg, _theme?.buttonDanger, dangerCol);
        Button btn       = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.colors = UIHelper.BtnColors(_theme?.buttonDanger, dangerCol,
            new Color(0.64f, 0.14f, 0.14f), new Color(0.28f, 0.06f, 0.06f));

        var btnLbl       = MakeLabel(btnGO.transform, "Lbl", "RESET PROGRESS", 15f);
        btnLbl.fontStyle = FontStyles.Bold;
        Stretch(btnLbl.gameObject, Vector2.zero, Vector2.one);

        bool confirming = false;
        btn.onClick.AddListener(() =>
        {
            if (!confirming)
            {
                confirming         = true;
                btnImg.color       = new Color(0.82f, 0.12f, 0.12f);
                ColorBlock cb      = btn.colors;
                cb.normalColor     = new Color(0.82f, 0.12f, 0.12f);
                cb.highlightedColor = new Color(0.95f, 0.16f, 0.16f);
                btn.colors         = cb;
                btnLbl.text        = "CONFIRM RESET";
                warnLbl.text       = "Click again to confirm. This cannot be undone!";
                warnLbl.color      = new Color(0.95f, 0.56f, 0.12f);
            }
            else
            {
                pdm?.ResetProgress();
                btn.interactable   = false;
                btnImg.color       = new Color(0.14f, 0.30f, 0.14f);
                btnLbl.text        = "RESET DONE";
                warnLbl.text       = "All progress has been reset.";
                warnLbl.color      = new Color(0.35f, 0.65f, 0.35f);
            }
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Reusable construction helpers
    // ═════════════════════════════════════════════════════════════════════════

    private void ShowPanel(GameObject panel)
    {
        if (_activePanel != null) _activePanel.SetActive(false);
        _activePanel = panel;
        if (_activePanel != null) _activePanel.SetActive(true);
    }

    /// <summary>
    /// Creates a full-panel with a title label and a scroll view whose content GO is returned.
    /// </summary>
    private GameObject MakeTitledScrollPanel(Transform parent, string name, string titleText,
                                              out GameObject content, bool horizontal)
    {
        GameObject panel = MakeFullPanel(parent, name);

        VerticalLayoutGroup vl   = panel.AddComponent<VerticalLayoutGroup>();
        vl.padding               = new RectOffset(40, 40, 40, 20);
        vl.spacing               = 16f;
        vl.childControlHeight    = false;
        vl.childControlWidth     = true;
        vl.childForceExpandWidth = true;

        var title    = MakeLabel(panel.transform, "Title", titleText, 42f);
        title.color  = Color.white;
        title.fontStyle = FontStyles.Bold;
        SetSizeDelta(title, 0f, 58f);

        GameObject scrollView = BuildScrollView(panel.transform, 0f, !horizontal);
        scrollView.AddComponent<LayoutElement>().flexibleHeight = 1f;

        content = scrollView.transform.Find("Viewport/Content").gameObject;
        return panel;
    }

    /// <summary>Builds a ScrollRect + Viewport + Content hierarchy.</summary>
    private static GameObject BuildScrollView(Transform parent, float fixedHeight, bool vertical)
    {
        GameObject go   = new GameObject("ScrollView");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.sizeDelta     = new Vector2(0f, fixedHeight);

        ScrollRect scroll = go.AddComponent<ScrollRect>();
        scroll.horizontal = !vertical;
        scroll.vertical   = vertical;
        scroll.scrollSensitivity = 30f;

        // Viewport
        GameObject vp    = new GameObject("Viewport");
        vp.transform.SetParent(go.transform, false);
        RectTransform vpRT = vp.AddComponent<RectTransform>();
        vpRT.anchorMin   = Vector2.zero;
        vpRT.anchorMax   = Vector2.one;
        vpRT.offsetMin   = Vector2.zero;
        vpRT.offsetMax   = Vector2.zero;
        vp.AddComponent<RectMask2D>();

        // Content
        GameObject content   = new GameObject("Content");
        content.transform.SetParent(vp.transform, false);
        RectTransform cRT    = content.AddComponent<RectTransform>();

        if (vertical)
        {
            cRT.anchorMin = new Vector2(0f, 1f);
            cRT.anchorMax = new Vector2(1f, 1f);
            cRT.pivot     = new Vector2(0.5f, 1f);
            cRT.offsetMin = Vector2.zero;
            cRT.offsetMax = Vector2.zero;
        }
        else
        {
            cRT.anchorMin = new Vector2(0f, 0f);
            cRT.anchorMax = new Vector2(0f, 1f);
            cRT.pivot     = new Vector2(0f, 0.5f);
            cRT.offsetMin = Vector2.zero;
            cRT.offsetMax = Vector2.zero;
        }

        scroll.viewport = vpRT;
        scroll.content  = cRT;
        return go;
    }

    private static Slider BuildSlider(Transform parent, float initial, UITheme theme, UnityEngine.Events.UnityAction<float> onChange)
    {
        // Background
        GameObject bg    = new GameObject("BG");
        bg.transform.SetParent(parent, false);
        RectTransform bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin   = new Vector2(0f, 0.3f);
        bgRT.anchorMax   = new Vector2(1f, 0.7f);
        bgRT.offsetMin   = Vector2.zero;
        bgRT.offsetMax   = Vector2.zero;
        Image bgImg      = bg.AddComponent<Image>();
        UIHelper.ApplyImage(bgImg, theme?.sliderBackground, new Color(0.18f, 0.18f, 0.18f));

        // Fill area
        GameObject fa    = new GameObject("FillArea");
        fa.transform.SetParent(parent, false);
        RectTransform faRT = fa.AddComponent<RectTransform>();
        faRT.anchorMin   = new Vector2(0f, 0.3f);
        faRT.anchorMax   = new Vector2(1f, 0.7f);
        faRT.offsetMin   = new Vector2(5f, 0f);
        faRT.offsetMax   = new Vector2(-15f, 0f);

        GameObject fill  = new GameObject("Fill");
        fill.transform.SetParent(fa.transform, false);
        RectTransform fillRT = fill.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = new Vector2(1f, 1f);
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        Image fillImg    = fill.AddComponent<Image>();
        UIHelper.ApplyImage(fillImg, theme?.sliderFill, new Color(0.2f, 0.6f, 1f));

        // Handle
        GameObject ha    = new GameObject("HandleArea");
        ha.transform.SetParent(parent, false);
        RectTransform haRT = ha.AddComponent<RectTransform>();
        haRT.anchorMin   = Vector2.zero;
        haRT.anchorMax   = Vector2.one;
        haRT.offsetMin   = new Vector2(10f, 0f);
        haRT.offsetMax   = new Vector2(-10f, 0f);

        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(ha.transform, false);
        RectTransform handleRT = handle.AddComponent<RectTransform>();
        handleRT.sizeDelta = new Vector2(20f, 0f);
        handleRT.anchorMin = Vector2.zero;
        handleRT.anchorMax = new Vector2(0f, 1f);
        Image handleImg    = handle.AddComponent<Image>();
        UIHelper.ApplyImage(handleImg, theme?.sliderHandle, Color.white);

        Slider slider      = parent.gameObject.AddComponent<Slider>();
        slider.minValue    = 0f;
        slider.maxValue    = 1f;
        slider.value       = initial;
        slider.fillRect    = fillRT;
        slider.handleRect  = handleRT;
        slider.targetGraphic = handleImg;
        slider.onValueChanged.AddListener(onChange);
        return slider;
    }

    private static void AddEmptyState(Transform parent, string msg)
    {
        var lbl      = parent.gameObject.AddComponent<TextMeshProUGUI>();
        // Can't add TMP to existing GO with Image — use child
        var go       = new GameObject("EmptyState");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.1f, 0.35f);
        rt.anchorMax = new Vector2(0.9f, 0.65f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var tmp      = go.AddComponent<TextMeshProUGUI>();
        tmp.text     = msg;
        tmp.fontSize = 22f;
        tmp.color    = new Color(0.44f, 0.44f, 0.44f);
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;
        // Clean up the mistakenly added component
        Destroy(lbl);
    }

    private void AddNavButton(Transform parent, string label, Action onClick,
                               Color normal = default, Color hover = default)
    {
        if (normal == default) normal = NavNormal;
        if (hover  == default) hover  = NavHover;

        GameObject go    = new GameObject($"Nav_{label}");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 52f);
        Image img        = go.AddComponent<Image>();
        UIHelper.ApplyImage(img, _theme?.buttonNav, normal);
        Button btn       = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.colors = UIHelper.BtnColors(_theme?.buttonNav, normal, hover, normal * 0.65f);
        btn.onClick.AddListener(() => onClick());

        var lbl          = MakeLabel(go.transform, "Lbl", label, 20f);
        lbl.color        = Color.white;
        lbl.alignment    = TextAlignmentOptions.Center;
        Stretch(lbl.gameObject, Vector2.zero, Vector2.one, new Vector2(0f, 0f), Vector2.zero);
    }

    private GameObject MakeLargeButton(Transform parent, string label, Color color, Action onClick)
    {
        GameObject go    = new GameObject($"Btn_{label}");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        Image img        = go.AddComponent<Image>();
        UIHelper.ApplyImage(img, _theme?.buttonPrimary, color);
        Button btn       = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.colors = UIHelper.BtnColors(_theme?.buttonPrimary, color, color * 1.28f, color * 0.65f);
        btn.onClick.AddListener(() => onClick());

        var lbl          = new GameObject("Lbl");
        lbl.transform.SetParent(go.transform, false);
        Stretch(lbl, Vector2.zero, Vector2.one);
        var tmp          = lbl.AddComponent<TextMeshProUGUI>();
        tmp.text         = label;
        tmp.fontSize     = 32f;
        tmp.fontStyle    = FontStyles.Bold;
        tmp.alignment    = TextAlignmentOptions.Center;
        tmp.color        = Color.white;
        return go;
    }

    private void MakeFilterButton(Transform parent, string label, Color color, Action onClick)
    {
        GameObject go    = new GameObject($"Filter_{label}");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(110f, 0f);
        Image img        = go.AddComponent<Image>();
        UIHelper.ApplyImage(img, _theme?.buttonNav, color);
        Button btn       = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.colors = UIHelper.BtnColors(_theme?.buttonNav, color, color * 1.3f, color * 0.65f);
        btn.onClick.AddListener(() => onClick());

        var lbl          = new GameObject("Lbl");
        lbl.transform.SetParent(go.transform, false);
        Stretch(lbl, Vector2.zero, Vector2.one);
        var tmp          = lbl.AddComponent<TextMeshProUGUI>();
        tmp.text         = label;
        tmp.fontSize     = 17f;
        tmp.alignment    = TextAlignmentOptions.Center;
        tmp.color        = Color.white;
    }

    private TextMeshProUGUI MakeLabel(Transform parent, string name, string text, float size)
    {
        GameObject go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var tmp        = go.AddComponent<TextMeshProUGUI>();
        tmp.text       = text;
        tmp.fontSize   = size;
        tmp.alignment  = TextAlignmentOptions.Center;
        tmp.color      = Color.white;
        tmp.enableWordWrapping = true;
        if (_font != null) tmp.font = _font;
        return tmp;
    }

    private static GameObject MakeFullPanel(Transform parent, string name)
    {
        GameObject go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        Stretch(go, Vector2.zero, Vector2.one);
        go.AddComponent<Image>().color = PanelColor;
        go.SetActive(false);
        return go;
    }

    private static GameObject MakeBG(Transform parent, string name, Color color)
    {
        GameObject go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        go.AddComponent<Image>().color = color;
        return go;
    }

    private static void Stretch(GameObject go, Vector2 anchorMin, Vector2 anchorMax,
                                  Vector2 offsetMin = default, Vector2 offsetMax = default)
    {
        RectTransform rt = go.GetComponent<RectTransform>();
        if (rt == null) rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private static void SetSizeDelta(TextMeshProUGUI tmp, float w, float h)
        => SetSizeDelta(tmp.gameObject, w, h);

    private static void SetSizeDelta(GameObject go, float w, float h)
    {
        RectTransform rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
    }

    private static void EnsureEventSystem()
    {
        var existing = FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
        UnityEngine.InputSystem.UI.InputSystemUIInputModule module;

        if (existing != null)
        {
            // Remove any legacy module
            var old = existing.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            if (old != null) UnityEngine.Object.Destroy(old);

            module = existing.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            if (module == null)
                module = existing.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }
        else
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            module = es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // Always reassign defaults — the scene YAML reference is broken
        module.AssignDefaultActions();
    }
}
