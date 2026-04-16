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

    private Vector2Int _tile;

    // ── Init ──────────────────────────────────────────────────────────────────

    public void Init(Vector2Int tile, BuildingDefinition def = null)
    {
        _tile      = tile;
        Definition = def;
        Level      = 0;
    }

    // ── Click selection ───────────────────────────────────────────────────────

    private void OnMouseDown()
    {
        // Ignore clicks that land on a UI element (hotbar, panels, etc.)
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        Current = this;
        OnSelected?.Invoke(this);
    }

    // ── Upgrading ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Spends the next tier's resources and applies the upgrade multipliers to
    /// any <see cref="Barricade"/> or <see cref="Turret"/> on this object.
    /// Returns false when already max level or resources are insufficient.
    /// </summary>
    public bool TryUpgrade()
    {
        if (Definition?.upgrades == null || Level >= Definition.upgrades.Length)
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

        Level++;
        OnSelected?.Invoke(this);   // refresh the info panel
        return true;
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private void OnDestroy()
    {
        if (Current == this)
        {
            Current = null;
            OnSelected?.Invoke(null);   // tell UI to close the panel
        }

        BuildingManager.Instance?.FreeTile(_tile);
    }
}
