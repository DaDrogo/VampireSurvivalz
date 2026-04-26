using System;
using UnityEngine;

/// <summary>
/// Attached at runtime to every building placed by <see cref="BuildingManager"/>.
/// Tracks the building definition, upgrade level, and frees the tile when destroyed.
/// Click the object in-game to select it for the upgrade panel.
/// </summary>
public class PlacedBuilding : MonoBehaviour
{
    // ── Selection ─────────────────────────────────────────────────────────────

    /// <summary>The last placed building the player clicked, or null if none.</summary>
    public static PlacedBuilding Current { get; private set; }

    /// <summary>Fired when a placed building is clicked, or null when it is deselected/destroyed.</summary>
    public static event Action<PlacedBuilding> OnSelected;

    // ── Data ──────────────────────────────────────────────────────────────────

    public BuildingDefinition Definition { get; private set; }

    /// <summary>0 = base level; equals Definition.upgrades.Length when fully upgraded.</summary>
    public int Level { get; private set; }

    private Vector2Int    _tile;
    private bool          _skipFreeTile;

    // ── Selection glow ────────────────────────────────────────────────────────

    private SpriteRenderer _glowSR;
    private static readonly Color GlowTint = new Color(1f, 0.82f, 0.2f);

    // ── Init ──────────────────────────────────────────────────────────────────

    private void Awake()
    {
        BuildGlow();
        OnSelected += HandleGlobalSelected;
    }

    public void Init(Vector2Int tile, BuildingDefinition def = null)
    {
        _tile      = tile;
        Definition = def;
        Level      = 0;
    }

    private void Update()
    {
        if (_glowSR == null || !_glowSR.gameObject.activeSelf) return;
        float alpha = 0.4f + 0.35f * Mathf.Sin(Time.time * 5f);
        _glowSR.color = new Color(GlowTint.r, GlowTint.g, GlowTint.b, alpha);
    }

    private void BuildGlow()
    {
        SpriteRenderer src = GetComponentInChildren<SpriteRenderer>();

        GameObject go      = new GameObject("Glow");
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * 1.3f;

        _glowSR                = go.AddComponent<SpriteRenderer>();
        _glowSR.sprite         = src != null ? src.sprite : MakeWhiteSquare();
        _glowSR.sortingLayerID = src != null ? src.sortingLayerID : 0;
        _glowSR.sortingOrder   = src != null ? src.sortingOrder - 1 : -1;
        _glowSR.color          = new Color(GlowTint.r, GlowTint.g, GlowTint.b, 0f);
        go.SetActive(false);
    }

    private void HandleGlobalSelected(PlacedBuilding pb)
    {
        if (_glowSR == null) return;
        _glowSR.gameObject.SetActive(pb == this);
    }

    private static Sprite MakeWhiteSquare()
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    public void Select()
    {
        Current = this;
        OnSelected?.Invoke(this);
    }

    public static void Deselect()
    {
        Current = null;
        OnSelected?.Invoke(null);
    }

    // ── Upgrading ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Spends the next tier's resources and applies the upgrade multipliers to
    /// any <see cref="Barricade"/> or <see cref="Turret"/> on this object.
    /// Returns false when already max level or resources are insufficient.
    /// </summary>
    public bool TryUpgrade()
    {
        // Citadel manages its own tier system and costs
        if (TryGetComponent(out Citadel citadel))
        {
            bool ok = citadel.TryUpgrade();
            if (ok) OnSelected?.Invoke(this);
            return ok;
        }

        if (Definition?.upgrades == null || Level >= Definition.upgrades.Length)
            return false;

        // Citadel tier gates upgrade level: Tier 2 allows level 1, Tier 3 allows level 2, etc.
        if (Citadel.Instance != null && Level >= Citadel.Instance.MaxBuildingLevel)
            return false;

        BuildingUpgradeTier tier = Definition.upgrades[Level];

        if (ResourceManager.Instance == null) return false;
        if (ResourceManager.Instance.Wood  < tier.woodCost ||
            ResourceManager.Instance.Metal < tier.metalCost)
            return false;

        ResourceManager.Instance.AddResource("Wood",  -tier.woodCost);
        ResourceManager.Instance.AddResource("Metal", -tier.metalCost);

        if (TryGetComponent(out Barricade barricade))
            barricade.ApplyUpgrade(tier.healthMult);

        if (TryGetComponent(out Turret turret))
            turret.ApplyUpgrade(tier.healthMult, tier.fireRateMult, tier.rangeMult);

        if (TryGetComponent(out ResourceProducer producer))
            producer.ApplyUpgrade(tier.healthMult, tier.fireRateMult);

        Level++;
        OnSelected?.Invoke(this);   // refresh the info panel
        return true;
    }

    // ── Choice upgrade ────────────────────────────────────────────────────────

    /// <summary>
    /// Transforms this building into the prefab specified by <paramref name="choice"/>.
    /// Deducts resources, spawns the replacement, then destroys self.
    /// Returns false when resources are insufficient or the choice has no prefab.
    /// </summary>
    public bool TryUpgradeToChoice(BuildingUpgradeChoice choice)
    {
        if (choice?.prefab == null) return false;
        if (ResourceManager.Instance == null) return false;
        if (ResourceManager.Instance.Wood  < choice.woodCost ||
            ResourceManager.Instance.Metal < choice.metalCost) return false;

        ResourceManager.Instance.AddResource("Wood",  -choice.woodCost);
        ResourceManager.Instance.AddResource("Metal", -choice.metalCost);

        // Close the panel before destroying so UI doesn't see a dangling reference
        if (Current == this) { Current = null; OnSelected?.Invoke(null); }

        _skipFreeTile = true;   // SwapBuilding re-registers the tile; don't free it on destroy
        BuildingManager.Instance?.SwapBuilding(_tile, transform.position, choice);
        Destroy(gameObject);
        return true;
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private void OnDestroy()
    {
        OnSelected -= HandleGlobalSelected;

        if (Current == this)
        {
            Current = null;
            OnSelected?.Invoke(null);   // tell UI to close the panel
        }

        if (!_skipFreeTile)
            BuildingManager.Instance?.FreeTile(_tile);

        BuildingManager.Instance?.UntrackBuilding(this);
    }
}
