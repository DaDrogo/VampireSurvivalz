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
[DefaultExecutionOrder(5)]
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState { Preparation, Wave, GameOver }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("UI Theme")]
    [SerializeField] private UITheme _theme;

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

    [Header("Vampire Coffin")]
    [Tooltip("VampireCoffin prefab spawned dynamically in the enemy room at game start.")]
    [SerializeField] private GameObject vampireCoffinPrefab;

    [Header("Citadel")]
    [Tooltip("Citadel prefab spawned in the player room at game start.")]
    [SerializeField] private GameObject citadelPrefab;

    [Header("Siege Enemies")]
    [Tooltip("Enemy prefab with siegeMode = true. Spawned in addition to regular enemies from this wave onwards.")]
    [SerializeField] private GameObject siegeEnemyPrefab;
    [Tooltip("Wave number from which siege enemies start spawning (1-based).")]
    [SerializeField] private int siegeEnemyStartWave = 3;
    [Tooltip("Number of siege enemies added per wave (capped to a reasonable maximum).")]
    [SerializeField] private int siegeEnemiesPerWave = 1;

    [Header("Spawn Area — Polygon (takes priority)")]
    [Tooltip("Draw a PolygonCollider2D (Trigger, no Rigidbody) over valid spawn ground. " +
             "When assigned, the ring below is ignored.")]
    [SerializeField] private PolygonCollider2D spawnArea;

    [Tooltip("Fallback list of Transforms used when no PolygonCollider2D is set.")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("Spawn Area — Obstacle Rejection")]
    [Tooltip("Layer mask for walls/geometry. Candidates overlapping this mask are rejected.")]
    [SerializeField] private LayerMask obstacleLayer;

    [Tooltip("Radius of the Physics2D.OverlapCircle check (~half enemy width).")]
    [SerializeField] private float spawnOverlapRadius = 0.4f;

    [Tooltip("Max random samples before falling back to nearest-walkable.")]
    [SerializeField] [Range(10, 200)] private int spawnMaxAttempts = 50;

    [Header("Spawn Area — Rect Fallback (used when no Polygon or Points assigned)")]
    [SerializeField] private Vector2 spawnCenter  = Vector2.zero;
    [SerializeField] private Vector2 spawnAreaSize = new Vector2(20f, 20f);

    [Header("Player Spawning")]
    [Tooltip("Player prefab to instantiate at game start. Leave empty if the player is already placed in the scene.")]
    [SerializeField] private GameObject playerPrefab;
    [Tooltip("World-space fallback hint for player spawn when no room data is available yet.")]
    [SerializeField] private Vector2 playerSpawnHint = Vector2.zero;

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

    /// <summary>Fired after the player is spawned or repositioned. UI should (re)bind HP here.</summary>
    public event Action<PlayerController> OnPlayerSpawned;

    // ── Private ───────────────────────────────────────────────────────────────

    private int          _enemiesSpawned;
    private int          _coinsEarnedThisRun;
    private GameObject   _gameOverScreen;
    private GameObject   _playerInstance;
    private MapGenerator _mapGenerator;
    private GameObject   _coffinInstance;
    private GameObject   _citadelInstance;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        if (DayNightManager.Instance == null)
        {
            var dnm = gameObject.AddComponent<DayNightManager>();
            dnm.SetTheme(_theme);
        }
    }

    private void OnEnable()
    {
        // VampireEnemy may not exist yet — we subscribe lazily in Update
    }

    private void Update()
    {
        // Late-bind victory subscription once the vampire is instantiated
        if (_vampireSubscribed) return;
        if (VampireEnemy.Instance == null) return;
        VampireEnemy.Instance.OnPermanentlyKilled += TriggerVictory;
        _vampireSubscribed = true;
    }

    private bool _vampireSubscribed = false;

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
        _coinsEarnedThisRun = 0;

        if (_gameOverScreen != null)
            _gameOverScreen.SetActive(false);

        ResourceManager.Instance?.ResetResources();

        if (_mapGenerator == null)
            _mapGenerator = FindAnyObjectByType<MapGenerator>();

        if (_mapGenerator != null)
        {
            _mapGenerator.Generate();
            PathfindingGrid.Instance?.BuildGrid();
        }
        else
        {
            Debug.LogWarning("GameManager: MapGenerator not found. Starting game without map regeneration.");
        }

        SpawnCoffin();
        SpawnCitadel();

        AudioManager.Instance?.StartGameMusic();
        SpawnOrRepositionPlayer();
        EnterPreparation();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Player spawning
    // ═════════════════════════════════════════════════════════════════════════

    private void SpawnOrRepositionPlayer()
    {
        Vector2 spawnPos = FindPlayerSpawnPosition();

        // Locate existing instance (could be scene-placed or previously instantiated)
        if (_playerInstance == null)
            _playerInstance = GameObject.FindGameObjectWithTag("Player");

        if (_playerInstance != null)
        {
            // Teleport the player; zero velocity so no carry-over from before
            if (_playerInstance.TryGetComponent(out Rigidbody2D rb))
            {
                rb.position        = spawnPos;
                rb.linearVelocity  = Vector2.zero;
            }
            else
            {
                _playerInstance.transform.position = spawnPos;
            }
        }
        else if (playerPrefab != null)
        {
            _playerInstance = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
        }
        else
        {
            Debug.LogWarning("GameManager: No player found in scene and no playerPrefab assigned. " +
                             "Assign a player prefab or place a tagged 'Player' object in the scene.");
        }

        if (_playerInstance != null && _playerInstance.TryGetComponent(out PlayerController pc))
            OnPlayerSpawned?.Invoke(pc);
    }

    /// <summary>
    /// Picks a safe world position for the player to spawn.
    /// Uses the enemy room centre; falls back to <see cref="playerSpawnHint"/> if unavailable.
    /// </summary>
    private Vector2 FindPlayerSpawnPosition()
    {
        Room enemyRoom = HouseManager.Instance?.EnemyRoom;
        if (enemyRoom != null)
        {
            PathNode node = PathfindingGrid.Instance?.FindNearestWalkable(enemyRoom.WorldCenter);
            return node != null ? node.WorldPos : enemyRoom.WorldCenter;
        }

        PathNode fallback = PathfindingGrid.Instance?.FindNearestWalkable(playerSpawnHint);
        return fallback != null ? fallback.WorldPos : playerSpawnHint;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  State transitions
    // ═════════════════════════════════════════════════════════════════════════

    private void EnterPreparation()
    {
        CurrentState  = GameState.Preparation;
        TimeRemaining = preparationDuration;

        if (WaveNumber > 0)
            AudioManager.Instance?.PlayWaveSurvived();

        OnStateChanged?.Invoke(CurrentState);
        StartCoroutine(PreparationCountdown());
    }

    private void EnterWave()
    {
        WaveNumber++;
        CurrentState    = GameState.Wave;
        _enemiesSpawned = 0;
        EnemiesThisWave  = baseEnemyCount + (WaveNumber - 1) * enemyCountIncreasePerWave;
        EnemiesRemaining += EnemiesThisWave;   // accumulate — previous wave enemies may still be alive

        AudioManager.Instance?.PlayWaveStart();
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

        PersistentDataManager.Instance?.RecordGameOver(WaveNumber);
        PersistentDataManager.Instance?.SaveKills();

        AudioManager.Instance?.PlayGameOver();
        OnStateChanged?.Invoke(CurrentState);
        ShowGameOverScreen();
    }

    // ── Coffin spawning ───────────────────────────────────────────────────────

    private void SpawnCitadel()
    {
        if (citadelPrefab == null) return;

        if (_citadelInstance != null)
            Destroy(_citadelInstance);

        // Spawn in the player room (opposite side from the enemy room)
        Room playerRoom = HouseManager.Instance?.PlayerRoom;
        // PlayerRoom is null at game start — fall back to player spawn hint position
        Vector2 spawnPos = playerSpawnHint;

        if (playerRoom != null)
        {
            PathNode node = PathfindingGrid.Instance?.FindNearestWalkable(playerRoom.WorldCenter);
            spawnPos = node != null ? node.WorldPos : playerRoom.WorldCenter;
        }
        else
        {
            PathNode node = PathfindingGrid.Instance?.FindNearestWalkable(playerSpawnHint);
            spawnPos = node != null ? node.WorldPos : playerSpawnHint;
        }

        _citadelInstance = Instantiate(citadelPrefab, spawnPos, Quaternion.identity);
    }

    private void SpawnCoffin()
    {
        if (vampireCoffinPrefab == null) return;

        // Destroy previous coffin from last run
        if (_coffinInstance != null)
            Destroy(_coffinInstance);

        Room enemyRoom = HouseManager.Instance?.EnemyRoom;
        Vector2 spawnPos;

        if (enemyRoom != null)
        {
            // Place near the center of the enemy room, snapped to nearest walkable tile
            PathNode node = PathfindingGrid.Instance?.FindNearestWalkable(enemyRoom.WorldCenter);
            spawnPos = node != null ? node.WorldPos : enemyRoom.WorldCenter;
        }
        else
        {
            // No room system — fall back to world origin
            spawnPos = Vector2.zero;
            Debug.LogWarning("[GameManager] EnemyRoom not found — spawning coffin at world origin.");
        }

        _coffinInstance = Instantiate(vampireCoffinPrefab, spawnPos, Quaternion.identity);
    }

    // ── Called by VampireEnemy system ────────────────────────────────────────

    /// <summary>
    /// Spawn additional enemies (e.g. vampire thralls). Uses the same pool/prefab as waves.
    /// </summary>
    public void SpawnExtraEnemies(int count)
    {
        if (enemyPrefab == null) return;
        float healthMult = 1f + (WaveNumber - 1) * healthScalePerWave;
        float speedMult  = 1f + (WaveNumber - 1) * speedScalePerWave;
        float damageMult = 1f + (WaveNumber - 1) * damageScalePerWave;
        for (int i = 0; i < count; i++)
            SpawnEnemy(healthMult, speedMult, damageMult);
    }

    /// <summary>Called when the vampire is permanently killed — player wins.</summary>
    public void TriggerVictory()
    {
        if (CurrentState == GameState.GameOver) return;
        StopAllCoroutines();
        CurrentState   = GameState.GameOver;
        Time.timeScale = 0f;
        AudioManager.Instance?.PlayVictory();
        OnStateChanged?.Invoke(CurrentState);
        ShowVictoryScreen();
    }

    private void ShowVictoryScreen()
    {
        EnsureEventSystem();

        GameObject canvasGO = new GameObject("VictoryCanvas");
        Canvas canvas       = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        CanvasScaler scaler        = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 1f;
        canvasGO.AddComponent<GraphicRaycaster>();

        GameObject screen = new GameObject("VictoryScreen");
        screen.transform.SetParent(canvasGO.transform, false);
        RectTransform sr = screen.AddComponent<RectTransform>();
        sr.anchorMin = Vector2.zero;
        sr.anchorMax = Vector2.one;
        sr.offsetMin = sr.offsetMax = Vector2.zero;
        screen.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        GameObject panel         = new GameObject("Panel");
        panel.transform.SetParent(screen.transform, false);
        RectTransform pr         = panel.AddComponent<RectTransform>();
        pr.anchorMin             = new Vector2(0.5f, 0.5f);
        pr.anchorMax             = new Vector2(0.5f, 0.5f);
        pr.pivot                 = new Vector2(0.5f, 0.5f);
        pr.sizeDelta             = new Vector2(420f, 320f);
        Image gameWonImg        = panel.AddComponent<Image>();
        UIHelper.ApplyImage(gameWonImg, _theme?.menuBackground, new Color(0.18f, 0.55f, 0.18f));


        VerticalLayoutGroup layout    = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding                = new RectOffset(30, 30, 30, 30);
        layout.spacing                = 18f;
        layout.childControlHeight     = true;
        layout.childControlWidth      = true;
        layout.childForceExpandHeight = true;
        layout.childForceExpandWidth  = true;

        TMP_FontAsset font = _theme?.font != null
            ? _theme.font
            : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        var title       = MakeLabel(panel.transform, "Title", "VICTORY!", font, 58f);
        title.color     = new Color(0.9f, 0.85f, 0.1f);
        title.fontStyle = FontStyles.Bold;

        var sub         = MakeLabel(panel.transform, "Sub", "The vampire is slain.", font, 26f);
        sub.color       = new Color(0.8f, 0.8f, 0.8f);

        GameObject btnGO = new GameObject("MenuButton");
        btnGO.transform.SetParent(panel.transform, false);
        btnGO.AddComponent<RectTransform>();
        Image victoryMenuImg = btnGO.AddComponent<Image>();
        UIHelper.ApplyImage(victoryMenuImg, _theme?.buttonNav, new Color(0.18f, 0.18f, 0.55f));
        Button btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = victoryMenuImg;
        btn.colors = UIHelper.BtnColors(_theme?.buttonSecondary,
            new Color(0.18f, 0.18f, 0.55f), new Color(0.25f, 0.25f, 0.72f), new Color(0.10f, 0.10f, 0.38f));
        btn.onClick.AddListener(ReturnToMainMenu);
        var lbl = MakeLabel(btnGO.transform, "Label", "Main Menu", font, 32f);
        lbl.GetComponent<RectTransform>().anchorMin = Vector2.zero;
        lbl.GetComponent<RectTransform>().anchorMax = Vector2.one;
    }

    // ── Called by Enemy.Die() ─────────────────────────────────────────────────

    public void OnEnemyDied()
    {
        EnemiesRemaining = Mathf.Max(0, EnemiesRemaining - 1);
        OnEnemiesRemainingChanged?.Invoke(EnemiesRemaining);
        PersistentDataManager.Instance?.AddKills(1);
        PersistentDataManager.Instance?.AddCurrency(1);
        _coinsEarnedThisRun++;
    }

    // ── Restart / Return to Menu ──────────────────────────────────────────────

    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void ReturnToMainMenu()
    {
        Time.timeScale = 1f;
        SceneTransitionManager.Instance?.LoadScene("MainMenuScene");
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

        // Siege enemies start appearing from siegeEnemyStartWave onwards
        if (siegeEnemyPrefab != null && WaveNumber >= siegeEnemyStartWave)
        {
            int count = Mathf.Min(siegeEnemiesPerWave + (WaveNumber - siegeEnemyStartWave), 5);
            for (int i = 0; i < count; i++)
            {
                SpawnEnemy(healthMult * 1.5f, speedMult * 0.7f, damageMult, siegeEnemyPrefab);
                yield return new WaitForSeconds(timeBetweenSpawns);
            }
        }

        // Next wave starts after the interval — regardless of surviving enemies
        EnterPreparation();
    }

    private void SpawnEnemy(float healthMult, float speedMult, float damageMult,
                             GameObject prefabOverride = null)
    {
        GameObject prefab = prefabOverride ?? enemyPrefab;
        if (prefab == null)
        {
            Debug.LogWarning("GameManager: enemyPrefab not assigned.");
            return;
        }

        Vector2 pos = GetSpawnPosition();
        var go = PoolManager.Instance != null
            ? PoolManager.Instance.Get(prefab, pos, Quaternion.identity)
            : Instantiate(prefab, pos, Quaternion.identity);

        if (go.TryGetComponent(out Enemy enemy))
            enemy.ApplyWaveScaling(healthMult, speedMult, damageMult);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Spawn position — polygon → point list → ring fallback, all obstacle-checked
    // ═════════════════════════════════════════════════════════════════════════

    private Vector2 GetSpawnPosition()
    {
        Room enemyRoom = HouseManager.Instance?.EnemyRoom;
        if (enemyRoom != null && _mapGenerator != null)
            return SampleEnemyRoom(enemyRoom);

        // Legacy fallback (polygon / point list / rect)
        Vector2 seed = spawnArea != null
            ? SamplePolygon()
            : (spawnPoints != null && spawnPoints.Length > 0 ? SamplePointList() : SampleRing());

        for (int i = 0; i < spawnMaxAttempts; i++)
        {
            Vector2 candidate = spawnArea != null
                ? SamplePolygon()
                : (spawnPoints != null && spawnPoints.Length > 0 ? SamplePointList() : SampleRing());

            if (IsSpawnCandidateValid(candidate))
                return candidate;
        }

        PathNode nearest = PathfindingGrid.Instance?.FindNearestWalkable(seed);
        return nearest != null ? nearest.WorldPos : seed;
    }

    private Vector2 SampleEnemyRoom(Room room)
    {
        RectInt bounds = room.TileBounds;

        for (int i = 0; i < spawnMaxAttempts; i++)
        {
            int     x         = UnityEngine.Random.Range(bounds.xMin, bounds.xMax);
            int     y         = UnityEngine.Random.Range(bounds.yMin, bounds.yMax);
            Vector2 candidate = _mapGenerator.TileToWorldCenter(new Vector2Int(x, y));

            if (IsSpawnCandidateValid(candidate))
                return candidate;
        }

        // Fallback: room centre snapped to nearest walkable
        PathNode node = PathfindingGrid.Instance?.FindNearestWalkable(room.WorldCenter);
        return node != null ? node.WorldPos : room.WorldCenter;
    }

    // ── PolygonCollider2D source ──────────────────────────────────────────────

    private Vector2 SamplePolygon()
    {
        Bounds b = spawnArea.bounds;

        for (int i = 0; i < spawnMaxAttempts; i++)
        {
            Vector2 candidate = new Vector2(
                UnityEngine.Random.Range(b.min.x, b.max.x),
                UnityEngine.Random.Range(b.min.y, b.max.y));

            if (spawnArea.OverlapPoint(candidate))
                return candidate;
        }

        // Polygon is very small or concave — return its center
        return b.center;
    }

    // ── Transform list source ─────────────────────────────────────────────────

    private Vector2 SamplePointList()
    {
        // Pick a random non-null transform from the list
        int start = UnityEngine.Random.Range(0, spawnPoints.Length);
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            Transform t = spawnPoints[(start + i) % spawnPoints.Length];
            if (t != null) return t.position;
        }

        return Vector2.zero;
    }

    // ── Rect fallback ─────────────────────────────────────────────────────────

    private Vector2 SampleRing()
    {
        float hw = spawnAreaSize.x * 0.5f;
        float hh = spawnAreaSize.y * 0.5f;

        return spawnCenter + new Vector2(
            UnityEngine.Random.Range(-hw, hw),
            UnityEngine.Random.Range(-hh, hh));
    }

    private bool IsSpawnCandidateValid(Vector2 candidate)
    {
        // Reject overlap with wall/obstacle layers.
        if (Physics2D.OverlapCircle(candidate, spawnOverlapRadius, obstacleLayer) != null)
            return false;

        // Also reject non-walkable grid cells when a pathfinding grid exists.
        PathfindingGrid grid = PathfindingGrid.Instance;
        if (grid == null) return true;

        PathNode node = grid.NodeFromWorld(candidate);
        return node != null && !node.IsWall && !node.IsBarricade;
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
        scaler.matchWidthOrHeight  = 1f;

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
        panelRect.sizeDelta         = new Vector2(420f, 380f);
        Image gameOverImg        = panel.AddComponent<Image>();
        UIHelper.ApplyImage(gameOverImg, _theme?.menuBackground, new Color(0.18f, 0.55f, 0.18f));

        VerticalLayoutGroup layout  = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding              = new RectOffset(30, 30, 30, 30);
        layout.spacing              = 18f;
        layout.childControlHeight   = true;
        layout.childControlWidth    = true;
        layout.childForceExpandHeight = true;
        layout.childForceExpandWidth  = true;

        TMP_FontAsset font = _theme?.font != null
            ? _theme.font
            : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        var title       = MakeLabel(panel.transform, "Title",    "GAME OVER",               font, 54f);
        title.color     = new Color(0.9f, 0.15f, 0.15f);
        title.fontStyle = FontStyles.Bold;

        var waveLine    = MakeLabel(panel.transform, "WaveLine", $"You survived {WaveNumber} wave(s)", font, 26f);
        waveLine.color = new Color(0f, 0f, 0f, 1f);

        int totalCoins  = PersistentDataManager.Instance?.TotalCurrency ?? 0;
        var coinLine    = MakeLabel(panel.transform, "CoinLine",
                          $"+{_coinsEarnedThisRun} coins  (total: {totalCoins})", font, 22f);
        coinLine.color  = new Color(1f, 0.85f, 0.2f);

        // Restart button
        GameObject btnGO = new GameObject("RestartButton");
        btnGO.transform.SetParent(panel.transform, false);
        btnGO.AddComponent<RectTransform>();
        Image restartImg        = btnGO.AddComponent<Image>();
        UIHelper.ApplyImage(restartImg, _theme?.buttonNav, new Color(0.18f, 0.55f, 0.18f));

        Button btn              = btnGO.AddComponent<Button>();
        btn.targetGraphic       = restartImg;
        btn.colors              = UIHelper.BtnColors(_theme?.buttonNav,
            new Color(0.18f, 0.55f, 0.18f), new Color(0.25f, 0.72f, 0.25f), new Color(0.10f, 0.38f, 0.10f));
        btn.onClick.AddListener(RestartGame);

        var btnLabel            = MakeLabel(btnGO.transform, "Label", "Restart", font, 32f);
        var btnLabelRect        = btnLabel.GetComponent<RectTransform>();
        btnLabelRect.anchorMin  = Vector2.zero;
        btnLabelRect.anchorMax  = Vector2.one;
        btnLabelRect.offsetMin  = Vector2.zero;
        btnLabelRect.offsetMax  = Vector2.zero;

        // Main Menu button
        GameObject menuBtnGO    = new GameObject("MainMenuButton");
        menuBtnGO.transform.SetParent(panel.transform, false);
        menuBtnGO.AddComponent<RectTransform>();
        Image menuImg               = menuBtnGO.AddComponent<Image>();
        UIHelper.ApplyImage(menuImg, _theme?.buttonDanger, new Color(0.15f, 0.32f, 0.62f));

        Button menuBtn              = menuBtnGO.AddComponent<Button>();
        menuBtn.targetGraphic       = menuImg;
        menuBtn.colors              = UIHelper.BtnColors(_theme?.buttonSecondary,
            new Color(0.15f, 0.32f, 0.62f), new Color(0.22f, 0.45f, 0.82f), new Color(0.08f, 0.20f, 0.42f));
        menuBtn.onClick.AddListener(ReturnToMainMenu);

        var menuLabel               = MakeLabel(menuBtnGO.transform, "Label", "Main Menu", font, 32f);
        var menuLabelRect           = menuLabel.GetComponent<RectTransform>();
        menuLabelRect.anchorMin     = Vector2.zero;
        menuLabelRect.anchorMax     = Vector2.one;
        menuLabelRect.offsetMin     = Vector2.zero;
        menuLabelRect.offsetMax     = Vector2.zero;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Gizmos
    // ═════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmos()
    {
        // Polygon spawn area bounds
        if (spawnArea != null)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.15f);
            Gizmos.DrawCube(spawnArea.bounds.center, spawnArea.bounds.size);
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f);
            Gizmos.DrawWireCube(spawnArea.bounds.center, spawnArea.bounds.size);
            return; // polygon defined — skip ring gizmos
        }

        // Spawn point markers
        if (spawnPoints != null)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f);
            foreach (Transform t in spawnPoints)
            {
                if (t == null) continue;
                Gizmos.DrawWireSphere(t.position, spawnOverlapRadius);
            }
            if (spawnPoints.Length > 0) return;
        }

        // Rect fallback
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.12f);
        Gizmos.DrawCube(spawnCenter, spawnAreaSize);
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f);
        Gizmos.DrawWireCube(spawnCenter, spawnAreaSize);
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
