using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Self-contained virtual joystick for mobile movement.
/// PlayerController creates one instance in Awake and polls Direction each FixedUpdate.
/// Visibility is controlled by PersistentDataManager.VirtualJoystickEnabled.
/// </summary>
public class VirtualJoystick : MonoBehaviour
{
    public static VirtualJoystick Instance { get; private set; }

    /// <summary>Normalised movement direction while a finger is dragging; zero otherwise.</summary>
    public Vector2 Direction { get; private set; }

    private GameObject    _canvasGO;
    private RectTransform _knobRT;

    private const float OuterRadius = 120f;   // reference-resolution units
    private const float KnobSize    = 90f;
    private const float EdgePadding = 40f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        BuildUI();

        bool show = PersistentDataManager.Instance != null
            ? PersistentDataManager.Instance.VirtualJoystickEnabled
            : IsMobilePlatform();
        _canvasGO.SetActive(show);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_canvasGO != null) Destroy(_canvasGO);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void SetVisible(bool visible)
    {
        if (_canvasGO != null) _canvasGO.SetActive(visible);
    }

    public static bool IsMobilePlatform() =>
        Application.platform == RuntimePlatform.Android ||
        Application.platform == RuntimePlatform.IPhonePlayer;

    // ── Called by JoystickInputHandler ───────────────────────────────────────

    internal void OnDrag(Vector2 localDelta)
    {
        Vector2 clamped = Vector2.ClampMagnitude(localDelta, OuterRadius);
        _knobRT.anchoredPosition = clamped;
        Direction = clamped.magnitude > 4f ? clamped / OuterRadius : Vector2.zero;
    }

    internal void OnRelease()
    {
        _knobRT.anchoredPosition = Vector2.zero;
        Direction = Vector2.zero;
    }

    // ── UI construction ───────────────────────────────────────────────────────

    private void BuildUI()
    {
        // ── Canvas ──────────────────────────────────────────────────────────
        _canvasGO = new GameObject("VirtualJoystickCanvas");

        var canvas           = _canvasGO.AddComponent<Canvas>();
        canvas.renderMode    = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder  = 50;   // above game, below pause menu (100)

        var scaler                  = _canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode          = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution  = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight   = 0.5f;

        _canvasGO.AddComponent<GraphicRaycaster>();

        // ── Safe-area wrapper ────────────────────────────────────────────────
        var safeGO  = new GameObject("SafeArea");
        safeGO.transform.SetParent(_canvasGO.transform, false);
        var safeRT  = safeGO.AddComponent<RectTransform>();
        safeRT.anchorMin = Vector2.zero;
        safeRT.anchorMax = Vector2.one;
        safeRT.offsetMin = safeRT.offsetMax = Vector2.zero;
        safeGO.AddComponent<SafeAreaFitter>();

        // ── Outer ring ───────────────────────────────────────────────────────
        var outerGO = new GameObject("OuterRing");
        outerGO.transform.SetParent(safeGO.transform, false);

        var outerRT           = outerGO.AddComponent<RectTransform>();
        outerRT.anchorMin     = outerRT.anchorMax = Vector2.zero;   // bottom-left
        outerRT.pivot         = new Vector2(0.5f, 0.5f);
        outerRT.sizeDelta     = new Vector2(OuterRadius * 2f, OuterRadius * 2f);
        outerRT.anchoredPosition = new Vector2(EdgePadding + OuterRadius,
                                               EdgePadding + OuterRadius);

        var outerImg   = outerGO.AddComponent<Image>();
        outerImg.sprite = MakeCircle(128);
        outerImg.type   = Image.Type.Simple;
        outerImg.color  = new Color(0.08f, 0.08f, 0.08f, 0.55f);

        var handler        = outerGO.AddComponent<JoystickInputHandler>();
        handler.Joystick   = this;
        handler.OuterRT    = outerRT;

        // ── Knob ─────────────────────────────────────────────────────────────
        var knobGO = new GameObject("Knob");
        knobGO.transform.SetParent(outerGO.transform, false);

        _knobRT             = knobGO.AddComponent<RectTransform>();
        _knobRT.anchorMin   = _knobRT.anchorMax = new Vector2(0.5f, 0.5f);
        _knobRT.pivot       = new Vector2(0.5f, 0.5f);
        _knobRT.sizeDelta   = new Vector2(KnobSize, KnobSize);
        _knobRT.anchoredPosition = Vector2.zero;

        var knobImg   = knobGO.AddComponent<Image>();
        knobImg.sprite = MakeCircle(64);
        knobImg.type   = Image.Type.Simple;
        knobImg.color  = new Color(0.88f, 0.88f, 0.88f, 0.72f);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Sprite MakeCircle(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float c = size * 0.5f;
        float r = c - 1f;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dist = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
            byte  a    = (byte)(Mathf.Clamp01(r - dist + 1f) * 255f);
            pixels[y * size + x] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}

/// <summary>Sits on the outer ring Image and forwards pointer events to VirtualJoystick.</summary>
internal class JoystickInputHandler : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    internal VirtualJoystick Joystick;
    internal RectTransform   OuterRT;

    public void OnPointerDown(PointerEventData e)
    {
        BuildingManager.Instance?.CancelPlacement();
        PlacedBuilding.Deselect();
        ProcessDrag(e);
    }
    public void OnDrag      (PointerEventData e) => ProcessDrag(e);
    public void OnPointerUp (PointerEventData e) => Joystick.OnRelease();

    private void ProcessDrag(PointerEventData e)
    {
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                OuterRT, e.position, e.pressEventCamera, out Vector2 local))
            Joystick.OnDrag(local);
    }
}
