using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The player's home base. Central to the base-building loop.
///
/// Tier system (1–3):
///   Tier controls the maximum upgrade level buildings can reach.
///   Tier 1 = no upgrades; Tier 2 = buildings can upgrade once; Tier 3 = twice.
///   Player upgrades the Citadel by holding Interact near it.
///
/// Aura:
///   Buffs all buildings in the same room, type determined by the selected character.
///   Buff is applied once per tier gained (stackable via multiplier).
///
/// Loss condition:
///   If the Citadel is destroyed, the game is over.
///
/// Build radius:
///   BuildingManager refuses placement outside CitadelBuildRadius.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class Citadel : Building
{
    public static Citadel Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("UI Theme")]
    [SerializeField] private UITheme _theme;

    [Header("Tiers")]
    [Tooltip("Upgrade costs from Tier 1→2 (index 0) and Tier 2→3 (index 1).")]
    [SerializeField] private CitadelTierData[] tierUpgrades =
    {
        new() { label = "Tier 2",  woodCost = 60,  metalCost = 40 },
        new() { label = "Tier 3",  woodCost = 120, metalCost = 80 },
    };

    [Header("Build Radius")]
    [Tooltip("Max world-space distance from the Citadel at which buildings can be placed.")]
    [SerializeField] private float buildRadius = 12f;
    [Tooltip("Width of the radius indicator circle line in world units.")]
    [SerializeField] private float radiusLineWidth = 0.08f;

    [Header("Production")]
    [Tooltip("\"Wood\", \"Metal\", or \"Both\"")]
    [SerializeField] private string outputType         = "Both";
    [SerializeField] private int    amountPerCycle     = 3;
    [SerializeField] private float  productionInterval = 15f;

    // ── Events ────────────────────────────────────────────────────────────────

    public event Action<int> OnTierChanged;

    // ── Public state ──────────────────────────────────────────────────────────

    public int  Tier    { get; private set; } = 1;
    public int  MaxTier => tierUpgrades.Length + 1;

    /// <summary>Buildings may only be upgraded up to this level (0-based). Tier-1.</summary>
    public int MaxBuildingLevel => Tier - 1;

    public float BuildRadius
    {
        get
        {
            float r = buildRadius;
            for (int i = 0; i < Tier - 1 && i < tierUpgrades.Length; i++)
                r += tierUpgrades[i].buildRadiusBonus;
            return r;
        }
    }

    public string ProductionOutput   => outputType;
    public int    ProductionAmount   => amountPerCycle;
    public float  ProductionInterval => productionInterval;

    public CitadelTierData GetNextTierData() =>
        Tier < MaxTier ? tierUpgrades[Tier - 1] : null;

    // ── Private ───────────────────────────────────────────────────────────────

    // Tracks the tier at which the aura was last applied per building
    private readonly Dictionary<PlacedBuilding, int> _auraAppliedTier = new();

    // UI hint
    private GameObject      _hintGO;
    private TextMeshProUGUI _hintText;

    // Radius indicator
    private LineRenderer _radiusLine;
    private const int    RADIUS_SEGMENTS = 64;

    // Production
    private float _productionTimer;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    protected override void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        base.Awake();
        if (TryGetComponent(out SpriteRenderer sr))
            sr.sortingOrder = 5;
    }

    private void Start()
    {
        BuildingManager.OnBuildingPlaced += OnNewBuildingPlaced;
        BuildHint();
        BuildRadiusIndicator();
    }

    private void OnDestroy()
    {
        BuildingManager.OnBuildingPlaced -= OnNewBuildingPlaced;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (ResourceManager.Instance == null) return;
        _productionTimer += Time.deltaTime;
        if (_productionTimer >= productionInterval)
        {
            _productionTimer -= productionInterval;
            Produce();
        }
    }

    private void Produce()
    {
        switch (outputType.ToLowerInvariant())
        {
            case "wood":
                ResourceManager.Instance.AddResource("Wood", amountPerCycle);
                FloatingTextManager.Instance?.Spawn(transform.position, amountPerCycle, FloatingTextManager.ResourceKind.Wood);
                break;
            case "metal":
                ResourceManager.Instance.AddResource("Metal", amountPerCycle);
                FloatingTextManager.Instance?.Spawn(transform.position, amountPerCycle, FloatingTextManager.ResourceKind.Metal);
                break;
            default: // "both"
                ResourceManager.Instance.AddResource("Wood",  amountPerCycle);
                ResourceManager.Instance.AddResource("Metal", amountPerCycle);
                FloatingTextManager.Instance?.Spawn(transform.position + new Vector3(-0.2f, 0f, 0f), amountPerCycle, FloatingTextManager.ResourceKind.Wood);
                FloatingTextManager.Instance?.Spawn(transform.position + new Vector3( 0.2f, 0f, 0f), amountPerCycle, FloatingTextManager.ResourceKind.Metal);
                break;
        }
    }

    // ── Upgrade ───────────────────────────────────────────────────────────────

    public bool TryUpgrade()
    {
        if (Tier >= MaxTier) return false;

        CitadelTierData cost = tierUpgrades[Tier - 1];
        ResourceManager rm = ResourceManager.Instance;
        if (rm == null) return false;
        if (rm.Wood < cost.woodCost || rm.Metal < cost.metalCost)
            return false;

        rm.AddResource("Wood",  -cost.woodCost);
        rm.AddResource("Metal", -cost.metalCost);
        Tier++;
        OnTierChanged?.Invoke(Tier);

        amountPerCycle     = Mathf.Max(1, Mathf.RoundToInt(amountPerCycle * cost.productionMult));
        productionInterval = Mathf.Max(1f, productionInterval / cost.productionMult);

        ApplyAuraToRoomBuildings();
        UpdateVisual();
        return true;
    }

    // ── Aura ──────────────────────────────────────────────────────────────────

    private void OnNewBuildingPlaced(PlacedBuilding building) => ApplyAuraToBuilding(building);

    private void ApplyAuraToRoomBuildings()
    {
        if (BuildingManager.Instance == null) return;
        foreach (PlacedBuilding pb in BuildingManager.Instance.AllPlaced)
            ApplyAuraToBuilding(pb);

        // Prune destroyed entries
        var dead = new List<PlacedBuilding>();
        foreach (var kv in _auraAppliedTier)
            if (kv.Key == null) dead.Add(kv.Key);
        foreach (var k in dead) _auraAppliedTier.Remove(k);
    }

    private void ApplyAuraToBuilding(PlacedBuilding building)
    {
        if (building == null) return;
        if (!IsInCitadelRoom(building.transform.position)) return;

        CharacterDefinition ch = CharacterApplicator.Instance?.ActiveCharacter;
        if (ch == null || ch.auraType == CitadelAuraType.None) return;

        int lastTier = _auraAppliedTier.GetValueOrDefault(building, 1);
        if (lastTier >= Tier) return;

        float mult = 1f + ch.auraStrengthPerTier;
        for (int t = lastTier; t < Tier; t++)
            ApplyMultiplier(building, ch.auraType, mult);

        _auraAppliedTier[building] = Tier;
    }

    private static void ApplyMultiplier(PlacedBuilding building, CitadelAuraType auraType, float mult)
    {
        switch (auraType)
        {
            case CitadelAuraType.BarricadeHealth:
                if (building.TryGetComponent(out Barricade barricade))
                    barricade.ApplyUpgrade(mult);
                break;

            case CitadelAuraType.TurretFireRate:
                if (building.TryGetComponent(out Turret turretFR))
                    turretFR.ApplyUpgrade(1f, mult, 1f);
                break;

            case CitadelAuraType.TurretRange:
                if (building.TryGetComponent(out Turret turretRng))
                    turretRng.ApplyUpgrade(1f, 1f, mult);
                break;

            case CitadelAuraType.ResourceProduction:
                if (building.TryGetComponent(out ResourceProducer producer))
                    producer.ApplyUpgrade(1f, mult);
                break;
        }
    }

    private bool IsInCitadelRoom(Vector2 pos)
    {
        HouseManager hm = HouseManager.Instance;
        if (hm == null) return true; // no room system — always in range
        Room myRoom   = hm.GetRoomAt(transform.position);
        Room bldRoom  = hm.GetRoomAt(pos);
        return myRoom != null && myRoom == bldRoom;
    }

    // ── Destruction ───────────────────────────────────────────────────────────

    protected override void OnDied()
    {
        GameManager.Instance?.TriggerGameOver();
        base.OnDied();
    }

    // ── Visual ────────────────────────────────────────────────────────────────

    private void UpdateVisual()
    {
        if (!TryGetComponent(out SpriteRenderer sr)) return;
        sr.color = Tier switch
        {
            1 => Color.white,
            2 => new Color(1f, 0.85f, 0.3f),
            3 => new Color(0.3f, 1f, 0.5f),
            _ => Color.white,
        };
        UpdateRadiusIndicator();
    }

    // ── Radius indicator ──────────────────────────────────────────────────────

    private void BuildRadiusIndicator()
    {
        var go = new GameObject("RadiusIndicator");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;

        _radiusLine                  = go.AddComponent<LineRenderer>();
        _radiusLine.useWorldSpace    = false;
        _radiusLine.loop             = true;
        _radiusLine.positionCount    = RADIUS_SEGMENTS;
        _radiusLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _radiusLine.receiveShadows   = false;

        // Use a plain unlit material so it works without a texture
        _radiusLine.material         = new Material(Shader.Find("Sprites/Default"));
        _radiusLine.sortingOrder     = 2;

        UpdateRadiusIndicator();
    }

    private void UpdateRadiusIndicator()
    {
        if (_radiusLine == null) return;

        float r = BuildRadius;
        _radiusLine.startWidth = radiusLineWidth;
        _radiusLine.endWidth   = radiusLineWidth;

        Color c = Tier switch
        {
            2 => new Color(1f, 0.85f, 0.3f, 0.65f),
            3 => new Color(0.3f, 1f, 0.5f, 0.65f),
            _ => new Color(0.4f, 0.8f, 1f, 0.55f),
        };
        _radiusLine.startColor = c;
        _radiusLine.endColor   = c;

        for (int i = 0; i < RADIUS_SEGMENTS; i++)
        {
            float angle = i * (2f * Mathf.PI / RADIUS_SEGMENTS);
            _radiusLine.SetPosition(i, new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0f));
        }
    }

    // ── World-space hint label ────────────────────────────────────────────────

    private void BuildHint()
    {
        _hintGO = new GameObject("CitadelHint");
        _hintGO.transform.SetParent(transform, false);
        _hintGO.transform.localPosition = new Vector3(0f, 1.2f, 0f);

        var canvas         = _hintGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;

        var rt             = _hintGO.GetComponent<RectTransform>();
        rt.sizeDelta       = new Vector2(4f, 1f);
        rt.localScale      = Vector3.one * 0.025f;

        _hintText          = _hintGO.AddComponent<TextMeshProUGUI>();
        _hintText.font     = _theme?.font != null
            ? _theme.font
            : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        _hintText.fontSize = 28f;
        _hintText.alignment = TextAlignmentOptions.Center;
        _hintText.color    = new Color(1f, 0.9f, 0.3f);
        _hintText.text     = "";
        _hintGO.SetActive(false);
    }

    private void ShowHint(string msg)
    {
        if (_hintGO == null) return;
        _hintText.text = msg;
        _hintGO.SetActive(true);
    }

    private void HideHint()
    {
        if (_hintGO != null) _hintGO.SetActive(false);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, BuildRadius);
    }
}

[Serializable]
public class CitadelTierData
{
    public string label            = "Upgrade";
    public int    woodCost;
    public int    metalCost;
    [Tooltip("How much the build radius grows when this tier is reached.")]
    public float  buildRadiusBonus  = 4f;
    [Tooltip("Multiplier applied to production amount and interval on reaching this tier (1.3 = +30% more, 30% faster).")]
    public float  productionMult    = 1.3f;
}
