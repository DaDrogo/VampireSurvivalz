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
    [Header("Content — assign ScriptableObject arrays in Inspector")]
    [SerializeField] private CharacterDefinition[] characters;
    [SerializeField] private LevelDefinition[]     levels;
    [SerializeField] private LexikonEntry[]        lexikonEntries;
    [SerializeField] private MilestoneDefinition[] milestones;

    // ── Panels ────────────────────────────────────────────────────────────────
    private GameObject _homePanel;
    private GameObject _characterPanel;
    private GameObject _levelPanel;
    private GameObject _lexikonPanel;
    private GameObject _milestonesPanel;
    private GameObject _settingsPanel;
    private GameObject _activePanel;

    // ── Lexikon live refs ─────────────────────────────────────────────────────
    private TextMeshProUGUI _lexikonCurrencyLabel;
    private TextMeshProUGUI _homeCurrencyLabel;

    // ── Selection state ───────────────────────────────────────────────────────
    private int _selectedCharIndex;

    // ── Colours ───────────────────────────────────────────────────────────────
    private static readonly Color BgColor        = new Color(0.06f, 0.06f, 0.08f);
    private static readonly Color SidebarColor   = new Color(0.08f, 0.08f, 0.10f);
    private static readonly Color PanelColor     = new Color(0.10f, 0.10f, 0.13f);
    private static readonly Color NavNormal      = new Color(0.12f, 0.12f, 0.15f);
    private static readonly Color NavHover       = new Color(0.18f, 0.22f, 0.30f);
    private static readonly Color CardNormal     = new Color(0.12f, 0.12f, 0.15f);
    private static readonly Color CardSelected   = new Color(0.18f, 0.35f, 0.65f);
    private static readonly Color AccentRed      = new Color(0.85f, 0.14f, 0.14f);

    private TMP_FontAsset _font;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        _selectedCharIndex = PersistentDataManager.Instance?.SelectedCharacterIndex ?? 0;
    }

    private void Start()
    {
        _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (lexikonEntries == null || lexikonEntries.Length == 0)
            lexikonEntries = CreateDefaultLexikonEntries();
        EnsureEventSystem();
        BuildUI();
        ShowPanel(_homePanel);
    }

    private static LexikonEntry[] CreateDefaultLexikonEntries()
    {
        LexikonEntry E(string name, LexikonCategory cat, Color col,
                       string desc, string stats, bool def = true, int cost = 0)
        {
            var e = ScriptableObject.CreateInstance<LexikonEntry>();
            e.entryName = name; e.category = cat; e.color = col;
            e.description = desc; e.stats = stats;
            e.isUnlockedByDefault = def; e.unlockCost = cost;
            return e;
        }
        var T = LexikonCategory.Turret;
        var B = LexikonCategory.Building;
        return new[]
        {
            E("Turret Arrow",    T, new Color(0.76f,0.87f,0.55f), "Basic ranged turret. Fires arrows at the nearest enemy.",                         "HP: 100  DMG: 10  Range: 10  Rate: 1/s"),
            E("Turret Cannon",   T, new Color(0.85f,0.42f,0.18f), "Heavy turret. Slower but deals high damage per shot.",                             "HP: 150  DMG: 40  Range: 8  Rate: 0.4/s", false, 100),
            E("Turret Chain",    T, new Color(0.40f,0.72f,0.95f), "Chains lightning between nearby enemies after the first hit.",                     "HP: 120  DMG: 15  Range: 9  Rate: 0.8/s", false, 80),
            E("Turret Frost",    T, new Color(0.55f,0.80f,1.00f), "Slows enemies in range. Great for crowd control.",                                 "HP: 100  DMG: 8   Range: 7  Rate: 1.2/s", false, 80),
            E("Turret Stun",     T, new Color(0.95f,0.90f,0.30f), "Occasionally stuns enemies, leaving them unable to move.",                         "HP: 110  DMG: 12  Range: 8  Rate: 0.9/s", false, 60),
            E("Turret Poison",   T, new Color(0.42f,0.80f,0.32f), "Applies a damage-over-time poison effect on hit.",                                 "HP: 90   DMG: 6+DoT  Range: 9  Rate: 1/s", false, 70),
            E("Turret Fire",     T, new Color(1.00f,0.45f,0.10f), "Launches fireballs that deal splash damage on impact.",                            "HP: 130  DMG: 30  Range: 7  Rate: 0.5/s  AoE: 2m", false, 90),
            E("Barricade",       B, new Color(0.60f,0.42f,0.22f), "A sturdy wall that blocks enemy movement. Can be damaged.",                        "HP: 100  No output"),
            E("Turret (Base)",   B, new Color(0.52f,0.60f,0.70f), "Foundation platform for placing any turret.",                                      "HP: 80   No output"),
            E("Resource Producer",B,new Color(0.44f,0.72f,0.44f), "Generates Wood or Metal over time. Core economy building.",                        "HP: 150  Output: 3 Wood/10s"),
            E("Wood Mill",       B, new Color(0.72f,0.54f,0.28f), "Upgrade of the Resource Producer. Produces Wood at an increased rate.",             "HP: 175  Output: 5 Wood/10s"),
            E("Metal Smelter",   B, new Color(0.70f,0.70f,0.82f), "Upgrade of the Resource Producer. Smelts ore into metal.",                         "HP: 200  Output: 5 Metal/10s"),
            E("Trade Post",      B, new Color(0.92f,0.76f,0.10f), "Premium upgrade. Produces both Wood and Metal. Expensive but versatile.",           "HP: 175  Output: 4W+4M/12s", false, 50),
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
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // ── Background ────────────────────────────────────────────────────────
        Stretch(MakeBG(canvasGO.transform, "BG", BgColor), Vector2.zero, Vector2.one);

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
        AddNavButton(sidebar.transform, "Characters", () => ShowPanel(_characterPanel));
        AddNavButton(sidebar.transform, "Levels",     () => ShowPanel(_levelPanel));
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
        _characterPanel  = BuildCharacterPanel(content.transform);
        _levelPanel      = BuildLevelPanel(content.transform);
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

        VerticalLayoutGroup vl = panel.AddComponent<VerticalLayoutGroup>();
        vl.padding             = new RectOffset(0, 0, 0, 0);
        vl.childControlHeight  = true;
        vl.childControlWidth   = true;
        vl.childForceExpandHeight = true;
        vl.childForceExpandWidth  = true;

        // Centre column
        GameObject col   = new GameObject("Col");
        col.transform.SetParent(panel.transform, false);
        col.AddComponent<RectTransform>();
        VerticalLayoutGroup colVL  = col.AddComponent<VerticalLayoutGroup>();
        colVL.padding              = new RectOffset(0, 0, 120, 80);
        colVL.spacing              = 28f;
        colVL.childControlHeight   = false;
        colVL.childControlWidth    = false;
        colVL.childForceExpandWidth  = false;
        colVL.childForceExpandHeight = false;
        colVL.childAlignment       = TextAnchor.UpperCenter;

        var title       = MakeLabel(col.transform, "Title", "VAMPIRE SURVIVALZ", 68f);
        title.color     = AccentRed;
        title.fontStyle = FontStyles.Bold;
        SetSizeDelta(title, 860f, 84f);

        int bestWave    = PersistentDataManager.Instance?.BestWave ?? 0;
        var best        = MakeLabel(col.transform, "Best", $"Best Wave:  {bestWave}", 30f);
        best.color      = new Color(0.62f, 0.62f, 0.66f);
        SetSizeDelta(best, 400f, 44f);

        int coins       = PersistentDataManager.Instance?.TotalCurrency ?? 0;
        _homeCurrencyLabel       = MakeLabel(col.transform, "Coins", $"Coins:  {coins}", 26f);
        _homeCurrencyLabel.color = new Color(1f, 0.85f, 0.2f);
        SetSizeDelta(_homeCurrencyLabel, 400f, 38f);

        var playBtn     = MakeLargeButton(col.transform, "PLAY",
                          new Color(0.14f, 0.52f, 0.14f), OnPlayClicked);
        SetSizeDelta(playBtn, 280f, 70f);

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
    //  Panel: Characters
    // ═════════════════════════════════════════════════════════════════════════

    private GameObject BuildCharacterPanel(Transform parent)
    {
        GameObject panel = MakeTitledScrollPanel(parent, "CharacterPanel",
                           "SELECT CHARACTER", out GameObject content, horizontal: true);

        if (characters == null || characters.Length == 0)
        {
            AddEmptyState(content.transform, "No characters defined.\nCreate CharacterDefinition assets and assign them here.");
            return panel;
        }

        HorizontalLayoutGroup hl = content.AddComponent<HorizontalLayoutGroup>();
        hl.spacing               = 20f;
        hl.padding               = new RectOffset(20, 20, 20, 20);
        hl.childControlHeight    = false;
        hl.childControlWidth     = false;
        content.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        for (int i = 0; i < characters.Length; i++)
        {
            if (characters[i] == null) continue;
            int captured = i;
            BuildCharacterCard(content.transform, characters[i], i == _selectedCharIndex,
                               () => SelectCharacter(captured));
        }
        return panel;
    }

    private void BuildCharacterCard(Transform parent, CharacterDefinition def,
                                     bool selected, Action onClick)
    {
        GameObject card  = new GameObject($"Card_{def.characterName}");
        card.transform.SetParent(parent, false);
        SetSizeDelta(card, 200f, 290f);
        Image img        = card.AddComponent<Image>();
        img.color        = selected ? CardSelected : CardNormal;

        Button btn       = card.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());

        VerticalLayoutGroup vl = card.AddComponent<VerticalLayoutGroup>();
        vl.padding             = new RectOffset(12, 12, 14, 14);
        vl.spacing             = 8f;
        vl.childControlWidth   = true;
        vl.childForceExpandWidth = true;

        // Colour swatch
        GameObject sw    = new GameObject("Swatch");
        sw.transform.SetParent(card.transform, false);
        sw.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 70f);
        sw.AddComponent<Image>().color = def.color;

        var name         = MakeLabel(card.transform, "Name", def.characterName, 20f);
        name.color       = Color.white;
        name.fontStyle   = FontStyles.Bold;
        name.alignment   = TextAlignmentOptions.Center;
        SetSizeDelta(name, 0f, 28f);

        var desc         = MakeLabel(card.transform, "Desc", def.description, 15f);
        desc.color       = new Color(0.66f, 0.66f, 0.66f);
        desc.enableWordWrapping = true;
        SetSizeDelta(desc, 0f, 72f);

        var stats        = MakeLabel(card.transform, "Stats",
            $"HP ×{def.healthMultiplier:F1}   SPD ×{def.speedMultiplier:F1}", 14f);
        stats.color      = new Color(0.55f, 0.88f, 0.55f);
        SetSizeDelta(stats, 0f, 22f);
    }

    private void SelectCharacter(int index)
    {
        _selectedCharIndex = index;
        PersistentDataManager.Instance?.SelectCharacter(index);

        // Update card colours in-place — no rebuild needed
        var content = _characterPanel.transform.Find("ScrollView/Viewport/Content");
        if (content == null) return;
        int cardIndex = 0;
        for (int i = 0; i < content.childCount; i++)
        {
            Image img = content.GetChild(i).GetComponent<Image>();
            if (img == null) continue;
            img.color = cardIndex == index ? CardSelected : CardNormal;
            cardIndex++;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Panel: Levels
    // ═════════════════════════════════════════════════════════════════════════

    private GameObject BuildLevelPanel(Transform parent)
    {
        GameObject panel = MakeTitledScrollPanel(parent, "LevelPanel",
                           "SELECT LEVEL", out GameObject content, horizontal: true);

        if (levels == null || levels.Length == 0)
        {
            AddEmptyState(content.transform, "No levels defined.\nCreate LevelDefinition assets and assign them here.");
            return panel;
        }

        HorizontalLayoutGroup hl = content.AddComponent<HorizontalLayoutGroup>();
        hl.spacing               = 20f;
        hl.padding               = new RectOffset(20, 20, 20, 20);
        hl.childControlHeight    = false;
        hl.childControlWidth     = false;
        content.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        int bestWave = PersistentDataManager.Instance?.BestWave ?? 0;

        for (int i = 0; i < levels.Length; i++)
        {
            if (levels[i] == null) continue;
            LevelDefinition lvl  = levels[i];
            bool unlocked        = lvl.isUnlockedByDefault || bestWave >= lvl.unlockAtBestWave;
            int  captured        = i;
            BuildLevelCard(content.transform, lvl, unlocked,
                () => { if (unlocked) SceneTransitionManager.Instance?.LoadScene(lvl.sceneName); });
        }
        return panel;
    }

    private void BuildLevelCard(Transform parent, LevelDefinition lvl,
                                 bool unlocked, Action onClick)
    {
        GameObject card  = new GameObject($"LvlCard_{lvl.levelName}");
        card.transform.SetParent(parent, false);
        SetSizeDelta(card, 220f, 300f);
        Image img        = card.AddComponent<Image>();
        img.color        = unlocked ? CardNormal : new Color(0.08f, 0.08f, 0.09f);

        Button btn       = card.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.interactable  = unlocked;
        if (unlocked) btn.onClick.AddListener(() => onClick());

        VerticalLayoutGroup vl = card.AddComponent<VerticalLayoutGroup>();
        vl.padding             = new RectOffset(12, 12, 14, 14);
        vl.spacing             = 8f;
        vl.childControlWidth   = true;
        vl.childForceExpandWidth = true;

        // Preview swatch
        GameObject sw    = new GameObject("Swatch");
        sw.transform.SetParent(card.transform, false);
        sw.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 100f);
        sw.AddComponent<Image>().color = unlocked ? lvl.previewColor : new Color(0.14f, 0.14f, 0.14f);

        if (!unlocked)
        {
            var lockLbl  = MakeLabel(sw.transform, "Lock", "LOCKED", 22f);
            lockLbl.color = new Color(0.5f, 0.5f, 0.5f);
            Stretch(lockLbl.gameObject, Vector2.zero, Vector2.one);
        }

        var name         = MakeLabel(card.transform, "Name", lvl.levelName, 22f);
        name.color       = unlocked ? Color.white : new Color(0.38f, 0.38f, 0.38f);
        name.fontStyle   = FontStyles.Bold;
        SetSizeDelta(name, 0f, 30f);

        var desc         = MakeLabel(card.transform, "Desc", lvl.description, 15f);
        desc.color       = new Color(0.58f, 0.58f, 0.58f);
        desc.enableWordWrapping = true;
        SetSizeDelta(desc, 0f, 80f);

        if (!unlocked)
        {
            var req      = MakeLabel(card.transform, "Req", $"Reach wave {lvl.unlockAtBestWave}", 14f);
            req.color    = new Color(0.82f, 0.66f, 0.18f);
            SetSizeDelta(req, 0f, 24f);
        }
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
            MakeFilterButton(filterRow.transform, "All",       new Color(0.22f, 0.22f, 0.26f), () => {});
            MakeFilterButton(filterRow.transform, "Turrets",   new Color(0.28f, 0.14f, 0.44f), () => {});
            MakeFilterButton(filterRow.transform, "Buildings", new Color(0.14f, 0.32f, 0.18f), () => {});
            MakeFilterButton(filterRow.transform, "Enemies",   new Color(0.48f, 0.12f, 0.12f), () => {});
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

        MakeFilterButton(filterRow.transform, "All",       new Color(0.22f, 0.22f, 0.26f), () => SetFilter(null));
        MakeFilterButton(filterRow.transform, "Turrets",   new Color(0.28f, 0.14f, 0.44f), () => SetFilter(LexikonCategory.Turret));
        MakeFilterButton(filterRow.transform, "Buildings", new Color(0.14f, 0.32f, 0.18f), () => SetFilter(LexikonCategory.Building));
        MakeFilterButton(filterRow.transform, "Enemies",   new Color(0.48f, 0.12f, 0.12f), () => SetFilter(LexikonCategory.Enemy));

        return panel;
    }

    private GameObject BuildLexikonRow(Transform parent, LexikonEntry entry)
    {
        var pdm          = PersistentDataManager.Instance;
        bool unlocked    = pdm?.IsUnlocked(entry) ?? entry.isUnlockedByDefault;

        GameObject row   = new GameObject($"Row_{entry.entryName}");
        row.transform.SetParent(parent, false);
        row.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 78f);
        row.AddComponent<Image>().color = unlocked
            ? new Color(0.12f, 0.12f, 0.15f)
            : new Color(0.08f, 0.08f, 0.10f);

        LexikonCategoryTag tag = row.AddComponent<LexikonCategoryTag>();
        tag.Category           = entry.category;

        HorizontalLayoutGroup hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.padding               = new RectOffset(0, 14, 8, 8);
        hl.spacing               = 14f;
        hl.childControlHeight    = true;

        // Colour badge
        GameObject badge    = new GameObject("Badge");
        badge.transform.SetParent(row.transform, false);
        badge.AddComponent<RectTransform>().sizeDelta = new Vector2(8f, 0f);
        badge.AddComponent<Image>().color = unlocked ? entry.color : new Color(0.25f, 0.25f, 0.25f);
        badge.AddComponent<LayoutElement>().preferredWidth = 8f;

        // Text block
        GameObject txt      = new GameObject("Text");
        txt.transform.SetParent(row.transform, false);
        txt.AddComponent<RectTransform>();
        txt.AddComponent<LayoutElement>().flexibleWidth = 1f;
        VerticalLayoutGroup tvl  = txt.AddComponent<VerticalLayoutGroup>();
        tvl.childControlHeight   = false;
        tvl.childControlWidth    = true;
        tvl.childForceExpandWidth = true;
        tvl.padding              = new RectOffset(0, 0, 4, 4);

        var entryName    = MakeLabel(txt.transform, "Name",
            unlocked ? entry.entryName : $"??? ({entry.category})", 20f);
        entryName.color  = unlocked ? Color.white : new Color(0.40f, 0.40f, 0.40f);
        entryName.fontStyle = FontStyles.Bold;
        entryName.alignment = TextAlignmentOptions.Left;
        SetSizeDelta(entryName, 0f, 26f);

        var desc         = MakeLabel(txt.transform, "Desc",
            unlocked ? entry.description : "Unlock to reveal this entry.", 15f);
        desc.color       = new Color(0.60f, 0.60f, 0.60f);
        desc.alignment   = TextAlignmentOptions.Left;
        SetSizeDelta(desc, 0f, 22f);

        if (unlocked && !string.IsNullOrEmpty(entry.stats))
        {
            var stats    = MakeLabel(txt.transform, "Stats", entry.stats, 14f);
            stats.color  = new Color(0.50f, 0.86f, 0.50f);
            stats.alignment = TextAlignmentOptions.Left;
            SetSizeDelta(stats, 0f, 18f);
        }

        // Right column: unlock button / unlocked badge / category label
        if (!entry.isUnlockedByDefault && !unlocked)
        {
            // Unlock button
            GameObject btnGO    = new GameObject("UnlockBtn");
            btnGO.transform.SetParent(row.transform, false);
            btnGO.AddComponent<LayoutElement>().preferredWidth = 130f;
            Image btnImg    = btnGO.AddComponent<Image>();
            btnImg.color    = new Color(0.62f, 0.50f, 0.06f);
            Button btn      = btnGO.AddComponent<Button>();
            btn.targetGraphic = btnImg;

            var btnLbl  = MakeLabel(btnGO.transform, "BtnLbl",
                $"Unlock\n{entry.unlockCost} coins", 14f);
            btnLbl.alignment = TextAlignmentOptions.Center;
            btnLbl.color     = Color.white;
            Stretch(btnLbl.gameObject, Vector2.zero, Vector2.one);

            // Pre-create unlocked badge (hidden)
            GameObject badge2   = new GameObject("UnlockedBadge");
            badge2.transform.SetParent(row.transform, false);
            badge2.AddComponent<RectTransform>();
            badge2.AddComponent<LayoutElement>().preferredWidth = 130f;
            var badge2Lbl       = MakeLabel(badge2.transform, "Lbl", "✓ UNLOCKED", 16f);
            badge2Lbl.color     = new Color(0.20f, 0.88f, 0.20f);
            badge2Lbl.fontStyle = FontStyles.Bold;
            badge2Lbl.alignment = TextAlignmentOptions.Center;
            Stretch(badge2Lbl.gameObject, Vector2.zero, Vector2.one);
            badge2.SetActive(false);

            btn.onClick.AddListener(() => OnUnlockClicked(entry, row, btnGO, badge2, badge));
        }
        else if (!entry.isUnlockedByDefault)
        {
            // Was locked, now unlocked — show green badge
            var unlockBadge     = MakeLabel(row.transform, "UnlockedBadge", "✓ UNLOCKED", 16f);
            unlockBadge.color   = new Color(0.20f, 0.88f, 0.20f);
            unlockBadge.fontStyle = FontStyles.Bold;
            unlockBadge.alignment = TextAlignmentOptions.Center;
            unlockBadge.gameObject.AddComponent<LayoutElement>().preferredWidth = 130f;
        }
        else
        {
            // Always-unlocked: show category label
            var catLbl       = MakeLabel(row.transform, "Cat", entry.category.ToString(), 14f);
            catLbl.color     = new Color(0.45f, 0.45f, 0.50f);
            catLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 88f;
        }

        return row;
    }

    private void OnUnlockClicked(LexikonEntry entry, GameObject row,
                                  GameObject unlockBtn, GameObject unlockedBadge,
                                  GameObject colorBadge)
    {
        if (PersistentDataManager.Instance == null) return;

        bool success = PersistentDataManager.Instance.UnlockItem(entry);
        if (!success) return; // insufficient coins — button stays visible

        // Update row visuals in-place
        unlockBtn.SetActive(false);
        unlockedBadge.SetActive(true);

        // Restore colour badge
        colorBadge.GetComponent<Image>().color = entry.color;

        // Update name & description
        var nameLabel = row.transform.Find("Text/Name")?.GetComponent<TextMeshProUGUI>();
        if (nameLabel != null) { nameLabel.text = entry.entryName; nameLabel.color = Color.white; }

        var descLabel = row.transform.Find("Text/Desc")?.GetComponent<TextMeshProUGUI>();
        if (descLabel != null) descLabel.text = entry.description;

        row.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.15f);

        // Refresh coin counters
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

        BuildSlider(sliderGO.transform, initial, v =>
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
        btnImg.color     = isFS ? new Color(0.14f, 0.52f, 0.14f) : new Color(0.36f, 0.14f, 0.14f);
        Button btn       = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImg;

        var btnLbl       = MakeLabel(btnGO.transform, "Lbl", isFS ? "ON" : "OFF", 20f);
        btnLbl.fontStyle = FontStyles.Bold;
        Stretch(btnLbl.gameObject, Vector2.zero, Vector2.one);

        btn.onClick.AddListener(() =>
        {
            bool next        = !Screen.fullScreen;
            Screen.fullScreen = next;
            btnImg.color     = next ? new Color(0.14f, 0.52f, 0.14f) : new Color(0.36f, 0.14f, 0.14f);
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
        btnImg.color     = dangerCol;
        Button btn       = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        ColorBlock cb    = btn.colors;
        cb.normalColor      = dangerCol;
        cb.highlightedColor = new Color(0.64f, 0.14f, 0.14f);
        cb.pressedColor     = new Color(0.28f, 0.06f, 0.06f);
        btn.colors          = cb;

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
                cb                 = btn.colors;
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

    private static Slider BuildSlider(Transform parent, float initial, UnityEngine.Events.UnityAction<float> onChange)
    {
        // Background
        GameObject bg    = new GameObject("BG");
        bg.transform.SetParent(parent, false);
        RectTransform bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin   = new Vector2(0f, 0.3f);
        bgRT.anchorMax   = new Vector2(1f, 0.7f);
        bgRT.offsetMin   = Vector2.zero;
        bgRT.offsetMax   = Vector2.zero;
        bg.AddComponent<Image>().color = new Color(0.18f, 0.18f, 0.18f);

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
        fill.AddComponent<Image>().color = new Color(0.2f, 0.6f, 1f);

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
        handleImg.color    = Color.white;

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
        img.color        = normal;
        Button btn       = go.AddComponent<Button>();
        btn.targetGraphic = img;
        ColorBlock cb    = btn.colors;
        cb.normalColor      = normal;
        cb.highlightedColor = hover;
        cb.pressedColor     = normal * 0.65f;
        btn.colors          = cb;
        btn.onClick.AddListener(() => onClick());

        var lbl          = MakeLabel(go.transform, "Lbl", label, 20f);
        lbl.color        = Color.white;
        lbl.alignment    = TextAlignmentOptions.Left;
        Stretch(lbl.gameObject, Vector2.zero, Vector2.one, new Vector2(24f, 0f), Vector2.zero);
    }

    private GameObject MakeLargeButton(Transform parent, string label, Color color, Action onClick)
    {
        GameObject go    = new GameObject($"Btn_{label}");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        Image img        = go.AddComponent<Image>();
        img.color        = color;
        Button btn       = go.AddComponent<Button>();
        btn.targetGraphic = img;
        ColorBlock cb    = btn.colors;
        cb.normalColor      = color;
        cb.highlightedColor = color * 1.28f;
        cb.pressedColor     = color * 0.65f;
        btn.colors          = cb;
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
        img.color        = color;
        Button btn       = go.AddComponent<Button>();
        btn.targetGraphic = img;
        ColorBlock cb    = btn.colors;
        cb.normalColor      = color;
        cb.highlightedColor = color * 1.3f;
        cb.pressedColor     = color * 0.65f;
        btn.colors          = cb;
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
        RectTransform rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
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
