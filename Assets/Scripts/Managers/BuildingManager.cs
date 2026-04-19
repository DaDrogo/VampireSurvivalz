using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>One upgrade tier for a building. Assign in the Inspector on each BuildingDefinition.</summary>
[Serializable]
public class BuildingUpgradeTier
{
    public string label         = "Upgrade";
    public int    woodCost;
    public int    metalCost;
    [Min(1f)] public float healthMult   = 1f;
    [Min(1f)] public float fireRateMult = 1f;
    [Min(1f)] public float rangeMult    = 1f;
}

/// <summary>
/// One option shown in the upgrade-choice panel.
/// Replaces the placed building with a new prefab when selected.
/// </summary>
[Serializable]
public class BuildingUpgradeChoice
{
    public string     label;
    public GameObject prefab;
    public int        woodCost;
    public int        metalCost;
    [TextArea(1, 2)]
    public string     description;
}

/// <summary>
/// Defines one placeable building type.
/// Assign in the BuildingManager Inspector — index 0 = key 1, index 1 = key 2, etc.
/// </summary>
[Serializable]
public class BuildingDefinition
{
    public string       buildingName;
    public GameObject   prefab;
    [Tooltip("World-space size used for the OverlapBox placement check")]
    public Vector2      footprint    = Vector2.one;
    public int          woodCost;
    public int          metalCost;
    [TextArea(1, 2)]
    public string       description;
    [Tooltip("Always included in every loadout — player cannot remove it in setup.")]
    public bool         isBasic      = false;
    [Tooltip("The Citadel building — always slot 1, auto-spawned by GameManager.")]
    public bool         isCitadel    = false;
    [Tooltip("Stat-based upgrade tiers (Barricade / Turret style).")]
    public BuildingUpgradeTier[] upgrades;
    [Tooltip("If set, the upgrade button is replaced with a choice panel — pick one to transform this building.")]
    public BuildingUpgradeChoice[] upgradeChoices;
}

/// <summary>
/// Press 1 / 2 / … to enter placement mode.
/// A ghost follows the cursor — green = valid, red = blocked or too expensive.
/// Left-click to place, Right-click or Escape to cancel.
/// Stays in placement mode after placing so you can build multiple in a row.
/// </summary>
[DefaultExecutionOrder(-5)]   // Start() before UIManager (0) so hotbar sees filtered buildings
public class BuildingManager : MonoBehaviour
{
    public static BuildingManager Instance { get; private set; }

    [Header("Buildings  (index 0 → key 1,  index 1 → key 2, …)")]
    [SerializeField] private BuildingDefinition[] buildings;

    [Header("Grid Snapping")]
    [Tooltip("Assign a GameObject with a Grid component to snap to its cell centres. " +
             "Leave empty to use world-aligned 1×1 tile snapping.")]
    [SerializeField] private Grid placementGrid;

    [Header("Placement")]
    [Tooltip("Layers that block placement — include your structure and wall layers")]
    [SerializeField] private LayerMask blockingLayers;
    [SerializeField] private Color validColor   = new Color(0.2f, 1f,  0.2f, 0.45f);
    [SerializeField] private Color invalidColor = new Color(1f,  0.2f, 0.2f, 0.45f);

    // ── State ─────────────────────────────────────────────────────────────────

    public bool IsPlacing => _isPlacing;

    public static event Action<int>           OnSelectionChanged;
    public static event Action<PlacedBuilding> OnBuildingPlaced;

    public int BuildingCount => buildings?.Length ?? 0;
    public BuildingDefinition GetDefinition(int i) => buildings[i];

    private readonly List<PlacedBuilding> _allPlaced = new();
    public IReadOnlyList<PlacedBuilding> AllPlaced => _allPlaced;

    public void UntrackBuilding(PlacedBuilding pb) => _allPlaced.Remove(pb);

    private BuildingDefinition      _active;
    private int                     _activeIndex = -1;
    private GameObject              _ghost;
    private SpriteRenderer          _ghostSR;
    private bool                    _isPlacing;

    // Tile-coordinate set — one entry per occupied 1×1 cell
    private readonly HashSet<Vector2Int> _occupiedTiles = new();

    private static readonly Key[] Hotkeys =
        { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        ApplyLoadout();
    }

    /// <summary>
    /// Re-orders and trims the buildings array to match the player's selected loadout
    /// from PersistentDataManager. Called once at scene start, before UIManager.Start().
    /// </summary>
    private void ApplyLoadout()
    {
        if (buildings == null || buildings.Length == 0) return;

        string[] names = PersistentDataManager.Instance?.SelectedBuildingNames;

        // Name-based matching: preserves order citadel → basic → selected
        if (names != null && names.Length > 0)
        {
            var nameSet = new HashSet<string>(names, System.StringComparer.OrdinalIgnoreCase);
            var result  = new List<BuildingDefinition>();

            // Citadel first
            foreach (var b in buildings)
                if (b != null && b.isCitadel && nameSet.Contains(b.buildingName)) result.Add(b);

            // Basic (non-citadel)
            foreach (var b in buildings)
                if (b != null && b.isBasic && !b.isCitadel && nameSet.Contains(b.buildingName)) result.Add(b);

            // Selected non-fixed, in the order the player chose them
            foreach (string n in names)
            {
                foreach (var b in buildings)
                {
                    if (b != null && !b.isCitadel && !b.isBasic
                        && string.Equals(b.buildingName, n, System.StringComparison.OrdinalIgnoreCase))
                    { result.Add(b); break; }
                }
            }

            if (result.Count > 0) { buildings = result.ToArray(); return; }
        }

        // Fallback: include all buildings (no setup data yet)
        var fallback = new List<BuildingDefinition>();
        foreach (var b in buildings) if (b != null && b.isCitadel)             fallback.Add(b);
        foreach (var b in buildings) if (b != null && b.isBasic && !b.isCitadel) fallback.Add(b);
        foreach (var b in buildings) if (b != null && !b.isCitadel && !b.isBasic) fallback.Add(b);
        if (fallback.Count > 0) buildings = fallback.ToArray();
    }

    private void Update()
    {
        // Don't process input during Game Over
        if (GameManager.Instance?.CurrentState == GameManager.GameState.GameOver) return;

        HandleHotkeys();

        if (!_isPlacing) return;

        MoveGhost();
        UpdateGhostColor();

        if (Mouse.current.leftButton.wasPressedThisFrame)  TryPlace();
        if (Mouse.current.rightButton.wasPressedThisFrame) CancelPlacement();
        if (Keyboard.current.escapeKey.wasPressedThisFrame) CancelPlacement();
    }

    // ── Hotkeys ───────────────────────────────────────────────────────────────

    private void HandleHotkeys()
    {
        for (int i = 0; i < Mathf.Min(buildings.Length, Hotkeys.Length); i++)
        {
            if (!Keyboard.current[Hotkeys[i]].wasPressedThisFrame) continue;
            SelectBuilding(i);
            return;
        }
    }

    /// <summary>Enters placement mode for the building at <paramref name="index"/>.
    /// Pressing the same index again cancels (toggle). Called by hotkeys and the UI hotbar.</summary>
    public void SelectBuilding(int index)
    {
        if (index < 0 || index >= buildings.Length) return;
        if (_isPlacing && _active == buildings[index])
            CancelPlacement();
        else
            BeginPlacement(buildings[index]);
    }

    // ── Placement flow ────────────────────────────────────────────────────────

    private void BeginPlacement(BuildingDefinition def)
    {
        if (def.prefab == null)
        {
            Debug.LogWarning($"BuildingManager: '{def.buildingName}' has no prefab assigned.");
            return;
        }

        CancelPlacement();   // destroy any previous ghost first
        _active      = def;
        _activeIndex = System.Array.IndexOf(buildings, def);
        _isPlacing   = true;
        _ghost       = SpawnGhost(def);
        OnSelectionChanged?.Invoke(_activeIndex);
    }

    private void TryPlace()
    {
        Vector2 pos = SnappedMouseWorldPos();

        if (!CanAfford(_active))
        {
            Debug.Log($"BuildingManager: not enough resources to build {_active.buildingName}  " +
                      $"(need {_active.woodCost} Wood / {_active.metalCost} Metal).");
            return;
        }

        if (!IsAreaClear(pos, _active.footprint))
        {
            Debug.Log($"BuildingManager: placement blocked at {pos}.");
            return;
        }

        if (IsOnWall(pos))
        {
            Debug.Log($"BuildingManager: placement on wall at {pos}.");
            return;
        }

        if (IsTileOccupied(pos))
        {
            Debug.Log($"BuildingManager: tile already occupied at {pos}.");
            return;
        }

        if (!IsWithinCitadelRange(pos))
        {
            Debug.Log("BuildingManager: placement out of Citadel build radius.");
            return;
        }

        Vector2Int tile   = WorldToTileCoord(pos);
        GameObject placed = Instantiate(_active.prefab, pos, Quaternion.identity);
        var pb = placed.AddComponent<PlacedBuilding>();
        pb.Init(tile, _active);
        _occupiedTiles.Add(tile);
        _allPlaced.Add(pb);
        OnBuildingPlaced?.Invoke(pb);

        // Barricades start in Ghost state by default; build them immediately
        // since the player already paid through BuildingManager.
        if (placed.TryGetComponent(out Barricade barricade))
            barricade.BuildImmediate();

        SpendResources(_active);
        PersistentDataManager.Instance?.AddBuilding();
        // Intentionally stay in placement mode so the player can keep building.
    }

    public void CancelPlacement()
    {
        if (_ghost != null) Destroy(_ghost);
        _ghost       = null;
        _ghostSR     = null;
        _active      = null;
        _activeIndex = -1;
        if (_isPlacing)
            OnSelectionChanged?.Invoke(-1);
        _isPlacing = false;
    }

    // ── Ghost ─────────────────────────────────────────────────────────────────

    private GameObject SpawnGhost(BuildingDefinition def)
    {
        var go = new GameObject($"Ghost_{def.buildingName}");
        go.layer = 2; // Ignore Raycast — must not hit our own OverlapBox

        _ghostSR              = go.AddComponent<SpriteRenderer>();
        _ghostSR.sortingOrder = 20;

        // Mirror the prefab's sprite and scale; fall back to a plain square
        var prefabSR = def.prefab.GetComponentInChildren<SpriteRenderer>();
        if (prefabSR != null)
        {
            _ghostSR.sprite         = prefabSR.sprite;
            go.transform.localScale = def.prefab.transform.localScale;
        }
        else
        {
            _ghostSR.sprite         = WhiteSquareSprite();
            go.transform.localScale = new Vector3(def.footprint.x, def.footprint.y, 1f);
        }

        return go;
    }

    private void MoveGhost() => _ghost.transform.position = SnappedMouseWorldPos();

    private void UpdateGhostColor()
    {
        Vector2 pos   = SnappedMouseWorldPos();
        bool    valid = CanAfford(_active)
                     && IsAreaClear(pos, _active.footprint)
                     && !IsOnWall(pos)
                     && !IsTileOccupied(pos)
                     && IsWithinCitadelRange(pos);
        _ghostSR.color = valid ? validColor : invalidColor;
    }

    // ── Validation ────────────────────────────────────────────────────────────

    private bool CanAfford(BuildingDefinition def)
    {
        if (ResourceManager.Instance == null) return false;
        return ResourceManager.Instance.Wood  >= def.woodCost
            && ResourceManager.Instance.Metal >= def.metalCost;
    }

    /// <summary>
    /// Returns true when no collider on <see cref="blockingLayers"/> overlaps
    /// the placement footprint. The box is shrunk by 10 % so objects can be
    /// placed flush against each other without being blocked by their own edges.
    /// </summary>
    private bool IsAreaClear(Vector2 center, Vector2 size)
    {
        Collider2D hit = Physics2D.OverlapBox(center, size * 0.9f, 0f, blockingLayers);
        return hit == null;
    }

    /// <summary>
    /// Returns true when the snapped position lands on a wall tile according to
    /// the pathfinding grid. Works independently of layer masks so it always
    /// catches wall tiles even if they share a layer with walkable geometry.
    /// </summary>
    private static bool IsWithinCitadelRange(Vector2 pos)
    {
        if (Citadel.Instance == null) return true;
        return Vector2.Distance(pos, Citadel.Instance.transform.position) <= Citadel.Instance.BuildRadius;
    }

    private static bool IsOnWall(Vector2 snappedPos)
    {
        PathNode node = PathfindingGrid.Instance?.NodeFromWorld(snappedPos);
        return node != null && node.IsWall;
    }

    private static void SpendResources(BuildingDefinition def)
    {
        ResourceManager.Instance.AddResource("Wood",  -def.woodCost);
        ResourceManager.Instance.AddResource("Metal", -def.metalCost);
    }

    // ── Grid snapping ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the centre of the 1×1 tile that contains <paramref name="worldPosition"/>.
    /// Uses <see cref="placementGrid"/> when assigned; otherwise falls back to
    /// world-aligned integer-grid snapping (floor + 0.5 on each axis).
    /// </summary>
    public Vector3 GetNearestTileCenter(Vector3 worldPosition)
    {
        if (placementGrid != null)
        {
            Vector3Int cell = placementGrid.WorldToCell(worldPosition);
            return placementGrid.GetCellCenterWorld(cell);
        }

        // Manual fallback — snaps to centres of a 1×1 grid at world origin
        return new Vector3(
            Mathf.Floor(worldPosition.x) + 0.5f,
            Mathf.Floor(worldPosition.y) + 0.5f,
            0f);
    }

    /// <summary>Raw screen-to-world mouse position (z = 0).</summary>
    private static Vector3 MouseWorldPos()
    {
        Vector3 p = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        p.z = 0f;
        return p;
    }

    /// <summary>Mouse world position snapped to the nearest tile centre.</summary>
    private Vector2 SnappedMouseWorldPos() => GetNearestTileCenter(MouseWorldPos());

    // ── Tile occupancy ────────────────────────────────────────────────────────

    /// <summary>Converts a snapped world position to integer tile coordinates.</summary>
    private Vector2Int WorldToTileCoord(Vector2 snappedWorldPos)
    {
        if (placementGrid != null)
        {
            Vector3Int cell = placementGrid.WorldToCell(snappedWorldPos);
            return new Vector2Int(cell.x, cell.y);
        }
        return new Vector2Int(Mathf.FloorToInt(snappedWorldPos.x), Mathf.FloorToInt(snappedWorldPos.y));
    }

    private bool IsTileOccupied(Vector2 snappedWorldPos)
        => _occupiedTiles.Contains(WorldToTileCoord(snappedWorldPos));

    /// <summary>
    /// Marks the tile at <paramref name="worldPos"/> as occupied and returns its
    /// coordinate so the caller can pass it to <see cref="PlacedBuilding.Init"/>.
    /// Use this to block building placement on spawned resources or other world objects.
    /// </summary>
    public Vector2Int RegisterTile(Vector2 worldPos)
    {
        Vector2Int tile = WorldToTileCoord(worldPos);
        _occupiedTiles.Add(tile);
        return tile;
    }

    /// <summary>
    /// Called by <see cref="PlacedBuilding"/> when a building or occupant is destroyed,
    /// freeing the tile for future placement.
    /// </summary>
    public void FreeTile(Vector2Int tile) => _occupiedTiles.Remove(tile);

    /// <summary>
    /// Spawns a replacement building at <paramref name="worldPos"/> on the same tile.
    /// The tile stays occupied (the old building's OnDestroy will call FreeTile, but
    /// we add the tile back here so it is never truly free between the two buildings).
    /// </summary>
    public void SwapBuilding(Vector2Int tile, Vector3 worldPos, BuildingUpgradeChoice choice)
    {
        if (choice?.prefab == null) return;

        GameObject placed = Instantiate(choice.prefab, worldPos, Quaternion.identity);
        placed.AddComponent<PlacedBuilding>().Init(tile, null);   // null = fully-upgraded, no further choices
        _occupiedTiles.Add(tile);   // re-register so FreeTile from old building doesn't leave it open
    }

    private static Sprite WhiteSquareSprite()
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }

    // ── Editor gizmo ─────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (!_isPlacing || _active == null) return;
        Vector2 pos   = SnappedMouseWorldPos();
        bool    valid = CanAfford(_active) && IsAreaClear(pos, _active.footprint)
                     && !IsOnWall(pos) && !IsTileOccupied(pos);
        Gizmos.color  = valid ? new Color(0f, 1f, 0f, 0.25f) : new Color(1f, 0f, 0f, 0.25f);
        Gizmos.DrawWireCube(pos, _active.footprint);
    }
}
