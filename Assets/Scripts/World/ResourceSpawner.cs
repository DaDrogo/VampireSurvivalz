using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// One entry in the spawn list — a prefab plus how many to place and what overlap radius to use.
/// </summary>
[Serializable]
public class SpawnEntry
{
    [Tooltip("Must have a component that implements IInteractable")]
    public GameObject prefab;

    [Min(0)]
    public int count = 3;

    [Tooltip("Radius passed to Physics2D.OverlapCircle — match the prefab's collider half-extent")]
    [Min(0.01f)]
    public float overlapRadius = 0.45f;
}

/// <summary>
/// Spawns interactive resource objects (chairs, tables, crates, …) at random positions
/// inside a rectangular house area, avoiding walls and other obstacles.
///
/// Setup in the Inspector:
///   • Set <see cref="areaCenter"/> and <see cref="areaSize"/> to match the house interior.
///   • Assign <see cref="obstacleMask"/> to whatever layers count as blocked
///     (at minimum the layer your wall Tilemap uses).
///   • Add entries to <see cref="spawnEntries"/> — each prefab must implement IInteractable.
/// </summary>
public class ResourceSpawner : MonoBehaviour
{
    [Header("Grid Snapping")]
    [Tooltip("Assign the same Grid component used by BuildingManager. " +
             "Leave empty to fall back to world-aligned 1×1 tile snapping.")]
    [SerializeField] private Grid placementGrid;

    [Header("House Area")]
    [Tooltip("World-space centre of the rectangular spawn area")]
    [SerializeField] private Vector2 areaCenter = Vector2.zero;

    [Tooltip("World-space width and height of the spawn area")]
    [SerializeField] private Vector2 areaSize = new Vector2(8f, 8f);

    [Header("Spawnable Objects")]
    [SerializeField] private SpawnEntry[] spawnEntries;

    [Header("Placement")]
    [Tooltip("Layers treated as obstacles — include Walls, Structures, and any other blocking layer")]
    [SerializeField] private LayerMask obstacleMask;

    [Tooltip("How many random positions to try before giving up on one object")]
    [SerializeField] private int maxAttemptsPerObject = 50;

    [Tooltip("Shrink the usable area by this amount on each edge so objects never clip the wall boundary")]
    [SerializeField] private float edgeMargin = 0.5f;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Start() => SpawnAll();

    // ── Core ──────────────────────────────────────────────────────────────────

    private void SpawnAll()
    {
        if (spawnEntries == null || spawnEntries.Length == 0) return;

        // Build the candidate tile list once; all entries share the same area.
        List<Vector2> tiles = BuildTileCenterList();
        if (tiles.Count == 0)
        {
            Debug.LogWarning("ResourceSpawner: no tile centers found inside the spawn area.");
            return;
        }

        foreach (SpawnEntry entry in spawnEntries)
        {
            if (entry.prefab == null)
            {
                Debug.LogWarning("ResourceSpawner: a SpawnEntry has no prefab assigned — skipping.");
                continue;
            }

            ValidateInteractable(entry.prefab);

            int spawned = 0;
            for (int i = 0; i < entry.count; i++)
            {
                if (TryGetFreeTile(entry.overlapRadius, tiles, out Vector2 pos))
                {
                    GameObject resource = Instantiate(entry.prefab, pos, Quaternion.identity);

                    // Block the player from building on this tile.
                    // PlacedBuilding.OnDestroy automatically frees the tile when the resource
                    // is harvested or destroyed.
                    if (BuildingManager.Instance != null)
                    {
                        Vector2Int tile = BuildingManager.Instance.RegisterTile(pos);
                        resource.AddComponent<PlacedBuilding>().Init(tile);
                    }

                    spawned++;
                }
                else
                {
                    Debug.LogWarning(
                        $"ResourceSpawner: could not place '{entry.prefab.name}' " +
                        $"after {maxAttemptsPerObject} attempts — area may be too crowded.");
                }
            }

            Debug.Log($"[ResourceSpawner] Spawned {spawned}/{entry.count} × {entry.prefab.name}.");
        }
    }

    // ── Tile-centre sampling ──────────────────────────────────────────────────

    /// <summary>
    /// Builds the list of all tile centres that fall inside the (margin-shrunk) area.
    /// Uses <see cref="placementGrid"/> when assigned; otherwise snaps to the
    /// nearest 1×1 world-aligned tile centre (same logic as BuildingManager).
    /// </summary>
    private List<Vector2> BuildTileCenterList()
    {
        Vector2 halfExtents = areaSize * 0.5f - Vector2.one * edgeMargin;

        if (halfExtents.x <= 0f || halfExtents.y <= 0f)
        {
            Debug.LogError("ResourceSpawner: edgeMargin is larger than the area — nothing can spawn.");
            return new List<Vector2>();
        }

        Vector2 min = (Vector2)areaCenter - halfExtents;
        Vector2 max = (Vector2)areaCenter + halfExtents;

        var tiles = new List<Vector2>();

        if (placementGrid != null)
        {
            // Use the Grid component for exact cell centres
            Vector3Int cellMin = placementGrid.WorldToCell(min);
            Vector3Int cellMax = placementGrid.WorldToCell(max);

            for (int cx = cellMin.x; cx <= cellMax.x; cx++)
            {
                for (int cy = cellMin.y; cy <= cellMax.y; cy++)
                {
                    Vector2 centre = placementGrid.GetCellCenterWorld(new Vector3Int(cx, cy, 0));
                    if (centre.x >= min.x && centre.x <= max.x &&
                        centre.y >= min.y && centre.y <= max.y)
                        tiles.Add(centre);
                }
            }
        }
        else
        {
            // Manual fallback: 1×1 world-aligned grid (floor + 0.5 on each axis)
            float x0 = Mathf.Floor(min.x) + 0.5f;
            if (x0 < min.x) x0 += 1f;

            float y0 = Mathf.Floor(min.y) + 0.5f;
            if (y0 < min.y) y0 += 1f;

            for (float tx = x0; tx <= max.x + 0.001f; tx += 1f)
                for (float ty = y0; ty <= max.y + 0.001f; ty += 1f)
                    tiles.Add(new Vector2(tx, ty));
        }

        return tiles;
    }

    /// <summary>
    /// Picks random tile centres from <paramref name="tiles"/> (with replacement)
    /// until one is clear of obstacles, or <see cref="maxAttemptsPerObject"/> is reached.
    /// </summary>
    private bool TryGetFreeTile(float radius, List<Vector2> tiles, out Vector2 result)
    {
        int attempts = Mathf.Min(maxAttemptsPerObject, tiles.Count * 2);

        for (int i = 0; i < attempts; i++)
        {
            Vector2 candidate = tiles[Random.Range(0, tiles.Count)];

            if (Physics2D.OverlapCircle(candidate, radius, obstacleMask) == null)
            {
                result = candidate;
                return true;
            }
        }

        result = areaCenter;
        return false;
    }

    // ── Validation ────────────────────────────────────────────────────────────

    private static void ValidateInteractable(GameObject prefab)
    {
        // IInteractable is an interface — GetComponentInChildren covers any child too
        if (prefab.GetComponentInChildren<IInteractable>() == null)
        {
            Debug.LogWarning(
                $"ResourceSpawner: '{prefab.name}' has no IInteractable component. " +
                "The player will not be able to interact with it.");
        }
    }

    // ── Editor gizmos ─────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        // Full area boundary
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.15f);
        Gizmos.DrawCube(areaCenter, areaSize);
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireCube(areaCenter, areaSize);

        // Effective inner area (after margin)
        Vector2 innerSize = areaSize - Vector2.one * (edgeMargin * 2f);
        if (innerSize.x > 0f && innerSize.y > 0f)
        {
            Gizmos.color = new Color(1f, 1f, 0.3f, 0.5f);
            Gizmos.DrawWireCube(areaCenter, innerSize);
        }

        // Tile centres — shown as small crosses so you can verify grid alignment
        Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.6f);
        List<Vector2> tiles = BuildTileCenterList();
        foreach (Vector2 t in tiles)
        {
            const float s = 0.1f;
            Gizmos.DrawLine(t + Vector2.left * s, t + Vector2.right * s);
            Gizmos.DrawLine(t + Vector2.down * s, t + Vector2.up   * s);
        }
    }
}
