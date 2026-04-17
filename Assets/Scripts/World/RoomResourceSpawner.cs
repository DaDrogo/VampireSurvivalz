using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Spawns two layers of objects in every BSP room after each map generation:
///
///   1. Wall harvestables  — HarvestableObject prefabs along the inner wall ring.
///   2. Interior clusters  — decorative/blocking nodes (trees, rocks, …) grown as a
///      spatially-connected BFS blob in the room interior. The blob size scales with
///      the number of interior tiles so large rooms feel denser.
///
/// All objects are destroyed and re-created on map regeneration.
///
/// Setup: add this component to any persistent scene object, then in the Inspector:
///   • Assign Harvestable Prefabs  (wall resources)
///   • Assign Cluster Prefabs      (interior forest/mountain nodes)
///   • Set Obstacle Mask           (wall Tilemap layer at minimum)
/// </summary>
public class RoomResourceSpawner : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Wall Harvestables")]
    [Tooltip("Randomly chosen per tile. Each should have a HarvestableObject component.")]
    [SerializeField] private GameObject[] harvestablePrefabs;

    [Tooltip("Fraction of wall-ring tiles filled with harvestables (0.10 = 10 %).")]
    [SerializeField] [Range(0f, 1f)] private float wallDensity = 0.15f;

    [Header("Interior Cluster Nodes")]
    [Tooltip("Forest/mountain objects spawned as a connected cluster inside the room.")]
    [SerializeField] private GameObject[] clusterPrefabs;

    [Tooltip("Fraction of interior tiles filled by the cluster (0.10 = 10 %).")]
    [SerializeField] [Range(0f, 0.5f)] private float clusterDensity = 0.12f;

    [Header("Placement")]
    [Tooltip("Layer mask for walls and blocking geometry — occupancy check for both layers.")]
    [SerializeField] private LayerMask obstacleMask;

    [Tooltip("Overlap-circle radius for the physics check. ~0.4 for 1-unit tiles.")]
    [SerializeField] private float overlapRadius = 0.4f;

    // ── Private ───────────────────────────────────────────────────────────────

    private MapGenerator              _mapGenerator;
    private readonly List<GameObject> _spawned = new List<GameObject>();

    // Cardinal neighbour offsets used by the BFS cluster growth
    private static readonly Vector2Int[] CardinalDirs =
    {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
    };

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Start()
    {
        _mapGenerator = FindAnyObjectByType<MapGenerator>();

        if (HouseManager.Instance != null)
        {
            HouseManager.Instance.OnRoomsBuilt += SpawnAll;

            // HouseManager.Start may have already run — spawn immediately if rooms exist.
            if (HouseManager.Instance.Rooms.Count > 0)
                SpawnAll();
        }
        else
        {
            Debug.LogWarning("[RoomResourceSpawner] HouseManager not found.");
        }
    }

    private void OnDestroy()
    {
        if (HouseManager.Instance != null)
            HouseManager.Instance.OnRoomsBuilt -= SpawnAll;
    }

    // ── Core ──────────────────────────────────────────────────────────────────

    private void SpawnAll()
    {
        foreach (GameObject obj in _spawned)
            if (obj != null) Destroy(obj);
        _spawned.Clear();

        if (_mapGenerator == null || HouseManager.Instance == null) return;

        // Build once — tiles that touch a door opening (with 1-tile margin each side).
        // Harvestables placed here would block the entrance.
        HashSet<Vector2Int> doorBlocked = BuildDoorBlockedSet();

        foreach (Room room in HouseManager.Instance.Rooms)
        {
            // Shared occupancy set prevents both passes from placing on the same tile
            var occupied = new HashSet<Vector2Int>();
            SpawnWallRing(room, occupied, doorBlocked);
            SpawnCluster(room, occupied);
        }
    }

    // ── Pass 1: wall-ring harvestables ────────────────────────────────────────

    private void SpawnWallRing(Room room, HashSet<Vector2Int> occupied, HashSet<Vector2Int> doorBlocked)
    {
        if (harvestablePrefabs == null || harvestablePrefabs.Length == 0) return;

        List<Vector2Int> ring = GetWallRingTiles(room);
        ring.RemoveAll(t => doorBlocked.Contains(t));
        Shuffle(ring);

        int target  = Mathf.Max(1, Mathf.RoundToInt(ring.Count * wallDensity));
        int spawned = 0;

        foreach (Vector2Int tile in ring)
        {
            if (spawned >= target) break;
            if (!TryClaim(tile, occupied)) continue;

            Place(harvestablePrefabs, tile);
            spawned++;
        }
    }

    // ── Pass 2: interior BFS cluster ──────────────────────────────────────────

    private void SpawnCluster(Room room, HashSet<Vector2Int> occupied)
    {
        if (clusterPrefabs == null || clusterPrefabs.Length == 0) return;

        List<Vector2Int> interior = GetInteriorTiles(room);
        if (interior.Count == 0) return;

        int target = Mathf.Max(1, Mathf.RoundToInt(interior.Count * clusterDensity));

        // Build a fast lookup for valid interior positions
        var interiorSet = new HashSet<Vector2Int>(interior);

        // Pick a random seed that is not already occupied
        Shuffle(interior);
        Vector2Int seed = Vector2Int.zero;
        bool foundSeed  = false;
        foreach (Vector2Int t in interior)
        {
            if (!occupied.Contains(t) && IsPhysicallyClear(t))
            { seed = t; foundSeed = true; break; }
        }
        if (!foundSeed) return;

        // BFS blob growth: frontier holds tiles adjacent to the current cluster.
        // Picking randomly from the frontier at each step produces an organic shape.
        var cluster  = new List<Vector2Int>();
        var frontier = new List<Vector2Int> { seed };
        var inFront  = new HashSet<Vector2Int> { seed };

        while (cluster.Count < target && frontier.Count > 0)
        {
            // Random pick from frontier gives irregular, natural-looking blobs
            int         idx  = Random.Range(0, frontier.Count);
            Vector2Int  tile = frontier[idx];
            frontier.RemoveAt(idx);
            inFront.Remove(tile);

            if (!TryClaim(tile, occupied)) continue;

            cluster.Add(tile);

            // Expand to cardinal neighbours that are interior and not yet seen
            foreach (Vector2Int dir in CardinalDirs)
            {
                Vector2Int nb = tile + dir;
                if (interiorSet.Contains(nb) && !occupied.Contains(nb) && !inFront.Contains(nb))
                {
                    frontier.Add(nb);
                    inFront.Add(nb);
                }
            }
        }

        foreach (Vector2Int tile in cluster)
            Place(clusterPrefabs, tile);
    }

    // ── Door clearance ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all floor tile coords that touch a door opening, including a
    /// 1-tile margin on each side of the door width. Harvestables are excluded
    /// from these tiles so entrances are never blocked.
    /// </summary>
    private HashSet<Vector2Int> BuildDoorBlockedSet()
    {
        var blocked = new HashSet<Vector2Int>();
        if (_mapGenerator == null) return blocked;

        const int margin = 1;   // extra clearance tiles on each side of the opening

        foreach (MapGenerator.DoorInfo door in _mapGenerator.DoorInfos)
        {
            if (door.IsHorizontalWall)
            {
                // Wall runs left-right at y = WallCoord.
                // Block floor tiles immediately above and below the opening.
                int xMin = door.TilePos.x - margin;
                int xMax = door.TilePos.x + door.Width + margin;
                for (int x = xMin; x < xMax; x++)
                {
                    blocked.Add(new Vector2Int(x, door.WallCoord - 1)); // room below
                    blocked.Add(new Vector2Int(x, door.WallCoord + 1)); // room above
                }
            }
            else
            {
                // Wall runs top-bottom at x = WallCoord.
                // Block floor tiles immediately left and right of the opening.
                int yMin = door.TilePos.y - margin;
                int yMax = door.TilePos.y + door.Width + margin;
                for (int y = yMin; y < yMax; y++)
                {
                    blocked.Add(new Vector2Int(door.WallCoord - 1, y)); // room left
                    blocked.Add(new Vector2Int(door.WallCoord + 1, y)); // room right
                }
            }
        }

        return blocked;
    }

    // ── Tile helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns tile coords of the outermost floor row/column on each side of the room
    /// (the "inner wall ring" — floor tiles directly adjacent to a wall tile).
    /// </summary>
    private static List<Vector2Int> GetWallRingTiles(Room room)
    {
        RectInt b    = room.TileBounds;
        var     list = new List<Vector2Int>();

        for (int x = b.xMin; x < b.xMax; x++)
        {
            list.Add(new Vector2Int(x, b.yMin));       // bottom row
            list.Add(new Vector2Int(x, b.yMax - 1));   // top row
        }
        for (int y = b.yMin + 1; y < b.yMax - 1; y++)
        {
            list.Add(new Vector2Int(b.xMin,     y));   // left column
            list.Add(new Vector2Int(b.xMax - 1, y));   // right column
        }

        return list;
    }

    /// <summary>
    /// Returns tile coords of all floor tiles strictly inside the wall ring
    /// (at least one tile away from every wall edge).
    /// </summary>
    private static List<Vector2Int> GetInteriorTiles(Room room)
    {
        RectInt b    = room.TileBounds;
        var     list = new List<Vector2Int>();

        for (int x = b.xMin + 3; x < b.xMax - 3; x++)
            for (int y = b.yMin + 3; y < b.yMax - 3; y++)
                list.Add(new Vector2Int(x, y));

        return list;
    }

    // ── Placement helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns true and marks the tile occupied when it passes both the
    /// in-frame occupancy check (HashSet) and the physics overlap check.
    /// </summary>
    private bool TryClaim(Vector2Int tile, HashSet<Vector2Int> occupied)
    {
        if (occupied.Contains(tile))  return false;
        if (!IsPhysicallyClear(tile)) return false;
        occupied.Add(tile);
        return true;
    }

    private bool IsPhysicallyClear(Vector2Int tile)
    {
        Vector2 world = _mapGenerator.TileToWorldCenter(tile);
        return Physics2D.OverlapCircle(world, overlapRadius, obstacleMask) == null;
    }

    private void Place(GameObject[] prefabs, Vector2Int tile)
    {
        GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];
        if (prefab == null) return;

        Vector2    world = _mapGenerator.TileToWorldCenter(tile);
        GameObject obj   = Instantiate(prefab, world, Quaternion.identity);
        _spawned.Add(obj);
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
