using UnityEngine;

/// <summary>
/// A destructible world object (crate, furniture, etc.) that yields Wood or Metal
/// when the player holds the interact button for <see cref="holdDuration"/> seconds.
/// A world-space progress bar is created procedurally — no prefab setup required.
/// </summary>
public class HarvestableObject : MonoBehaviour, IHoldInteractable
{
    public enum ResourceType { Wood, Metal, Random }

    [Header("Harvesting")]
    [SerializeField] private float holdDuration = 2f;

    [Header("Loot")]
    [SerializeField] private ResourceType resourceType = ResourceType.Wood;
    [SerializeField] private int minAmount = 5;
    [SerializeField] private int maxAmount = 15;

    [Header("Progress Bar")]
    [Tooltip("Position of the bar relative to this object's pivot")]
    [SerializeField] private Vector3 barOffset = new Vector3(0f, 0.7f, 0f);

    // ── IHoldInteractable ─────────────────────────────────────────────────────

    public float HoldDuration => holdDuration;

    // ── Private bar state ─────────────────────────────────────────────────────

    private GameObject  _barRoot;       // background sprite + parent
    private Transform   _fillTransform;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake() => BuildProgressBar();

    // ── IHoldInteractable callbacks ───────────────────────────────────────────

    public void OnHoldStart()
    {
        _barRoot.SetActive(true);
        SetFill(0f);
    }

    public void OnHoldTick(float progress) => SetFill(progress);

    public void OnHoldCancelled()
    {
        _barRoot.SetActive(false);
        SetFill(0f);
    }

    public void OnHoldCompleted()
    {
        _barRoot.SetActive(false);
        GiveResource();
        Destroy(gameObject);
    }

    // ── Resource logic ────────────────────────────────────────────────────────

    private void GiveResource()
    {
        if (ResourceManager.Instance == null)
        {
            Debug.LogWarning("HarvestableObject: no ResourceManager in scene.");
            return;
        }

        string type = resourceType switch
        {
            ResourceType.Wood   => "Wood",
            ResourceType.Metal  => "Metal",
            ResourceType.Random => Random.value < 0.5f ? "Wood" : "Metal",
            _                   => "Wood"
        };

        int amount = Random.Range(minAmount, maxAmount + 1);
        ResourceManager.Instance.AddResource(type, amount);
        Debug.Log($"Harvested {amount} {type} from {name}.");
    }

    // ── Progress bar (procedural) ─────────────────────────────────────────────

    /// <summary>
    /// Creates a two-layer SpriteRenderer bar (dark background + green fill).
    /// The fill grows left-to-right by combining localPosition and localScale on X.
    /// Bar size in world space: 1 unit wide × 0.12 units tall.
    /// </summary>
    private void BuildProgressBar()
    {
        Sprite white = CreateWhiteSprite();

        // ── Background ────────────────────────────────────────────
        _barRoot = new GameObject("HarvestProgressBar");
        _barRoot.transform.SetParent(transform);
        _barRoot.transform.localPosition = barOffset;
        _barRoot.transform.localScale    = new Vector3(1f, 0.12f, 1f);

        SpriteRenderer bg = _barRoot.AddComponent<SpriteRenderer>();
        bg.sprite       = white;
        bg.color        = new Color(0.15f, 0.15f, 0.15f, 0.85f);
        bg.sortingOrder = 10;

        // ── Fill ──────────────────────────────────────────────────
        // Starts with scale.x = 0 (invisible). SetFill() drives both
        // localPosition.x and localScale.x so the bar grows from the left edge.
        GameObject fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(_barRoot.transform);
        fillGO.transform.localPosition = new Vector3(-0.5f, 0f, 0f);
        fillGO.transform.localScale    = new Vector3(0f, 1f, 1f);
        _fillTransform = fillGO.transform;

        SpriteRenderer fill = fillGO.AddComponent<SpriteRenderer>();
        fill.sprite       = white;
        fill.color        = new Color(0.2f, 0.85f, 0.3f, 1f);
        fill.sortingOrder = 11;

        _barRoot.SetActive(false);
    }

    /// <param name="t">Fill amount 0..1 — bar grows left to right.</param>
    private void SetFill(float t)
    {
        t = Mathf.Clamp01(t);
        // Center of the fill sprite sits at (-0.5 + t*0.5) in bg-local space,
        // keeping the left edge pinned to -0.5 regardless of t.
        _fillTransform.localPosition = new Vector3(-0.5f + t * 0.5f, 0f, 0f);
        _fillTransform.localScale    = new Vector3(t, 1f, 1f);
    }

    /// <summary>Creates a 1×1 white pixel sprite with PPU=1 (1 unit wide at scale 1).</summary>
    private static Sprite CreateWhiteSprite()
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }
}
