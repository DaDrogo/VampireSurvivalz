using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Random = UnityEngine.Random;

/// <summary>
/// Procedural house layout using Binary Space Partitioning (BSP).
///
/// Execution order -10 ensures this runs in Awake BEFORE PathfindingGrid (which
/// builds its grid in Start, after all Awake calls have completed).
///
/// Inspector setup:
///   • Assign Tilemap_Floor and Tilemap_Walls from the Grid hierarchy.
///   • Assign a floor TileBase and a wall TileBase.
///   • Set Map Width / Height (in tiles). The map auto-centres on Map Center (default world 0,0).
///   • Right-click the component → "Generate Now" to preview in Edit mode.
/// </summary>
[DefaultExecutionOrder(-10)]
public class MapGenerator : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Tilemaps")]
    [SerializeField] private Tilemap floorTilemap;
    [SerializeField] private Tilemap wallTilemap;

    [Header("Tiles")]
    [SerializeField] private TileBase floorTile;
    [SerializeField] private TileBase wallTile;
    [SerializeField] private TileBase doorTile;

    [Header("Map Size")]
    [Tooltip("Total map width in tiles (includes the 1-tile perimeter wall on each side)")]
    [SerializeField] private int mapWidth  = 40;
    [Tooltip("Total map height in tiles")]
    [SerializeField] private int mapHeight = 30;
    [Tooltip("World-space point the map is centred on")]
    [SerializeField] private Vector2 mapCenter = Vector2.zero;

    [Header("BSP Room Division")]
    [Tooltip("Smallest a room can be on either axis before splits stop")]
    [SerializeField] [Range(4, 12)] private int minRoomSize = 6;
    [Tooltip("Recursion depth — 2 = up to 4 rooms, 3 = 8, 4 = 16")]
    [SerializeField] [Range(1, 5)]  private int splitDepth  = 3;
    [Tooltip("Width of the door opening cut in each dividing wall")]
    [SerializeField] [Range(1, 3)]  private int doorWidth        = 2;
    [Tooltip("Minimum tiles between a door and the wall corner")]
    [SerializeField] [Range(1, 4)]  private int doorCornerMargin = 2;

    [Header("Seed")]
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private int  seed;

    // ── BSP output (read by HouseManager after Generate) ─────────────────────

    /// <summary>Carries all data HouseManager needs to create a Door GameObject.</summary>
    public struct DoorInfo
    {
        /// <summary>Tile position of the first (lowest x or y) tile of the opening.</summary>
        public Vector2Int TilePos;
        /// <summary>Number of tiles in the opening.</summary>
        public int Width;
        /// <summary>True = wall runs horizontally (x varies); False = wall runs vertically (y varies).</summary>
        public bool IsHorizontalWall;
        /// <summary>Fixed-axis tile coordinate of the wall row/column.</summary>
        public int WallCoord;
    }

    private readonly List<RectInt>  _leafRooms = new List<RectInt>();
    private readonly List<DoorInfo> _doorInfos = new List<DoorInfo>();

    /// <summary>BSP leaf rooms (floor tiles only, no walls). Populated after every Generate call.</summary>
    public IReadOnlyList<RectInt>  LeafRooms => _leafRooms;
    /// <summary>Door openings cut in dividing walls. Populated after every Generate call.</summary>
    public IReadOnlyList<DoorInfo> DoorInfos  => _doorInfos;
    /// <summary>Fired at the end of every Generate call (including on Awake).</summary>
    public event Action OnGenerated;

    // ── Derived origin (computed each generate call) ──────────────────────────

    // Bottom-left tile coordinate, kept in sync with mapCenter + map size.
    private Vector2Int Origin =>
        new Vector2Int(
            Mathf.RoundToInt(mapCenter.x - mapWidth  * 0.5f),
            Mathf.RoundToInt(mapCenter.y - mapHeight * 0.5f));

    // ── Public state (read by ResourceSpawner / GameManager) ─────────────────

    /// <summary>World-space centre of the walkable interior.</summary>
    public Vector2 InteriorCenter => mapCenter;

    /// <summary>World-space size of the walkable interior (map minus 1-tile perimeter).</summary>
    public Vector2 InteriorSize => new Vector2(mapWidth - 2, mapHeight - 2);

    public int Seed => seed;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (useRandomSeed) seed = Random.Range(0, int.MaxValue);
        Random.InitState(seed);
        Generate();
    }

    // ── Generation entry point ────────────────────────────────────────────────

    /// <summary>
    /// Clears both tilemaps and builds a new layout from scratch.
    /// Also callable from the Inspector context menu or at runtime (e.g. new wave).
    /// </summary>
    [ContextMenu("Generate Now")]
    public void Generate()
    {
        if (!ValidateReferences()) return;

        _leafRooms.Clear();
        _doorInfos.Clear();

        var origin = Origin;

        floorTilemap.ClearAllTiles();
        wallTilemap.ClearAllTiles();

        // 1. Flood-fill floor
        for (int x = origin.x; x < origin.x + mapWidth;  x++)
        for (int y = origin.y; y < origin.y + mapHeight; y++)
            floorTilemap.SetTile(new Vector3Int(x, y, 0), floorTile);

        // 2. Solid outer perimeter
        DrawPerimeter(origin);

        // 3. BSP interior walls + doors
        var interior = new RectInt(
            origin.x + 1,
            origin.y + 1,
            mapWidth  - 2,
            mapHeight - 2);

        SplitRoom(interior, splitDepth);

        // Tell tilemaps to recalculate their bounds (needed by PathfindingGrid)
        wallTilemap.CompressBounds();
        floorTilemap.CompressBounds();

        Debug.Log($"[MapGenerator] {mapWidth}×{mapHeight}  centre={mapCenter}  seed={seed}  depth={splitDepth}  rooms={_leafRooms.Count}  doors={_doorInfos.Count}");
        OnGenerated?.Invoke();
    }

    // ── Perimeter ─────────────────────────────────────────────────────────────

    private void DrawPerimeter(Vector2Int origin)
    {
        for (int x = origin.x; x < origin.x + mapWidth; x++)
        {
            SetWall(x, origin.y);
            SetWall(x, origin.y + mapHeight - 1);
        }
        for (int y = origin.y + 1; y < origin.y + mapHeight - 1; y++)
        {
            SetWall(origin.x,                y);
            SetWall(origin.x + mapWidth - 1, y);
        }
    }

    // ── BSP ───────────────────────────────────────────────────────────────────

    private void SplitRoom(RectInt room, int depth)
    {
        if (depth <= 0) { _leafRooms.Add(room); return; }

        bool canH = room.height >= minRoomSize * 2 + 1;
        bool canV = room.width  >= minRoomSize * 2 + 1;

        if (!canH && !canV) { _leafRooms.Add(room); return; }

        // Split along the longer axis; when equal alternate to avoid same-direction bias
        bool horizontal;
        if      (canH && !canV) horizontal = true;
        else if (canV && !canH) horizontal = false;
        else                    horizontal = room.height >= room.width;

        if (horizontal) SplitHorizontal(room, depth);
        else            SplitVertical(room, depth);
    }

    private void SplitHorizontal(RectInt room, int depth)
    {
        // Pick a row that keeps at least minRoomSize tiles on each side
        int splitY = Random.Range(room.yMin + minRoomSize, room.yMax - minRoomSize);

        for (int x = room.xMin; x < room.xMax; x++)
            SetWall(x, splitY);

        CutDoor(room.xMin, room.xMax, splitY, isHorizontalWall: true);

        SplitRoom(new RectInt(room.xMin, room.yMin,  room.width, splitY - room.yMin),     depth - 1);
        SplitRoom(new RectInt(room.xMin, splitY + 1, room.width, room.yMax - splitY - 1), depth - 1);
    }

    private void SplitVertical(RectInt room, int depth)
    {
        int splitX = Random.Range(room.xMin + minRoomSize, room.xMax - minRoomSize);

        for (int y = room.yMin; y < room.yMax; y++)
            SetWall(splitX, y);

        CutDoor(room.yMin, room.yMax, splitX, isHorizontalWall: false);

        SplitRoom(new RectInt(room.xMin,  room.yMin, splitX - room.xMin,      room.height), depth - 1);
        SplitRoom(new RectInt(splitX + 1, room.yMin, room.xMax - splitX - 1,  room.height), depth - 1);
    }

    /// <summary>
    /// Punches a door gap of <see cref="doorWidth"/> tiles in a dividing wall,
    /// keeping it at least 1 tile away from either end.
    /// </summary>
    /// <param name="rangeMin">Start of the wall along the varying axis (inclusive).</param>
    /// <param name="rangeMax">End of the wall along the varying axis (exclusive).</param>
    /// <param name="wallCoord">Fixed axis coordinate of the wall tile.</param>
    /// <param name="isHorizontalWall">True = wall runs left-right (vary X); false = top-bottom (vary Y).</param>
    private void CutDoor(int rangeMin, int rangeMax, int wallCoord, bool isHorizontalWall)
    {
        // Keep doors away from corners by doorCornerMargin tiles on each side
        int lo  = rangeMin + doorCornerMargin;
        int hi  = rangeMax - doorWidth - doorCornerMargin;

        // Guard: if the room is too narrow the door goes in the middle
        if (hi <= lo) hi = lo + 1;

        int pos = Random.Range(lo, hi);

        for (int i = 0; i < doorWidth; i++)
        {
            if (isHorizontalWall) PlaceDoorTile(pos + i, wallCoord);
            else                  PlaceDoorTile(wallCoord, pos + i);
        }

        // Record for HouseManager
        _doorInfos.Add(new DoorInfo
        {
            TilePos          = isHorizontalWall ? new Vector2Int(pos, wallCoord) : new Vector2Int(wallCoord, pos),
            Width            = doorWidth,
            IsHorizontalWall = isHorizontalWall,
            WallCoord        = wallCoord,
        });
    }

    // ── World-space conversion helpers (used by HouseManager) ────────────────

    /// <summary>World-space centre of a single tile.</summary>
    public Vector2 TileToWorldCenter(Vector2Int tile)
    {
        if (floorTilemap == null)
            return new Vector2(tile.x + 0.5f, tile.y + 0.5f);
        return (Vector2)floorTilemap.CellToWorld(new Vector3Int(tile.x, tile.y, 0))
               + new Vector2(0.5f, 0.5f);
    }

    /// <summary>World-space centre of a tile rectangle (average of min/max tile centres).</summary>
    public Vector2 TileRectWorldCenter(RectInt rect)
    {
        Vector2 minC = TileToWorldCenter(new Vector2Int(rect.xMin, rect.yMin));
        Vector2 maxC = TileToWorldCenter(new Vector2Int(rect.xMax - 1, rect.yMax - 1));
        return (minC + maxC) * 0.5f;
    }

    /// <summary>World-space size of a tile rectangle (1 tile = 1 world unit).</summary>
    public Vector2 TileRectWorldSize(RectInt rect) => new Vector2(rect.width, rect.height);

    // ── Tile helpers ──────────────────────────────────────────────────────────

    private void SetWall(int x, int y)
    {
        var p = new Vector3Int(x, y, 0);
        wallTilemap.SetTile(p, wallTile);
        floorTilemap.SetTile(p, null);
    }

    private void ClearWall(int x, int y)
    {
        var p = new Vector3Int(x, y, 0);
        wallTilemap.SetTile(p, null);
        floorTilemap.SetTile(p, floorTile);
    }

    private void PlaceDoorTile(int x, int y)
    {
        var p = new Vector3Int(x, y, 0);
        wallTilemap.SetTile(p, null);
        floorTilemap.SetTile(p, doorTile != null ? doorTile : floorTile);
    }

    // ── Validation ────────────────────────────────────────────────────────────

    private bool ValidateReferences()
    {
        bool ok = true;
        if (floorTilemap == null) { Debug.LogError("MapGenerator: floorTilemap not assigned."); ok = false; }
        if (wallTilemap  == null) { Debug.LogError("MapGenerator: wallTilemap not assigned.");  ok = false; }
        if (floorTile    == null) { Debug.LogError("MapGenerator: floorTile not assigned.");    ok = false; }
        if (wallTile     == null) { Debug.LogError("MapGenerator: wallTile not assigned.");     ok = false; }
        return ok;
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        var center3 = new Vector3(mapCenter.x, mapCenter.y, 0f);
        var size3   = new Vector3(mapWidth, mapHeight, 0f);

        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.15f);
        Gizmos.DrawCube(center3, size3);
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
        Gizmos.DrawWireCube(center3, size3);

        // Walkable interior (perimeter excluded)
        Gizmos.color = new Color(1f, 1f, 0.3f, 0.6f);
        Gizmos.DrawWireCube(center3, size3 - new Vector3(2f, 2f, 0f));
    }
}
