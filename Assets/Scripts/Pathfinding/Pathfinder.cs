using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stateless A* implementation.
///
/// Public API:
///   FindPath    — single shortest path (original behaviour, unchanged).
///   FindKPaths  — up to k distinct paths via iterative node-penalty inflation.
///                 Use this from Enemy.Start() to power probability-based path selection.
/// </summary>
public static class Pathfinder
{
    // ── Cost constants ────────────────────────────────────────────────────────

    private const float STRAIGHT_COST       = 10f;
    private const float DIAGONAL_COST       = 14f;    // ≈ √2 × 10
    private const float BARRICADE_COST      = 100f;   // extra penalty per barricade cell
    private const float ALTERNATE_PENALTY   = 50f;    // added per node when finding alternative paths

    // ── Public: single path ───────────────────────────────────────────────────

    /// <summary>
    /// Returns the shortest A* path from <paramref name="startWorld"/> to
    /// <paramref name="endWorld"/>, or <c>null</c> if unreachable.
    /// </summary>
    public static List<Vector2> FindPath(Vector2 startWorld, Vector2 endWorld, bool avoidBarricades)
    {
        PathfindingGrid grid = PathfindingGrid.Instance;
        if (grid == null) return null;

        PathNode start = grid.FindNearestWalkable(startWorld);
        PathNode end   = grid.FindNearestWalkable(endWorld);
        if (start == null || end == null)                  return null;
        if (avoidBarricades && end.IsBarricade)            return null;

        return SearchPath(start, end, avoidBarricades, null, grid);
    }

    // ── Public: k distinct paths ──────────────────────────────────────────────

    /// <summary>
    /// Finds up to <paramref name="maxPaths"/> distinct paths using iterative
    /// node-penalty inflation. The first element is always the shortest path.
    /// Subsequent elements route through different corridors of the map.
    ///
    /// Returns <c>null</c> when the destination is completely unreachable.
    /// </summary>
    public static List<List<Vector2>> FindKPaths(
        Vector2 startWorld, Vector2 endWorld,
        bool avoidBarricades,
        int maxPaths = 4)
    {
        PathfindingGrid grid = PathfindingGrid.Instance;
        if (grid == null) return null;

        PathNode startNode = grid.FindNearestWalkable(startWorld);
        PathNode endNode   = grid.FindNearestWalkable(endWorld);
        if (startNode == null || endNode == null)   return null;
        if (avoidBarricades && endNode.IsBarricade) return null;

        var results    = new List<List<Vector2>>(maxPaths);
        var penaltyMap = new Dictionary<PathNode, float>(64);

        // Run A* up to (maxPaths + a few extra) times to collect distinct routes.
        int maxAttempts = maxPaths + 3;
        for (int attempt = 0; attempt < maxAttempts && results.Count < maxPaths; attempt++)
        {
            List<Vector2> path = SearchPath(startNode, endNode, avoidBarricades, penaltyMap, grid);
            if (path == null) break;

            // Accept path if it is the first result, or distinct from the shortest.
            if (results.Count == 0 || IsDistinctFrom(path, results[0], grid))
                results.Add(path);

            // Penalise this path's nodes so the next search routes elsewhere.
            foreach (Vector2 wp in path)
            {
                PathNode node = grid.NodeFromWorld(wp);
                if (node == null) continue;
                penaltyMap.TryGetValue(node, out float prev);
                penaltyMap[node] = prev + ALTERNATE_PENALTY;
            }
        }

        return results.Count > 0 ? results : null;
    }

    // ── Core search (shared by both public methods) ───────────────────────────

    private static List<Vector2> SearchPath(
        PathNode startNode, PathNode endNode,
        bool avoidBarricades,
        Dictionary<PathNode, float> extraCosts,
        PathfindingGrid grid)
    {
        int version = grid.NextSearchVersion();

        List<PathNode>    openList  = new List<PathNode>(64);
        HashSet<PathNode> closedSet = new HashSet<PathNode>();

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
                if (neighbour.IsWall)                                continue;
                if (avoidBarricades && neighbour.IsBarricade)        continue;
                if (closedSet.Contains(neighbour))                   continue;

                float moveCost = current.GCost + MoveCost(current, neighbour);
                if (neighbour.IsBarricade) moveCost += BARRICADE_COST;

                // Apply alternative-path penalty when provided
                if (extraCosts != null && extraCosts.TryGetValue(neighbour, out float penalty))
                    moveCost += penalty;

                bool inOpen = neighbour.SearchVersion == version;

                if (!inOpen || moveCost < neighbour.GCost)
                {
                    InitNode(neighbour, version, moveCost, Heuristic(neighbour, endNode), current);
                    if (!inOpen) openList.Add(neighbour);
                }
            }
        }

        return null;
    }

    // ── Path distinctness check ───────────────────────────────────────────────

    /// <summary>
    /// Two paths are "distinct" when their midpoint nodes are at least 3 grid
    /// cells apart — cheap proxy for "goes through a different corridor".
    /// </summary>
    private static bool IsDistinctFrom(List<Vector2> candidate, List<Vector2> reference, PathfindingGrid grid)
    {
        if (candidate.Count < 2 || reference.Count < 2) return false;

        PathNode midA = grid.NodeFromWorld(candidate[candidate.Count / 2]);
        PathNode midB = grid.NodeFromWorld(reference[reference.Count / 2]);
        if (midA == null || midB == null) return false;

        int dx = Mathf.Abs(midA.GridX - midB.GridX);
        int dy = Mathf.Abs(midA.GridY - midB.GridY);
        return dx + dy >= 3;
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
        PathNode best   = list[0];
        int      bestIdx = 0;
        for (int i = 1; i < list.Count; i++)
        {
            PathNode n = list[i];
            if (n.FCost < best.FCost || (n.FCost == best.FCost && n.HCost < best.HCost))
            {
                best     = n;
                bestIdx  = i;
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
        int dx     = Mathf.Abs(a.GridX - b.GridX);
        int dy     = Mathf.Abs(a.GridY - b.GridY);
        int diag   = Mathf.Min(dx, dy);
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
