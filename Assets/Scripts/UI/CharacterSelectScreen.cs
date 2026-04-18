using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen character selection overlay shown in SampleScene before the game starts.
///
/// Place on any GameObject in SampleScene and assign the <see cref="characters"/> array.
/// GameManager will NOT auto-start while this screen is active, regardless of its
/// autoStartOnSceneLoad setting.
/// </summary>
[DefaultExecutionOrder(1)]  // Awake before GameManager (order 5) so Instance is set first
public class CharacterSelectScreen : MonoBehaviour
{
    public static CharacterSelectScreen Instance { get; private set; }

    [SerializeField] private CharacterDefinition[] characters;

    // ── State ─────────────────────────────────────────────────────────────────

    private CharacterDefinition _selected;
    private int                 _selectedIndex;
    private Image[]             _cardBgs;

    // ── UI refs ───────────────────────────────────────────────────────────────

    private GameObject          _canvasGO;
    private TextMeshProUGUI     _detailName;
    private Image               _detailColorBar;
    private TextMeshProUGUI     _detailDescription;
    private Transform           _statsContainer;
    private Transform           _passivesContainer;
    private TMP_FontAsset       _font;

    // ── Colors ────────────────────────────────────────────────────────────────

    private static readonly Color CardNormal   = new Color(0.10f, 0.10f, 0.14f);
    private static readonly Color CardSelected = new Color(0.15f, 0.35f, 0.70f);
    private static readonly Color BgColor      = new Color(0.05f, 0.05f, 0.08f, 0.97f);
    private static readonly Color PanelColor   = new Color(0.08f, 0.08f, 0.12f, 1f);
    private static readonly Color DividerColor = new Color(0.25f, 0.25f, 0.30f);

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        if (characters == null || characters.Length == 0)
        {
            Debug.LogWarning("CharacterSelectScreen: no characters assigned — starting game immediately.");
            Instance = null;
            Destroy(gameObject);
            return;
        }

        _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        BuildScreen();

        _selectedIndex = PersistentDataManager.Instance != null
            ? Mathf.Clamp(PersistentDataManager.Instance.SelectedCharacterIndex, 0, characters.Length - 1)
            : 0;

        SelectCharacter(_selectedIndex);
    }

    // ── Screen builder ────────────────────────────────────────────────────────

    private void BuildScreen()
    {
        _canvasGO                   = new GameObject("CharSelectCanvas");
        Canvas canvas               = _canvasGO.AddComponent<Canvas>();
        canvas.renderMode           = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder         = 50;

        CanvasScaler scaler         = _canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode          = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution  = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight   = 0.5f;
        _canvasGO.AddComponent<GraphicRaycaster>();

        // Root full-screen background
        GameObject root         = new GameObject("Root");
        root.transform.SetParent(_canvasGO.transform, false);
        RectTransform rootRT    = root.AddComponent<RectTransform>();
        rootRT.anchorMin        = Vector2.zero;
        rootRT.anchorMax        = Vector2.one;
        rootRT.offsetMin        = Vector2.zero;
        rootRT.offsetMax        = Vector2.zero;
        root.AddComponent<Image>().color = BgColor;

        HorizontalLayoutGroup hlg   = root.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                 = new RectOffset(48, 48, 48, 48);
        hlg.spacing                 = 0f;
        hlg.childControlHeight      = true;
        hlg.childControlWidth       = true;
        hlg.childForceExpandHeight  = true;
        hlg.childForceExpandWidth   = true;

        BuildLeftPanel(root.transform);
        BuildDivider(root.transform);
        BuildRightPanel(root.transform);
    }

    // ── Left panel (card list) ────────────────────────────────────────────────

    private void BuildLeftPanel(Transform parent)
    {
        GameObject left = MakeGroup(parent, "LeftPanel");
        left.AddComponent<Image>().color = Color.clear;

        LayoutElement le    = left.AddComponent<LayoutElement>();
        le.preferredWidth   = 440f;
        le.flexibleWidth    = 0f;

        VerticalLayoutGroup vlg     = left.AddComponent<VerticalLayoutGroup>();
        vlg.spacing                 = 20f;
        vlg.childControlHeight      = true;
        vlg.childControlWidth       = true;
        vlg.childForceExpandHeight  = true;
        vlg.childForceExpandWidth   = true;

        // Title
        var title           = MakeLabel(left.transform, "Title", "SELECT\nCHARACTER", 42f,
                                        new Color(0.9f, 0.15f, 0.15f));
        title.fontStyle     = FontStyles.Bold;
        title.alignment     = TextAlignmentOptions.Center;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 110f;

        // Card scroll
        GameObject scrollGO = BuildScrollView(left.transform, vertical: true);
        scrollGO.GetComponent<LayoutElement>().flexibleHeight = 1f;

        Transform content   = scrollGO.transform.Find("Viewport/Content");

        _cardBgs            = new Image[characters.Length];
        for (int i = 0; i < characters.Length; i++)
            BuildCard(content, i);

        // Start button
        BuildStartButton(left.transform);
    }

    private void BuildCard(Transform content, int i)
    {
        int idx                 = i;
        CharacterDefinition def = characters[i];

        GameObject card         = new GameObject($"Card_{i}");
        card.transform.SetParent(content, false);

        Image cardBg            = card.AddComponent<Image>();
        cardBg.color            = CardNormal;
        _cardBgs[i]             = cardBg;

        Button btn              = card.AddComponent<Button>();
        btn.targetGraphic       = cardBg;
        ColorBlock cb           = btn.colors;
        cb.normalColor          = Color.white;
        cb.highlightedColor     = new Color(1.15f, 1.15f, 1.15f);
        cb.pressedColor         = new Color(0.85f, 0.85f, 0.85f);
        btn.colors              = cb;
        btn.onClick.AddListener(() => SelectCharacter(idx));

        card.AddComponent<LayoutElement>().preferredHeight = 86f;

        HorizontalLayoutGroup hlg   = card.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                 = new RectOffset(14, 14, 14, 14);
        hlg.spacing                 = 14f;
        hlg.childAlignment          = TextAnchor.MiddleLeft;
        hlg.childControlHeight      = true;
        hlg.childControlWidth       = false;
        hlg.childForceExpandHeight  = true;
        hlg.childForceExpandWidth   = false;

        // Color swatch strip
        GameObject swatch       = new GameObject("Swatch");
        swatch.transform.SetParent(card.transform, false);
        swatch.AddComponent<Image>().color = def != null ? def.color : Color.grey;
        swatch.AddComponent<LayoutElement>().preferredWidth = 10f;

        // Name + stat preview
        GameObject nameCol      = MakeGroup(card.transform, "NameCol");
        nameCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
        VerticalLayoutGroup nameVLG     = nameCol.AddComponent<VerticalLayoutGroup>();
        nameVLG.childControlHeight      = true;
        nameVLG.childControlWidth       = true;
        nameVLG.childForceExpandHeight  = true;
        nameVLG.childForceExpandWidth   = true;

        var nameLabel           = MakeLabel(nameCol.transform, "Name", def?.characterName ?? "—", 24f, Color.white);
        nameLabel.fontStyle     = FontStyles.Bold;

        string preview = def != null
            ? $"HP ×{def.healthMultiplier:0.0}   SPD ×{def.speedMultiplier:0.0}   DMG ×{def.damageMultiplier:0.0}"
            : "";
        MakeLabel(nameCol.transform, "Preview", preview, 15f, new Color(0.55f, 0.55f, 0.55f));
    }

    private void BuildStartButton(Transform parent)
    {
        GameObject go       = new GameObject("StartButton");
        go.transform.SetParent(parent, false);

        Image img           = go.AddComponent<Image>();
        img.color           = new Color(0.15f, 0.52f, 0.15f);

        Button btn          = go.AddComponent<Button>();
        btn.targetGraphic   = img;
        ColorBlock cb       = btn.colors;
        cb.normalColor      = Color.white;
        cb.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
        cb.pressedColor     = new Color(0.8f, 0.8f, 0.8f);
        btn.colors          = cb;
        btn.onClick.AddListener(OnStartClicked);

        go.AddComponent<LayoutElement>().preferredHeight = 72f;

        var lbl         = MakeLabel(go.transform, "Label", "START GAME", 32f, Color.white);
        lbl.fontStyle   = FontStyles.Bold;
        var lblRT       = lbl.GetComponent<RectTransform>();
        lblRT.anchorMin = Vector2.zero;
        lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = Vector2.zero;
        lblRT.offsetMax = Vector2.zero;
    }

    // ── Divider ───────────────────────────────────────────────────────────────

    private void BuildDivider(Transform parent)
    {
        GameObject div      = new GameObject("Divider");
        div.transform.SetParent(parent, false);
        div.AddComponent<Image>().color = DividerColor;
        LayoutElement le    = div.AddComponent<LayoutElement>();
        le.preferredWidth   = 2f;
        le.flexibleWidth    = 0f;
    }

    // ── Right panel (detail view) ─────────────────────────────────────────────

    private void BuildRightPanel(Transform parent)
    {
        GameObject right        = MakeGroup(parent, "RightPanel");
        right.AddComponent<LayoutElement>().flexibleWidth = 1f;

        VerticalLayoutGroup vlg     = right.AddComponent<VerticalLayoutGroup>();
        vlg.padding                 = new RectOffset(52, 8, 8, 8);
        vlg.spacing                 = 18f;
        vlg.childControlHeight      = false;
        vlg.childControlWidth       = true;
        vlg.childForceExpandHeight  = false;
        vlg.childForceExpandWidth   = true;

        // ── Name row (color bar + name) ───────────────────────────────────────
        GameObject nameRow          = MakeGroup(right.transform, "NameRow");
        nameRow.AddComponent<LayoutElement>().preferredHeight = 74f;

        HorizontalLayoutGroup nameHLG   = nameRow.AddComponent<HorizontalLayoutGroup>();
        nameHLG.spacing                 = 20f;
        nameHLG.childAlignment          = TextAnchor.MiddleLeft;
        nameHLG.childControlHeight      = true;
        nameHLG.childControlWidth       = false;
        nameHLG.childForceExpandHeight  = true;
        nameHLG.childForceExpandWidth   = false;

        // Color accent bar
        GameObject colorBar         = new GameObject("ColorBar");
        colorBar.transform.SetParent(nameRow.transform, false);
        _detailColorBar             = colorBar.AddComponent<Image>();
        colorBar.AddComponent<LayoutElement>().preferredWidth = 8f;

        // Name label
        GameObject nameGO           = MakeGroup(nameRow.transform, "NameLabelWrap");
        nameGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
        _detailName                 = MakeLabel(nameGO.transform, "NameLabel", "", 52f, Color.white);
        _detailName.fontStyle       = FontStyles.Bold;
        var nameRT                  = _detailName.GetComponent<RectTransform>();
        nameRT.anchorMin            = Vector2.zero;
        nameRT.anchorMax            = Vector2.one;
        nameRT.offsetMin            = Vector2.zero;
        nameRT.offsetMax            = Vector2.zero;

        // ── Description ───────────────────────────────────────────────────────
        _detailDescription          = MakeLabel(right.transform, "Description", "", 21f,
                                                 new Color(0.72f, 0.72f, 0.72f));
        _detailDescription.enableWordWrapping = true;
        _detailDescription.alignment = TextAlignmentOptions.TopLeft;
        _detailDescription.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;

        MakeSeparator(right.transform);

        // ── Stats ─────────────────────────────────────────────────────────────
        var statsHeader         = MakeLabel(right.transform, "StatsHeader", "STATS", 16f,
                                            new Color(0.5f, 0.5f, 0.55f));
        statsHeader.fontStyle   = FontStyles.Bold;
        statsHeader.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

        // Stats container (rebuilt when selection changes)
        _statsContainer         = MakeGroup(right.transform, "StatsContainer").transform;
        _statsContainer.gameObject.AddComponent<LayoutElement>().preferredHeight = 200f;

        VerticalLayoutGroup statsVLG    = _statsContainer.gameObject.AddComponent<VerticalLayoutGroup>();
        statsVLG.spacing                = 10f;
        statsVLG.childControlHeight     = false;
        statsVLG.childControlWidth      = true;
        statsVLG.childForceExpandHeight = false;
        statsVLG.childForceExpandWidth  = true;

        MakeSeparator(right.transform);

        // ── Passives ──────────────────────────────────────────────────────────
        var passivesHeader          = MakeLabel(right.transform, "PassivesHeader", "PASSIVE EFFECTS",
                                                16f, new Color(0.5f, 0.5f, 0.55f));
        passivesHeader.fontStyle    = FontStyles.Bold;
        passivesHeader.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

        // Passives scroll (rebuilt when selection changes)
        GameObject passivesScroll   = BuildScrollView(right.transform, vertical: true);
        passivesScroll.GetComponent<LayoutElement>().flexibleHeight = 1f;
        _passivesContainer          = passivesScroll.transform.Find("Viewport/Content");
    }

    // ── Selection logic ───────────────────────────────────────────────────────

    private void SelectCharacter(int index)
    {
        if (index < 0 || index >= characters.Length) return;

        _selectedIndex = index;
        _selected      = characters[index];

        // Highlight cards
        for (int i = 0; i < _cardBgs.Length; i++)
            if (_cardBgs[i] != null)
                _cardBgs[i].color = i == index ? CardSelected : CardNormal;

        RefreshDetailPanel();

        PersistentDataManager.Instance?.SelectCharacter(index);
    }

    private void RefreshDetailPanel()
    {
        if (_selected == null) return;

        // Name + color bar
        if (_detailName     != null) _detailName.text  = _selected.characterName.ToUpper();
        if (_detailColorBar != null) _detailColorBar.color = _selected.color;
        if (_detailDescription != null) _detailDescription.text = _selected.description;

        // Rebuild stat rows
        if (_statsContainer != null)
        {
            for (int i = _statsContainer.childCount - 1; i >= 0; i--)
                DestroyImmediate(_statsContainer.GetChild(i).gameObject);

            BuildStatRow(_statsContainer, "Health",  _selected.color, _selected.healthMultiplier);
            BuildStatRow(_statsContainer, "Speed",   _selected.color, _selected.speedMultiplier);
            BuildStatRow(_statsContainer, "Damage",  _selected.color, _selected.damageMultiplier);

            // Starting resources
            GameObject resRow       = MakeGroup(_statsContainer, "ResourceRow");
            resRow.AddComponent<LayoutElement>().preferredHeight = 26f;
            var resLabel            = MakeLabel(resRow.transform, "Resources",
                $"Starting:   {_selected.startingWood} Wood   |   {_selected.startingMetal} Metal",
                18f, new Color(0.65f, 0.75f, 0.45f));
            resLabel.alignment      = TextAlignmentOptions.Left;
            var resRT               = resLabel.GetComponent<RectTransform>();
            resRT.anchorMin         = Vector2.zero;
            resRT.anchorMax         = Vector2.one;
            resRT.offsetMin         = Vector2.zero;
            resRT.offsetMax         = Vector2.zero;
        }

        // Rebuild passive rows
        if (_passivesContainer != null)
        {
            for (int i = _passivesContainer.childCount - 1; i >= 0; i--)
                DestroyImmediate(_passivesContainer.GetChild(i).gameObject);

            bool hasPassives = _selected.passiveEffects != null && _selected.passiveEffects.Length > 0;

            if (hasPassives)
            {
                foreach (var p in _selected.passiveEffects)
                    BuildPassiveRow(_passivesContainer, p);
            }
            else
            {
                var none        = MakeLabel(_passivesContainer, "None", "No passive effects.", 20f,
                                            new Color(0.38f, 0.38f, 0.38f));
                none.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
            }
        }
    }

    // ── Row builders ─────────────────────────────────────────────────────────

    private void BuildStatRow(Transform parent, string statName, Color barColor, float multiplier)
    {
        float  fillPct = Mathf.Clamp01(multiplier / 2f);
        string delta = multiplier > 1.005f ? $"+{(multiplier - 1f)*100f:0}%"
                     : multiplier < 0.995f ? $"-{(1f - multiplier)*100f:0}%"
                     : "base";
        Color deltaCol = multiplier > 1.005f ? new Color(0.35f, 0.90f, 0.35f)
                       : multiplier < 0.995f ? new Color(0.90f, 0.35f, 0.35f)
                       : new Color(0.55f, 0.55f, 0.55f);

        GameObject row          = MakeGroup(parent, statName + "Row");
        row.AddComponent<LayoutElement>().preferredHeight = 34f;

        HorizontalLayoutGroup hlg   = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment          = TextAnchor.MiddleLeft;
        hlg.spacing                 = 14f;
        hlg.childControlHeight      = true;
        hlg.childControlWidth       = false;
        hlg.childForceExpandHeight  = true;
        hlg.childForceExpandWidth   = false;

        // Stat label
        var nameLabel           = MakeLabel(row.transform, "Name", statName, 20f,
                                            new Color(0.72f, 0.72f, 0.72f));
        nameLabel.alignment     = TextAlignmentOptions.Right;
        nameLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 90f;

        // Bar background
        GameObject barBG        = new GameObject("BarBG");
        barBG.transform.SetParent(row.transform, false);
        barBG.AddComponent<Image>().color = new Color(0.14f, 0.14f, 0.18f);
        barBG.AddComponent<LayoutElement>().preferredWidth = 280f;

        // Fill — anchor-relative inside barBG, not affected by parent HLG
        GameObject fill         = new GameObject("Fill");
        fill.transform.SetParent(barBG.transform, false);
        fill.AddComponent<Image>().color = barColor;
        RectTransform fillRT    = fill.GetComponent<RectTransform>();
        fillRT.anchorMin        = new Vector2(0f,       0.15f);
        fillRT.anchorMax        = new Vector2(fillPct,  0.85f);
        fillRT.offsetMin        = Vector2.zero;
        fillRT.offsetMax        = Vector2.zero;

        // Value
        var valLabel            = MakeLabel(row.transform, "Value", $"×{multiplier:0.0}", 20f, Color.white);
        valLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 46f;

        // Delta
        var deltaLabel          = MakeLabel(row.transform, "Delta", delta, 16f, deltaCol);
        deltaLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 54f;
    }

    private void BuildPassiveRow(Transform parent, PassiveEffect passive)
    {
        if (passive == null) return;

        GameObject row          = new GameObject("PassiveRow");
        row.transform.SetParent(parent, false);
        row.AddComponent<Image>().color = PanelColor;

        LayoutElement rowLE     = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight   = 76f;
        rowLE.flexibleHeight    = 0f;

        HorizontalLayoutGroup hlg   = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                 = new RectOffset(14, 14, 10, 10);
        hlg.spacing                 = 14f;
        hlg.childAlignment          = TextAnchor.MiddleLeft;
        hlg.childControlHeight      = true;
        hlg.childControlWidth       = false;
        hlg.childForceExpandHeight  = true;
        hlg.childForceExpandWidth   = false;

        // Accent strip in character color
        GameObject strip            = new GameObject("Strip");
        strip.transform.SetParent(row.transform, false);
        strip.AddComponent<Image>().color = _selected != null ? _selected.color : Color.white;
        strip.AddComponent<LayoutElement>().preferredWidth = 4f;

        // Text column
        GameObject textCol      = MakeGroup(row.transform, "TextCol");
        textCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
        VerticalLayoutGroup tVLG    = textCol.AddComponent<VerticalLayoutGroup>();
        tVLG.spacing                = 3f;
        tVLG.childControlHeight     = true;
        tVLG.childControlWidth      = true;
        tVLG.childForceExpandHeight = true;
        tVLG.childForceExpandWidth  = true;

        var nameLabel           = MakeLabel(textCol.transform, "Name",
                                            passive.effectName ?? "", 19f,
                                            new Color(0.90f, 0.85f, 0.40f));
        nameLabel.fontStyle     = FontStyles.Bold;

        var descLabel           = MakeLabel(textCol.transform, "Desc",
                                            passive.effectDescription ?? "", 16f,
                                            new Color(0.70f, 0.70f, 0.70f));
        descLabel.enableWordWrapping = true;
    }

    // ── Start flow ────────────────────────────────────────────────────────────

    private void OnStartClicked()
    {
        if (_selected == null) _selected = characters[0];

        GameManager.Instance.OnPlayerSpawned += ApplyCharacterToPlayer;

        _canvasGO.SetActive(false);
        GameManager.Instance?.StartGame();
    }

    private void ApplyCharacterToPlayer(PlayerController player)
    {
        player.ApplyCharacter(_selected);

        if (_selected != null)
        {
            ResourceManager.Instance?.AddResource("Wood",  _selected.startingWood);
            ResourceManager.Instance?.AddResource("Metal", _selected.startingMetal);
        }

        GameManager.Instance.OnPlayerSpawned -= ApplyCharacterToPlayer;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private GameObject BuildScrollView(Transform parent, bool vertical)
    {
        GameObject sv   = new GameObject("ScrollView");
        sv.transform.SetParent(parent, false);
        sv.AddComponent<LayoutElement>();   // caller sets preferred/flexible size

        ScrollRect sr   = sv.AddComponent<ScrollRect>();
        sr.horizontal   = !vertical;
        sr.vertical     = vertical;
        sr.scrollSensitivity = 30f;

        GameObject vp   = new GameObject("Viewport");
        vp.transform.SetParent(sv.transform, false);
        RectTransform vpRT  = vp.AddComponent<RectTransform>();
        vpRT.anchorMin  = Vector2.zero;
        vpRT.anchorMax  = Vector2.one;
        vpRT.offsetMin  = Vector2.zero;
        vpRT.offsetMax  = Vector2.zero;
        vp.AddComponent<RectMask2D>();
        sr.viewport     = vpRT;

        GameObject content  = new GameObject("Content");
        content.transform.SetParent(vp.transform, false);
        RectTransform cRT   = content.AddComponent<RectTransform>();
        cRT.anchorMin   = vertical ? new Vector2(0f, 1f) : new Vector2(0f, 0f);
        cRT.anchorMax   = vertical ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        cRT.pivot       = vertical ? new Vector2(0.5f, 1f) : new Vector2(0f, 0.5f);
        cRT.offsetMin   = Vector2.zero;
        cRT.offsetMax   = Vector2.zero;
        sr.content      = cRT;

        VerticalLayoutGroup vlg     = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing                 = 8f;
        vlg.childControlHeight      = false;
        vlg.childControlWidth       = true;
        vlg.childForceExpandHeight  = false;
        vlg.childForceExpandWidth   = true;

        ContentSizeFitter csf       = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit             = ContentSizeFitter.FitMode.PreferredSize;

        return sv;
    }

    private static GameObject MakeGroup(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    private void MakeSeparator(Transform parent)
    {
        var sep = new GameObject("Separator");
        sep.transform.SetParent(parent, false);
        sep.AddComponent<Image>().color = DividerColor;
        sep.AddComponent<LayoutElement>().preferredHeight = 1f;
    }

    private TextMeshProUGUI MakeLabel(Transform parent, string name, string text, float size, Color color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var tmp             = go.AddComponent<TextMeshProUGUI>();
        tmp.text            = text;
        tmp.fontSize        = size;
        tmp.color           = color;
        tmp.alignment       = TextAlignmentOptions.Left;
        tmp.enableWordWrapping = false;
        if (_font != null) tmp.font = _font;
        return tmp;
    }
}
