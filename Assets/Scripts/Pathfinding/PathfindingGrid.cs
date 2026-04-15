using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Singleton that owns the A* grid.
///
/// Setup in the Inspector:
///   • Assign the Tilemap that holds your wall tiles to <see cref="wallTilemap"/>.
///   • Drag every TileBase asset that counts as a wall into <see cref="wallTiles"/>.
///   • Set <see cref="cellSize"/> to match your Tilemap's cell size (usually 1).
///
/// The grid is built once in Awake from the Tilemap bounds.
/// Barricades register/unregister themselves when built or destroyed.
/// </summary>
public class PathfindingGrid : MonoBehaviour
{
    public static PathfindingGrid Instance { get; private set; }

    [Header("Tilemap")]
    [Tooltip("The Tilemap layer that contains wall tiles")]
    [SerializeField] private Tilemap wallTilemap;

    [Tooltip("Every TileBase that should be treated as an impassable wall")]
    [SerializeField] private TileBase[] wallTiles;

    [Header("Grid Settings")]
    [Tooltip("World-space size of one grid cell — should match your Tilemap Cell Size")]
    [SerializeField] private float cellSize = 1f;

    // ── Public grid info ──────────────────────────────────────────────────────

    public int   Width  { get; private set; }
    public int   Height { get; private set; }

    /// <summary>Incremented once per A* search so stale node data is ignored without clearing every node.</summary>
    public int   SearchVersion { get; private set; }

    // ── Private ───────────────────────────────────────────────────────────────

    private PathNode[,] _grid;
    private Vector2     _originWorld; // world position of cell (0,0)

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    // BuildGrid is called by GameManager.StartGame() after MapGenerator.Generate()
    // has painted tiles, so the grid always reflects the current map layout.

    // ═════════════════════════════════════════════════════════════════════════
    //  Grid construction
    // ═════════════════════════════════════════════════════════════════════════

    public void BuildGrid()
    {
        if (wallTilemap == null)
        {
            Debug.LogError("PathfindingGrid: wallTilemap not assigned — grid not built.");
            return;
        }

        // Compress bounds to the actual used cells
        wallTilemap.CompressBounds();
        BoundsInt bounds = wallTilemap.cellBounds;

        Width  = bounds.size.x;
        Height = bounds.size.y;

        // World-space origin of the (0,0) grid cell
        _originWorld = wallTilemap.CellToWorld(bounds.min) + new Vector3(cellSize * 0.5f, cellSize * 0.5f, 0f);

        _grid = new PathNode[Width, Height];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                Vector3Int cellPos = new Vector3Int(bounds.min.x + x, bounds.min.y + y, 0);
                Vector2    worldPos = wallTilemap.CellToWorld(cellPos) + new Vector3(cellSize * 0.5f, cellSize * 0.5f, 0f);

                var node     = new PathNode(x, y, worldPos);
                node.IsWall  = IsTileWall(cellPos);
                _grid[x, y]  = node;
            }
        }

        Debug.Log($"[PathfindingGrid] Built {Width}×{Height} grid.");
    }

    private bool IsTileWall(Vector3Int cellPos)
    {
        if (wallTiles == null || wallTiles.Length == 0) return false;
        TileBase tile = wallTilemap.GetTile(cellPos);
        if (tile == null) return false;
        foreach (TileBase wt in wallTiles)
            if (tile == wt) return true;
        return false;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Barricade registration
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Called by <see cref="Barricade"/> when it is built.</summary>
    public void RegisterBarricade(Vector2 worldPos, Barricade barricade)
    {
        if (!TryGetNode(worldPos, out PathNode node)) return;
        node.IsBarricade  = true;
        node.BarricadeRef = barricade;
    }

    /// <summary>Called by <see cref="Barricade"/> when it is destroyed.</summary>
    public void UnregisterBarricade(Vector2 worldPos)
    {
        if (!TryGetNode(worldPos, out PathNode node)) return;
        node.IsBarricade  = false;
        node.BarricadeRef = null;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Node access
    // ═════════════════════════════════════════════════════════════════════════

    public PathNode NodeFromWorld(Vector2 worldPos)
    {
        TryGetNode(worldPos, out PathNode node);
        return node;
    }

    private bool TryGetNode(Vector2 worldPos, out PathNode node)
    {
        node = null;
        if (_grid == null) return false;

        int x = Mathf.FloorToInt((worldPos.x - _originWorld.x) / cellSize + 0.5f);
        int y = Mathf.FloorToInt((worldPos.y - _originWorld.y) / cellSize + 0.5f);

        if (x < 0 || x >= Width || y < 0 || y >= Height) return false;
        node = _grid[x, y];
        return node != null;
    }

    /// <summary>
    /// Returns up to 8 orthogonal + diagonal neighbours.
    /// Corner-cutting is prevented: a diagonal neighbour is only included when
    /// both adjacent cardinal neighbours are non-wall.
    /// </summary>
    public PathNode[] GetNeighbours(PathNode node)
    {
        // Pre-allocate max 8; fill and trim
        PathNode[] buffer = new PathNode[8];
        int count = 0;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;

                int nx = node.GridX + dx;
                int ny = node.GridY + dy;

                if (nx < 0 || nx >= Width || ny < 0 || ny >= Height) continue;

                // Prevent corner-cutting through wall-wall diagonal
                if (dx != 0 && dy != 0)
                {
                    PathNode cardinalA = _grid[node.GridX + dx, node.GridY];
                    PathNode cardinalB = _grid[node.GridX,      node.GridY + dy];
                    if (cardinalA.IsWall || cardinalB.IsWall) continue;
                }

                buffer[count++] = _grid[nx, ny];
            }
        }

        // Return exact-size array
        PathNode[] result = new PathNode[count];
        System.Array.Copy(buffer, result, count);
        return result;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Search versioning
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Call once before each A* search to invalidate stale node data.</summary>
    public int NextSearchVersion() => ++SearchVersion;

    // ═════════════════════════════════════════════════════════════════════════
    //  Editor gizmos
    // ═════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmosSelected()
    {
        if (_grid == null) return;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                PathNode n = _grid[x, y];
                if (n.IsWall)
                    Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.4f);
                else if (n.IsBarricade)
                    Gizmos.color = new Color(1f, 0.65f, 0f, 0.4f);
                else
                    continue; // skip empty walkable cells to reduce clutter

                Gizmos.DrawCube(n.WorldPos, Vector3.one * (cellSize * 0.85f));
            }
        }
    }
}
