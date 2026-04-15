using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Master game-state machine: Preparation → Wave → (back to Preparation) … → GameOver.
///
/// Replaces the old WaveManager — remove the WaveManager component from your scene.
/// Enemy.Die() should call GameManager.Instance.OnEnemyDied().
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState { Preparation, Wave, GameOver }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Preparation")]
    [SerializeField] private float preparationDuration = 60f;

    [Header("Wave Scaling — Enemy Count")]
    [Tooltip("Enemies in wave 1")]
    [SerializeField] private int baseEnemyCount            = 10;
    [Tooltip("Extra enemies added each wave  (wave 2 = base+this, wave 3 = base+2×this …)")]
    [SerializeField] private int enemyCountIncreasePerWave = 5;
    [SerializeField] private float timeBetweenSpawns       = 0.4f;

    [Header("Wave Scaling — Enemy Stats  (0.10 = +10 % per wave)")]
    [Tooltip("Max health multiplier added per wave")]
    [SerializeField] private float healthScalePerWave  = 0.10f;
    [Tooltip("Move speed multiplier added per wave")]
    [SerializeField] private float speedScalePerWave   = 0.10f;
    [Tooltip("Attack damage multiplier added per wave")]
    [SerializeField] private float damageScalePerWave  = 0.10f;
    [SerializeField] private GameObject enemyPrefab;

    [Header("Spawn Area")]
    [SerializeField] private Vector2 spawnCenter      = Vector2.zero;
    [SerializeField] private Vector2 safeZoneSize     = new Vector2(10f, 10f);
    [SerializeField] private float   spawnBorderWidth = 5f;

    [Header("Startup")]
    [Tooltip("If true, the game starts automatically on scene load. Disable when using a menu Start button.")]
    [SerializeField] private bool autoStartOnSceneLoad = true;

    // ── Public state (read by UI / other systems) ─────────────────────────────

    public GameState CurrentState      { get; private set; } = GameState.Preparation;
    public int       WaveNumber        { get; private set; } = 0;
    public float     TimeRemaining     { get; private set; }
    public int       EnemiesRemaining  { get; private set; }
    public int       EnemiesThisWave   { get; private set; }

    // ── Events for UI ─────────────────────────────────────────────────────────

    /// <summary>Fired whenever the state changes.</summary>
    public event Action<GameState> OnStateChanged;

    /// <summary>Fired when the wave number increments (at the start of each Wave).</summary>
    public event Action<int> OnWaveNumberChanged;

    /// <summary>Fired every frame during Preparation with seconds remaining.</summary>
    public event Action<float> OnTimerChanged;

    /// <summary>Fired every time an enemy dies during a Wave.</summary>
    public event Action<int> OnEnemiesRemainingChanged;

    // ── Private ───────────────────────────────────────────────────────────────

    private int _enemiesSpawned;
    private GameObject _gameOverScreen;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (autoStartOnSceneLoad)
            StartGame();
    }

    /// <summary>
    /// Entry point for a UI Start button.
    /// Regenerates the map, rebuilds the pathfinding grid, and starts the preparation phase.
    /// </summary>
    public void StartGame()
    {
        StopAllCoroutines();
        Time.timeScale = 1f;

        WaveNumber = 0;
        TimeRemaining = 0f;
        EnemiesRemaining = 0;
        EnemiesThisWave = 0;
        _enemiesSpawned = 0;

        if (_gameOverScreen != null)
            _gameOverScreen.SetActive(false);

        ResourceManager.Instance?.ResetResources();

        MapGenerator mapGenerator = FindAnyObjectByType<MapGenerator>();
        if (mapGenerator != null)
        {
            mapGenerator.Generate();
            PathfindingGrid.Instance?.BuildGrid();
        }
        else
        {
            Debug.LogWarning("GameManager: MapGenerator not found. Starting game without map regeneration.");
        }

        EnterPreparation();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  State transitions
    // ═════════════════════════════════════════════════════════════════════════

    private void EnterPreparation()
    {
        CurrentState  = GameState.Preparation;
        TimeRemaining = preparationDuration;

        OnStateChanged?.Invoke(CurrentState);
        StartCoroutine(PreparationCountdown());
    }

    private void EnterWave()
    {
        WaveNumber++;
        CurrentState    = GameState.Wave;
        _enemiesSpawned = 0;
        EnemiesThisWave = baseEnemyCount + (WaveNumber - 1) * enemyCountIncreasePerWave;
        EnemiesRemaining = EnemiesThisWave;

        OnWaveNumberChanged?.Invoke(WaveNumber);
        OnStateChanged?.Invoke(CurrentState);
        OnEnemiesRemainingChanged?.Invoke(EnemiesRemaining);

        StartCoroutine(SpawnWave());
    }

    // ── Called by PlayerController.Die() ─────────────────────────────────────

    public void TriggerGameOver()
    {
        if (CurrentState == GameState.GameOver) return;

        StopAllCoroutines();
        CurrentState   = GameState.GameOver;
        Time.timeScale = 0f;

        OnStateChanged?.Invoke(CurrentState);
        ShowGameOverScreen();
    }

    // ── Called by Enemy.Die() ─────────────────────────────────────────────────

    public void OnEnemyDied()
    {
        EnemiesRemaining = Mathf.Max(0, EnemiesRemaining - 1);
        OnEnemiesRemainingChanged?.Invoke(EnemiesRemaining);

        // Only end the wave after every enemy has been spawned AND killed
        if (_enemiesSpawned >= EnemiesThisWave && EnemiesRemaining <= 0)
            EnterPreparation();
    }

    // ── Restart ───────────────────────────────────────────────────────────────

    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Coroutines
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator PreparationCountdown()
    {
        while (TimeRemaining > 0f)
        {
            TimeRemaining -= Time.deltaTime;
            OnTimerChanged?.Invoke(Mathf.Max(0f, TimeRemaining));
            yield return null;
        }

        EnterWave();
    }

    private IEnumerator SpawnWave()
    {
        // Each stat scales linearly: wave 1 = ×1.0, wave 2 = ×1.10, wave 3 = ×1.20 …
        float healthMult = 1f + (WaveNumber - 1) * healthScalePerWave;
        float speedMult  = 1f + (WaveNumber - 1) * speedScalePerWave;
        float damageMult = 1f + (WaveNumber - 1) * damageScalePerWave;

        Debug.Log($"[Wave {WaveNumber}] Spawning {EnemiesThisWave} enemies — " +
                  $"HP ×{healthMult:F2}  Speed ×{speedMult:F2}  DMG ×{damageMult:F2}");

        while (_enemiesSpawned < EnemiesThisWave)
        {
            SpawnEnemy(healthMult, speedMult, damageMult);
            _enemiesSpawned++;
            yield return new WaitForSeconds(timeBetweenSpawns);
        }
    }

    private void SpawnEnemy(float healthMult, float speedMult, float damageMult)
    {
        if (enemyPrefab == null)
        {
            Debug.LogWarning("GameManager: enemyPrefab not assigned.");
            return;
        }

        var go = Instantiate(enemyPrefab, GetSpawnPosition(), Quaternion.identity);

        if (go.TryGetComponent(out Enemy enemy))
            enemy.ApplyWaveScaling(healthMult, speedMult, damageMult);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Spawn position
    // ═════════════════════════════════════════════════════════════════════════

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
        if      (roll < tbArea)              local = new Vector2(UnityEngine.Random.Range(-hw, hw),  UnityEngine.Random.Range(ih,  ih  + spawnBorderWidth));
        else if (roll < 2f * tbArea)         local = new Vector2(UnityEngine.Random.Range(-hw, hw),  UnityEngine.Random.Range(-ih - spawnBorderWidth, -ih));
        else if (roll < 2f * tbArea + lrArea) local = new Vector2(UnityEngine.Random.Range(iw,  iw  + spawnBorderWidth), UnityEngine.Random.Range(-ih, ih));
        else                                 local = new Vector2(UnityEngine.Random.Range(-iw - spawnBorderWidth, -iw), UnityEngine.Random.Range(-ih, ih));

        return spawnCenter + local;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Game Over UI
    // ═════════════════════════════════════════════════════════════════════════

    private void ShowGameOverScreen()
    {
        if (_gameOverScreen != null) { _gameOverScreen.SetActive(true); return; }
        BuildGameOverScreen();
    }

    private void BuildGameOverScreen()
    {
        EnsureEventSystem();

        GameObject canvasGO = new GameObject("GameOverCanvas");
        Canvas canvas       = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        CanvasScaler scaler        = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // Dim overlay
        _gameOverScreen = new GameObject("GameOverScreen");
        _gameOverScreen.transform.SetParent(canvasGO.transform, false);

        RectTransform overlayRect = _gameOverScreen.AddComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        _gameOverScreen.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        // Panel
        GameObject panel            = new GameObject("Panel");
        panel.transform.SetParent(_gameOverScreen.transform, false);
        RectTransform panelRect     = panel.AddComponent<RectTransform>();
        panelRect.anchorMin         = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax         = new Vector2(0.5f, 0.5f);
        panelRect.pivot             = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta         = new Vector2(420f, 280f);
        panel.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 0.96f);

        VerticalLayoutGroup layout  = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding              = new RectOffset(30, 30, 30, 30);
        layout.spacing              = 18f;
        layout.childControlHeight   = true;
        layout.childControlWidth    = true;
        layout.childForceExpandHeight = true;
        layout.childForceExpandWidth  = true;

        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        var title       = MakeLabel(panel.transform, "Title",    "GAME OVER",               font, 54f);
        title.color     = new Color(0.9f, 0.15f, 0.15f);
        title.fontStyle = FontStyles.Bold;

        var waveLine    = MakeLabel(panel.transform, "WaveLine", $"You survived {WaveNumber} wave(s)", font, 26f);
        waveLine.color  = new Color(0.8f, 0.8f, 0.8f);

        // Restart button
        GameObject btnGO = new GameObject("RestartButton");
        btnGO.transform.SetParent(panel.transform, false);
        btnGO.AddComponent<RectTransform>();
        btnGO.AddComponent<Image>().color = new Color(0.18f, 0.55f, 0.18f);

        Button btn              = btnGO.AddComponent<Button>();
        ColorBlock cols         = btn.colors;
        cols.normalColor        = new Color(0.18f, 0.55f, 0.18f);
        cols.highlightedColor   = new Color(0.25f, 0.72f, 0.25f);
        cols.pressedColor       = new Color(0.10f, 0.38f, 0.10f);
        btn.colors              = cols;
        btn.onClick.AddListener(RestartGame);

        var btnLabel            = MakeLabel(btnGO.transform, "Label", "Restart", font, 32f);
        var btnLabelRect        = btnLabel.GetComponent<RectTransform>();
        btnLabelRect.anchorMin  = Vector2.zero;
        btnLabelRect.anchorMax  = Vector2.one;
        btnLabelRect.offsetMin  = Vector2.zero;
        btnLabelRect.offsetMax  = Vector2.zero;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Gizmos
    // ═════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 0f, 0.12f);
        Gizmos.DrawCube(spawnCenter, safeZoneSize);
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(spawnCenter, safeZoneSize);

        Vector2 outer = safeZoneSize + Vector2.one * (spawnBorderWidth * 2f);
        Gizmos.color  = new Color(1f, 0f, 0f, 0.08f);
        Gizmos.DrawCube(spawnCenter, outer);
        Gizmos.color  = Color.red;
        Gizmos.DrawWireCube(spawnCenter, outer);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════════════

    private static TextMeshProUGUI MakeLabel(Transform parent, string name, string text,
                                              TMP_FontAsset font, float size)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var tmp      = go.AddComponent<TextMeshProUGUI>();
        tmp.text     = text;
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color    = Color.white;
        if (font != null) tmp.font = font;
        return tmp;
    }

    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }
}
