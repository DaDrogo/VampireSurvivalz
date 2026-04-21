using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton that owns all Room and Door GameObjects in the scene.
///
/// On Start it reads the BSP data from <see cref="MapGenerator"/> and spawns
/// Room + Door objects as children of itself. When the map is regenerated
/// (e.g. on wave restart) it tears down and rebuilds them automatically.
///
/// Setup: place this script on a persistent GameObject in the scene.
/// Execution order -5 ensures this runs before GameManager (default 0) so
/// EnemyRoom is always set before the player is positioned.
/// No additional wiring is needed — it finds MapGenerator automatically.
/// </summary>
[DefaultExecutionOrder(-5)]
public class HouseManager : MonoBehaviour
{
    public static HouseManager Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Tooltip("Auto-found if left empty.")]
    [SerializeField] private MapGenerator mapGenerator;

    [Tooltip("Corrupted land tiles painted on the randomly selected enemy room.")]
    [SerializeField] private UnityEngine.Tilemaps.TileBase[] corruptedLandTiles;

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>All rooms currently in the scene.</summary>
    public IReadOnlyList<Room> Rooms => _rooms;

    /// <summary>All doors currently in the scene.</summary>
    public IReadOnlyList<Door> Doors => _doors;

    /// <summary>The room the player is currently inside (null until first room entered).</summary>
    public Room PlayerRoom { get; private set; }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired whenever the player moves from one room to another.
    /// Args: (previousRoom, newRoom). previousRoom may be null on first entry.
    /// </summary>
    public event Action<Room, Room> OnPlayerRoomChanged;

    /// <summary>Fired after all rooms and doors have been built (or rebuilt on regeneration).</summary>
    public event Action OnRoomsBuilt;

    /// <summary>The room designated as the enemy spawn room for the current map.</summary>
    public Room EnemyRoom { get; private set; }

    // ── Private ───────────────────────────────────────────────────────────────

    private readonly List<Room> _rooms = new List<Room>();
    private readonly List<Door> _doors = new List<Door>();

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (mapGenerator == null)
            mapGenerator = FindAnyObjectByType<MapGenerator>();

        if (mapGenerator == null)
        {
            Debug.LogError("[HouseManager] MapGenerator not found in scene.");
            return;
        }

        // MapGenerator.Awake (order -10) already ran Generate() before our Start,
        // so LeafRooms / DoorInfos are already populated.
        BuildRoomsAndDoors();

        // Subscribe so future regenerations (wave restart, etc.) auto-rebuild.
        mapGenerator.OnGenerated += OnMapRegenerated;
    }

    private void OnDestroy()
    {
        if (mapGenerator != null)
            mapGenerator.OnGenerated -= OnMapRegenerated;
    }

    // ── Map regeneration ──────────────────────────────────────────────────────

    private void OnMapRegenerated()
    {
        ClearAll();
        BuildRoomsAndDoors();
    }

    private void ClearAll()
    {
        foreach (Room r in _rooms) if (r != null) Destroy(r.gameObject);
        foreach (Door d in _doors) if (d != null) Destroy(d.gameObject);
        _rooms.Clear();
        _doors.Clear();
        PlayerRoom = null;
        EnemyRoom  = null;
    }

    // ── Construction ──────────────────────────────────────────────────────────

    private void BuildRoomsAndDoors()
    {
        // ── 1. Rooms ─────────────────────────────────────────────────────────
        foreach (RectInt tileBounds in mapGenerator.LeafRooms)
        {
            Vector2 center = mapGenerator.TileRectWorldCenter(tileBounds);
            Vector2 size   = mapGenerator.TileRectWorldSize(tileBounds);

            var go   = new GameObject($"Room {_rooms.Count} [{tileBounds.width}x{tileBounds.height}]");
            go.transform.SetParent(transform, worldPositionStays: false);

            var room = go.AddComponent<Room>();
            room.Initialize(tileBounds, center, size);
            _rooms.Add(room);
        }

        // ── 2. Doors ─────────────────────────────────────────────────────────
        foreach (MapGenerator.DoorInfo di in mapGenerator.DoorInfos)
        {
            Room roomA = FindAdjacentRoom(di, side: 0);
            Room roomB = FindAdjacentRoom(di, side: 1);

            if (roomA == null || roomB == null)
            {
                Debug.LogWarning($"[HouseManager] Could not match both rooms for door at tile {di.TilePos}. " +
                                 $"wall={di.WallCoord} horiz={di.IsHorizontalWall}");
                continue;
            }

            // Centre the door on the midpoint of the opening's tile span
            Vector2Int midTile = di.IsHorizontalWall
                ? new Vector2Int(di.TilePos.x + di.Width / 2, di.WallCoord)
                : new Vector2Int(di.WallCoord, di.TilePos.y + di.Width / 2);

            Vector2 worldPos = mapGenerator.TileToWorldCenter(midTile);

            var go   = new GameObject($"Door ({di.TilePos.x},{di.TilePos.y})");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);

            var door = go.AddComponent<Door>();
            door.Initialize(roomA, roomB, di.Width, di.IsHorizontalWall);

            roomA.AddDoor(door);
            roomB.AddDoor(door);
            _doors.Add(door);
        }

        Debug.Log($"[HouseManager] Built {_rooms.Count} rooms, {_doors.Count} doors.");

        SelectEnemyRoom();
        OnRoomsBuilt?.Invoke();
    }

    private void SelectEnemyRoom()
    {
        EnemyRoom = _rooms.Count > 0 ? _rooms[UnityEngine.Random.Range(0, _rooms.Count)] : null;

        if (EnemyRoom != null && mapGenerator != null && corruptedLandTiles != null && corruptedLandTiles.Length > 0)
            mapGenerator.PaintRoomTiles(EnemyRoom.TileBounds, corruptedLandTiles);
    }

    /// <summary>
    /// Finds the room on one side of a dividing wall.
    ///
    /// BSP invariant used here:
    ///   Horizontal wall at y = WallCoord:
    ///     side 0 (below) → room.yMax == WallCoord
    ///     side 1 (above) → room.yMin == WallCoord + 1
    ///   Vertical wall at x = WallCoord:
    ///     side 0 (left)  → room.xMax == WallCoord
    ///     side 1 (right) → room.xMin == WallCoord + 1
    ///
    /// A secondary overlap check on the perpendicular axis ensures the correct
    /// room is chosen when multiple rooms share the same wall edge.
    /// </summary>
    private Room FindAdjacentRoom(MapGenerator.DoorInfo di, int side)
    {
        foreach (Room room in _rooms)
        {
            RectInt b = room.TileBounds;

            if (di.IsHorizontalWall)
            {
                // Check y edge
                bool edgeMatch = side == 0
                    ? b.yMax == di.WallCoord          // room below: top edge == wall row
                    : b.yMin == di.WallCoord + 1;     // room above: bottom edge == row after wall

                if (!edgeMatch) continue;

                // Door x range must overlap this room's x range
                int doorXEnd = di.TilePos.x + di.Width;
                if (di.TilePos.x < b.xMax && doorXEnd > b.xMin) return room;
            }
            else
            {
                // Check x edge
                bool edgeMatch = side == 0
                    ? b.xMax == di.WallCoord          // room left: right edge == wall column
                    : b.xMin == di.WallCoord + 1;     // room right: left edge == column after wall

                if (!edgeMatch) continue;

                // Door y range must overlap this room's y range
                int doorYEnd = di.TilePos.y + di.Width;
                if (di.TilePos.y < b.yMax && doorYEnd > b.yMin) return room;
            }
        }

        return null;
    }

    // ── Player room tracking ──────────────────────────────────────────────────

    /// <summary>Called by Room.OnTriggerEnter2D when the player enters.</summary>
    internal void NotifyPlayerEnteredRoom(Room room)
    {
        if (room == PlayerRoom) return;

        Room previous = PlayerRoom;
        PlayerRoom    = room;
        OnPlayerRoomChanged?.Invoke(previous, room);
    }

    // ── Query helpers ─────────────────────────────────────────────────────────

    /// <summary>Returns all rooms directly reachable from <paramref name="room"/> through a door.</summary>
    public List<Room> GetAdjacentRooms(Room room)
    {
        var result = new List<Room>(room.Doors.Count);
        foreach (Door d in room.Doors)
            result.Add(d.GetOtherRoom(room));
        return result;
    }

    /// <summary>Returns the doors that directly connect the two given rooms, if any.</summary>
    public List<Door> GetDoorsBetween(Room a, Room b)
    {
        var result = new List<Door>();
        foreach (Door d in _doors)
            if (d.Connects(a, b)) result.Add(d);
        return result;
    }

    /// <summary>Returns the room whose world bounds contain <paramref name="worldPos"/>, or null.</summary>
    public Room GetRoomAt(Vector2 worldPos)
    {
        foreach (Room room in _rooms)
            if (room.WorldBounds.Contains(worldPos)) return room;
        return null;
    }
}
