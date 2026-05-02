using TMPro;
using UnityEngine;

/// <summary>
/// World-space tent object placed in CampScene.
/// Spawned procedurally by CampMapGenerator or placed manually.
/// The player walks up and presses E to interact.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class CampTentObject : MonoBehaviour
{
    [SerializeField] public TentDefinition tentDefinition;

    [Header("Sprites")]
    [Tooltip("Sprite shown when purchased.")]
    [SerializeField] private Sprite purchasedSprite;
    [Tooltip("Sprite shown before purchase (greyed-out). Falls back to purchasedSprite.")]
    [SerializeField] private Sprite ghostSprite;

    private SpriteRenderer _sr;
    private TextMeshPro    _nameTmp;

    // Shared fallback sprite used when no art is assigned yet
    private static Sprite _fallback;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        _sr.sortingOrder = 3; // above floor (0) and walls (1)
    }

    private void Start()
    {
        if (tentDefinition != null) RefreshVisuals();
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    public void Init(TentDefinition def, Sprite purchased, Sprite ghost)
    {
        tentDefinition  = def;
        purchasedSprite = purchased;
        ghostSprite     = ghost;
        if (_sr == null) _sr = GetComponent<SpriteRenderer>();
        RefreshVisuals();
    }

    // ── Visual refresh ────────────────────────────────────────────────────────

    public void RefreshVisuals()
    {
        if (tentDefinition == null || _sr == null) return;

        bool owned = CampManager.Instance?.IsPurchased(tentDefinition) ?? false;

        // Priority: per-component override → tentDefinition.tentSprite → fallback square
        Sprite baseSprite = tentDefinition.tentSprite
                         ?? (purchasedSprite != null ? purchasedSprite : null)
                         ?? GetFallback();

        Sprite display = owned
            ? baseSprite
            : (ghostSprite != null ? ghostSprite : baseSprite);

        _sr.sprite       = display;
        _sr.sortingOrder = 3;
        _sr.color        = owned ? Color.white : new Color(0.42f, 0.42f, 0.42f, 0.70f);

        EnsureNameLabel();
        if (_nameTmp != null)
        {
            _nameTmp.text  = owned ? tentDefinition.tentName : "???";
            _nameTmp.color = owned ? new Color(1f, 0.85f, 0.2f) : new Color(0.55f, 0.55f, 0.55f);
        }
    }

    // ── Name label ────────────────────────────────────────────────────────────

    private void EnsureNameLabel()
    {
        if (_nameTmp != null) return;

        var go = new GameObject("NameLabel");
        go.transform.SetParent(transform);
        go.transform.localPosition = new Vector3(0f, 0.80f, 0f);
        go.transform.localScale    = new Vector3(0.012f, 0.012f, 0.012f);

        _nameTmp = go.AddComponent<TextMeshPro>();
        _nameTmp.fontSize           = 36f;
        _nameTmp.alignment          = TextAlignmentOptions.Center;
        _nameTmp.enableWordWrapping = false;
        _nameTmp.sortingOrder       = 5;
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    public void Interact()
        => CampSceneManager.Instance?.OpenTentUI(tentDefinition, this);

    public string GetInteractPrompt()
    {
        if (tentDefinition == null) return string.Empty;
        bool owned = CampManager.Instance?.IsPurchased(tentDefinition) ?? false;
        return owned
            ? $"[E] Talk to {tentDefinition.tentName}"
            : $"[E] Purchase  ({tentDefinition.cost} coins)";
    }

    // ── Fallback sprite (white square, created once and cached) ───────────────

    private static Sprite GetFallback()
    {
        if (_fallback != null) return _fallback;

        const int size = 32;
        var tex    = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();

        _fallback = Sprite.Create(tex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            size);
        return _fallback;
    }
}
