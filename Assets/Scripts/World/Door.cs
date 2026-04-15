using UnityEngine;

/// <summary>
/// Represents an opening (door gap) cut in a BSP dividing wall.
///
/// Created at runtime by <see cref="HouseManager"/> after map generation.
/// Stores world position (its Transform), the two rooms it connects, the
/// opening width in tiles, and the wall orientation.
/// </summary>
public class Door : MonoBehaviour
{
    // ── Data set by HouseManager ──────────────────────────────────────────────

    /// <summary>One of the two rooms this door connects.</summary>
    public Room RoomA { get; private set; }

    /// <summary>The other room this door connects.</summary>
    public Room RoomB { get; private set; }

    /// <summary>Opening width in tiles.</summary>
    public int TileWidth { get; private set; }

    /// <summary>
    /// True = the dividing wall runs horizontally (door opens up/down).
    /// False = the dividing wall runs vertically (door opens left/right).
    /// </summary>
    public bool IsHorizontalWall { get; private set; }

    /// <summary>World-space position of the centre of the door opening.</summary>
    public Vector2 WorldPosition => transform.position;

    // ── Initialisation ────────────────────────────────────────────────────────

    /// <summary>Called by HouseManager immediately after AddComponent.</summary>
    public void Initialize(Room roomA, Room roomB, int tileWidth, bool isHorizontalWall)
    {
        RoomA            = roomA;
        RoomB            = roomB;
        TileWidth        = tileWidth;
        IsHorizontalWall = isHorizontalWall;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Given one room, returns the room on the other side of this door.</summary>
    public Room GetOtherRoom(Room from) => from == RoomA ? RoomB : RoomA;

    /// <summary>Returns true if this door connects the two given rooms (order-independent).</summary>
    public bool Connects(Room a, Room b) =>
        (RoomA == a && RoomB == b) || (RoomA == b && RoomB == a);

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        // Door marker
        Gizmos.color = new Color(1f, 0.85f, 0f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.3f);

        float hw = IsHorizontalWall ? TileWidth * 0.5f : 0.2f;
        float hh = IsHorizontalWall ? 0.2f : TileWidth * 0.5f;
        Gizmos.color = new Color(1f, 0.85f, 0f, 0.4f);
        Gizmos.DrawCube(transform.position, new Vector3(hw * 2f, hh * 2f, 0.05f));

        // Lines from door to connected room centres
        if (RoomA != null)
        {
            Gizmos.color = new Color(1f, 0.85f, 0f, 0.35f);
            Gizmos.DrawLine(transform.position, RoomA.WorldCenter);
        }
        if (RoomB != null)
        {
            Gizmos.color = new Color(1f, 0.85f, 0f, 0.35f);
            Gizmos.DrawLine(transform.position, RoomB.WorldCenter);
        }
    }
}
