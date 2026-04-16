using UnityEngine;

/// <summary>
/// Attach to any GameObject that has an <see cref="IDamageable"/> component.
/// Procedurally builds a world-space health bar (background + fill) as child
/// objects and keeps it updated every frame.
///
/// Usage: add this component to the Barricade, Turret, and Enemy prefabs.
/// Tweak <see cref="barSize"/> and <see cref="verticalOffset"/> in the Inspector.
/// </summary>
public class WorldHealthBar : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Width and height of the bar in world units.")]
    [SerializeField] private Vector2 barSize = new(1f, 0.12f);
    [Tooltip("Offset from the object's pivot, in local space.")]
    [SerializeField] private Vector3 verticalOffset = new(0f, 0.7f, 0f);

    [Header("Colours")]
    [SerializeField] private Color fullColor  = new(0.15f, 0.85f, 0.15f, 1f);   // green
    [SerializeField] private Color emptyColor = new(0.85f, 0.15f, 0.15f, 1f);   // red
    [SerializeField] private Color bgColor    = new(0.1f,  0.1f,  0.1f,  0.8f);

    [Header("Behaviour")]
    [Tooltip("Hide the bar while at full health.")]
    [SerializeField] private bool hideWhenFull = true;
    [Tooltip("Sorting layer order for the bar sprites.")]
    [SerializeField] private int sortingOrder = 50;

    // ── Runtime refs ──────────────────────────────────────────────────────────

    private IDamageable   _target;
    private Transform     _barRoot;
    private Transform     _fill;
    private SpriteRenderer _fillSR;
    private float          _lastFraction = -1f;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        _target = GetComponent<IDamageable>();
        if (_target == null)
        {
            Debug.LogWarning($"[WorldHealthBar] {name} has no IDamageable — component disabled.", this);
            enabled = false;
            return;
        }
        BuildBar();
    }

    private void LateUpdate()
    {
        float fraction = _target.MaxHealth > 0f
            ? Mathf.Clamp01(_target.CurrentHealth / _target.MaxHealth)
            : 0f;

        // Only refresh visuals when health actually changed
        if (Mathf.Approximately(fraction, _lastFraction)) return;
        _lastFraction = fraction;

        RefreshBar(fraction);
    }

    // ── Bar construction ──────────────────────────────────────────────────────

    private void BuildBar()
    {
        // Root — fixed local offset above the object
        var root = new GameObject("HealthBar");
        root.transform.SetParent(transform);
        root.transform.localPosition = verticalOffset;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale    = Vector3.one;
        _barRoot = root.transform;

        // Background
        CreateQuad("BG", bgColor, sortingOrder, barSize)
            .SetParent(_barRoot, false);

        // Fill — same size initially; width is driven in RefreshBar
        var fillGO = CreateQuad("Fill", fullColor, sortingOrder + 1, barSize);
        fillGO.SetParent(_barRoot, false);
        _fill   = fillGO;
        _fillSR = fillGO.GetComponent<SpriteRenderer>();

        RefreshBar(1f);   // start at full
    }

    // ── Per-frame update ──────────────────────────────────────────────────────

    private void RefreshBar(float fraction)
    {
        // Lerp colour from red → green
        _fillSR.color = Color.Lerp(emptyColor, fullColor, fraction);

        // Scale fill width; keep its left edge fixed to the bar's left edge.
        //   Center of fill (pivot = 0.5) must sit at:
        //   x = leftEdge + fillWidth * 0.5
        //     = -barSize.x * 0.5 + barSize.x * fraction * 0.5
        float fillWidth = barSize.x * fraction;
        _fill.localPosition = new Vector3(
            -barSize.x * 0.5f + fillWidth * 0.5f,
            0f,
            -0.01f);   // z slightly in front of background
        _fill.localScale = new Vector3(fillWidth, barSize.y, 1f);

        // Hide when full (if configured) or when there is no health at all
        bool show = fraction > 0f && !(hideWhenFull && fraction >= 1f);
        _barRoot.gameObject.SetActive(show);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Transform CreateQuad(string quadName, Color color, int order, Vector2 size)
    {
        var go = new GameObject(quadName);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = WhiteSquare();
        sr.color        = color;
        sr.sortingOrder = order;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        return go.transform;
    }

    private static Sprite WhiteSquare()
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }
}
