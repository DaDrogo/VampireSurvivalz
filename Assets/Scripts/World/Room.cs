using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Represents one BSP leaf room in the procedural map.
///
/// Created at runtime by <see cref="HouseManager"/> after map generation.
/// A BoxCollider2D (Trigger) sized to the room's floor area detects when the
/// Player or Enemies enter/exit, keeping live occupant counts and firing events.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class Room : MonoBehaviour
{
    // ── Data set by HouseManager ──────────────────────────────────────────────

    /// <summary>BSP tile-coordinate bounds of this room's floor area.</summary>
    public RectInt TileBounds  { get; private set; }

    /// <summary>World-space centre of the room.</summary>
    public Vector2 WorldCenter { get; private set; }

    /// <summary>World-space size of the room (width × height in units).</summary>
    public Vector2 WorldSize   { get; private set; }

    /// <summary>World-space Bounds that encloses the room's floor area.</summary>
    public Bounds  WorldBounds => new Bounds(WorldCenter, WorldSize);

    // ── Occupancy ─────────────────────────────────────────────────────────────

    /// <summary>True while the player is inside the trigger.</summary>
    public bool HasPlayer    { get; private set; }

    /// <summary>Number of enemies currently inside the trigger.</summary>
    public int  EnemyCount   { get; private set; }

    /// <summary>All colliders currently overlapping the trigger.</summary>
    public IReadOnlyCollection<GameObject> Occupants => _occupants;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired when any collider enters. Arg: the entering GameObject.</summary>
    public event Action<GameObject> OnOccupantEntered;

    /// <summary>Fired when any collider exits. Arg: the exiting GameObject.</summary>
    public event Action<GameObject> OnOccupantExited;

    /// <summary>Fired when the player enters this room.</summary>
    public event Action OnPlayerEntered;

    /// <summary>Fired when the player exits this room.</summary>
    public event Action OnPlayerExited;

    // ── Doors ─────────────────────────────────────────────────────────────────

    /// <summary>Doors connected to this room. Populated by HouseManager.</summary>
    public IReadOnlyList<Door> Doors => _doors;

    // ── Private ───────────────────────────────────────────────────────────────

    private readonly List<Door>       _doors     = new List<Door>();
    private readonly HashSet<GameObject> _occupants = new HashSet<GameObject>();
    private BoxCollider2D             _trigger;

    // ── Initialisation ────────────────────────────────────────────────────────

    /// <summary>
    /// Called by HouseManager immediately after AddComponent.
    /// Sizes the trigger collider to the room's floor area (inset by half a tile
    /// on each side so it does not overlap the surrounding wall tiles).
    /// </summary>
    public void Initialize(RectInt tileBounds, Vector2 worldCenter, Vector2 worldSize)
    {
        TileBounds  = tileBounds;
        WorldCenter = worldCenter;
        WorldSize   = worldSize;

        transform.position = new Vector3(worldCenter.x, worldCenter.y, 0f);

        _trigger           = GetComponent<BoxCollider2D>();
        _trigger.isTrigger = true;
        _trigger.offset    = Vector2.zero;
        // Inset by 1 unit (= 1 tile) so the trigger sits safely inside the walls
        _trigger.size      = new Vector2(worldSize.x - 1f, worldSize.y - 1f);
    }

    /// <summary>Called by HouseManager after all doors are created.</summary>
    public void AddDoor(Door door)
    {
        if (!_doors.Contains(door))
            _doors.Add(door);
    }

    // ── Trigger callbacks ─────────────────────────────────────────────────────

    private void OnTriggerEnter2D(Collider2D other)
    {
        _occupants.Add(other.gameObject);
        OnOccupantEntered?.Invoke(other.gameObject);

        if (other.CompareTag("Player"))
        {
            HasPlayer = true;
            OnPlayerEntered?.Invoke();
            HouseManager.Instance?.NotifyPlayerEnteredRoom(this);
        }
        else if (other.CompareTag("Enemy"))
        {
            EnemyCount++;
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        _occupants.Remove(other.gameObject);
        OnOccupantExited?.Invoke(other.gameObject);

        if (other.CompareTag("Player"))
        {
            HasPlayer = false;
            OnPlayerExited?.Invoke();
        }
        else if (other.CompareTag("Enemy"))
        {
            EnemyCount = Mathf.Max(0, EnemyCount - 1);
        }
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (_trigger == null) return;

        Color fill  = HasPlayer
            ? new Color(0.1f, 0.9f, 0.1f, 0.15f)
            : new Color(0.2f, 0.5f, 1.0f, 0.08f);
        Color wire  = HasPlayer
            ? new Color(0.1f, 0.9f, 0.1f, 0.8f)
            : new Color(0.2f, 0.5f, 1.0f, 0.5f);

        Vector3 size = new Vector3(_trigger.size.x, _trigger.size.y, 0.05f);
        Gizmos.color = fill;
        Gizmos.DrawCube(transform.position, size);
        Gizmos.color = wire;
        Gizmos.DrawWireCube(transform.position, size);

        // Enemy count label position (no text in Gizmos, shown via name only)
    }
}
