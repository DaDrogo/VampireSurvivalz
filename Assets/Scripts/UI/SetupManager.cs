using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 3-step pre-game setup wizard: Character → Loadout → Level → START GAME.
/// Place on the manager object in SetupScene.
/// PersistentDataManager / AudioManager / SceneTransitionManager survive from MainMenuScene.
/// </summary>
[DefaultExecutionOrder(1)]
public class SetupManager : MonoBehaviour
{
    public static SetupManager Instance { get; private set; }

    [Header("Step 1 — Character")]
    [SerializeField] private CharacterDefinition[] characters;

    [Header("Step 2 — Loadout")]
    [SerializeField] private BuildingCard[] buildingCards;
    [SerializeField] private int maxLoadoutSize = 5;

    [Header("Step 3 — Level")]
    [SerializeField] private LevelDefinition[] levels;

    // ── Step state ────────────────────────────────────────────────────────────

    private int                  _step;
    private GameObject[]         _stepPanels;
    private Image[]              _stepPillBgs;
    private TextMeshProUGUI[]    _stepPillLabels;
    private Button               _backBtn;
    private Button               _nextBtn;
    private TextMeshProUGUI      _nextLabel;

    // ── Character step ────────────────────────────────────────────────────────

    private int                  _charIndex;
    private CharacterDefinition  _selectedChar;
    private Image[]              _charCardBgs;
    private TextMeshProUGUI      _detailName;
    private Image                _detailColorBar;
    private TextMeshProUGUI      _detailDescription;
    private Transform            _statsContainer;
    private Transform            _passivesContainer;

    // ── Loadout step ──────────────────────────────────────────────────────────

    private List<int>            _loadout = new List<int>();
    private Image[]              _loadoutCardBgs;
    private TextMeshProUGUI      _loadoutCounter;

    // ── Level step ────────────────────────────────────────────────────────────

    private int                  _levelIndex;
    private Image[]              _levelCardBgs;

    // ── Style ─────────────────────────────────────────────────────────────────

    private static readonly Color BgColor      = new Color(0.05f, 0.05f, 0.08f);
    private static readonly Color PanelColor   = new Color(0.09f, 0.09f, 0.12f);
    private static readonly Color CardNormal   = new Color(0.11f, 0.11f, 0.15f);
    private static readonly Color CardSelected = new Color(0.15f, 0.35f, 0.70f);
    private static readonly Color CardLocked   = new Color(0.07f, 0.07f, 0.09f);
    private static readonly Color DivColor     = new Color(0.20f, 0.20f, 0.26f);
    private static readonly Color AccentRed    = new Color(0.85f, 0.14f, 0.14f);
    private static readonly Color StepActive   = new Color(0.18f, 0.42f, 0.82f);
    private static readonly Color StepDone     = new Color(0.12f, 0.28f, 0.50f);
    private static readonly Color StepFuture   = new Color(0.10f, 0.10f, 0.14f);

    private TMP_FontAsset _font;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        var pdm     = PersistentDataManager.Instance;
        _charIndex  = Mathf.Clamp(pdm?.SelectedCharacterIndex ?? 0, 0,
                       Mathf.Max(0, (characters?.Length  ?? 1) - 1));
        _levelIndex = Mathf.Clamp(pdm?.SelectedLevelIndex     ?? 0, 0,
                       Mathf.Max(0, (levels?.Length      ?? 1) - 1));

        _loadout.Clear();
        if (pdm?.SelectedBuildingIndices != null)
            foreach (int i in pdm.SelectedBuildingIndices)
                if (buildingCards == null || i < buildingCards.Length)
                    _loadout.Add(i);

        if (_loadout.Count == 0 && buildingCards != null)
            for (int i = 0; i < Mathf.Min(maxLoadoutSize, buildingCards.Length); i++)
                _loadout.Add(i);

        EnsureEventSystem();
        BuildUI();
        GoToStep(0);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  UI construction
    // ═════════════════════════════════════════════════════════════════════════

    private void BuildUI()
    {
        var canvasGO              = new GameObject("SetupCanvas");
        var canvas                = canvasGO.AddComponent<Canvas>();
        canvas.renderMode         = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder       = 0;
        var scaler                = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        var root                  = StretchGO(canvasGO.transform, "Root");
        root.AddComponent<Image>().color = BgColor;
        var rootVLG               = root.AddComponent<VerticalLayoutGroup>();
        rootVLG.childControlHeight     = true;
        rootVLG.childControlWidth      = true;
        rootVLG.childForceExpandHeight = true;
        rootVLG.childForceExpandWidth  = true;

        BuildStepBar(root.transform);
        BuildContent(root.transform);
        BuildNavBar(root.transform);
    }

    // ── Step bar ──────────────────────────────────────────────────────────────

    private void BuildStepBar(Transform parent)
    {
        var bar = GroupGO(parent, "StepBar");
        bar.AddComponent<Image>().color = new Color(0.07f, 0.07f, 0.10f);
        bar.AddComponent<LayoutElement>().preferredHeight = 64f;

        var hlg              = bar.AddComponent<HorizontalLayoutGroup>();
        hlg.padding          = new RectOffset(60, 60, 0, 0);
        hlg.childAlignment   = TextAnchor.MiddleCenter;
        hlg.childControlHeight     = true;
        hlg.childControlWidth      = true;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = false;

        string[] names = { "1.  CHARACTER", "2.  LOADOUT", "3.  LEVEL" };
        _stepPillBgs   = new Image[3];
        _stepPillLabels = new TextMeshProUGUI[3];

        for (int i = 0; i < 3; i++)
        {
            if (i > 0)
            {
                var line = GroupGO(bar.transform, "Line");
                line.AddComponent<Image>().color = DivColor;
                line.AddComponent<LayoutElement>().preferredWidth = 100f;
            }

            var pill = GroupGO(bar.transform, $"Pill{i}");
            _stepPillBgs[i] = pill.AddComponent<Image>();
            pill.AddComponent<LayoutElement>().preferredWidth = 240f;

            var pillVLG         = pill.AddComponent<VerticalLayoutGroup>();
            pillVLG.childAlignment      = TextAnchor.MiddleCenter;
            pillVLG.childControlHeight  = true;
            pillVLG.childControlWidth   = true;
            pillVLG.childForceExpandHeight = true;
            pillVLG.childForceExpandWidth  = true;

            _stepPillLabels[i] = Label(pill.transform, names[i], 17f, new Color(0.45f, 0.45f, 0.50f));
            _stepPillLabels[i].fontStyle = FontStyles.Bold;
            _stepPillLabels[i].alignment = TextAlignmentOptions.Center;
        }
    }

    // ── Content (step panels stacked) ─────────────────────────────────────────

    private void BuildContent(Transform parent)
    {
        var area = GroupGO(parent, "Content");
        area.AddComponent<LayoutElement>().flexibleHeight = 1f;

        _stepPanels    = new GameObject[3];
        _stepPanels[0] = BuildCharacterStep(area.transform);
        _stepPanels[1] = BuildLoadoutStep(area.transform);
        _stepPanels[2] = BuildLevelStep(area.transform);

        foreach (var p in _stepPanels)
            StretchRT(p.GetComponent<RectTransform>());
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Step 0 — Character
    // ═════════════════════════════════════════════════════════════════════════

    private GameObject BuildCharacterStep(Transform parent)
    {
        var panel = GroupGO(parent, "CharacterStep");
        panel.AddComponent<Image>().color = Color.clear;
        var hlg = panel.AddComponent<HorizontalLayoutGroup>();
        hlg.childControlHeight     = true;
        hlg.childControlWidth      = true;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = true;

        // Left — card list
        var left = GroupGO(panel.transform, "CharList");
        left.AddComponent<Image>().color = PanelColor;
        left.AddComponent<LayoutElement>().preferredWidth = 480f;
        var leftVLG = left.AddComponent<VerticalLayoutGroup>();
        leftVLG.padding  = new RectOffset(24, 24, 28, 24);
        leftVLG.spacing  = 16f;
        leftVLG.childControlHeight     = true;
        leftVLG.childControlWidth      = true;
        leftVLG.childForceExpandHeight = true;
        leftVLG.childForceExpandWidth  = true;

        var title    = Label(left.transform, "SELECT\nCHARACTER", 34f, AccentRed);
        title.fontStyle  = FontStyles.Bold;
        title.alignment  = TextAlignmentOptions.Center;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 88f;

        var scroll   = ScrollView(left.transform, vertical: true);
        scroll.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var content  = scroll.transform.Find("Viewport/Content");

        _charCardBgs = new Image[characters?.Length ?? 0];
        if (characters != null)
        {
            for (int i = 0; i < characters.Length; i++)
            {
                if (characters[i] == null) continue;
                var def = characters[i];
                var idx = i;

                var card = GroupGO(content, $"CharCard{i}");
                card.AddComponent<LayoutElement>().preferredHeight = 84f;
                var cardImg = card.AddComponent<Image>();
                cardImg.color = CardNormal;
                _charCardBgs[i] = cardImg;

                var btn = card.AddComponent<Button>();
                btn.targetGraphic = cardImg;
                SetBtn(btn, Color.white, new Color(1.15f, 1.15f, 1.15f), new Color(0.85f, 0.85f, 0.85f));
                btn.onClick.AddListener(() => SelectCharacter(idx));

                var cardHLG = card.AddComponent<HorizontalLayoutGroup>();
                cardHLG.padding  = new RectOffset(12, 12, 12, 12);
                cardHLG.spacing  = 12f;
                cardHLG.childAlignment      = TextAnchor.MiddleLeft;
                cardHLG.childControlHeight  = true;
                cardHLG.childControlWidth   = false;
                cardHLG.childForceExpandHeight = true;
                cardHLG.childForceExpandWidth  = false;

                var sw = GroupGO(card.transform, "Swatch");
                sw.AddComponent<Image>().color = def.color;
                sw.AddComponent<LayoutElement>().preferredWidth = 10f;

                var nameCol = GroupGO(card.transform, "Col");
                nameCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
                var colVLG = nameCol.AddComponent<VerticalLayoutGroup>();
                colVLG.childControlHeight     = true;
                colVLG.childControlWidth      = true;
                colVLG.childForceExpandHeight = true;
                colVLG.childForceExpandWidth  = true;

                var n = Label(nameCol.transform, def.characterName, 22f, Color.white);
                n.fontStyle = FontStyles.Bold;
                Label(nameCol.transform,
                    $"HP ×{def.healthMultiplier:0.0}  SPD ×{def.speedMultiplier:0.0}  DMG ×{def.damageMultiplier:0.0}",
                    13f, new Color(0.50f, 0.50f, 0.50f));
            }
        }

        // Divider
        var div = GroupGO(panel.transform, "Div");
        div.AddComponent<Image>().color = DivColor;
        div.AddComponent<LayoutElement>().preferredWidth = 2f;

        // Right — detail
        var right = GroupGO(panel.transform, "CharDetail");
        right.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var rightVLG = right.AddComponent<VerticalLayoutGroup>();
        rightVLG.padding = new RectOffset(52, 52, 32, 32);
        rightVLG.spacing = 16f;
        rightVLG.childControlHeight     = false;
        rightVLG.childControlWidth      = true;
        rightVLG.childForceExpandHeight = false;
        rightVLG.childForceExpandWidth  = true;

        // Name row
        var nameRow = GroupGO(right.transform, "NameRow");
        nameRow.AddComponent<LayoutElement>().preferredHeight = 84f;
        var nameHLG = nameRow.AddComponent<HorizontalLayoutGroup>();
        nameHLG.spacing = 20f;
        nameHLG.childAlignment      = TextAnchor.MiddleLeft;
        nameHLG.childControlHeight  = true;
        nameHLG.childControlWidth   = false;
        nameHLG.childForceExpandHeight = true;
        nameHLG.childForceExpandWidth  = false;

        var colorBarGO = GroupGO(nameRow.transform, "ColorBar");
        _detailColorBar = colorBarGO.AddComponent<Image>();
        colorBarGO.AddComponent<LayoutElement>().preferredWidth = 8f;

        var nameWrap = GroupGO(nameRow.transform, "NameWrap");
        nameWrap.AddComponent<LayoutElement>().flexibleWidth = 1f;
        _detailName = Label(nameWrap.transform, "", 54f, Color.white);
        _detailName.fontStyle = FontStyles.Bold;
        StretchRT(_detailName.GetComponent<RectTransform>());

        // Description
        _detailDescription = Label(right.transform, "", 20f, new Color(0.66f, 0.66f, 0.66f));
        _detailDescription.enableWordWrapping = true;
        _detailDescription.alignment = TextAlignmentOptions.TopLeft;
        _detailDescription.gameObject.AddComponent<LayoutElement>().preferredHeight = 62f;

        Separator(right.transform);

        var sh = Label(right.transform, "STATS", 14f, new Color(0.46f, 0.46f, 0.52f));
        sh.fontStyle = FontStyles.Bold;
        sh.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

        var sc = GroupGO(right.transform, "StatsContainer");
        sc.AddComponent<LayoutElement>().preferredHeight = 215f;
        var scVLG = sc.AddComponent<VerticalLayoutGroup>();
        scVLG.spacing = 10f;
        scVLG.childControlHeight     = false;
        scVLG.childControlWidth      = true;
        scVLG.childForceExpandHeight = false;
        scVLG.childForceExpandWidth  = true;
        _statsContainer = sc.transform;

        Separator(right.transform);

        var ph = Label(right.transform, "PASSIVE EFFECTS", 14f, new Color(0.46f, 0.46f, 0.52f));
        ph.fontStyle = FontStyles.Bold;
        ph.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

        var passScroll = ScrollView(right.transform, vertical: true);
        passScroll.GetComponent<LayoutElement>().flexibleHeight = 1f;
        _passivesContainer = passScroll.transform.Find("Viewport/Content");

        return panel;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Step 1 — Loadout
    // ═════════════════════════════════════════════════════════════════════════

    private GameObject BuildLoadoutStep(Transform parent)
    {
        var panel = GroupGO(parent, "LoadoutStep");
        panel.AddComponent<Image>().color = Color.clear;
        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding  = new RectOffset(56, 56, 36, 36);
        vlg.spacing  = 20f;
        vlg.childControlHeight     = true;
        vlg.childControlWidth      = true;
        vlg.childForceExpandHeight = true;
        vlg.childForceExpandWidth  = true;

        // Header row
        var hdr = GroupGO(panel.transform, "Header");
        hdr.AddComponent<LayoutElement>().preferredHeight = 80f;
        var hdrHLG = hdr.AddComponent<HorizontalLayoutGroup>();
        hdrHLG.childAlignment      = TextAnchor.MiddleLeft;
        hdrHLG.childControlHeight  = true;
        hdrHLG.childControlWidth   = true;
        hdrHLG.childForceExpandHeight = true;
        hdrHLG.childForceExpandWidth  = true;

        var titleCol = GroupGO(hdr.transform, "TitleCol");
        titleCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var titleColVLG = titleCol.AddComponent<VerticalLayoutGroup>();
        titleColVLG.childControlHeight     = true;
        titleColVLG.childControlWidth      = true;
        titleColVLG.childForceExpandHeight = true;
        titleColVLG.childForceExpandWidth  = true;

        var ttl = Label(titleCol.transform, "CHOOSE YOUR LOADOUT", 36f, Color.white);
        ttl.fontStyle = FontStyles.Bold;
        Label(titleCol.transform, $"Select up to {maxLoadoutSize} buildings for your hotbar", 18f,
              new Color(0.52f, 0.52f, 0.58f));

        _loadoutCounter = Label(hdr.transform, $"{_loadout.Count} / {maxLoadoutSize}", 30f,
                                new Color(0.45f, 0.85f, 1f));
        _loadoutCounter.fontStyle = FontStyles.Bold;
        _loadoutCounter.alignment = TextAlignmentOptions.Right;
        _loadoutCounter.gameObject.AddComponent<LayoutElement>().preferredWidth = 130f;

        // Grid scroll
        var scroll  = ScrollView(panel.transform, vertical: true);
        scroll.GetComponent<LayoutElement>().flexibleHeight = 1f;
        var content = scroll.transform.Find("Viewport/Content");

        DestroyImmediate(content.GetComponent<VerticalLayoutGroup>());
        DestroyImmediate(content.GetComponent<ContentSizeFitter>());

        var grid              = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize         = new Vector2(310f, 172f);
        grid.spacing          = new Vector2(14f, 14f);
        grid.padding          = new RectOffset(8, 8, 8, 8);
        grid.constraint       = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount  = 4;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _loadoutCardBgs = new Image[buildingCards?.Length ?? 0];
        if (buildingCards != null)
            for (int i = 0; i < buildingCards.Length; i++)
                if (buildingCards[i] != null)
                    BuildLoadoutCard(content, i);

        return panel;
    }

    private void BuildLoadoutCard(Transform parent, int idx)
    {
        var card = buildingCards[idx];
        var go   = GroupGO(parent, $"LoadoutCard{idx}");

        var img  = go.AddComponent<Image>();
        img.color = _loadout.Contains(idx) ? CardSelected : CardNormal;
        _loadoutCardBgs[idx] = img;

        var btn  = go.AddComponent<Button>();
        btn.targetGraphic = img;
        SetBtn(btn, Color.white, new Color(1.15f, 1.15f, 1.15f), new Color(0.85f, 0.85f, 0.85f));
        btn.onClick.AddListener(() => ToggleLoadout(idx));

        var vlg  = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding  = new RectOffset(14, 14, 12, 12);
        vlg.spacing  = 6f;
        vlg.childAlignment     = TextAnchor.UpperLeft;
        vlg.childControlHeight = false;
        vlg.childControlWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth  = true;

        // Name + swatch row
        var nr   = GroupGO(go.transform, "NameRow");
        nr.AddComponent<LayoutElement>().preferredHeight = 26f;
        var nrHLG = nr.AddComponent<HorizontalLayoutGroup>();
        nrHLG.spacing = 10f;
        nrHLG.childAlignment      = TextAnchor.MiddleLeft;
        nrHLG.childControlHeight  = true;
        nrHLG.childControlWidth   = false;
        nrHLG.childForceExpandHeight = true;
        nrHLG.childForceExpandWidth  = false;

        var sw = GroupGO(nr.transform, "Swatch");
        sw.AddComponent<Image>().color = card.color;
        sw.AddComponent<LayoutElement>().preferredWidth = 10f;

        var nl = Label(nr.transform, card.displayName, 19f, Color.white);
        nl.fontStyle = FontStyles.Bold;
        nl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        // Description
        var dl = Label(go.transform, card.description, 13f, new Color(0.56f, 0.56f, 0.56f));
        dl.enableWordWrapping = true;
        dl.gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;

        // Cost
        Label(go.transform, $"{card.woodCost}W / {card.metalCost}M", 14f,
              new Color(0.68f, 0.86f, 0.42f)).gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

        // Stats
        if (!string.IsNullOrEmpty(card.statsSummary))
            Label(go.transform, card.statsSummary, 12f,
                  new Color(0.42f, 0.78f, 1f)).gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

        // Selected indicator
        var sel = Label(go.transform, _loadout.Contains(idx) ? "✓  IN LOADOUT" : "", 13f,
                        new Color(0.50f, 0.90f, 0.50f));
        sel.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
        // Tag the label so ToggleLoadout can find it
        sel.name = "SelLabel";
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Step 2 — Level
    // ═════════════════════════════════════════════════════════════════════════

    private GameObject BuildLevelStep(Transform parent)
    {
        var panel = GroupGO(parent, "LevelStep");
        panel.AddComponent<Image>().color = Color.clear;
        var vlg   = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(56, 56, 36, 36);
        vlg.spacing = 20f;
        vlg.childControlHeight     = true;
        vlg.childControlWidth      = true;
        vlg.childForceExpandHeight = true;
        vlg.childForceExpandWidth  = true;

        var ttl   = Label(panel.transform, "SELECT LEVEL", 40f, Color.white);
        ttl.fontStyle = FontStyles.Bold;
        ttl.alignment = TextAlignmentOptions.Left;
        ttl.gameObject.AddComponent<LayoutElement>().preferredHeight = 56f;

        var scroll  = ScrollView(panel.transform, vertical: false);
        scroll.GetComponent<LayoutElement>().flexibleHeight = 1f;
        var content = scroll.transform.Find("Viewport/Content");

        DestroyImmediate(content.GetComponent<VerticalLayoutGroup>());
        DestroyImmediate(content.GetComponent<ContentSizeFitter>());

        var hlg   = content.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 24f;
        hlg.padding = new RectOffset(16, 16, 16, 16);
        hlg.childAlignment      = TextAnchor.MiddleLeft;
        hlg.childControlHeight  = false;
        hlg.childControlWidth   = false;
        hlg.childForceExpandHeight = false;
        hlg.childForceExpandWidth  = false;
        content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        _levelCardBgs = new Image[levels?.Length ?? 0];
        int best = PersistentDataManager.Instance?.BestWave ?? 0;

        if (levels != null)
            for (int i = 0; i < levels.Length; i++)
                if (levels[i] != null)
                {
                    bool unlocked = levels[i].isUnlockedByDefault || best >= levels[i].unlockAtBestWave;
                    BuildLevelCard(content, i, unlocked);
                }

        return panel;
    }

    private void BuildLevelCard(Transform parent, int idx, bool unlocked)
    {
        var lvl  = levels[idx];

        var card = GroupGO(parent, $"LevelCard{idx}");
        var cardRT = card.GetComponent<RectTransform>();
        cardRT.sizeDelta = new Vector2(290f, 400f);

        var img  = card.AddComponent<Image>();
        img.color = !unlocked ? CardLocked : idx == _levelIndex ? CardSelected : CardNormal;
        _levelCardBgs[idx] = img;

        var btn  = card.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.interactable  = unlocked;
        if (unlocked)
        {
            SetBtn(btn, Color.white, new Color(1.15f, 1.15f, 1.15f), new Color(0.85f, 0.85f, 0.85f));
            btn.onClick.AddListener(() => SelectLevel(idx));
        }

        var vlg  = card.AddComponent<VerticalLayoutGroup>();
        vlg.padding  = new RectOffset(14, 14, 14, 14);
        vlg.spacing  = 10f;
        vlg.childControlWidth      = true;
        vlg.childForceExpandWidth  = true;

        // Preview swatch
        var sw   = GroupGO(card.transform, "Swatch");
        sw.AddComponent<Image>().color = unlocked ? lvl.previewColor : new Color(0.11f, 0.11f, 0.13f);
        sw.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 150f);

        if (!unlocked)
        {
            var lk = Label(sw.transform, "LOCKED", 22f, new Color(0.38f, 0.38f, 0.38f));
            StretchRT(lk.GetComponent<RectTransform>());
        }

        var nl = Label(card.transform, lvl.levelName, 24f, unlocked ? Color.white : new Color(0.33f, 0.33f, 0.33f));
        nl.fontStyle = FontStyles.Bold;
        nl.gameObject.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 34f);

        var dl = Label(card.transform, lvl.description, 15f, new Color(0.52f, 0.52f, 0.52f));
        dl.enableWordWrapping = true;
        dl.gameObject.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 90f);

        if (!unlocked)
        {
            var req = Label(card.transform, $"Reach wave {lvl.unlockAtBestWave}", 14f,
                            new Color(0.80f, 0.64f, 0.16f));
            req.gameObject.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 24f);
        }
        else if (idx == _levelIndex)
        {
            var selLbl = Label(card.transform, "● SELECTED", 15f, new Color(0.42f, 0.82f, 1f));
            selLbl.gameObject.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 24f);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Nav bar
    // ═════════════════════════════════════════════════════════════════════════

    private void BuildNavBar(Transform parent)
    {
        var bar = GroupGO(parent, "NavBar");
        bar.AddComponent<Image>().color = new Color(0.07f, 0.07f, 0.10f);
        bar.AddComponent<LayoutElement>().preferredHeight = 80f;

        var hlg = bar.AddComponent<HorizontalLayoutGroup>();
        hlg.padding  = new RectOffset(48, 48, 12, 12);
        hlg.childAlignment      = TextAnchor.MiddleCenter;
        hlg.childControlHeight  = true;
        hlg.childControlWidth   = false;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = false;

        // Back
        var backGO = GroupGO(bar.transform, "BackBtn");
        backGO.AddComponent<LayoutElement>().preferredWidth = 210f;
        var backImg = backGO.AddComponent<Image>();
        backImg.color = new Color(0.16f, 0.16f, 0.22f);
        _backBtn = backGO.AddComponent<Button>();
        _backBtn.targetGraphic = backImg;
        SetBtn(_backBtn, Color.white, new Color(1.15f, 1.15f, 1.15f), new Color(0.8f, 0.8f, 0.8f));
        _backBtn.onClick.AddListener(OnBack);
        var bl = Label(backGO.transform, "← BACK", 22f, Color.white);
        StretchRT(bl.GetComponent<RectTransform>());

        // Spacer
        var sp = GroupGO(bar.transform, "Spacer");
        sp.AddComponent<LayoutElement>().flexibleWidth = 1f;

        // Next / Start
        var nextGO = GroupGO(bar.transform, "NextBtn");
        nextGO.AddComponent<LayoutElement>().preferredWidth = 240f;
        var nextImg = nextGO.AddComponent<Image>();
        nextImg.color = new Color(0.15f, 0.52f, 0.15f);
        _nextBtn = nextGO.AddComponent<Button>();
        _nextBtn.targetGraphic = nextImg;
        SetBtn(_nextBtn, Color.white, new Color(1.15f, 1.15f, 1.15f), new Color(0.8f, 0.8f, 0.8f));
        _nextBtn.onClick.AddListener(OnNext);
        _nextLabel = Label(nextGO.transform, "NEXT →", 22f, Color.white);
        _nextLabel.fontStyle = FontStyles.Bold;
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

        for (int i = 0; i < _stepPillBgs.Length; i++)
        {
            bool active = i == _step, done = i < _step;
            _stepPillBgs[i].color    = active ? StepActive : done ? StepDone : StepFuture;
            _stepPillLabels[i].color = active ? Color.white : done
                                        ? new Color(0.58f, 0.74f, 1f) : new Color(0.38f, 0.38f, 0.42f);
        }

        _backBtn.gameObject.SetActive(_step > 0);

        bool isLast = _step == _stepPanels.Length - 1;
        _nextLabel.text = isLast ? "START GAME" : "NEXT  →";
        _nextBtn.GetComponent<Image>().color = isLast
            ? new Color(0.14f, 0.50f, 0.14f)
            : new Color(0.14f, 0.34f, 0.64f);

        // Refresh character detail on entering step 0
        if (_step == 0 && characters != null && characters.Length > 0)
            SelectCharacter(_charIndex, save: false);
    }

    private void OnBack() => GoToStep(_step - 1);
    private void OnNext()
    {
        if (_step < _stepPanels.Length - 1) GoToStep(_step + 1);
        else StartGame();
    }

    private void StartGame()
    {
        PersistentDataManager.Instance?.SetBuildingLoadout(_loadout.ToArray());
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
                    _charCardBgs[i].color = i == idx ? CardSelected : CardNormal;

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

        // Stat rows
        if (_statsContainer != null)
        {
            for (int i = _statsContainer.childCount - 1; i >= 0; i--)
                DestroyImmediate(_statsContainer.GetChild(i).gameObject);

            StatRow(_statsContainer, "Health", def.color, def.healthMultiplier);
            StatRow(_statsContainer, "Speed",  def.color, def.speedMultiplier);
            StatRow(_statsContainer, "Damage", def.color, def.damageMultiplier);

            var res = GroupGO(_statsContainer, "Resources");
            res.AddComponent<LayoutElement>().preferredHeight = 26f;
            var resLbl = Label(res.transform,
                $"Starting:   {def.startingWood} Wood   |   {def.startingMetal} Metal",
                16f, new Color(0.60f, 0.74f, 0.40f));
            StretchRT(resLbl.GetComponent<RectTransform>());
        }

        // Passives
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
                var none = Label(_passivesContainer, "No passive effects.", 18f,
                                 new Color(0.34f, 0.34f, 0.34f));
                none.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Loadout logic
    // ═════════════════════════════════════════════════════════════════════════

    private void ToggleLoadout(int idx)
    {
        bool isSelected = _loadout.Contains(idx);
        if (!isSelected && _loadout.Count >= maxLoadoutSize) return;

        if (isSelected) _loadout.Remove(idx);
        else            _loadout.Add(idx);

        // Update card bg
        if (_loadoutCardBgs != null && idx < _loadoutCardBgs.Length && _loadoutCardBgs[idx] != null)
            _loadoutCardBgs[idx].color = _loadout.Contains(idx) ? CardSelected : CardNormal;

        // Update "✓ IN LOADOUT" label — find it by name on the card's VLG children
        if (_loadoutCardBgs != null && idx < _loadoutCardBgs.Length && _loadoutCardBgs[idx] != null)
        {
            var cardT = _loadoutCardBgs[idx].transform;
            var selLbl = cardT.Find("SelLabel")?.GetComponent<TextMeshProUGUI>();
            if (selLbl != null) selLbl.text = _loadout.Contains(idx) ? "✓  IN LOADOUT" : "";
        }

        // Update counter
        if (_loadoutCounter != null)
        {
            _loadoutCounter.text  = $"{_loadout.Count} / {maxLoadoutSize}";
            _loadoutCounter.color = _loadout.Count >= maxLoadoutSize
                ? new Color(0.42f, 0.92f, 0.42f)
                : new Color(0.45f, 0.85f, 1f);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Level logic
    // ═════════════════════════════════════════════════════════════════════════

    private void SelectLevel(int idx)
    {
        if (levels == null || idx < 0 || idx >= levels.Length) return;
        _levelIndex = idx;
        int best    = PersistentDataManager.Instance?.BestWave ?? 0;

        if (_levelCardBgs != null)
            for (int i = 0; i < _levelCardBgs.Length; i++)
            {
                if (_levelCardBgs[i] == null) continue;
                bool unlocked = levels[i].isUnlockedByDefault || best >= levels[i].unlockAtBestWave;
                _levelCardBgs[i].color = i == idx ? CardSelected : unlocked ? CardNormal : CardLocked;
            }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Row builders
    // ═════════════════════════════════════════════════════════════════════════

    private void StatRow(Transform parent, string statName, Color barColor, float multiplier)
    {
        float  fillPct = Mathf.Clamp01(multiplier / 2f);
        string delta   = multiplier > 1.005f ? $"+{(multiplier-1f)*100f:0}%"
                       : multiplier < 0.995f ? $"-{(1f-multiplier)*100f:0}%"
                       : "base";
        Color  dCol    = multiplier > 1.005f ? new Color(0.32f, 0.88f, 0.32f)
                       : multiplier < 0.995f ? new Color(0.88f, 0.32f, 0.32f)
                       : new Color(0.48f, 0.48f, 0.48f);

        var row = GroupGO(parent, statName + "Row");
        row.AddComponent<LayoutElement>().preferredHeight = 34f;

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment      = TextAnchor.MiddleLeft;
        hlg.spacing             = 14f;
        hlg.childControlHeight  = true;
        hlg.childControlWidth   = false;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = false;

        var nl = Label(row.transform, statName, 18f, new Color(0.65f, 0.65f, 0.65f));
        nl.alignment = TextAlignmentOptions.Right;
        nl.gameObject.AddComponent<LayoutElement>().preferredWidth = 90f;

        // Bar
        var barBG = GroupGO(row.transform, "BarBG");
        barBG.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.17f);
        barBG.AddComponent<LayoutElement>().preferredWidth = 320f;

        var fill    = GroupGO(barBG.transform, "Fill");
        fill.AddComponent<Image>().color = barColor;
        var fillRT  = fill.GetComponent<RectTransform>();
        fillRT.anchorMin = new Vector2(0f,      0.15f);
        fillRT.anchorMax = new Vector2(fillPct, 0.85f);
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;

        Label(row.transform, $"×{multiplier:0.0}", 18f, Color.white)
            .gameObject.AddComponent<LayoutElement>().preferredWidth = 48f;
        Label(row.transform, delta, 14f, dCol)
            .gameObject.AddComponent<LayoutElement>().preferredWidth = 58f;
    }

    private void PassiveRow(Transform parent, PassiveEffect passive, Color accent)
    {
        if (passive == null) return;

        var row = GroupGO(parent, "Passive");
        row.AddComponent<Image>().color = new Color(0.10f, 0.10f, 0.14f);
        var le  = row.AddComponent<LayoutElement>();
        le.preferredHeight = 74f;
        le.flexibleHeight  = 0f;

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding  = new RectOffset(12, 12, 10, 10);
        hlg.spacing  = 12f;
        hlg.childAlignment      = TextAnchor.MiddleLeft;
        hlg.childControlHeight  = true;
        hlg.childControlWidth   = false;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = false;

        var strip = GroupGO(row.transform, "Strip");
        strip.AddComponent<Image>().color = accent;
        strip.AddComponent<LayoutElement>().preferredWidth = 4f;

        var col = GroupGO(row.transform, "Col");
        col.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var colVLG = col.AddComponent<VerticalLayoutGroup>();
        colVLG.spacing = 3f;
        colVLG.childControlHeight     = true;
        colVLG.childControlWidth      = true;
        colVLG.childForceExpandHeight = true;
        colVLG.childForceExpandWidth  = true;

        var n = Label(col.transform, passive.effectName ?? "", 17f, new Color(0.88f, 0.84f, 0.38f));
        n.fontStyle = FontStyles.Bold;
        var d = Label(col.transform, passive.effectDescription ?? "", 14f, new Color(0.66f, 0.66f, 0.66f));
        d.enableWordWrapping = true;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════════════

    private GameObject ScrollView(Transform parent, bool vertical)
    {
        var sv  = GroupGO(parent, "ScrollView");
        sv.AddComponent<LayoutElement>();

        var sr  = sv.AddComponent<ScrollRect>();
        sr.horizontal        = !vertical;
        sr.vertical          = vertical;
        sr.scrollSensitivity = 30f;

        var vp   = GroupGO(sv.transform, "Viewport");
        var vpRT = vp.GetComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero;
        vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = Vector2.zero;
        vpRT.offsetMax = Vector2.zero;
        vp.AddComponent<RectMask2D>();
        sr.viewport = vpRT;

        var content = GroupGO(vp.transform, "Content");
        var cRT     = content.GetComponent<RectTransform>();
        if (vertical) { cRT.anchorMin = new Vector2(0,1); cRT.anchorMax = new Vector2(1,1); cRT.pivot = new Vector2(0.5f,1); }
        else          { cRT.anchorMin = new Vector2(0,0); cRT.anchorMax = new Vector2(0,1); cRT.pivot = new Vector2(0,0.5f); }
        cRT.offsetMin = Vector2.zero;
        cRT.offsetMax = Vector2.zero;
        sr.content = cRT;

        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8f;
        vlg.childControlHeight     = false;
        vlg.childControlWidth      = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth  = true;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

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

    private void Separator(Transform parent)
    {
        var s = GroupGO(parent, "Sep");
        s.AddComponent<Image>().color = DivColor;
        s.AddComponent<LayoutElement>().preferredHeight = 1f;
    }

    private static void SetBtn(Button btn, Color normal, Color highlight, Color pressed)
    {
        var cb = btn.colors;
        cb.normalColor = normal; cb.highlightedColor = highlight; cb.pressedColor = pressed;
        btn.colors = cb;
    }

    private TextMeshProUGUI Label(Transform parent, string text, float size, Color color)
    {
        var go  = new GameObject(text.Length < 20 ? text : "Label");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var tmp              = go.AddComponent<TextMeshProUGUI>();
        tmp.text             = text;
        tmp.fontSize         = size;
        tmp.color            = color;
        tmp.alignment        = TextAlignmentOptions.Left;
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
