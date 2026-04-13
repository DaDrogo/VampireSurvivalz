using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stateless A* implementation.
///
/// <para>
/// Call <see cref="FindPath"/> with <c>avoidBarricades = true</c> first.
/// If that returns <c>null</c>, retry with <c>false</c> — enemies will then
/// route through barricades (dealing damage as they pass).
/// </para>
///
/// Uses an octile-distance heuristic for 8-directional grids and a version-counter
/// to avoid resetting every node before each search.
/// </summary>
public static class Pathfinder
{
    // Cost constants
    private const float STRAIGHT_COST  = 10f;
    private const float DIAGONAL_COST  = 14f;   // ≈ √2 × 10
    private const float BARRICADE_COST = 100f;  // extra penalty per barricade cell

    /// <summary>
    /// Finds an A* path from <paramref name="startWorld"/> to <paramref name="endWorld"/>.
    /// </summary>
    /// <param name="avoidBarricades">
    ///   When <c>true</c>, barricade cells are treated as walls.
    ///   When <c>false</c>, they are passable but expensive.
    /// </param>
    /// <returns>
    ///   Ordered list of world positions (start → goal), or <c>null</c> if unreachable.
    /// </returns>
    public static List<Vector2> FindPath(Vector2 startWorld, Vector2 endWorld, bool avoidBarricades)
    {
        PathfindingGrid grid = PathfindingGrid.Instance;
        if (grid == null) return null;

        PathNode startNode = grid.NodeFromWorld(startWorld);
        PathNode endNode   = grid.NodeFromWorld(endWorld);

        if (startNode == null || endNode == null)   return null;
        if (startNode.IsWall  || endNode.IsWall)    return null;
        if (avoidBarricades && endNode.IsBarricade) return null;

        int version = grid.NextSearchVersion();

        // ── Open / closed sets ────────────────────────────────────────────────
        // Simple sorted list for open set (fine for typical wave sizes).
        // Swap to a binary heap if you need hundreds of enemies simultaneously.
        List<PathNode>    openList   = new List<PathNode>(64);
        HashSet<PathNode> closedSet  = new HashSet<PathNode>();

        InitNode(startNode, version, 0f, Heuristic(startNode, endNode), null);
        openList.Add(startNode);

        while (openList.Count > 0)
        {
            PathNode current = PopLowestFCost(openList);
            if (current == endNode)
                return RetracePath(startNode, endNode);

            closedSet.Add(current);

            foreach (PathNode neighbour in grid.GetNeighbours(current))
            {
                if (neighbour.IsWall) continue;
                if (avoidBarricades && neighbour.IsBarricade) continue;
                if (closedSet.Contains(neighbour)) continue;

                float moveCost = current.GCost + MoveCost(current, neighbour);
                if (neighbour.IsBarricade) moveCost += BARRICADE_COST;

                bool inOpen = neighbour.SearchVersion == version;

                if (!inOpen || moveCost < neighbour.GCost)
                {
                    InitNode(neighbour, version, moveCost, Heuristic(neighbour, endNode), current);
                    if (!inOpen) openList.Add(neighbour);
                }
            }
        }

        return null; // No path found
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void InitNode(PathNode node, int version, float g, float h, PathNode parent)
    {
        node.SearchVersion = version;
        node.GCost         = g;
        node.HCost         = h;
        node.Parent        = parent;
    }

    private static PathNode PopLowestFCost(List<PathNode> list)
    {
        PathNode best = list[0];
        int bestIdx   = 0;
        for (int i = 1; i < list.Count; i++)
        {
            PathNode n = list[i];
            if (n.FCost < best.FCost || (n.FCost == best.FCost && n.HCost < best.HCost))
            {
                best    = n;
                bestIdx = i;
            }
        }
        list.RemoveAt(bestIdx);
        return best;
    }

    private static float MoveCost(PathNode a, PathNode b)
    {
        int dx = Mathf.Abs(a.GridX - b.GridX);
        int dy = Mathf.Abs(a.GridY - b.GridY);
        return (dx == 1 && dy == 1) ? DIAGONAL_COST : STRAIGHT_COST;
    }

    /// <summary>Octile distance — admissible heuristic for 8-directional grids.</summary>
    private static float Heuristic(PathNode a, PathNode b)
    {
        int dx = Mathf.Abs(a.GridX - b.GridX);
        int dy = Mathf.Abs(a.GridY - b.GridY);
        int diag = Mathf.Min(dx, dy);
        int straight = Mathf.Abs(dx - dy);
        return DIAGONAL_COST * diag + STRAIGHT_COST * straight;
    }

    private static List<Vector2> RetracePath(PathNode start, PathNode end)
    {
        List<Vector2> path = new List<Vector2>();
        PathNode current   = end;

        while (current != start)
        {
            path.Add(current.WorldPos);
            current = current.Parent;
        }
        path.Add(start.WorldPos);
        path.Reverse();
        return path;
    }
}
