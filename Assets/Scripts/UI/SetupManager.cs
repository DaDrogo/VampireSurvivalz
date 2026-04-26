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
    private TextMeshProUGUI    _detailName;
    private Image              _detailColorBar;
    private TextMeshProUGUI    _detailDescription;
    private Transform          _statsContainer;
    private Transform          _passivesContainer;

    // Loadout
    private List<int>          _loadout = new List<int>();
    private Image[]            _loadoutCardBgs;
    private TextMeshProUGUI    _loadoutCounter;

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
        bar.AddComponent<LayoutElement>().preferredHeight = 44f;

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
            var chipNum = Lbl(chip.transform, (i + 1).ToString(), 14f, Color.white);
            chipNum.fontStyle = FontStyles.Bold;
            chipNum.alignment = TextAlignmentOptions.Center;
            StretchRT(chipNum.GetComponent<RectTransform>());

            // Step name  ───────────────────────────────────
            _stepNameLabels[i] = Lbl(step.transform, names[i], 13f, C_TxtDim);
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

        var hlg = panel.AddComponent<HorizontalLayoutGroup>();
        hlg.childControlHeight     = true;
        hlg.childControlWidth      = true;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = true;

        // ── Left list (400px) ─────────────────────────────────────────────
        var left  = GroupGO(panel.transform, "Left");
        UIHelper.ApplyImage(left.AddComponent<Image>(), _theme?.panelBackground, C_Surface, Image.Type.Tiled);
        left.AddComponent<LayoutElement>().preferredWidth = 400f;

        var leftVLG = left.AddComponent<VerticalLayoutGroup>();
        leftVLG.padding  = new RectOffset(24, 24, 28, 24);
        leftVLG.spacing  = 12f;
        leftVLG.childControlHeight     = true;
        leftVLG.childControlWidth      = true;
        leftVLG.childForceExpandHeight = false;
        leftVLG.childForceExpandWidth  = true;

        var hdr = Lbl(left.transform, "SELECT CHARACTER", 13f, C_TxtMid);
        hdr.fontStyle = FontStyles.Bold;
        hdr.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

        var scroll  = MakeScrollView(left.transform, vertical: true);
        scroll.GetComponent<LayoutElement>().flexibleHeight = 1f;
        var listContent = scroll.transform.Find("Viewport/Content");

        _charCardBgs = new Image[characters?.Length ?? 0];
        if (characters != null)
        {
            for (int i = 0; i < characters.Length; i++)
            {
                if (characters[i] == null) continue;
                var def = characters[i];
                var idx = i;

                var card    = GroupGO(listContent, $"CC{i}");
                card.AddComponent<LayoutElement>().preferredHeight = 72f;
                var cardImg = card.AddComponent<Image>();
                UIHelper.ApplyImage(cardImg, _theme?.cardBackground, CardNormal, Image.Type.Tiled);
                _charCardBgs[i] = cardImg;

                var btn = card.AddComponent<Button>();
                btn.targetGraphic = cardImg;
                SetBtn(btn, Color.white, new Color(1.08f, 1.08f, 1.08f), new Color(0.88f, 0.88f, 0.88f));
                btn.onClick.AddListener(() => SelectCharacter(idx));

                var cardHLG = card.AddComponent<HorizontalLayoutGroup>();
                cardHLG.spacing                = 0f;
                cardHLG.childControlHeight     = true;
                cardHLG.childControlWidth      = true;
                cardHLG.childForceExpandHeight = true;
                cardHLG.childForceExpandWidth  = false;

                // Colour swatch (left strip)
                var sw = GroupGO(card.transform, "Swatch");
                sw.AddComponent<Image>().color = def.color;
                sw.AddComponent<LayoutElement>().preferredWidth = 6f;

                // Text col
                var col = GroupGO(card.transform, "Col");
                col.AddComponent<LayoutElement>().flexibleWidth = 1f;
                var colVLG = col.AddComponent<VerticalLayoutGroup>();
                colVLG.padding = new RectOffset(14, 14, 12, 12);
                colVLG.childControlHeight     = true;
                colVLG.childControlWidth      = true;
                colVLG.childForceExpandHeight = true;
                colVLG.childForceExpandWidth  = true;

                var nm = Lbl(col.transform, def.characterName, 18f, C_TxtHi);
                nm.fontStyle = FontStyles.Bold;
                Lbl(col.transform,
                    $"HP ×{def.healthMultiplier:0.0}   SPD ×{def.speedMultiplier:0.0}   DMG ×{def.damageMultiplier:0.0}",
                    12f, C_TxtMid);
            }
        }

        // ── Divider ───────────────────────────────────────────────────────
        var div = GroupGO(panel.transform, "Div");
        UIHelper.ApplyImage(div.AddComponent<Image>(), _theme?.stepBarBackground, C_Panel, Image.Type.Tiled);
        div.AddComponent<LayoutElement>().preferredWidth = 1f;
        

        // ── Right detail (flex) ───────────────────────────────────────────
        var right = GroupGO(panel.transform, "Right");
        UIHelper.ApplyImage(right.AddComponent<Image>(), _theme?.panelBackground, C_Panel, Image.Type.Tiled);
        right.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var rightVLG = right.AddComponent<VerticalLayoutGroup>();
        rightVLG.padding = new RectOffset(52, 52, 36, 36);
        rightVLG.spacing = 16f;
        rightVLG.childControlHeight     = false;
        rightVLG.childControlWidth      = true;
        rightVLG.childForceExpandHeight = false;
        rightVLG.childForceExpandWidth  = true;

        // Name + colour bar row
        var nameRow = GroupGO(right.transform, "NameRow");
        nameRow.AddComponent<LayoutElement>().preferredHeight = 72f;
        var nameHLG = nameRow.AddComponent<HorizontalLayoutGroup>();
        nameHLG.spacing                = 18f;
        nameHLG.childAlignment         = TextAnchor.MiddleLeft;
        nameHLG.childControlHeight     = true;
        nameHLG.childControlWidth      = false;
        nameHLG.childForceExpandHeight = true;
        nameHLG.childForceExpandWidth  = false;

        var cb = GroupGO(nameRow.transform, "ColorBar");
        _detailColorBar = cb.AddComponent<Image>();
        cb.AddComponent<LayoutElement>().preferredWidth = 6f;

        var nw = GroupGO(nameRow.transform, "NameWrap");
        nw.AddComponent<LayoutElement>().flexibleWidth = 1f;
        _detailName = Lbl(nw.transform, "", 48f, C_TxtHi);
        _detailName.fontStyle = FontStyles.Bold;
        StretchRT(_detailName.GetComponent<RectTransform>());

        // Description
        _detailDescription = Lbl(right.transform, "", 17f, C_TxtMid);
        _detailDescription.enableWordWrapping = true;
        _detailDescription.alignment = TextAlignmentOptions.TopLeft;
        _detailDescription.gameObject.AddComponent<LayoutElement>().preferredHeight = 56f;

        Sep(right.transform);

        var sh = Lbl(right.transform, "STATS", 12f, C_TxtDim);
        sh.fontStyle = FontStyles.Bold;
        sh.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

        var sc = GroupGO(right.transform, "Stats");
        sc.AddComponent<LayoutElement>().preferredHeight = 200f;
        var scVLG = sc.AddComponent<VerticalLayoutGroup>();
        scVLG.spacing = 8f;
        scVLG.childControlHeight     = false;
        scVLG.childControlWidth      = true;
        scVLG.childForceExpandHeight = false;
        scVLG.childForceExpandWidth  = true;
        _statsContainer = sc.transform;

        Sep(right.transform);

        var ph = Lbl(right.transform, "PASSIVES", 12f, C_TxtDim);
        ph.fontStyle = FontStyles.Bold;
        ph.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

        var ps = MakeScrollView(right.transform, vertical: true);
        ps.GetComponent<LayoutElement>().flexibleHeight = 1f;
        _passivesContainer = ps.transform.Find("Viewport/Content");

        return panel;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Step 1 — Loadout
    // ═════════════════════════════════════════════════════════════════════════

    private GameObject BuildLoadoutStep(Transform parent)
    {
        var panel = GroupGO(parent, "LoadoutStep");
        UIHelper.ApplyImage(panel.AddComponent<Image>(), _theme?.panelBackground, Color.clear, Image.Type.Tiled);

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding  = new RectOffset(48, 48, 28, 24);
        vlg.spacing  = 16f;
        vlg.childControlHeight     = true;
        vlg.childControlWidth      = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth  = true;

        // ── Header: subtitle + counter ────────────────────────────────────
        var hdr = GroupGO(panel.transform, "Header");
        hdr.AddComponent<LayoutElement>().preferredHeight = 32f;

        var hdrHLG = hdr.AddComponent<HorizontalLayoutGroup>();
        hdrHLG.childAlignment         = TextAnchor.MiddleLeft;
        hdrHLG.spacing                = 24f;
        hdrHLG.childControlHeight     = true;
        hdrHLG.childControlWidth      = false;
        hdrHLG.childForceExpandHeight = true;
        hdrHLG.childForceExpandWidth  = false;

        var sub = Lbl(hdr.transform,
            $"Citadel & basics always included  —  select up to {maxLoadoutSize} additional buildings",
            14f, C_TxtMid);
        sub.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        _loadoutCounter = Lbl(hdr.transform, $"{_loadout.Count} / {maxLoadoutSize}", 18f, C_Blue);
        _loadoutCounter.fontStyle = FontStyles.Bold;
        _loadoutCounter.alignment = TextAlignmentOptions.Right;
        _loadoutCounter.gameObject.AddComponent<LayoutElement>().preferredWidth = 72f;

        // ── Grid ──────────────────────────────────────────────────────────
        // Panel inner width = 1920 - 2×48 = 1824px
        // 3 cols: 3W + 2×16 spacing + 2×12 padding = 3W + 56 = 1824 → W = 589px
        var scroll  = MakeScrollView(panel.transform, vertical: true);
        var scrollLE = scroll.GetComponent<LayoutElement>();
        scrollLE.preferredHeight = 600f;
        scrollLE.flexibleHeight  = 1f;
        var gridContent = scroll.transform.Find("Viewport/Content");

        DestroyImmediate(gridContent.GetComponent<VerticalLayoutGroup>());
        DestroyImmediate(gridContent.GetComponent<ContentSizeFitter>());

        var grid             = gridContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize        = new Vector2(589f, 222f);
        grid.spacing         = new Vector2(16f, 16f);
        grid.padding         = new RectOffset(12, 12, 12, 12);
        grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        gridContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _loadoutCardBgs = new Image[buildingCards?.Length ?? 0];
        if (buildingCards != null)
            for (int i = 0; i < buildingCards.Length; i++)
                if (buildingCards[i] != null)
                    BuildLoadoutCard(gridContent, i);

        return panel;
    }

    // ─── Loadout card ─────────────────────────────────────────────────────────
    //  Grid cell: 589 × 222 px
    //  Structure:  [4px top accent bar]  +  [body VLG]
    //  Body:       name | divider | description | stats | <spacer> | footer

    private void BuildLoadoutCard(Transform parent, int idx)
    {
        var card       = buildingCards[idx];
        bool isCitadel = card.isCitadel;
        bool isBasic   = card.isBasic;
        bool isFixed   = isCitadel || isBasic;
        bool isChosen  = _loadout.Contains(idx);

        Color bgColor = isCitadel ? C_CardCit
                      : isBasic   ? C_CardBasic
                      : isChosen  ? C_CardSel
                      : C_CardNorm;

        Color accentColor = isCitadel ? C_Gold
                          : isBasic   ? C_Green
                          : isChosen  ? C_Blue
                          : new Color(0.20f, 0.20f, 0.28f);

        // Root — VLG
        var go    = GroupGO(parent, $"Card{idx}");
        var bgImg = go.AddComponent<Image>();
        UIHelper.ApplyImage(bgImg, _theme?.cardBackground, bgColor, Image.Type.Tiled);
        bgImg.color = bgColor;
        _loadoutCardBgs[idx] = bgImg;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = bgImg;
        if (!isFixed)
        {
            SetBtn(btn, Color.white, new Color(1.06f, 1.06f, 1.06f), new Color(0.90f, 0.90f, 0.90f));
            btn.onClick.AddListener(() => ToggleLoadout(idx));
        }
        else btn.interactable = false;

        var rootVLG = go.AddComponent<VerticalLayoutGroup>();
        rootVLG.spacing                = 0f;
        rootVLG.childControlHeight     = true;
        rootVLG.childControlWidth      = true;
        rootVLG.childForceExpandHeight = false;
        rootVLG.childForceExpandWidth  = true;

        // ── Top accent bar (4 px) ─────────────────────────────────────────
        var topBar = GroupGO(go.transform, "TopBar");
        topBar.AddComponent<Image>().color = accentColor;
        topBar.AddComponent<LayoutElement>().preferredHeight = 4f;

        // ── Body ──────────────────────────────────────────────────────────
        var body    = GroupGO(go.transform, "Body");
        body.AddComponent<LayoutElement>().flexibleHeight = 1f;

        var bodyVLG = body.AddComponent<VerticalLayoutGroup>();
        bodyVLG.padding                = new RectOffset(20, 20, 14, 14);
        bodyVLG.spacing                = 8f;
        bodyVLG.childControlHeight     = true;
        bodyVLG.childControlWidth      = true;
        bodyVLG.childForceExpandHeight = false;
        bodyVLG.childForceExpandWidth  = true;

        // Row 1 — name + optional badge ─────────────────────────────────
        var nameRow = GroupGO(body.transform, "NameRow");
        nameRow.AddComponent<LayoutElement>().preferredHeight = 28f;
        var nrHLG = nameRow.AddComponent<HorizontalLayoutGroup>();
        nrHLG.childAlignment         = TextAnchor.MiddleLeft;
        nrHLG.spacing                = 10f;
        nrHLG.childControlHeight     = true;
        nrHLG.childControlWidth      = false;
        nrHLG.childForceExpandHeight = true;
        nrHLG.childForceExpandWidth  = false;

        Color nameCol = isCitadel ? C_Gold
                      : isBasic   ? C_Green
                      : C_TxtHi;
        var nameL = Lbl(nameRow.transform, card.displayName, 19f, nameCol);
        nameL.fontStyle = FontStyles.Bold;
        nameL.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        if (isCitadel || isBasic)
        {
            string bt   = isCitadel ? "ALWAYS EQUIPPED" : "AUTO-INCLUDED";
            Color  bcol = isCitadel ? new Color(0.30f, 0.20f, 0.02f) : new Color(0.04f, 0.18f, 0.04f);
            var bp = GroupGO(nameRow.transform, "Badge");
            bp.AddComponent<Image>().color = bcol;
            bp.AddComponent<LayoutElement>().preferredWidth = isCitadel ? 128f : 108f;
            var bpVLG = bp.AddComponent<VerticalLayoutGroup>();
            bpVLG.childAlignment         = TextAnchor.MiddleCenter;
            bpVLG.childControlHeight     = true;
            bpVLG.childControlWidth      = true;
            bpVLG.childForceExpandHeight = true;
            bpVLG.childForceExpandWidth  = true;
            var bpL = Lbl(bp.transform, bt, 9f, accentColor);
            bpL.fontStyle = FontStyles.Bold;
            bpL.alignment = TextAlignmentOptions.Center;
        }

        // Row 2 — horizontal rule ────────────────────────────────────────
        var rule = GroupGO(body.transform, "Rule");
        rule.AddComponent<Image>().color = C_Div;
        rule.AddComponent<LayoutElement>().preferredHeight = 1f;

        // Row 3 — description ────────────────────────────────────────────
        var descL = Lbl(body.transform, card.description, 13f, C_TxtMid);
        descL.enableWordWrapping = true;
        descL.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;

        // Row 4 — stats summary ──────────────────────────────────────────
        if (!string.IsNullOrEmpty(card.statsSummary))
        {
            var sl = Lbl(body.transform, card.statsSummary, 13f, C_Cyan);
            sl.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
        }
        else
        {
            var sp = GroupGO(body.transform, "StatsSp");
            sp.AddComponent<LayoutElement>().preferredHeight = 20f;
        }

        // Row 5 — flexible spacer ────────────────────────────────────────
        var spacer = GroupGO(body.transform, "Spacer");
        spacer.AddComponent<LayoutElement>().flexibleHeight = 1f;

        // Row 6 — thin footer rule ───────────────────────────────────────
        var fRule = GroupGO(body.transform, "FRule");
        fRule.AddComponent<Image>().color = C_Div;
        fRule.AddComponent<LayoutElement>().preferredHeight = 1f;

        // Row 7 — footer: cost | status ──────────────────────────────────
        var footer = GroupGO(body.transform, "Footer");
        footer.AddComponent<LayoutElement>().preferredHeight = 22f;
        var fHLG = footer.AddComponent<HorizontalLayoutGroup>();
        fHLG.childAlignment         = TextAnchor.MiddleLeft;
        fHLG.childControlHeight     = true;
        fHLG.childControlWidth      = false;
        fHLG.childForceExpandHeight = true;
        fHLG.childForceExpandWidth  = false;

        var costL = Lbl(footer.transform, $"{card.woodCost}W  ·  {card.metalCost}M",
                        13f, new Color(0.58f, 0.82f, 0.36f));
        costL.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        string statusTxt   = isCitadel || isBasic ? "" : isChosen ? "✓  SELECTED" : "+ ADD";
        Color  statusColor = isChosen
            ? new Color(0.38f, 0.90f, 0.48f)
            : new Color(0.30f, 0.30f, 0.40f);
        var sel = Lbl(footer.transform, statusTxt, 12f, statusColor);
        sel.fontStyle = isChosen ? FontStyles.Bold : FontStyles.Normal;
        sel.alignment = TextAlignmentOptions.Right;
        sel.gameObject.AddComponent<LayoutElement>().preferredWidth = 90f;
        sel.name = "SelLabel";
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

        var dl = Lbl(info.transform, lvl.description, 14f, new Color(0.46f, 0.46f, 0.54f));
        dl.enableWordWrapping = true;
        dl.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 110f);

        if (!unlocked)
        {
            var req = Lbl(info.transform, $"Reach wave {lvl.unlockAtBestWave} to unlock", 13f,
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
            var sl = Lbl(selRow.transform, "● SELECTED", 13f, C_Blue);
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
        bar.AddComponent<LayoutElement>().preferredHeight = 56f;

        var hlg = bar.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(48, 48, 12, 12);
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlHeight     = true;
        hlg.childControlWidth      = false;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = true;

        // Back button
        var backGO = GroupGO(bar.transform, "BackBtn");
        backGO.AddComponent<LayoutElement>().preferredWidth = 240f;
        var backImg = backGO.AddComponent<Image>();
        UIHelper.ApplyImage(backImg, _theme?.buttonSecondary, Color.clear);
        _backBtn = backGO.AddComponent<Button>();
        _backBtn.targetGraphic = backImg;
        _backBtn.colors = UIHelper.BtnColors(_theme?.buttonSecondary,
            Color.white, new Color(1.08f, 1.08f, 1.08f), new Color(0.88f, 0.88f, 0.88f));
        _backBtn.onClick.AddListener(OnBack);
        _backLabel = Lbl(backGO.transform, "BACK", 16f, C_TxtMid);
        _backLabel.alignment = TextAlignmentOptions.Center;
        StretchRT(_backLabel.GetComponent<RectTransform>());

        // Spacer
        var sp = GroupGO(bar.transform, "Sp");
        sp.AddComponent<LayoutElement>().flexibleWidth = 1f;

        // Next — primary action
        var nextGO = GroupGO(bar.transform, "NextBtn");
        nextGO.AddComponent<LayoutElement>().preferredWidth = 240f;
        var nextImg = nextGO.AddComponent<Image>();
        UIHelper.ApplyImage(nextImg, _theme?.buttonPrimary, new Color(0.12f, 0.46f, 0.12f));
        _nextBtn = nextGO.AddComponent<Button>();
        _nextBtn.targetGraphic = nextImg;
        SetBtn(_nextBtn, Color.white, new Color(1.10f, 1.10f, 1.10f), new Color(0.84f, 0.84f, 0.84f));
        _nextBtn.onClick.AddListener(OnNext);
        _nextLabel = Lbl(nextGO.transform, "NEXT", 16f, Color.white);
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
                                              : C_TxtDim;
        }

        _backLabel.text = _step == 0 ? "MENU" : "BACK";

        bool isLast = _step == _stepPanels.Length - 1;
        _nextLabel.text = isLast ? "START" : "NEXT";
        _nextBtn.GetComponent<Image>().color = isLast
            ? new Color(0.09f, 0.40f, 0.09f)
            : new Color(0.12f, 0.46f, 0.12f);

        if (_step == 0 && characters != null && characters.Length > 0)
            SelectCharacter(_charIndex, save: false);
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

        if (_charCardBgs != null)
            for (int i = 0; i < _charCardBgs.Length; i++)
                if (_charCardBgs[i] != null)
                    _charCardBgs[i].color = i == idx ? C_CardSel : C_CardNorm;

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
                15f, new Color(0.58f, 0.74f, 0.38f));
            StretchRT(resLbl.GetComponent<RectTransform>());
        }

        if (_passivesContainer != null)
        {
            for (int i = _passivesContainer.childCount - 1; i >= 0; i--)
                DestroyImmediate(_passivesContainer.GetChild(i).gameObject);

            bool has = def.passiveEffects != null && def.passiveEffects.Length > 0;
            if (has)
                foreach (var p in def.passiveEffects)
                    PassiveRow(_passivesContainer, p, def.color);
            else
            {
                var none = Lbl(_passivesContainer, "No passive effects.", 16f,
                               new Color(0.30f, 0.30f, 0.36f));
                none.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
            }
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
        {
            var cardT = _loadoutCardBgs[idx].transform;

            _loadoutCardBgs[idx].color = nowIn ? C_CardSel : C_CardNorm;

            var topBar = cardT.Find("TopBar")?.GetComponent<Image>();
            if (topBar != null)
                topBar.color = nowIn ? C_Blue : new Color(0.20f, 0.20f, 0.28f);

            var sel = cardT.Find("Body/Footer/SelLabel")?.GetComponent<TextMeshProUGUI>();
            if (sel != null)
            {
                sel.text      = nowIn ? "✓  SELECTED" : "+ ADD";
                sel.color     = nowIn ? new Color(0.38f, 0.90f, 0.48f) : new Color(0.30f, 0.30f, 0.40f);
                sel.fontStyle = nowIn ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        if (_loadoutCounter != null)
        {
            _loadoutCounter.text  = $"{_loadout.Count} / {maxLoadoutSize}";
            _loadoutCounter.color = _loadout.Count >= maxLoadoutSize
                ? C_Green : C_Blue;
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

        var nl = Lbl(row.transform, statName, 17f, C_TxtMid);
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

        Lbl(row.transform, $"×{multiplier:0.0}", 17f, C_TxtHi)
            .gameObject.AddComponent<LayoutElement>().preferredWidth = 44f;
        Lbl(row.transform, delta, 13f, dCol)
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

        var n = Lbl(col.transform, passive.effectName ?? "", 16f, new Color(0.88f, 0.84f, 0.38f));
        n.fontStyle = FontStyles.Bold;
        var d = Lbl(col.transform, passive.effectDescription ?? "", 13f, C_TxtMid);
        d.enableWordWrapping = true;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════════════

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
