using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DefaultExecutionOrder(1)]
public class SetupManager : MonoBehaviour
{
    public static SetupManager Instance { get; private set; }

    [Header("UI Theme")]
    [SerializeField] private UITheme _theme;

    [Header("Step 1 — Character")]
    [SerializeField] private CharacterDefinition[] characters;

    [Header("Step 2 — Loadout")]
    [SerializeField] private BuildingCard[] buildingCards;
    [SerializeField] private int maxLoadoutSize = 5;

    [Header("Step 3 — Level")]
    [SerializeField] private LevelDefinition[] levels;

    // ── State ─────────────────────────────────────────────────────────────────

    private int                _step;
    private GameObject[]       _stepPanels;
    private Image[]            _stepChipBgs;    // number chips in step bar
    private TextMeshProUGUI[]  _stepNameLabels; // step name labels

    private Button             _backBtn;
    private TextMeshProUGUI    _backLabel;
    private Button             _nextBtn;
    private TextMeshProUGUI    _nextLabel;

    // Character
    private int                _charIndex;
    private CharacterDefinition _selectedChar;
    private Image[]            _charCardBgs;
    private Image[]            _charPortraitBgs;
    private RectTransform[]    _charCardRTs;
    private Image[]            _charTopBars;
    private GameObject[]       _charBadges;
    private Coroutine[]        _charScaleCoroutines;
    private Coroutine          _passivePillsCoroutine;
    private TextMeshProUGUI    _detailName;
    private Image              _detailColorBar;
    private TextMeshProUGUI    _detailDescription;
    private Transform          _statsContainer;
    private Transform          _passivesContainer;

    // Loadout
    private List<int>          _loadout = new List<int>();
    private Image[]            _loadoutCardBgs;
    private Image[]            _loadoutAccentBars;
    private TextMeshProUGUI[]  _loadoutSelLabels;
    private TextMeshProUGUI    _loadoutCounter;
    private Transform          _loadoutListContent;

    // Level
    private int                _levelIndex;
    private Image[]            _levelCardBgs;

    // ── Colour palette ────────────────────────────────────────────────────────

    static readonly Color C_BG        = new Color(0.05f, 0.05f, 0.07f);
    static readonly Color C_Surface   = new Color(0.08f, 0.08f, 0.11f);
    static readonly Color C_Panel     = new Color(0.10f, 0.10f, 0.14f);
    static readonly Color C_Div       = new Color(0.17f, 0.17f, 0.23f);

    // Cards
    static readonly Color C_CardNorm  = new Color(0.10f, 0.10f, 0.15f);
    static readonly Color C_CardSel   = new Color(0.09f, 0.19f, 0.40f);
    static readonly Color C_CardLock  = new Color(0.06f, 0.06f, 0.08f);
    static readonly Color C_CardCit   = new Color(0.13f, 0.10f, 0.02f);
    static readonly Color C_CardBasic = new Color(0.05f, 0.12f, 0.05f);

    // These kept for SelectCharacter/SelectLevel/ToggleLoadout usage
    static Color CardNormal   => C_CardNorm;
    static Color CardSelected => C_CardSel;
    static Color CardLocked   => C_CardLock;

    // Accents
    static readonly Color C_Blue      = new Color(0.26f, 0.60f, 1.00f);
    static readonly Color C_Gold      = new Color(1.00f, 0.82f, 0.18f);
    static readonly Color C_Green     = new Color(0.24f, 0.88f, 0.42f);
    static readonly Color C_Red       = new Color(0.85f, 0.18f, 0.18f);
    static readonly Color C_Cyan      = new Color(0.34f, 0.78f, 1.00f);

    // Text
    static readonly Color C_TxtHi     = new Color(0.92f, 0.92f, 0.96f);
    static readonly Color C_TxtMid    = new Color(0.52f, 0.52f, 0.62f);
    static readonly Color C_TxtDim    = new Color(0.30f, 0.30f, 0.38f);

    // Step bar
    static readonly Color C_StepAct   = new Color(0.26f, 0.60f, 1.00f);
    static readonly Color C_StepDone  = new Color(0.16f, 0.36f, 0.62f);
    static readonly Color C_StepFut   = new Color(0.14f, 0.14f, 0.20f);

    // Legacy aliases used by row-builder helpers
    static Color AccentRed => C_Red;

    private TMP_FontAsset _font;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        _font = _theme?.font != null
            ? _theme.font
            : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        var pdm     = PersistentDataManager.Instance;
        _charIndex  = Mathf.Clamp(pdm?.SelectedCharacterIndex ?? 0, 0,
                       Mathf.Max(0, (characters?.Length  ?? 1) - 1));
        _levelIndex = Mathf.Clamp(pdm?.SelectedLevelIndex     ?? 0, 0,
                       Mathf.Max(0, (levels?.Length      ?? 1) - 1));

        _loadout.Clear();
        if (pdm?.SelectedBuildingIndices != null)
        {
            foreach (int i in pdm.SelectedBuildingIndices)
            {
                bool isFixed = buildingCards != null && i < buildingCards.Length
                               && buildingCards[i] != null
                               && (buildingCards[i].isBasic || buildingCards[i].isCitadel);
                if (!isFixed) _loadout.Add(i);
            }
        }

        EnsureEventSystem();
        BuildUI();
        GoToStep(0);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Root UI
    // ═════════════════════════════════════════════════════════════════════════

    private void BuildUI()
    {
        var cvs                       = new GameObject("SetupCanvas");
        var canvas                    = cvs.AddComponent<Canvas>();
        canvas.renderMode             = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder           = 0;
        var scaler                    = cvs.AddComponent<CanvasScaler>();
        scaler.uiScaleMode            = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution    = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight     = 1f;
        cvs.AddComponent<GraphicRaycaster>();

        var root = StretchGO(cvs.transform, "Root");
        root.AddComponent<Image>().color = C_BG;
        var rootVLG = root.AddComponent<VerticalLayoutGroup>();
        rootVLG.childControlHeight     = true;
        rootVLG.childControlWidth      = true;
        rootVLG.childForceExpandHeight = true;
        rootVLG.childForceExpandWidth  = true;

        BuildStepBar(root.transform);

        var content = GroupGO(root.transform, "Content");
        content.AddComponent<LayoutElement>().flexibleHeight = 1f;
        content.AddComponent<LayoutElement>().preferredHeight = 900f;
        _stepPanels    = new GameObject[3];
        _stepPanels[0] = BuildCharacterStep(content.transform);
        _stepPanels[1] = BuildLoadoutStep(content.transform);
        _stepPanels[2] = BuildLevelStep(content.transform);
        foreach (var p in _stepPanels) StretchRT(p.GetComponent<RectTransform>());

        BuildNavBar(root.transform);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Step bar
    // ═════════════════════════════════════════════════════════════════════════

    private void BuildStepBar(Transform parent)
    {
        var bar    = GroupGO(parent, "StepBar");
        var barImg = bar.AddComponent<Image>();
        UIHelper.ApplyImage(barImg, _theme?.stepBarBackground, C_Surface, Image.Type.Tiled);
        bar.AddComponent<LayoutElement>().preferredHeight = 80f;

        // Thin accent line at the very bottom of the step bar
        // (we'll just use the bar background + a bottom border trick via child)
        var hlg             = bar.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment  = TextAnchor.MiddleCenter;
        hlg.childControlHeight     = true;
        hlg.childControlWidth      = true;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = true;

        string[] names    = { "CHARACTER", "LOADOUT", "LEVEL" };
        _stepChipBgs      = new Image[3];
        _stepNameLabels   = new TextMeshProUGUI[3];

        for (int i = 0; i < 3; i++)
        {
            // Divider between steps
            if (i > 0)
            {
                var dv    = GroupGO(hlg.transform, "StepDiv");
                var dvImg = dv.AddComponent<Image>();
                UIHelper.ApplyImage(dvImg, _theme?.stepDivider, C_Div, Image.Type.Tiled);
                dvImg.color = C_Div;
                var dvLE = dv.AddComponent<LayoutElement>();
                dvLE.preferredWidth  = 1f;
                dvLE.flexibleWidth   = 0f;
            }

            var step    = GroupGO(hlg.transform, $"Step{i}");
            var stepHLG = step.AddComponent<HorizontalLayoutGroup>();
            stepHLG.childAlignment         = TextAnchor.MiddleCenter;
            stepHLG.spacing                = 12f;
            stepHLG.childControlHeight     = true;
            stepHLG.childControlWidth      = false;
            stepHLG.childForceExpandHeight = true;
            stepHLG.childForceExpandWidth  = false;

            // Number chip  ─────────────────────────────────
            var chip   = GroupGO(step.transform, "Chip");
            chip.AddComponent<LayoutElement>().preferredWidth = 30f;
            _stepChipBgs[i] = chip.AddComponent<Image>();
            UIHelper.ApplyImage(_stepChipBgs[i], _theme?.stepChip, C_StepFut, Image.Type.Tiled);
            _stepChipBgs[i].color = C_StepFut;
            var chipNum = Lbl(chip.transform, (i + 1).ToString(), 24f, Color.white);
            chipNum.fontStyle = FontStyles.Bold;
            chipNum.alignment = TextAlignmentOptions.Center;
            StretchRT(chipNum.GetComponent<RectTransform>());

            // Step name  ───────────────────────────────────
            _stepNameLabels[i] = Lbl(step.transform, names[i], 24f, Color.white);
            _stepNameLabels[i].fontStyle = FontStyles.Bold;
            _stepNameLabels[i].alignment = TextAlignmentOptions.Left;
            _stepNameLabels[i].gameObject.AddComponent<LayoutElement>().preferredWidth = 106f;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Step 0 — Character
    // ═════════════════════════════════════════════════════════════════════════

    private GameObject BuildCharacterStep(Transform parent)
    {
        var panel = GroupGO(parent, "CharStep");
        panel.AddComponent<Image>().color = Color.clear;

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(24, 24, 16, 12);
        vlg.spacing                = 12f;
        vlg.childControlHeight     = true;
        vlg.childControlWidth      = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth  = true;

        // Cards row — one portrait card per character
        var cardsRow = GroupGO(panel.transform, "CardsRow");
        cardsRow.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var cardsHLG = cardsRow.AddComponent<HorizontalLayoutGroup>();
        cardsHLG.spacing                = 16f;
        cardsHLG.childAlignment         = TextAnchor.MiddleCenter;
        cardsHLG.childControlHeight     = true;
        cardsHLG.childControlWidth      = true;
        cardsHLG.childForceExpandHeight = true;
        cardsHLG.childForceExpandWidth  = true;

        int charCount        = characters?.Length ?? 0;
        _charCardBgs         = new Image[charCount];
        _charPortraitBgs     = new Image[charCount];
        _charCardRTs         = new RectTransform[charCount];
        _charTopBars         = new Image[charCount];
        _charBadges          = new GameObject[charCount];
        _charScaleCoroutines = new Coroutine[charCount];
        if (characters != null)
            for (int i = 0; i < characters.Length; i++)
                if (characters[i] != null)
                    BuildCharacterCard(cardsRow.transform, i);

        // Passives strip at the bottom
        var strip = GroupGO(panel.transform, "PassivesStrip");
        UIHelper.ApplyImage(strip.AddComponent<Image>(), _theme?.panelBackground, C_Surface, Image.Type.Tiled);
        strip.AddComponent<LayoutElement>().preferredHeight = 120f;

        var stripVLG = strip.AddComponent<VerticalLayoutGroup>();
        stripVLG.padding                = new RectOffset(20, 20, 8, 8);
        stripVLG.spacing                = 6f;
        stripVLG.childControlHeight     = true;
        stripVLG.childControlWidth      = true;
        stripVLG.childForceExpandHeight = false;
        stripVLG.childForceExpandWidth  = true;

        var passHdr = Lbl(strip.transform, "PASSIVE EFFECTS", 24f, Color.white);
        passHdr.fontStyle = FontStyles.Bold;
        passHdr.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

        var psScroll = MakeScrollView(strip.transform, vertical: true);
        var psContent = psScroll.transform.Find("Viewport/Content");
        DestroyImmediate(psContent.GetComponent<VerticalLayoutGroup>());
        DestroyImmediate(psContent.GetComponent<ContentSizeFitter>());
        var psHLG = psContent.gameObject.AddComponent<HorizontalLayoutGroup>();
        psHLG.spacing                = 12f;
        psHLG.childControlHeight     = true;
        psHLG.childControlWidth      = false;
        psHLG.childForceExpandHeight = true;
        psHLG.childForceExpandWidth  = false;
        psContent.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        var psSR = psScroll.GetComponent<ScrollRect>();
        psSR.horizontal = true;
        psSR.vertical   = false;
        psScroll.GetComponent<LayoutElement>().flexibleHeight = 1f;
        _passivesContainer = psContent;

        return panel;
    }

    private void BuildCharacterCard(Transform parent, int i)
    {
        var def = characters[i];
        var idx = i;

        var card    = GroupGO(parent, $"CharCard{i}");
        var cardImg = card.AddComponent<Image>();
        UIHelper.ApplyImage(cardImg, _theme?.cardBackground, C_CardNorm, Image.Type.Tiled);
        _charCardBgs[i] = cardImg;
        _charCardRTs[i] = card.GetComponent<RectTransform>();

        var btn = card.AddComponent<Button>();
        btn.targetGraphic = cardImg;
        SetBtn(btn, Color.white, new Color(1.05f, 1.05f, 1.05f), new Color(0.92f, 0.92f, 0.92f));
        btn.onClick.AddListener(() => SelectCharacter(idx));

        var cardVLG = card.AddComponent<VerticalLayoutGroup>();
        cardVLG.spacing                = 0f;
        cardVLG.childControlHeight     = true;
        cardVLG.childControlWidth      = true;
        cardVLG.childForceExpandHeight = false;
        cardVLG.childForceExpandWidth  = true;

        // ── Top accent bar (coloured on selection) ────────────────────────
        var topBar    = GroupGO(card.transform, "TopBar");
        var topBarImg = topBar.AddComponent<Image>();
        topBarImg.color = new Color(0.15f, 0.15f, 0.20f);
        topBar.AddComponent<LayoutElement>().preferredHeight = 5f;
        _charTopBars[i] = topBarImg;

        // ── Portrait area (flexible, takes most height) ───────────────────
        var portrait    = GroupGO(card.transform, "Portrait");
        portrait.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var portraitImg = portrait.AddComponent<Image>();
        portraitImg.color = new Color(def.color.r, def.color.g, def.color.b, 0.28f);
        _charPortraitBgs[i] = portraitImg;

        // Avatar circle — anchored, ignores layout
        var avatar   = GroupGO(portrait.transform, "Avatar");
        avatar.AddComponent<LayoutElement>().ignoreLayout = true;
        var avatarRT = avatar.GetComponent<RectTransform>();
        avatarRT.anchorMin        = new Vector2(0.5f, 0.52f);
        avatarRT.anchorMax        = new Vector2(0.5f, 0.52f);
        avatarRT.pivot            = new Vector2(0.5f, 0.5f);
        avatarRT.anchoredPosition = Vector2.zero;
        avatarRT.sizeDelta        = new Vector2(108f, 108f);
        avatar.AddComponent<Image>().color =
            new Color(def.color.r, def.color.g, def.color.b, 0.88f);

        var initGO  = GroupGO(avatar.transform, "Initial");
        StretchRT(initGO.GetComponent<RectTransform>());
        var initTxt = Lbl(initGO.transform,
            def.characterName.Length > 0 ? def.characterName.Substring(0, 1).ToUpper() : "?",
            50f, Color.white);
        initTxt.fontStyle = FontStyles.Bold;
        initTxt.alignment = TextAlignmentOptions.Center;
        StretchRT(initTxt.GetComponent<RectTransform>());

        // Name bar pinned to portrait bottom — ignores layout
        var nameBar   = GroupGO(portrait.transform, "NameBar");
        nameBar.AddComponent<LayoutElement>().ignoreLayout = true;
        var nameBarRT = nameBar.GetComponent<RectTransform>();
        nameBarRT.anchorMin        = new Vector2(0f, 0f);
        nameBarRT.anchorMax        = new Vector2(1f, 0f);
        nameBarRT.pivot            = new Vector2(0.5f, 0f);
        nameBarRT.anchoredPosition = Vector2.zero;
        nameBarRT.sizeDelta        = new Vector2(0f, 46f);
        nameBar.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.62f);
        var nameTxt = Lbl(nameBar.transform, def.characterName.ToUpper(), 24f, Color.white);
        nameTxt.fontStyle = FontStyles.Bold;
        nameTxt.alignment = TextAlignmentOptions.Center;
        StretchRT(nameTxt.GetComponent<RectTransform>());

        // ✓ Badge — top-right corner of portrait, hidden until selected
        var badge   = GroupGO(portrait.transform, "Badge");
        badge.AddComponent<LayoutElement>().ignoreLayout = true;
        var badgeRT = badge.GetComponent<RectTransform>();
        badgeRT.anchorMin        = new Vector2(1f, 1f);
        badgeRT.anchorMax        = new Vector2(1f, 1f);
        badgeRT.pivot            = new Vector2(1f, 1f);
        badgeRT.anchoredPosition = new Vector2(-6f, -6f);
        badgeRT.sizeDelta        = new Vector2(44f, 30f);
        badge.AddComponent<Image>().color = new Color(
            def.color.r * 0.5f + 0.05f,
            def.color.g * 0.5f + 0.05f,
            def.color.b * 0.5f + 0.15f, 0.93f);
        var badgeTxt = Lbl(badge.transform, "✓", 24f, Color.white);
        badgeTxt.fontStyle = FontStyles.Bold;
        badgeTxt.alignment = TextAlignmentOptions.Center;
        StretchRT(badgeTxt.GetComponent<RectTransform>());
        badge.SetActive(false);
        _charBadges[i] = badge;

        // ── Info area (fixed height below portrait) ───────────────────────
        var info = GroupGO(card.transform, "Info");
        info.AddComponent<Image>().color = C_Panel;
        info.AddComponent<LayoutElement>().preferredHeight = 230f;

        var infoVLG = info.AddComponent<VerticalLayoutGroup>();
        infoVLG.padding                = new RectOffset(16, 16, 12, 12);
        infoVLG.spacing                = 7f;
        infoVLG.childControlHeight     = false;
        infoVLG.childControlWidth      = true;
        infoVLG.childForceExpandHeight = false;
        infoVLG.childForceExpandWidth  = true;

        var desc = Lbl(info.transform, def.description ?? "", 20f, C_TxtMid);
        desc.enableWordWrapping = true;
        desc.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        Sep(info.transform);

        StatRowCompact(info.transform, "HP",  def.color, def.healthMultiplier);
        StatRowCompact(info.transform, "SPD", def.color, def.speedMultiplier);
        StatRowCompact(info.transform, "DMG", def.color, def.damageMultiplier);

        var res = Lbl(info.transform,
            $"{def.startingWood}W  ·  {def.startingMetal}M  starting",
            20f, new Color(0.58f, 0.74f, 0.38f));
        res.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
    }

    private void StatRowCompact(Transform parent, string label, Color barColor, float multiplier)
    {
        float  fill  = Mathf.Clamp01(multiplier / 2f);
        string delta = multiplier > 1.005f ? $"+{(multiplier-1f)*100f:0}%"
                     : multiplier < 0.995f ? $"-{(1f-multiplier)*100f:0}%"
                     : "base";
        Color  dCol  = multiplier > 1.005f ? C_Green
                     : multiplier < 0.995f ? C_Red
                     : Color.white;

        var row = GroupGO(parent, label + "Row");
        row.AddComponent<LayoutElement>().preferredHeight = 20f;
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.spacing                = 8f;
        hlg.childControlHeight     = true;
        hlg.childControlWidth      = false;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = false;

        var nl = Lbl(row.transform, label, 20f, C_TxtMid);
        nl.alignment = TextAlignmentOptions.Right;
        nl.gameObject.AddComponent<LayoutElement>().preferredWidth = 32f;

        var barBG = GroupGO(row.transform, "Bar");
        barBG.AddComponent<Image>().color = new Color(0.10f, 0.10f, 0.14f);
        barBG.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var fillGO = GroupGO(barBG.transform, "Fill");
        fillGO.AddComponent<Image>().color = barColor;
        var fRT    = fillGO.GetComponent<RectTransform>();
        fRT.anchorMin = new Vector2(0f,   0.1f);
        fRT.anchorMax = new Vector2(fill, 0.9f);
        fRT.offsetMin = fRT.offsetMax = Vector2.zero;

        var dL = Lbl(row.transform, delta, 20f, dCol);
        dL.alignment = TextAlignmentOptions.Right;
        dL.gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Step 1 — Loadout
    // ═════════════════════════════════════════════════════════════════════════

    private GameObject BuildLoadoutStep(Transform parent)
    {
        var panel = GroupGO(parent, "LoadoutStep");
        panel.AddComponent<Image>().color = C_BG;

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(0, 0, 0, 0);
        vlg.spacing                = 0f;
        vlg.childControlHeight     = true;
        vlg.childControlWidth      = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth  = true;

        // Scrollable card list (full height — no info strip)
        var scroll = MakeScrollView(panel.transform, vertical: true);
        scroll.GetComponent<LayoutElement>().flexibleHeight = 1f;
        var cnt = scroll.transform.Find("Viewport/Content");

        DestroyImmediate(cnt.GetComponent<VerticalLayoutGroup>());
        DestroyImmediate(cnt.GetComponent<ContentSizeFitter>());
        var listVLG = cnt.gameObject.AddComponent<VerticalLayoutGroup>();
        listVLG.spacing                = 0f;
        listVLG.childControlHeight     = true;
        listVLG.childControlWidth      = true;
        listVLG.childForceExpandHeight = false;
        listVLG.childForceExpandWidth  = true;
        cnt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Cards are built lazily in RebuildLoadoutCards() when this step is shown
        _loadoutListContent = cnt;

        return panel;
    }

    private void RebuildLoadoutCards()
    {
        if (_loadoutListContent == null) return;

        // Clear previous cards
        for (int i = _loadoutListContent.childCount - 1; i >= 0; i--)
            DestroyImmediate(_loadoutListContent.GetChild(i).gameObject);

        // Determine which buildings this character may pick from
        CharacterDefinition def = characters != null && _charIndex < characters.Length
            ? characters[_charIndex] : null;
        HashSet<BuildingCard> allowed = null;
        if (def?.availableBuildings != null && def.availableBuildings.Length > 0)
            allowed = new HashSet<BuildingCard>(def.availableBuildings);

        int cardCount      = buildingCards?.Length ?? 0;
        _loadoutCardBgs    = new Image[cardCount];
        _loadoutAccentBars = new Image[cardCount];
        _loadoutSelLabels  = new TextMeshProUGUI[cardCount];

        // Remove previously selected buildings that are no longer available
        if (buildingCards != null && allowed != null)
            _loadout.RemoveAll(i => i < buildingCards.Length && buildingCards[i] != null
                                 && !buildingCards[i].isCitadel && !buildingCards[i].isBasic
                                 && !allowed.Contains(buildingCards[i]));

        // ── Section: always-equipped ──────────────────────────────────────
        AddSectionHeader(_loadoutListContent, "ALWAYS EQUIPPED", C_Gold);
        if (buildingCards != null)
            for (int i = 0; i < buildingCards.Length; i++)
                if (buildingCards[i] != null && (buildingCards[i].isCitadel || buildingCards[i].isBasic))
                    BuildLoadoutCard(_loadoutListContent, i);

        // ── Section: selectable buildings ─────────────────────────────────
        AddSectionSeparator(_loadoutListContent);
        Transform selHdr = AddSectionHeader(_loadoutListContent, "SELECT BUILDINGS", C_Blue);
        _loadoutCounter = Lbl(selHdr, $"{_loadout.Count} / {maxLoadoutSize}", 14f, C_Blue);
        _loadoutCounter.fontStyle = FontStyles.Bold;
        _loadoutCounter.alignment = TextAlignmentOptions.Right;
        _loadoutCounter.gameObject.AddComponent<LayoutElement>().preferredWidth = 72f;

        if (buildingCards != null)
            for (int i = 0; i < buildingCards.Length; i++)
                if (buildingCards[i] != null && !buildingCards[i].isCitadel && !buildingCards[i].isBasic)
                    if (allowed == null || allowed.Contains(buildingCards[i]))
                        BuildLoadoutCard(_loadoutListContent, i);
    }

    // ─── Loadout card ──────────────────────────────────────────────────────────
    //  Full-width list row: [6px strip | 150px icon area | content VLG]
    //  Content: name 22pt | description 15pt | stats (optional) | footer 14pt

    private void BuildLoadoutCard(Transform parent, int idx)
    {
        var card       = buildingCards[idx];
        bool isCitadel = card.isCitadel;
        bool isBasic   = card.isBasic;
        bool isFixed   = isCitadel || isBasic;
        bool isChosen  = _loadout.Contains(idx);

        Color accentColor = isCitadel ? C_Gold
                          : isBasic   ? C_Green
                          : isChosen  ? C_Blue
                          : new Color(0.20f, 0.20f, 0.28f);
        Color bgColor     = isCitadel ? C_CardCit
                          : isBasic   ? C_CardBasic
                          : isChosen  ? C_CardSel
                          : C_CardNorm;

        // ── Card row (full-width, 170px tall) ─────────────────────────────
        var row    = GroupGO(parent, $"Card{idx}");
        var rowImg = row.AddComponent<Image>();
        UIHelper.ApplyImage(rowImg, _theme?.cardBackground, bgColor, Image.Type.Tiled);
        _loadoutCardBgs[idx] = rowImg;
        row.AddComponent<LayoutElement>().preferredHeight = 170f;

        if (!isFixed)
        {
            var btn = row.AddComponent<Button>();
            btn.targetGraphic = rowImg;
            SetBtn(btn, Color.white, new Color(1.06f, 1.06f, 1.06f), new Color(0.90f, 0.90f, 0.90f));
            int ci = idx;
            btn.onClick.AddListener(() => ToggleLoadout(ci));
        }

        var rowHLG = row.AddComponent<HorizontalLayoutGroup>();
        rowHLG.spacing                = 0f;
        rowHLG.childControlHeight     = true;
        rowHLG.childControlWidth      = true;
        rowHLG.childForceExpandHeight = true;
        rowHLG.childForceExpandWidth  = false;

        // 6px accent strip ─────────────────────────────────────────────────
        var strip = GroupGO(row.transform, "AccentStrip");
        strip.AddComponent<Image>().color = accentColor;
        strip.AddComponent<LayoutElement>().preferredWidth = 6f;
        _loadoutAccentBars[idx] = strip.GetComponent<Image>();

        // 150px icon area ──────────────────────────────────────────────────
        var iconArea = GroupGO(row.transform, "IconArea");
        iconArea.AddComponent<LayoutElement>().preferredWidth = 150f;
        iconArea.AddComponent<Image>().color =
            new Color(card.color.r * 0.10f + 0.03f,
                      card.color.g * 0.10f + 0.03f,
                      card.color.b * 0.12f + 0.04f);

        Sprite prefabSprite = card.icon != null ? card.icon
            : card.buildingDef?.prefab != null
                ? card.buildingDef.prefab.GetComponentInChildren<SpriteRenderer>()?.sprite
                : null;

        if (prefabSprite != null)
        {
            var iconImg = GroupGO(iconArea.transform, "Icon");
            var iconRT  = iconImg.GetComponent<RectTransform>();
            iconRT.anchorMin       = new Vector2(0.1f, 0.1f);
            iconRT.anchorMax       = new Vector2(0.9f, 0.9f);
            iconRT.offsetMin       = Vector2.zero;
            iconRT.offsetMax       = Vector2.zero;
            var img            = iconImg.AddComponent<Image>();
            img.sprite         = prefabSprite;
            img.preserveAspect = true;
            img.color          = Color.white;
        }
        else
        {
            // Fallback: large initial letter centered in the icon area
            var initGO  = GroupGO(iconArea.transform, "Initial");
            StretchRT(initGO.GetComponent<RectTransform>());
            var initTxt = Lbl(initGO.transform,
                card.displayName.Length > 0 ? card.displayName.Substring(0, 1).ToUpper() : "?",
                52f, new Color(card.color.r, card.color.g, card.color.b, 0.55f));
            initTxt.fontStyle = FontStyles.Bold;
            initTxt.alignment = TextAlignmentOptions.Center;
            StretchRT(initTxt.GetComponent<RectTransform>());
        }

        // Content area (takes remaining width) ─────────────────────────────
        var content = GroupGO(row.transform, "Content");
        content.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var cVLG = content.AddComponent<VerticalLayoutGroup>();
        cVLG.padding                = new RectOffset(18, 18, 14, 12);
        cVLG.spacing                = 6f;
        cVLG.childControlHeight     = true;
        cVLG.childControlWidth      = true;
        cVLG.childForceExpandHeight = false;
        cVLG.childForceExpandWidth  = true;

        // Name row: name + status badge ────────────────────────────────────
        var nameRow = GroupGO(content.transform, "NameRow");
        nameRow.AddComponent<LayoutElement>().preferredHeight = 32f;
        var nrHLG = nameRow.AddComponent<HorizontalLayoutGroup>();
        nrHLG.childAlignment         = TextAnchor.MiddleLeft;
        nrHLG.spacing                = 12f;
        nrHLG.childControlHeight     = true;
        nrHLG.childControlWidth      = true;
        nrHLG.childForceExpandHeight = true;
        nrHLG.childForceExpandWidth  = false;

        var nameL = Lbl(nameRow.transform, card.displayName, 24f, Color.white);
        nameL.fontStyle = FontStyles.Bold;
        nameL.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        if (isFixed)
        {
            string bt   = isCitadel ? "ALWAYS EQUIPPED" : "AUTO-INCLUDED";
            Color  bcol = isCitadel ? new Color(0.30f, 0.20f, 0.02f) : new Color(0.04f, 0.18f, 0.04f);
            var bp = GroupGO(nameRow.transform, "Badge");
            bp.AddComponent<Image>().color = bcol;
            bp.AddComponent<LayoutElement>().preferredWidth = 130f;
            var bVLG = bp.AddComponent<VerticalLayoutGroup>();
            bVLG.childAlignment         = TextAnchor.MiddleCenter;
            bVLG.childControlHeight     = true;
            bVLG.childControlWidth      = true;
            bVLG.childForceExpandHeight = true;
            bVLG.childForceExpandWidth  = true;
            var bL = Lbl(bp.transform, bt, 20f, accentColor);
            bL.fontStyle = FontStyles.Bold;
            bL.alignment = TextAlignmentOptions.Center;
        }

        // Description ──────────────────────────────────────────────────────
        var descL = Lbl(content.transform, card.description, 20f, C_TxtMid);
        descL.enableWordWrapping = true;
        descL.gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;

        // Stats line (only if filled in) ───────────────────────────────────
        if (!string.IsNullOrEmpty(card.statsSummary))
        {
            var sl = Lbl(content.transform, card.statsSummary, 20f, C_Cyan);
            sl.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
        }

        // Footer: cost | status ────────────────────────────────────────────
        var footer = GroupGO(content.transform, "Footer");
        footer.AddComponent<LayoutElement>().preferredHeight = 26f;
        var fHLG = footer.AddComponent<HorizontalLayoutGroup>();
        fHLG.childAlignment         = TextAnchor.MiddleLeft;
        fHLG.childControlHeight     = true;
        fHLG.childControlWidth      = true;
        fHLG.childForceExpandHeight = true;
        fHLG.childForceExpandWidth  = false;

        var costL = Lbl(footer.transform, $"{card.woodCost}W  ·  {card.metalCost}M", 20f,
            new Color(0.58f, 0.82f, 0.36f));
        costL.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        string statusTxt = isFixed   ? "ALWAYS EQUIPPED"
                         : isChosen  ? "✓  SELECTED"
                         : "+ ADD";
        Color statusCol  = isFixed   ? new Color(accentColor.r, accentColor.g, accentColor.b, 0.70f)
                         : isChosen  ? new Color(0.38f, 0.90f, 0.48f)
                         : new Color(0.30f, 0.30f, 0.40f);
        var selL = Lbl(footer.transform, statusTxt, 20f, statusCol);
        selL.fontStyle = (isFixed || isChosen) ? FontStyles.Bold : FontStyles.Normal;
        selL.alignment = TextAlignmentOptions.Right;
        selL.gameObject.AddComponent<LayoutElement>().preferredWidth = 140f;
        if (!isFixed) _loadoutSelLabels[idx] = selL;

        // 1px divider below card ───────────────────────────────────────────
        var div = GroupGO(parent, $"Div{idx}");
        div.AddComponent<Image>().color = C_Div;
        div.AddComponent<LayoutElement>().preferredHeight = 1f;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Step 2 — Level
    // ═════════════════════════════════════════════════════════════════════════

    private GameObject BuildLevelStep(Transform parent)
    {
        var panel = GroupGO(parent, "LevelStep");
        UIHelper.ApplyImage(panel.AddComponent<Image>(), _theme?.panelBackground, Color.clear, Image.Type.Tiled);

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(48, 48, 36, 36);
        vlg.spacing = 20f;
        vlg.childControlHeight     = true;
        vlg.childControlWidth      = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth  = true;

        var ttl = Lbl(panel.transform, "SELECT LEVEL", 30f, C_TxtHi);
        ttl.fontStyle = FontStyles.Bold;
        ttl.gameObject.AddComponent<LayoutElement>().preferredHeight = 90f;

        var scroll  = MakeScrollView(panel.transform, vertical: false);
        scroll.GetComponent<LayoutElement>().flexibleHeight = 1f;
        var cnt = scroll.transform.Find("Viewport/Content");

        DestroyImmediate(cnt.GetComponent<VerticalLayoutGroup>());
        DestroyImmediate(cnt.GetComponent<ContentSizeFitter>());

        var hlg = cnt.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing               = 20f;
        hlg.padding               = new RectOffset(0, 0, 0, 0);
        hlg.childAlignment        = TextAnchor.UpperLeft;
        hlg.childControlHeight    = false;
        hlg.childControlWidth     = false;
        hlg.childForceExpandHeight = false;
        hlg.childForceExpandWidth  = false;
        cnt.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        _levelCardBgs = new Image[levels?.Length ?? 0];
        int best = PersistentDataManager.Instance?.BestWave ?? 0;
        if (levels != null)
            for (int i = 0; i < levels.Length; i++)
                if (levels[i] != null)
                    BuildLevelCard(cnt, i, levels[i].isUnlockedByDefault || best >= levels[i].unlockAtBestWave);

        return panel;
    }

    private void BuildLevelCard(Transform parent, int idx, bool unlocked)
    {
        var lvl = levels[idx];

        var card   = GroupGO(parent, $"LvlCard{idx}");
        var cardRT = card.GetComponent<RectTransform>();
        cardRT.sizeDelta = new Vector2(340f, 480f);

        var img  = card.AddComponent<Image>();
        Color lvlCardColor = !unlocked ? C_CardLock : idx == _levelIndex ? C_CardSel : C_CardNorm;
        UIHelper.ApplyImage(img, _theme?.cardBackground, lvlCardColor, Image.Type.Tiled);
        img.color = lvlCardColor;   // restore tint after ApplyImage (sprite tinting still works)
        _levelCardBgs[idx] = img;

        var btn = card.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.interactable  = unlocked;
        if (unlocked)
        {
            SetBtn(btn, Color.white, new Color(1.06f, 1.06f, 1.06f), new Color(0.88f, 0.88f, 0.88f));
            btn.onClick.AddListener(() => SelectLevel(idx));
        }

        var vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.spacing                = 0f;
        vlg.childControlWidth      = true;
        vlg.childForceExpandWidth  = true;

        // Colour swatch / preview (top half of card)
        var swatch = GroupGO(card.transform, "Swatch");
        swatch.AddComponent<Image>().color = unlocked ? lvl.previewColor : new Color(0.08f, 0.08f, 0.10f);
        swatch.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 200f);

        if (!unlocked)
        {
            var lk = Lbl(swatch.transform, "LOCKED", 20f, new Color(0.30f, 0.30f, 0.36f));
            lk.fontStyle = FontStyles.Bold;
            lk.alignment = TextAlignmentOptions.Center;
            StretchRT(lk.GetComponent<RectTransform>());
        }

        // Info area
        var info    = GroupGO(card.transform, "Info");
        var infoVLG = info.AddComponent<VerticalLayoutGroup>();
        infoVLG.padding = new RectOffset(18, 18, 16, 16);
        infoVLG.spacing = 10f;
        infoVLG.childControlWidth      = true;
        infoVLG.childForceExpandWidth  = true;
        info.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 280f);

        Color nameCol = unlocked ? C_TxtHi : new Color(0.30f, 0.30f, 0.36f);
        var nl = Lbl(info.transform, lvl.levelName, 22f, nameCol);
        nl.fontStyle = FontStyles.Bold;
        nl.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 32f);

        // Divider
        var dv = GroupGO(info.transform, "Div");
        UIHelper.ApplyImage(dv.AddComponent<Image>(), _theme?.stepBarBackground, C_Panel, Image.Type.Tiled);
        dv.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 1f);

        var dl = Lbl(info.transform, lvl.description, 22f, new Color(0.46f, 0.46f, 0.54f));
        dl.enableWordWrapping = true;
        dl.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 110f);

        if (!unlocked)
        {
            var req = Lbl(info.transform, $"Reach wave {lvl.unlockAtBestWave} to unlock", 22f,
                          new Color(0.76f, 0.62f, 0.14f));
            req.enableWordWrapping = true;
            req.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 36f);
        }
        else if (idx == _levelIndex)
        {
            var selRow = GroupGO(info.transform, "SelRow");
            selRow.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 28f);
            selRow.AddComponent<Image>().color = new Color(0.10f, 0.22f, 0.40f);
            var selVLG = selRow.AddComponent<VerticalLayoutGroup>();
            selVLG.childAlignment      = TextAnchor.MiddleCenter;
            selVLG.childControlHeight  = true;
            selVLG.childControlWidth   = true;
            selVLG.childForceExpandHeight = true;
            selVLG.childForceExpandWidth  = true;
            var sl = Lbl(selRow.transform, "● SELECTED", 22f, C_Blue);
            sl.fontStyle = FontStyles.Bold;
            sl.alignment = TextAlignmentOptions.Center;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Nav bar
    // ═════════════════════════════════════════════════════════════════════════

    private void BuildNavBar(Transform parent)
    {
        var bar    = GroupGO(parent, "NavBar");
        var barImg = bar.AddComponent<Image>();
        UIHelper.ApplyImage(barImg, _theme?.stepBarBackground, C_Surface, Image.Type.Tiled);
        bar.AddComponent<LayoutElement>().preferredHeight = 80f;

        var hlg = bar.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(48, 48, 12, 12);
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlHeight     = true;
        hlg.childControlWidth      = false;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = true;

        // Back button
        var backGO = GroupGO(bar.transform, "BackBtn");
        backGO.AddComponent<LayoutElement>().preferredWidth = 200f;
        var backImg = backGO.AddComponent<Image>();
        UIHelper.ApplyImage(backImg, _theme?.buttonSecondary, Color.clear);
        _backBtn = backGO.AddComponent<Button>();
        _backBtn.targetGraphic = backImg;
        _backBtn.colors = UIHelper.BtnColors(_theme?.buttonSecondary,
            Color.white, new Color(1.08f, 1.08f, 1.08f), new Color(0.88f, 0.88f, 0.88f));
        _backBtn.onClick.AddListener(OnBack);
        _backLabel = Lbl(backGO.transform, "BACK", 20f, C_TxtMid);
        _backLabel.alignment = TextAlignmentOptions.Center;
        StretchRT(_backLabel.GetComponent<RectTransform>());

        // Spacer
        var sp = GroupGO(bar.transform, "Sp");
        sp.AddComponent<LayoutElement>().flexibleWidth = 1f;

        // Next — primary action
        var nextGO = GroupGO(bar.transform, "NextBtn");
        nextGO.AddComponent<LayoutElement>().preferredWidth = 200f;
        var nextImg = nextGO.AddComponent<Image>();
        UIHelper.ApplyImage(nextImg, _theme?.buttonPrimary, new Color(0.12f, 0.46f, 0.12f));
        _nextBtn = nextGO.AddComponent<Button>();
        _nextBtn.targetGraphic = nextImg;
        SetBtn(_nextBtn, Color.white, new Color(1.10f, 1.10f, 1.10f), new Color(0.84f, 0.84f, 0.84f));
        _nextBtn.onClick.AddListener(OnNext);
        _nextLabel = Lbl(nextGO.transform, "NEXT", 20f, Color.white);
        _nextLabel.fontStyle = FontStyles.Bold;
        _nextLabel.alignment = TextAlignmentOptions.Center;
        StretchRT(_nextLabel.GetComponent<RectTransform>());
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Navigation
    // ═════════════════════════════════════════════════════════════════════════

    private void GoToStep(int step)
    {
        _step = Mathf.Clamp(step, 0, _stepPanels.Length - 1);

        for (int i = 0; i < _stepPanels.Length; i++)
            _stepPanels[i].SetActive(i == _step);

        for (int i = 0; i < _stepChipBgs.Length; i++)
        {
            bool active = i == _step, done = i < _step;
            _stepChipBgs[i].color    = active ? C_StepAct : done ? C_StepDone : C_StepFut;
            _stepNameLabels[i].color = active ? C_TxtHi
                                     : done   ? new Color(0.46f, 0.66f, 0.90f)
                                              : Color.white;
        }

        _backLabel.text = _step == 0 ? "MENU" : "BACK";

        bool isLast = _step == _stepPanels.Length - 1;
        _nextLabel.text = isLast ? "START" : "NEXT";
        _nextBtn.GetComponent<Image>().color = isLast
            ? new Color(0.09f, 0.40f, 0.09f)
            : new Color(0.12f, 0.46f, 0.12f);

        if (_step == 0 && characters != null && characters.Length > 0)
            SelectCharacter(_charIndex, save: false);

        if (_step == 1) RebuildLoadoutCards();
    }

    private void OnBack()
    {
        if (_step == 0) SceneManager.LoadScene("MainMenuScene");
        else            GoToStep(_step - 1);
    }
    private void OnNext()
    {
        if (_step < _stepPanels.Length - 1) GoToStep(_step + 1);
        else StartGame();
    }

    private void StartGame()
    {
        PersistentDataManager.Instance?.SetSelectedCharacterDefinition(
            characters != null && _charIndex < characters.Length ? characters[_charIndex] : null);

        PersistentDataManager.Instance?.SetBuildingLoadout(_loadout.ToArray());

        if (PersistentDataManager.Instance != null && buildingCards != null)
        {
            var defs  = new List<BuildingDefinition>();
            var names = new List<string>();

            // Citadel / always-included first
            foreach (var bc in buildingCards)
            {
                if (bc == null || bc.buildingDef == null) continue;
                if (bc.isCitadel || bc.isBasic)
                {
                    defs.Add(bc.buildingDef);
                    names.Add(bc.buildingDef.buildingName);
                }
            }

            // Player-selected buildings
            foreach (int i in _loadout)
            {
                if (i >= buildingCards.Length || buildingCards[i] == null || buildingCards[i].buildingDef == null) continue;
                defs.Add(buildingCards[i].buildingDef);
                names.Add(buildingCards[i].buildingDef.buildingName);
            }

            PersistentDataManager.Instance.SetBuildingDefinitions(defs.ToArray());
            PersistentDataManager.Instance.SetBuildingLoadoutNames(names.ToArray());
        }

        PersistentDataManager.Instance?.SelectLevel(_levelIndex);

        string scene = "SampleScene";
        if (levels != null && _levelIndex < levels.Length && levels[_levelIndex] != null)
            scene = levels[_levelIndex].sceneName;

        SceneTransitionManager.Instance?.LoadScene(scene);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Character logic
    // ═════════════════════════════════════════════════════════════════════════

    private void SelectCharacter(int idx, bool save = true)
    {
        if (characters == null || idx < 0 || idx >= characters.Length) return;
        _charIndex    = idx;
        _selectedChar = characters[idx];

        // Character-specific select sound
        if (_selectedChar?.selectSound != null)
            AudioManager.Instance?.PlaySFX(_selectedChar.selectSound);

        for (int i = 0; i < (_charCardBgs?.Length ?? 0); i++)
        {
            bool sel = i == idx;
            var  def = characters[i];

            // Card background — dark character-tinted on selection
            if (_charCardBgs[i] != null)
                _charCardBgs[i].color = sel
                    ? new Color(def.color.r * 0.18f + 0.04f,
                                def.color.g * 0.18f + 0.04f,
                                def.color.b * 0.22f + 0.06f)
                    : C_CardNorm;

            // Top accent bar — character colour when selected
            if (_charTopBars != null && i < _charTopBars.Length && _charTopBars[i] != null)
                _charTopBars[i].color = sel ? def.color : new Color(0.15f, 0.15f, 0.20f);

            // ✓ Badge
            if (_charBadges != null && i < _charBadges.Length && _charBadges[i] != null)
                _charBadges[i].SetActive(sel);

            // Scale punch animation
            if (_charCardRTs != null && i < _charCardRTs.Length && _charCardRTs[i] != null)
            {
                if (_charScaleCoroutines[i] != null) StopCoroutine(_charScaleCoroutines[i]);
                _charScaleCoroutines[i] = StartCoroutine(AnimateScale(_charCardRTs[i], sel ? 1.05f : 0.97f, 0.14f));
            }
        }

        RefreshDetail();
        if (save) PersistentDataManager.Instance?.SelectCharacter(idx);
    }

    private void RefreshDetail()
    {
        var def = _selectedChar;
        if (def == null) return;

        if (_detailName        != null) _detailName.text        = def.characterName.ToUpper();
        if (_detailColorBar    != null) _detailColorBar.color   = def.color;
        if (_detailDescription != null) _detailDescription.text = def.description;

        if (_statsContainer != null)
        {
            for (int i = _statsContainer.childCount - 1; i >= 0; i--)
                DestroyImmediate(_statsContainer.GetChild(i).gameObject);

            StatRow(_statsContainer, "Health", def.color, def.healthMultiplier);
            StatRow(_statsContainer, "Speed",  def.color, def.speedMultiplier);
            StatRow(_statsContainer, "Damage", def.color, def.damageMultiplier);

            var res = GroupGO(_statsContainer, "Resources");
            res.AddComponent<LayoutElement>().preferredHeight = 26f;
            var resLbl = Lbl(res.transform,
                $"Starting:   {def.startingWood} Wood   |   {def.startingMetal} Metal",
                22f, new Color(0.58f, 0.74f, 0.38f));
            StretchRT(resLbl.GetComponent<RectTransform>());
        }

        if (_passivesContainer != null)
        {
            for (int i = _passivesContainer.childCount - 1; i >= 0; i--)
                DestroyImmediate(_passivesContainer.GetChild(i).gameObject);

            if (_passivePillsCoroutine != null) StopCoroutine(_passivePillsCoroutine);
            _passivePillsCoroutine = StartCoroutine(AnimatePassivePills(def));
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Loadout logic
    // ═════════════════════════════════════════════════════════════════════════

    private void ToggleLoadout(int idx)
    {
        if (buildingCards != null && idx < buildingCards.Length && buildingCards[idx] != null
            && (buildingCards[idx].isBasic || buildingCards[idx].isCitadel)) return;

        bool wasIn = _loadout.Contains(idx);
        if (!wasIn && _loadout.Count >= maxLoadoutSize) return;

        if (wasIn) _loadout.Remove(idx);
        else       _loadout.Add(idx);

        bool nowIn = _loadout.Contains(idx);

        if (_loadoutCardBgs != null && idx < _loadoutCardBgs.Length && _loadoutCardBgs[idx] != null)
            _loadoutCardBgs[idx].color = nowIn ? C_CardSel : C_CardNorm;

        if (_loadoutAccentBars != null && idx < _loadoutAccentBars.Length && _loadoutAccentBars[idx] != null)
            _loadoutAccentBars[idx].color = nowIn ? C_Blue : new Color(0.20f, 0.20f, 0.28f);

        if (_loadoutSelLabels != null && idx < _loadoutSelLabels.Length && _loadoutSelLabels[idx] != null)
        {
            _loadoutSelLabels[idx].text      = nowIn ? "✓  SELECTED" : "+ ADD";
            _loadoutSelLabels[idx].color     = nowIn ? new Color(0.38f, 0.90f, 0.48f) : new Color(0.30f, 0.30f, 0.40f);
            _loadoutSelLabels[idx].fontStyle = nowIn ? FontStyles.Bold : FontStyles.Normal;
        }

        if (_loadoutCounter != null)
        {
            _loadoutCounter.text  = $"{_loadout.Count} / {maxLoadoutSize}";
            _loadoutCounter.color = _loadout.Count >= maxLoadoutSize ? C_Green : C_Blue;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Level logic
    // ═════════════════════════════════════════════════════════════════════════

    private void SelectLevel(int idx)
    {
        if (levels == null || idx < 0 || idx >= levels.Length) return;
        _levelIndex = idx;
        int best = PersistentDataManager.Instance?.BestWave ?? 0;

        if (_levelCardBgs != null)
            for (int i = 0; i < _levelCardBgs.Length; i++)
            {
                if (_levelCardBgs[i] == null) continue;
                bool unlocked = levels[i].isUnlockedByDefault || best >= levels[i].unlockAtBestWave;
                _levelCardBgs[i].color = i == idx ? C_CardSel : unlocked ? C_CardNorm : C_CardLock;
            }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Row builders (unchanged)
    // ═════════════════════════════════════════════════════════════════════════

    private void StatRow(Transform parent, string statName, Color barColor, float multiplier)
    {
        float  fill  = Mathf.Clamp01(multiplier / 2f);
        string delta = multiplier > 1.005f ? $"+{(multiplier-1f)*100f:0}%"
                     : multiplier < 0.995f ? $"-{(1f-multiplier)*100f:0}%"
                     : "base";
        Color  dCol  = multiplier > 1.005f ? new Color(0.32f, 0.88f, 0.32f)
                     : multiplier < 0.995f ? new Color(0.88f, 0.32f, 0.32f)
                     : new Color(0.44f, 0.44f, 0.44f);

        var row = GroupGO(parent, statName + "Row");
        row.AddComponent<LayoutElement>().preferredHeight = 34f;
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.spacing                = 14f;
        hlg.childControlHeight     = true;
        hlg.childControlWidth      = false;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = false;

        var nl = Lbl(row.transform, statName, 22f, C_TxtMid);
        nl.alignment = TextAlignmentOptions.Right;
        nl.gameObject.AddComponent<LayoutElement>().preferredWidth = 80f;

        var barBG = GroupGO(row.transform, "BarBG");
        barBG.AddComponent<Image>().color = new Color(0.10f, 0.10f, 0.14f);
        barBG.AddComponent<LayoutElement>().preferredWidth = 300f;

        var fll   = GroupGO(barBG.transform, "Fill");
        fll.AddComponent<Image>().color = barColor;
        var fRT   = fll.GetComponent<RectTransform>();
        fRT.anchorMin = new Vector2(0f,   0.15f);
        fRT.anchorMax = new Vector2(fill, 0.85f);
        fRT.offsetMin = Vector2.zero;
        fRT.offsetMax = Vector2.zero;

        Lbl(row.transform, $"×{multiplier:0.0}", 22f, C_TxtHi)
            .gameObject.AddComponent<LayoutElement>().preferredWidth = 44f;
        Lbl(row.transform, delta, 22f, dCol)
            .gameObject.AddComponent<LayoutElement>().preferredWidth = 52f;
    }

    private void PassiveRow(Transform parent, PassiveEffect passive, Color accent)
    {
        if (passive == null) return;
        var row = GroupGO(parent, "Passive");
        row.AddComponent<Image>().color = new Color(0.09f, 0.09f, 0.12f);
        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 72f;
        le.flexibleHeight  = 0f;

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(12, 12, 10, 10);
        hlg.spacing                = 12f;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlHeight     = true;
        hlg.childControlWidth      = false;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = false;

        var strip = GroupGO(row.transform, "Strip");
        strip.AddComponent<Image>().color = accent;
        strip.AddComponent<LayoutElement>().preferredWidth = 4f;

        var col = GroupGO(row.transform, "Col");
        col.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var colVLG = col.AddComponent<VerticalLayoutGroup>();
        colVLG.spacing                = 3f;
        colVLG.childControlHeight     = true;
        colVLG.childControlWidth      = true;
        colVLG.childForceExpandHeight = true;
        colVLG.childForceExpandWidth  = true;

        var n = Lbl(col.transform, passive.effectName ?? "", 22f, new Color(0.88f, 0.84f, 0.38f));
        n.fontStyle = FontStyles.Bold;
        var d = Lbl(col.transform, passive.effectDescription ?? "", 22f, C_TxtMid);
        d.enableWordWrapping = true;
    }

    private void PassivePill(Transform parent, PassiveEffect passive, Color accent)
    {
        if (passive == null) return;

        var pill = GroupGO(parent, "Pill");
        UIHelper.ApplyImage(pill.AddComponent<Image>(), _theme?.cardBackground,
            new Color(0.09f, 0.09f, 0.12f), Image.Type.Tiled);
        pill.AddComponent<LayoutElement>().preferredWidth = 260f;

        var vlg = pill.AddComponent<VerticalLayoutGroup>();
        vlg.spacing                = 0f;
        vlg.childControlHeight     = true;
        vlg.childControlWidth      = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth  = true;

        // Top accent bar
        var bar = GroupGO(pill.transform, "AccentBar");
        bar.AddComponent<Image>().color = accent;
        bar.AddComponent<LayoutElement>().preferredHeight = 3f;

        // Content
        var content = GroupGO(pill.transform, "Content");
        content.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var cVLG = content.AddComponent<VerticalLayoutGroup>();
        cVLG.padding                = new RectOffset(10, 10, 8, 8);
        cVLG.spacing                = 4f;
        cVLG.childControlHeight     = false;
        cVLG.childControlWidth      = true;
        cVLG.childForceExpandHeight = false;
        cVLG.childForceExpandWidth  = true;

        var nameLbl = Lbl(content.transform, passive.effectName ?? "", 22f,
            new Color(0.88f, 0.84f, 0.38f));
        nameLbl.fontStyle = FontStyles.Bold;
        nameLbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

        var descLbl = Lbl(content.transform, passive.effectDescription ?? "", 22f, C_TxtMid);
        descLbl.enableWordWrapping = true;
        descLbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Animations
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator AnimateScale(RectTransform rt, float target, float duration)
    {
        if (rt == null) yield break;
        Vector3 start = rt.localScale;
        Vector3 end   = new Vector3(target, target, 1f);
        float   t     = 0f;
        while (t < duration)
        {
            t            += Time.unscaledDeltaTime;
            rt.localScale = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }
        rt.localScale = end;
    }

    private IEnumerator AnimatePassivePills(CharacterDefinition def)
    {
        bool has = def?.passiveEffects != null && def.passiveEffects.Length > 0;
        if (!has)
        {
            var none = Lbl(_passivesContainer, "No passive effects.", 22f,
                           new Color(0.30f, 0.30f, 0.36f));
            none.gameObject.AddComponent<LayoutElement>().preferredWidth = 220f;
            yield break;
        }

        foreach (var p in def.passiveEffects)
        {
            if (p == null) continue;
            PassivePill(_passivesContainer, p, def.color);
            var pill = _passivesContainer.GetChild(_passivesContainer.childCount - 1).gameObject;
            var cg   = pill.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            StartCoroutine(FadeIn(cg, 0.20f));
            yield return new WaitForSecondsRealtime(0.08f);
        }
    }

    private IEnumerator FadeIn(CanvasGroup cg, float duration)
    {
        if (cg == null) yield break;
        float t = 0f;
        while (t < duration)
        {
            t       += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Clamp01(t / duration);
            yield return null;
        }
        cg.alpha = 1f;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════════════

    private Transform AddSectionHeader(Transform parent, string title, Color accent)
    {
        var hdr = GroupGO(parent, "SectionHdr");
        hdr.AddComponent<Image>().color = new Color(0.07f, 0.07f, 0.10f);
        hdr.AddComponent<LayoutElement>().preferredHeight = 36f;

        var hlg = hdr.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(16, 16, 0, 0);
        hlg.spacing                = 10f;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlHeight     = true;
        hlg.childControlWidth      = true;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = false;

        var stripe = GroupGO(hdr.transform, "Stripe");
        stripe.AddComponent<Image>().color = accent;
        stripe.AddComponent<LayoutElement>().preferredWidth = 3f;

        var lbl = Lbl(hdr.transform, title, 22f, Color.white);
        lbl.fontStyle = FontStyles.Bold;
        lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        return hdr.transform;
    }

    private void AddSectionSeparator(Transform parent)
    {
        var sep = GroupGO(parent, "SectionSep");
        sep.AddComponent<Image>().color = C_Div;
        sep.AddComponent<LayoutElement>().preferredHeight = 8f;
    }

    private GameObject MakeScrollView(Transform parent, bool vertical)
    {
        var sv = GroupGO(parent, "Scroll");
        sv.AddComponent<LayoutElement>();
        var sr = sv.AddComponent<ScrollRect>();
        sr.horizontal        = !vertical;
        sr.vertical          = vertical;
        sr.scrollSensitivity = 30f;

        var vp   = GroupGO(sv.transform, "Viewport");
        var vpRT = vp.GetComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = Vector2.zero; vpRT.offsetMax = Vector2.zero;
        vp.AddComponent<RectMask2D>();
        sr.viewport = vpRT;

        var cnt   = GroupGO(vp.transform, "Content");
        var cRT   = cnt.GetComponent<RectTransform>();
        if (vertical) { cRT.anchorMin = new Vector2(0,1); cRT.anchorMax = new Vector2(1,1); cRT.pivot = new Vector2(0.5f,1); }
        else          { cRT.anchorMin = new Vector2(0,0); cRT.anchorMax = new Vector2(0,1); cRT.pivot = new Vector2(0,0.5f); }
        cRT.offsetMin = Vector2.zero; cRT.offsetMax = Vector2.zero;
        sr.content = cRT;

        var vlg = cnt.AddComponent<VerticalLayoutGroup>();
        vlg.spacing                = 8f;
        vlg.childControlHeight     = false;
        vlg.childControlWidth      = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth  = true;
        cnt.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return sv;
    }

    private static GameObject StretchGO(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return go;
    }

    private static GameObject GroupGO(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    private static void StretchRT(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private void Sep(Transform parent)
    {
        var s = GroupGO(parent, "Sep");
        s.AddComponent<Image>().color = C_Div;
        s.AddComponent<LayoutElement>().preferredHeight = 1f;
    }

    private static void SetBtn(Button btn, Color normal, Color highlight, Color pressed)
    {
        var cb = btn.colors;
        cb.normalColor = normal; cb.highlightedColor = highlight; cb.pressedColor = pressed;
        btn.colors = cb;
    }

    private TextMeshProUGUI Lbl(Transform parent, string text, float size, Color color)
    {
        var go  = new GameObject(text.Length > 0 && text.Length < 24 ? text : "Lbl");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var tmp              = go.AddComponent<TextMeshProUGUI>();
        tmp.text             = text;
        tmp.fontSize         = size;
        tmp.color            = color;
        tmp.alignment        = TextAlignmentOptions.Left;
        tmp.enableWordWrapping = false;
        tmp.overflowMode     = TextOverflowModes.Ellipsis;
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
