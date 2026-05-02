using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the Camp scene HUD and the tent detail popup.
/// The player walks around the world; pressing E near a CampTentObject
/// calls OpenTentUI() which shows the detail panel on screen.
///
/// Attach to the manager GameObject in CampScene.
/// </summary>
public class CampSceneManager : MonoBehaviour
{
    public static CampSceneManager Instance { get; private set; }

    [Header("UI Theme")]
    [SerializeField] private UITheme _theme;

    // ── Colours ───────────────────────────────────────────────────────────────
    private static readonly Color BgDim      = new Color(0f,    0f,    0f,    0.65f);
    private static readonly Color HeaderColor = new Color(0.06f, 0.06f, 0.09f, 0.92f);
    private static readonly Color PanelColor  = new Color(0.08f, 0.08f, 0.11f, 0.97f);
    private static readonly Color AccentGold  = new Color(1f,    0.85f, 0.2f);
    private static readonly Color DivColor    = new Color(0.18f, 0.18f, 0.22f);

    // ── Font ──────────────────────────────────────────────────────────────────
    private TMP_FontAsset _font;

    // ── HUD refs ──────────────────────────────────────────────────────────────
    private TextMeshProUGUI _coinLabel;
    private TextMeshProUGUI _promptLabel;

    // ── Detail popup refs ─────────────────────────────────────────────────────
    private GameObject       _popup;
    private Image            _detailPortrait;
    private TextMeshProUGUI  _detailName;
    private TextMeshProUGUI  _detailDesc;
    private TextMeshProUGUI  _detailUnlocks;
    private GameObject       _detailActionArea;

    // ── Dialogue overlay ──────────────────────────────────────────────────────
    private GameObject _dialogueOverlay;

    // ── Current selection ─────────────────────────────────────────────────────
    private TentDefinition  _selectedTent;
    private CampTentObject  _selectedTentObj;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        _font = _theme?.font != null
            ? _theme.font
            : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        EnsureEventSystem();
        BuildUI();

        if (PersistentDataManager.Instance != null)
            PersistentDataManager.Instance.OnCurrencyChanged += RefreshCoinLabel;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (PersistentDataManager.Instance != null)
            PersistentDataManager.Instance.OnCurrencyChanged -= RefreshCoinLabel;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Public API
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Called by CampPlayerController when a tent is in range.</summary>
    public void SetPrompt(string text)
    {
        if (_promptLabel == null) return;
        _promptLabel.text = text;
        _promptLabel.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    /// <summary>Called by CampTentObject.Interact() to open the detail popup.</summary>
    public void OpenTentUI(TentDefinition def, CampTentObject tentObj)
    {
        if (def == null) return;
        _selectedTent    = def;
        _selectedTentObj = tentObj;
        PopulatePopup(def);
        _popup?.SetActive(true);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  UI construction
    // ═════════════════════════════════════════════════════════════════════════

    private void BuildUI()
    {
        // ── Canvas ────────────────────────────────────────────────────────────
        var canvasGO              = new GameObject("CampCanvas");
        var canvas                = canvasGO.AddComponent<Canvas>();
        canvas.renderMode         = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder       = 10;
        var scaler                = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 1f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // ── Header bar ────────────────────────────────────────────────────────
        BuildHeader(canvasGO.transform);

        // ── Interact prompt (bottom centre) ───────────────────────────────────
        BuildPromptLabel(canvasGO.transform);

        // ── Tent detail popup (centre screen, hidden until interaction) ───────
        BuildPopup(canvasGO.transform);

        // ── Dialogue overlay ──────────────────────────────────────────────────
        BuildDialogueOverlay(canvasGO.transform);
    }

    // ── Header ────────────────────────────────────────────────────────────────

    private void BuildHeader(Transform parent)
    {
        const float HDR_H = 72f;

        var hdr    = new GameObject("Header");
        hdr.transform.SetParent(parent, false);
        hdr.AddComponent<Image>().color = HeaderColor;
        var rt = hdr.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(0f, -HDR_H); rt.offsetMax = Vector2.zero;

        var hl = hdr.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(28, 20, 12, 12);
        hl.spacing = 20f;
        hl.childControlHeight = true; hl.childForceExpandHeight = true;
        hl.childControlWidth  = false; hl.childForceExpandWidth  = false;

        var title = MakeLabel(hdr.transform, "Title", "CAMP", 36f);
        title.color     = new Color(0.85f, 0.14f, 0.14f);
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Left;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        int coins = PersistentDataManager.Instance?.TotalCurrency ?? 0;
        _coinLabel = MakeLabel(hdr.transform, "Coins", $"Coins: {coins}", 24f);
        _coinLabel.color = AccentGold;
        _coinLabel.alignment = TextAlignmentOptions.Right;
        _coinLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 200f;

        // Back button
        var backGO = new GameObject("BackBtn");
        backGO.transform.SetParent(hdr.transform, false);
        backGO.AddComponent<LayoutElement>().preferredWidth = 120f;
        var backImg = backGO.AddComponent<Image>();
        UIHelper.ApplyImage(backImg, _theme?.buttonSecondary, new Color(0.18f, 0.18f, 0.24f));
        var back = backGO.AddComponent<Button>();
        back.targetGraphic = backImg;
        back.onClick.AddListener(OnBackClicked);
        var backLbl = MakeLabel(backGO.transform, "Lbl", "< Back", 20f);
        backLbl.color = Color.white;
        Stretch(backLbl.gameObject, Vector2.zero, Vector2.one);
    }

    // ── Prompt ────────────────────────────────────────────────────────────────

    private void BuildPromptLabel(Transform parent)
    {
        // Background strip (Image)
        var go = new GameObject("PromptLabel");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.offsetMin = new Vector2(0f, 20f);
        rt.offsetMax = new Vector2(0f, 64f);
        go.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        // Text child (TextMeshProUGUI must be on a separate GO from Image)
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(go.transform, false);
        var textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero; textRT.offsetMax = Vector2.zero;

        _promptLabel = textGO.AddComponent<TextMeshProUGUI>();
        _promptLabel.text      = string.Empty;
        _promptLabel.fontSize  = 22f;
        _promptLabel.color     = Color.white;
        _promptLabel.alignment = TextAlignmentOptions.Center;
        if (_font != null) _promptLabel.font = _font;

        go.SetActive(false);
    }

    // ── Popup ─────────────────────────────────────────────────────────────────

    private void BuildPopup(Transform parent)
    {
        // Dim backdrop — Image must come first so RectTransform is created
        var dim = new GameObject("PopupDim");
        dim.transform.SetParent(parent, false);
        var dimImg = dim.AddComponent<Image>();
        dimImg.color = BgDim;
        var dimRT = dim.GetComponent<RectTransform>();
        dimRT.anchorMin = Vector2.zero; dimRT.anchorMax = Vector2.one;
        dimRT.offsetMin = Vector2.zero; dimRT.offsetMax = Vector2.zero;

        // Close on backdrop click
        var dimBtn = dim.AddComponent<Button>();
        dimBtn.targetGraphic = dimImg;
        dimBtn.targetGraphic = dimImg;
        dimBtn.onClick.AddListener(() => _popup?.SetActive(false));

        // Panel centred on screen
        const float PANEL_W = 440f;
        const float PANEL_H = 560f;
        var panel = new GameObject("Panel");
        panel.transform.SetParent(dim.transform, false);
        UIHelper.ApplyImage(panel.AddComponent<Image>(), _theme?.panelBackground, PanelColor);
        var panelRT = panel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f); panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot     = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(PANEL_W, PANEL_H);

        var vl = panel.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(28, 28, 28, 28);
        vl.spacing = 14f;
        vl.childControlHeight = false; vl.childForceExpandHeight = false;
        vl.childControlWidth  = true;  vl.childForceExpandWidth  = true;

        // Portrait
        var portGO = new GameObject("Portrait");
        portGO.transform.SetParent(panel.transform, false);
        portGO.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 160f);
        _detailPortrait = portGO.AddComponent<Image>();
        _detailPortrait.preserveAspect = true;
        _detailPortrait.color = Color.clear;

        // Name
        _detailName = MakeLabel(panel.transform, "TentName", "", 28f);
        _detailName.fontStyle = FontStyles.Bold;
        _detailName.alignment = TextAlignmentOptions.Left;
        _detailName.gameObject.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 36f);

        // Description
        _detailDesc = MakeLabel(panel.transform, "Desc", "", 18f);
        _detailDesc.color = new Color(0.56f, 0.56f, 0.60f);
        _detailDesc.enableWordWrapping = true;
        _detailDesc.alignment = TextAlignmentOptions.Left;
        _detailDesc.gameObject.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 60f);

        // Divider
        var div = new GameObject("Div");
        div.transform.SetParent(panel.transform, false);
        div.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 1f);
        div.AddComponent<Image>().color = DivColor;

        // Unlocks
        _detailUnlocks = MakeLabel(panel.transform, "Unlocks", "", 15f);
        _detailUnlocks.color = new Color(0.42f, 0.82f, 0.50f);
        _detailUnlocks.enableWordWrapping = true;
        _detailUnlocks.alignment = TextAlignmentOptions.Left;
        _detailUnlocks.gameObject.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 48f);

        // Action area
        _detailActionArea = new GameObject("ActionArea");
        _detailActionArea.transform.SetParent(panel.transform, false);
        _detailActionArea.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 56f);

        _popup = dim;
        _popup.SetActive(false);
    }

    // ── Dialogue overlay ──────────────────────────────────────────────────────

    private void BuildDialogueOverlay(Transform canvasParent)
    {
        _dialogueOverlay = new GameObject("DialogueOverlay");
        _dialogueOverlay.transform.SetParent(canvasParent, false);
        var dimmer = _dialogueOverlay.AddComponent<Image>();
        dimmer.color = new Color(0f, 0f, 0f, 0.72f);
        dimmer.raycastTarget = true;
        var overlayRT = _dialogueOverlay.GetComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero; overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero; overlayRT.offsetMax = Vector2.zero;

        var box = new GameObject("Box");
        box.transform.SetParent(_dialogueOverlay.transform, false);
        UIHelper.ApplyImage(box.AddComponent<Image>(), _theme?.panelBackground, new Color(0.10f, 0.10f, 0.13f));
        var boxRT = box.GetComponent<RectTransform>();
        boxRT.anchorMin = new Vector2(0.25f, 0.28f); boxRT.anchorMax = new Vector2(0.75f, 0.72f);
        boxRT.offsetMin = Vector2.zero; boxRT.offsetMax = Vector2.zero;

        var vl = box.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(36, 36, 30, 30);
        vl.spacing = 18f;
        vl.childControlHeight = false; vl.childForceExpandHeight = false;
        vl.childControlWidth  = true;  vl.childForceExpandWidth  = true;

        var textLbl = MakeLabel(box.transform, "DialogueText", "", 22f);
        textLbl.color = Color.white;
        textLbl.alignment = TextAlignmentOptions.Center;
        textLbl.enableWordWrapping = true;
        textLbl.gameObject.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 140f);

        var closeBtnGO = new GameObject("CloseBtn");
        closeBtnGO.transform.SetParent(box.transform, false);
        closeBtnGO.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 50f);
        var closeImg = closeBtnGO.AddComponent<Image>();
        UIHelper.ApplyImage(closeImg, _theme?.buttonSecondary, new Color(0.18f, 0.18f, 0.24f));
        var closeBtn = closeBtnGO.AddComponent<Button>();
        closeBtn.targetGraphic = closeImg;
        closeBtn.onClick.AddListener(() => _dialogueOverlay.SetActive(false));
        var closeLbl = MakeLabel(closeBtnGO.transform, "Lbl", "Close", 20f);
        closeLbl.color = Color.white;
        Stretch(closeLbl.gameObject, Vector2.zero, Vector2.one);

        _dialogueOverlay.SetActive(false);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Popup population
    // ═════════════════════════════════════════════════════════════════════════

    private void PopulatePopup(TentDefinition tent)
    {
        bool owned = CampManager.Instance?.IsPurchased(tent) ?? false;

        if (_detailPortrait != null)
        {
            if (tent.npcSprite != null) _detailPortrait.sprite = tent.npcSprite;
            _detailPortrait.color = tent.npcSprite != null
                ? (owned ? Color.white : new Color(0.35f, 0.35f, 0.35f, 0.55f))
                : new Color(0.18f, 0.18f, 0.20f);
        }

        if (_detailName != null)
        {
            _detailName.text  = owned ? tent.tentName : $"??? ({tent.tentName})";
            _detailName.color = owned ? AccentGold : new Color(0.50f, 0.50f, 0.50f);
        }

        if (_detailDesc != null)
            _detailDesc.text = tent.description;

        if (_detailUnlocks != null)
        {
            string preview = BuildUnlockPreview(tent);
            _detailUnlocks.text = preview;
            _detailUnlocks.gameObject.SetActive(!string.IsNullOrEmpty(preview));
        }

        if (_detailActionArea != null)
        {
            for (int i = _detailActionArea.transform.childCount - 1; i >= 0; i--)
                Destroy(_detailActionArea.transform.GetChild(i).gameObject);

            if (owned) BuildTalkButton(_detailActionArea.transform, tent);
            else       BuildBuyButton(_detailActionArea.transform, tent);
        }
    }

    private void BuildBuyButton(Transform parent, TentDefinition tent)
    {
        bool canAfford = (PersistentDataManager.Instance?.TotalCurrency ?? 0) >= tent.cost;

        var btnGO = new GameObject("BuyBtn");
        btnGO.transform.SetParent(parent, false);
        var rt = btnGO.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        var btnImg = btnGO.AddComponent<Image>();
        UIHelper.ApplyImage(btnImg, _theme?.buttonGold,
            canAfford ? new Color(0.14f, 0.42f, 0.14f) : new Color(0.28f, 0.18f, 0.08f));
        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.interactable  = canAfford;

        string label = canAfford ? $"Purchase  ({tent.cost} coins)" : $"Need {tent.cost} coins";
        var lbl = MakeLabel(btnGO.transform, "Lbl", label, 20f);
        lbl.color = Color.white;
        Stretch(lbl.gameObject, Vector2.zero, Vector2.one);

        btn.onClick.AddListener(() => OnPurchaseClicked(tent));
    }

    private void BuildTalkButton(Transform parent, TentDefinition tent)
    {
        // Close button always shown
        BuildCloseButton(parent);

        if (tent.dialogueLines == null || tent.dialogueLines.Length == 0) return;

        var btnGO = new GameObject("TalkBtn");
        btnGO.transform.SetParent(parent, false);
        var rt = btnGO.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        var btnImg = btnGO.AddComponent<Image>();
        UIHelper.ApplyImage(btnImg, _theme?.buttonSecondary, new Color(0.14f, 0.26f, 0.42f));
        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener(() =>
        {
            _popup?.SetActive(false);
            ShowDialogue(tent);
        });

        var lbl = MakeLabel(btnGO.transform, "Lbl", "Talk", 20f);
        lbl.color = Color.white;
        Stretch(lbl.gameObject, Vector2.zero, Vector2.one);
    }

    private void BuildCloseButton(Transform parent)
    {
        var go = new GameObject("CloseBtn");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(1f, 0.5f);
        rt.offsetMin = new Vector2(-90f, 0f); rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        UIHelper.ApplyImage(img, _theme?.buttonSecondary, new Color(0.20f, 0.12f, 0.12f));
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => _popup?.SetActive(false));

        var lbl = MakeLabel(go.transform, "X", "✕", 20f);
        lbl.color = Color.white;
        Stretch(lbl.gameObject, Vector2.zero, Vector2.one);
    }

    private void OnPurchaseClicked(TentDefinition tent)
    {
        if (CampManager.Instance == null || !CampManager.Instance.Purchase(tent)) return;

        // Refresh world-space tent
        _selectedTentObj?.RefreshVisuals();

        // Rebuild popup to show Talk button
        PopulatePopup(tent);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Dialogue overlay
    // ═════════════════════════════════════════════════════════════════════════

    private void ShowDialogue(TentDefinition tent)
    {
        if (tent.dialogueLines == null || tent.dialogueLines.Length == 0) return;
        string line = tent.dialogueLines[Random.Range(0, tent.dialogueLines.Length)];
        var textComp = _dialogueOverlay?.transform
            .Find("Box/DialogueText")?.GetComponent<TextMeshProUGUI>();
        if (textComp != null) textComp.text = $"\"{line}\"\n\n— {tent.tentName}";
        _dialogueOverlay?.SetActive(true);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════════════

    private static string BuildUnlockPreview(TentDefinition tent)
    {
        var parts = new List<string>();
        if (tent.unlocksCharacters != null)
            foreach (var c in tent.unlocksCharacters)
                if (c != null) parts.Add(c.characterName);
        if (tent.unlocksBuildingCards != null)
            foreach (var b in tent.unlocksBuildingCards)
                if (b != null) parts.Add(b.displayName);
        if (tent.startingWoodBonus  > 0) parts.Add($"+{tent.startingWoodBonus} Wood/run");
        if (tent.startingMetalBonus > 0) parts.Add($"+{tent.startingMetalBonus} Metal/run");
        return parts.Count == 0 ? string.Empty : "Unlocks: " + string.Join(", ", parts);
    }

    private void OnBackClicked()
    {
        if (SceneTransitionManager.Instance != null)
            SceneTransitionManager.Instance.LoadScene("MainMenuScene");
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenuScene");
    }

    private void RefreshCoinLabel(int newAmount)
    {
        if (_coinLabel != null) _coinLabel.text = $"Coins: {newAmount}";
    }

    private TextMeshProUGUI MakeLabel(Transform parent, string name, string text, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.Center; tmp.color = Color.white;
        tmp.enableWordWrapping = true;
        if (_font != null) tmp.font = _font;
        return tmp;
    }

    private static void Stretch(GameObject go, Vector2 anchorMin, Vector2 anchorMax)
    {
        var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static void EnsureEventSystem()
    {
        var existing = Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
        if (existing != null)
        {
            var old = existing.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            if (old != null) Object.Destroy(old);
            if (existing.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null)
            {
                var m = existing.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                m.AssignDefaultActions();
            }
        }
        else
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            var m = es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            m.AssignDefaultActions();
        }
    }
}
