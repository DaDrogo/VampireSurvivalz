using System;
using System.Collections;
using UnityEngine;

public class WaveManager : MonoBehaviour
{
    public enum WaveState { Preparation, Combat }

    public static WaveManager Instance { get; private set; }

    [Header("Preparation")]
    [SerializeField] private float preparationDuration = 30f;

    [Header("Wave Scaling")]
    [SerializeField] private int baseEnemyCount = 10;
    [SerializeField] private int enemyCountIncreasePerWave = 5;
    [Tooltip("Stat multiplier added per wave on top of the base 1.0 (e.g. 0.15 = +15% per wave)")]
    [SerializeField] private float difficultyIncreasePerWave = 0.15f;

    [Header("Spawning")]
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private float timeBetweenSpawns = 0.4f;

    [Header("Spawn Area")]
    [SerializeField] private Vector2 spawnCenter = Vector2.zero;
    [SerializeField] private Vector2 safeZoneSize = new Vector2(10f, 10f);
    [SerializeField] private float spawnBorderWidth = 5f;

    // ── Public state (read by UI) ─────────────────────────────────────────────

    public WaveState CurrentState  { get; private set; } = WaveState.Preparation;
    public int        WaveNumber   { get; private set; } = 0;
    public float      PrepTimeLeft { get; private set; }

    // ── Events ────────────────────────────────────────────────────────────────

    public event Action<WaveState> OnStateChanged;
    public event Action<int>       OnWaveNumberChanged;
    public event Action<float>     OnPrepTimerChanged;  // fires every frame during Preparation

    // ── Private tracking ──────────────────────────────────────────────────────

    private int _enemiesToSpawn;
    private int _enemiesSpawned;
    private int _enemiesAlive;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start() => StartPreparation();

    // ── State transitions ─────────────────────────────────────────────────────

    private void StartPreparation()
    {
        CurrentState = WaveState.Preparation;
        PrepTimeLeft = preparationDuration;
        OnStateChanged?.Invoke(CurrentState);
        StartCoroutine(PreparationCountdown());
    }

    private void StartCombat()
    {
        WaveNumber++;
        CurrentState     = WaveState.Combat;
        _enemiesToSpawn  = baseEnemyCount + (WaveNumber - 1) * enemyCountIncreasePerWave;
        _enemiesSpawned  = 0;
        _enemiesAlive    = 0;

        OnWaveNumberChanged?.Invoke(WaveNumber);
        OnStateChanged?.Invoke(CurrentState);

        StartCoroutine(SpawnWave());
    }

    // ── Coroutines ────────────────────────────────────────────────────────────

    private IEnumerator PreparationCountdown()
    {
        while (PrepTimeLeft > 0f)
        {
            PrepTimeLeft -= Time.deltaTime;
            OnPrepTimerChanged?.Invoke(PrepTimeLeft);
            yield return null;
        }

        PrepTimeLeft = 0f;
        StartCombat();
    }

    private IEnumerator SpawnWave()
    {
        while (_enemiesSpawned < _enemiesToSpawn)
        {
            SpawnEnemy();
            _enemiesSpawned++;
            yield return new WaitForSeconds(timeBetweenSpawns);
        }
    }

    private void SpawnEnemy()
    {
        if (enemyPrefab == null)
        {
            Debug.LogWarning("WaveManager: enemyPrefab not assigned.");
            return;
        }

        GameObject go = Instantiate(enemyPrefab, GetSpawnPosition(), Quaternion.identity);
        _enemiesAlive++;

        float mult = 1f + (WaveNumber - 1) * difficultyIncreasePerWave;
        if (go.TryGetComponent(out Enemy enemy))
            enemy.SetDifficultyMultiplier(mult);
    }

    // ── Called by Enemy.Die() ─────────────────────────────────────────────────

    public void OnEnemyDied()
    {
        _enemiesAlive = Mathf.Max(0, _enemiesAlive - 1);

        // Wave ends only after every enemy has been spawned AND killed
        if (_enemiesSpawned >= _enemiesToSpawn && _enemiesAlive <= 0)
            StartPreparation();
    }

    // ── Spawn position sampling ───────────────────────────────────────────────

    private Vector2 GetSpawnPosition()
    {
        float hw = safeZoneSize.x * 0.5f + spawnBorderWidth;
        float hh = safeZoneSize.y * 0.5f + spawnBorderWidth;
        float iw = safeZoneSize.x * 0.5f;
        float ih = safeZoneSize.y * 0.5f;

        float tbArea    = hw * 2f * spawnBorderWidth;
        float lrArea    = ih * 2f * spawnBorderWidth;
        float totalArea = 2f * tbArea + 2f * lrArea;
        float roll      = UnityEngine.Random.value * totalArea;

        Vector2 local;
        if (roll < tbArea)
            local = new Vector2(UnityEngine.Random.Range(-hw, hw),  UnityEngine.Random.Range(ih, ih + spawnBorderWidth));
        else if (roll < 2f * tbArea)
            local = new Vector2(UnityEngine.Random.Range(-hw, hw),  UnityEngine.Random.Range(-ih - spawnBorderWidth, -ih));
        else if (roll < 2f * tbArea + lrArea)
            local = new Vector2(UnityEngine.Random.Range(iw, iw + spawnBorderWidth),  UnityEngine.Random.Range(-ih, ih));
        else
            local = new Vector2(UnityEngine.Random.Range(-iw - spawnBorderWidth, -iw), UnityEngine.Random.Range(-ih, ih));

        return spawnCenter + local;
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        // Safe zone
        Gizmos.color = new Color(0f, 1f, 0f, 0.15f);
        Gizmos.DrawCube(spawnCenter, safeZoneSize);
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(spawnCenter, safeZoneSize);

        // Spawn ring
        Vector2 outerSize = safeZoneSize + Vector2.one * (spawnBorderWidth * 2f);
        Gizmos.color = new Color(1f, 0f, 0f, 0.1f);
        Gizmos.DrawCube(spawnCenter, outerSize);
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(spawnCenter, outerSize);
    }
}
