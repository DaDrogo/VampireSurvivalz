using System;
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
                if (TryGetFreePosition(entry.overlapRadius, out Vector2 pos))
                {
                    Instantiate(entry.prefab, pos, Quaternion.identity);
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

    // ── Position sampling ─────────────────────────────────────────────────────

    /// <summary>
    /// Finds a random position inside the (margin-shrunk) area that is clear of obstacles.
    /// Returns false if <see cref="maxAttemptsPerObject"/> trials all hit something.
    /// </summary>
    private bool TryGetFreePosition(float radius, out Vector2 result)
    {
        Vector2 halfExtents = areaSize * 0.5f - Vector2.one * edgeMargin;

        // Guard: if margin ate the whole area, log once and bail out
        if (halfExtents.x <= 0f || halfExtents.y <= 0f)
        {
            Debug.LogError("ResourceSpawner: edgeMargin is larger than the area — nothing can spawn.");
            result = areaCenter;
            return false;
        }

        for (int attempt = 0; attempt < maxAttemptsPerObject; attempt++)
        {
            Vector2 candidate = new Vector2(
                areaCenter.x + Random.Range(-halfExtents.x, halfExtents.x),
                areaCenter.y + Random.Range(-halfExtents.y, halfExtents.y)
            );

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
    }
}
