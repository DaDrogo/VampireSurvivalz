using UnityEngine;

/// <summary>
/// Spawns and shelters the VampireEnemy.
///
/// Lifecycle:
///   Night start      → activates the vampire at the coffin's position.
///   DawnTransition   → recalls the vampire; it banks XP, levels up, and sleeps.
///   Coffin destroyed → vampire loses its coffin and can now be permanently killed.
///
/// Passive bonus: every <see cref="passiveXpInterval"/> seconds while the vampire sleeps,
/// the coffin accumulates ambient XP that is granted in bulk on recall.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class VampireCoffin : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 300f;

    [Header("Passive XP (while vampire sleeps)")]
    [SerializeField] private float passiveXpAmount   = 5f;
    [SerializeField] private float passiveXpInterval = 30f;

    [Header("Vampire")]
    [Tooltip("VampireEnemy prefab instantiated on the first Night. Leave empty if placed in scene.")]
    [SerializeField] private GameObject vampirePrefab;

    [Header("Night Respawn")]
    [Tooltip("Seconds after being banished before the vampire tries to respawn.")]
    [SerializeField] private float respawnDelay = 30f;
    [Tooltip("MaxHealth multiplier applied on each mid-night respawn (0.25 = +25 % per respawn).")]
    [SerializeField] private float respawnHealthBonus = 0.25f;

    // ── IDamageable / IEnemyAttackable ────────────────────────────────────────

    public float CurrentHealth { get; private set; }
    public float MaxHealth     => maxHealth;
    public bool  IsDestroyed   => this == null || CurrentHealth <= 0f;

    public void ReceiveEnemyAttack(float damage, float _) { }

    public void TakeDamage(float damage) { }

    // ── Private ───────────────────────────────────────────────────────────────

    private float _passiveXpTimer      = 0f;
    private float _accumulatedPassiveXp = 0f;
    private bool  _vampireOut           = false;

    private float _respawnTimer = -1f;   // -1 = not running
    private int   _respawnCount = 0;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        SpriteColliderAutoFit.Fit(gameObject);
        CurrentHealth = maxHealth;

        if (TryGetComponent(out SpriteRenderer sr))
            sr.sortingOrder = 5;   // above floor/wall tiles (order 0–2), below UI
    }

    // OnEnable fires during the Awake phase — before GameManager.Awake creates DayNightManager.
    // Subscribe in Start() instead, which runs after all Awakes have completed.
    private void Start()
    {
        if (DayNightManager.Instance != null)
            DayNightManager.Instance.OnPhaseChanged += OnPhaseChanged;
        else
            Debug.LogWarning("[VampireCoffin] DayNightManager not found at Start — vampire will not spawn.", this);
    }

    private void OnDisable()
    {
        if (DayNightManager.Instance != null)
            DayNightManager.Instance.OnPhaseChanged -= OnPhaseChanged;
        UnsubscribeVampireEvents();
    }

    private void Update()
    {
        // Accumulate passive XP only while the vampire is sleeping here
        if (!_vampireOut)
        {
            _passiveXpTimer += Time.deltaTime;
            if (_passiveXpTimer >= passiveXpInterval)
            {
                _passiveXpTimer       -= passiveXpInterval;
                _accumulatedPassiveXp += passiveXpAmount;
            }
        }

        // Mid-night respawn countdown
        if (_respawnTimer > 0f)
        {
            _respawnTimer -= Time.deltaTime;
            if (_respawnTimer <= 0f)
            {
                _respawnTimer = -1f;
                TryNightRespawn();
            }
        }
    }

    // ── Phase events ──────────────────────────────────────────────────────────

    private void OnPhaseChanged(DayNightManager.Phase phase)
    {
        if (phase == DayNightManager.Phase.Night)
            SpawnVampire();
        else if (phase == DayNightManager.Phase.DawnTransition)
            RecallVampire();
    }

    private void SpawnVampire()
    {
        VampireEnemy vampire = VampireEnemy.Instance;

        if (vampire == null && vampirePrefab != null)
            vampire = Instantiate(vampirePrefab, transform.position, Quaternion.identity)
                      .GetComponent<VampireEnemy>();

        if (vampire == null) return;

        SubscribeVampireEvents(vampire);

        vampire.transform.position = transform.position;
        vampire.Activate(this);
        _vampireOut = true;
    }

    private void RecallVampire()
    {
        _respawnTimer = -1f;
        _respawnCount = 0;

        VampireEnemy vampire = VampireEnemy.Instance;
        if (vampire == null || !vampire.gameObject.activeSelf) return;

        // Grant accumulated passive XP then process level-ups inside ReturnToCoffin
        if (_accumulatedPassiveXp > 0f)
        {
            vampire.GrantXP(_accumulatedPassiveXp);
            _accumulatedPassiveXp = 0f;
        }

        vampire.ReturnToCoffin();
        _vampireOut = false;
    }

    private void TryNightRespawn()
    {
        if (DayNightManager.Instance == null) return;
        if (DayNightManager.Instance.CurrentPhase != DayNightManager.Phase.Night) return;
        if (GameManager.Instance?.CurrentState == GameManager.GameState.GameOver) return;

        _respawnCount++;

        VampireEnemy vampire = VampireEnemy.Instance;
        if (vampire != null)
            vampire.ScaleMaxHealth(1f + respawnHealthBonus);

        SpawnVampire();
    }

    // ── Vampire event wiring ──────────────────────────────────────────────────

    private void SubscribeVampireEvents(VampireEnemy vampire)
    {
        vampire.OnBanished          -= OnVampireBanished;
        vampire.OnBanished          += OnVampireBanished;
        vampire.OnPermanentlyKilled -= OnVampirePermanentlyKilled;
        vampire.OnPermanentlyKilled += OnVampirePermanentlyKilled;
    }

    private void UnsubscribeVampireEvents()
    {
        VampireEnemy vampire = VampireEnemy.Instance;
        if (vampire == null) return;
        vampire.OnBanished          -= OnVampireBanished;
        vampire.OnPermanentlyKilled -= OnVampirePermanentlyKilled;
    }

    private void OnVampireBanished()
    {
        _vampireOut   = false;
        _respawnTimer = respawnDelay;
    }

    private void OnVampirePermanentlyKilled()
    {
        _respawnTimer = -1f;
        _respawnCount = 0;
        _vampireOut   = false;
    }

    // ── Destruction ───────────────────────────────────────────────────────────

    private void DestroyCoffin()
    {
        VampireEnemy.Instance?.LoseCoffin();
        Destroy(gameObject);
    }
}
