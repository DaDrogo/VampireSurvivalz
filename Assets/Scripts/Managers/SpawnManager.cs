using System.Collections;
using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance { get; private set; }

    [Header("Prefab")]
    [SerializeField] private GameObject enemyPrefab;

    [Header("Spawn Timing")]
    [SerializeField] private float initialSpawnInterval = 3f;
    [SerializeField] private float minimumSpawnInterval = 0.5f;
    [SerializeField] private float intervalDecreasePerWave = 0.2f;

    [Header("House Area (safe zone — no spawning inside)")]
    [SerializeField] private Vector2 houseCenter = Vector2.zero;
    [SerializeField] private Vector2 houseSize = new Vector2(10f, 10f);

    [Header("Spawn Ring")]
    [Tooltip("How far outside the house boundary enemies can spawn")]
    [SerializeField] private float spawnBorderWidth = 5f;

    [Header("Limits")]
    [SerializeField] private int maxLiveEnemies = 20;

    private int _liveEnemies;
    private float _currentInterval;
    private int _waveCount;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _currentInterval = initialSpawnInterval;
        StartCoroutine(SpawnLoop());
    }

    // ── Spawn loop ────────────────────────────────────────────────────────────

    private IEnumerator SpawnLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(_currentInterval);

            if (_liveEnemies < maxLiveEnemies)
            {
                SpawnEnemy();
                _waveCount++;

                // Gradually tighten the interval each wave, never below the minimum
                _currentInterval = Mathf.Max(
                    minimumSpawnInterval,
                    initialSpawnInterval - _waveCount * intervalDecreasePerWave
                );
            }
        }
    }

    private void SpawnEnemy()
    {
        if (enemyPrefab == null)
        {
            Debug.LogWarning("SpawnManager: no enemyPrefab assigned.");
            return;
        }

        Vector2 spawnPos = GetSpawnPosition();
        Instantiate(enemyPrefab, spawnPos, Quaternion.identity);
        _liveEnemies++;
    }

    // ── Called by Enemy.Die() ─────────────────────────────────────────────────

    public void OnEnemyDied() => _liveEnemies = Mathf.Max(0, _liveEnemies - 1);

    // ── Position sampling ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns a random point in the rectangular border ring surrounding the house area.
    /// The ring is divided into four edge strips (top, bottom, left, right); one strip
    /// is chosen at random, then a point is sampled uniformly within it.
    /// </summary>
    private Vector2 GetSpawnPosition()
    {
        float hw = houseSize.x * 0.5f + spawnBorderWidth; // half outer-rect width
        float hh = houseSize.y * 0.5f + spawnBorderWidth; // half outer-rect height
        float iw = houseSize.x * 0.5f;                    // half inner-rect width
        float ih = houseSize.y * 0.5f;                    // half inner-rect height

        // Weight strips by their area so sampling is uniform across the full border
        float topBottomArea = (hw * 2f) * spawnBorderWidth;
        float leftRightArea = (ih * 2f) * spawnBorderWidth;
        float totalArea = 2f * topBottomArea + 2f * leftRightArea;

        float roll = Random.value * totalArea;
        Vector2 local;

        if (roll < topBottomArea)
        {
            // Top strip
            local = new Vector2(Random.Range(-hw, hw), Random.Range(ih, ih + spawnBorderWidth));
        }
        else if (roll < 2f * topBottomArea)
        {
            // Bottom strip
            local = new Vector2(Random.Range(-hw, hw), Random.Range(-ih - spawnBorderWidth, -ih));
        }
        else if (roll < 2f * topBottomArea + leftRightArea)
        {
            // Right strip
            local = new Vector2(Random.Range(iw, iw + spawnBorderWidth), Random.Range(-ih, ih));
        }
        else
        {
            // Left strip
            local = new Vector2(Random.Range(-iw - spawnBorderWidth, -iw), Random.Range(-ih, ih));
        }

        return houseCenter + local;
    }

    private void OnDrawGizmos()
    {
        // Inner safe zone (house)
        Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
        Gizmos.DrawCube(houseCenter, houseSize);
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(houseCenter, houseSize);

        // Outer spawn ring
        Vector2 outerSize = houseSize + Vector2.one * (spawnBorderWidth * 2f);
        Gizmos.color = new Color(1f, 0f, 0f, 0.1f);
        Gizmos.DrawCube(houseCenter, outerSize);
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(houseCenter, outerSize);
    }
}
