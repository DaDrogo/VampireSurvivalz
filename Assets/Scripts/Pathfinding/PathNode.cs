using UnityEngine;

/// <summary>
/// One cell in the PathfindingGrid.
/// A* state fields are reset via the version-counter pattern — no per-search array clears needed.
/// </summary>
public class PathNode
{
    // ── Grid identity ─────────────────────────────────────────────────────────

    public readonly int   GridX;
    public readonly int   GridY;
    public readonly Vector2 WorldPos;

    // ── Passability ───────────────────────────────────────────────────────────

    /// <summary>Permanently impassable (Tilemap wall tile).</summary>
    public bool IsWall { get; set; }

    /// <summary>True while a built Barricade occupies this node.</summary>
    public bool IsBarricade { get; set; }

    /// <summary>Reference to the barricade so enemies can call TakeDamage.</summary>
    public Barricade BarricadeRef { get; set; }

    // ── A* state (version-gated) ──────────────────────────────────────────────

    /// <summary>
    /// Which search last touched this node.
    /// If SearchVersion != PathfindingGrid.CurrentSearchVersion the A* fields below are stale.
    /// </summary>
    public int SearchVersion { get; set; } = -1;

    public float  GCost  { get; set; }
    public float  HCost  { get; set; }
    public float  FCost  => GCost + HCost;
    public PathNode Parent { get; set; }

    // ── Construction ──────────────────────────────────────────────────────────

    public PathNode(int gridX, int gridY, Vector2 worldPos)
    {
        GridX    = gridX;
        GridY    = gridY;
        WorldPos = worldPos;
    }
}
