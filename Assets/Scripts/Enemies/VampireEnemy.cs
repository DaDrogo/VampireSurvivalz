using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boss-level vampire enemy. Singleton — only one active at a time.
/// Active only at night; returns to coffin at dawn.
/// Gains XP by attacking/destroying buildings and levels up, choosing random upgrades.
/// Can only be permanently killed while vulnerable (HolyWell active or silver bullet hit).
/// Without vulnerability, reaching 0 HP triggers a banish — it respawns next night.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class VampireEnemy : MonoBehaviour, IDamageable
{
    public static VampireEnemy Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Base Stats")]
    [SerializeField] private float baseMaxHealth    = 200f;
    [SerializeField] private float baseMoveSpeed    = 2.2f;
    [SerializeField] private float attackDamage     = 20f;
    [SerializeField] private float attackInterval   = 1.5f;
    [SerializeField] private float attackRange      = 1.2f;
    [SerializeField] private float contactDamage    = 15f;
    [SerializeField] private float contactCooldown  = 1f;

    [Header("XP & Levelling")]
    [Tooltip("Cumulative XP thresholds to reach each next level. Index 0 = XP to reach Lv2, etc.")]
    [SerializeField] private float[] xpThresholds = { 30f, 70f, 130f, 200f, 280f };
    [SerializeField] private float xpPerAttackHit     = 5f;
    [SerializeField] private float xpPerBuildingKill  = 25f;

    [Header("Upgrades")]
    [SerializeField] private List<VampireUpgrade> availableUpgrades = new();

    [Header("Thrall")]
    [SerializeField] private GameObject thrallPrefab;

    // ── Events ────────────────────────────────────────────────────────────────

    public event Action<float, float> OnHealthChanged;     // (current, max)
    public event Action<int>          OnLevelChanged;      // new level
    public event Action               OnPermanentlyKilled;
    public event Action               OnBanished;
    public event Action               OnAttackPerformed;

    // ── IDamageable ───────────────────────────────────────────────────────────

    public float CurrentHealth { get; private set; }
    public float MaxHealth     { get; private set; }

    // ── Public state ──────────────────────────────────────────────────────────

    public int   Level     { get; private set; } = 1;
    public float CurrentXP { get; private set; } = 0f;
    public bool  IsVulnerable => _holyWellCount > 0 || _vulnerabilityTimer > 0f;

    public float XPProgressToNextLevel
    {
        get
        {
            int idx = Level - 1;
            if (idx >= xpThresholds.Length) return 1f;
            float prev = idx == 0 ? 0f : xpThresholds[idx - 1];
            float next = xpThresholds[idx];
            return Mathf.Clamp01((CurrentXP - prev) / (next - prev));
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private Rigidbody2D  _rb;
    private Transform    _player;
    private VampireCoffin _coffin;
    private bool         _isActive;
    private bool         _hasCoffin = true;

    private float _moveSpeed;
    private float _lifestealPercent  = 0f;
    private float _vulnerabilityTimer = 0f;
    private int   _holyWellCount     = 0;

    // Periodic AOEs
    private struct AoeEntry { public float radius, damage, interval, timer; }
    private readonly List<AoeEntry> _periodicAoes = new();

    // Phase dash
    private bool  _phaseDashEnabled  = false;
    private float _phaseDashInterval = 8f;
    private float _phaseDashRange    = 6f;
    private float _phaseDashTimer    = 0f;

    // Thrall spawn
    private bool  _thrallSpawnEnabled = false;
    private int   _thrallCount        = 0;
    private float _thrallInterval     = 20f;
    private float _thrallTimer        = 0f;

    // Upgrade pool (copy so we don't mutate the SO list)
    private readonly List<VampireUpgrade> _upgradePool = new();

    // Navigation — room-BFS (same approach as Enemy.cs)
    private Room    _myRoom;
    private Room    _playerRoom;
    private Door    _nextDoor;
    private Vector2 _navTarget;
    private Vector2 _doorApproachPt;
    private Vector2 _doorExitPt;
    private bool    _approachingDoor;
    private float   _navTimer;

    private const float PATH_REFRESH = 0.5f;
    private const float DOOR_REACH   = 0.8f;

    // Combat
    private IEnemyAttackable _attackTarget;
    private MonoBehaviour    _attackTargetMB;
    private float            _attackTimer;

    // Physics
    private readonly RaycastHit2D[] _castBuffer    = new RaycastHit2D[16];
    private readonly Collider2D[]   _overlapBuffer = new Collider2D[16];
    private float _contactTimer = 0f;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _rb = GetComponent<Rigidbody2D>();
        _rb.gravityScale   = 0f;
        _rb.freezeRotation = true;

        MaxHealth     = baseMaxHealth;
        CurrentHealth = MaxHealth;
        _moveSpeed    = baseMoveSpeed;

        _upgradePool.AddRange(availableUpgrades);

        gameObject.SetActive(false);
    }

    private void Start()
    {
        var go = GameObject.FindGameObjectWithTag("Player");
        if (go != null) _player = go.transform;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!_isActive) return;

        if (_vulnerabilityTimer > 0f)
            _vulnerabilityTimer -= Time.deltaTime;

        TickPeriodicAoes();
        TickPhaseDash();
        TickThrallSpawn();
    }

    private void FixedUpdate()
    {
        if (!_isActive) return;
        if (_contactTimer > 0f) _contactTimer -= Time.fixedDeltaTime;

        if (_player == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) _player = go.transform;
            else return;
        }

        _navTimer -= Time.fixedDeltaTime;
        if (_navTimer <= 0f)
        {
            _navTimer = PATH_REFRESH;
            UpdateNavigation();
        }

        // Door traversal waypoints
        if (_nextDoor != null)
        {
            if (_approachingDoor && Vector2.Distance(_rb.position, _doorApproachPt) < DOOR_REACH)
            {
                _approachingDoor = false;
                _navTarget = _doorExitPt;
            }
            else if (!_approachingDoor && Vector2.Distance(_rb.position, _doorExitPt) < DOOR_REACH)
            {
                UpdateNavigation();
            }
        }

        // Resolve active attack target
        if (_attackTarget != null)
        {
            if (_attackTargetMB == null || _attackTarget.IsDestroyed || _attackTarget.CurrentHealth <= 0f)
                ClearAttackTarget();
            else
            {
                _rb.linearVelocity = Vector2.zero;
                TickAttack();
                return;
            }
        }

        if (CheckLinecastForTarget()) return;
        Navigate();
    }

    // ── Activation ────────────────────────────────────────────────────────────

    public void Activate(VampireCoffin coffin)
    {
        _coffin   = coffin;
        _hasCoffin = coffin != null;
        _isActive = true;
        gameObject.SetActive(true);

        var go = GameObject.FindGameObjectWithTag("Player");
        if (go != null) _player = go.transform;

        _navTimer = 0f;
        UpdateNavigation();
    }

    public void Deactivate()
    {
        _isActive       = false;
        _contactTimer   = 0f;
        _rb.linearVelocity = Vector2.zero;
        _attackTarget   = null;
        _attackTargetMB = null;
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Called by VampireCoffin at dawn: bank XP, level up, then sleep until next night.
    /// </summary>
    public void ReturnToCoffin()
    {
        ProcessLevelUps();
        Deactivate();
    }

    public void LoseCoffin()
    {
        _coffin    = null;
        _hasCoffin = false;
    }

    // ── Vulnerability API ─────────────────────────────────────────────────────

    public void MakeVulnerable(float duration) =>
        _vulnerabilityTimer = Mathf.Max(_vulnerabilityTimer, duration);

    public void RegisterHolyWell()   => _holyWellCount++;
    public void UnregisterHolyWell() => _holyWellCount = Mathf.Max(0, _holyWellCount - 1);

    // ── Upgrade API (called by VampireUpgrade subclasses) ────────────────────

    public void AddLifestealPercent(float pct)  => _lifestealPercent += pct;
    public void AddSpeedBonus(float bonus)       => _moveSpeed        += bonus;

    public void AddPeriodicAoe(float radius, float damage, float interval)
    {
        _periodicAoes.Add(new AoeEntry { radius = radius, damage = damage, interval = interval, timer = interval });
    }

    public void AddThrallSpawn(int count, float interval)
    {
        _thrallSpawnEnabled = true;
        _thrallCount        = count;
        _thrallInterval     = interval;
        _thrallTimer        = 0f;
    }

    public void SetupPhaseDash(float interval, float range)
    {
        _phaseDashEnabled  = true;
        _phaseDashInterval = interval;
        _phaseDashRange    = range;
        _phaseDashTimer    = interval * 0.5f;
    }

    // ── IDamageable ───────────────────────────────────────────────────────────

    public void TakeDamage(float damage)
    {
        if (!_isActive) return;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);

        if (CurrentHealth <= 0f)
            HandleDeath();
    }

    // ── XP ────────────────────────────────────────────────────────────────────

    public void OnBuildingDestroyed() => GainXP(xpPerBuildingKill);

    /// <summary>Grants XP directly (e.g. from coffin passive accumulation).</summary>
    public void GrantXP(float amount) => GainXP(amount);

    private void GainXP(float amount)
    {
        CurrentXP += amount;
        ProcessLevelUps();
    }

    private void ProcessLevelUps()
    {
        while (Level - 1 < xpThresholds.Length && CurrentXP >= xpThresholds[Level - 1])
        {
            Level++;
            OnLevelChanged?.Invoke(Level);
            MaxHealth     *= 1.1f;
            CurrentHealth  = MaxHealth;
            OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
            ApplyRandomUpgrade();
        }
    }

    private void ApplyRandomUpgrade()
    {
        if (_upgradePool.Count == 0) return;

        int idx            = UnityEngine.Random.Range(0, _upgradePool.Count);
        VampireUpgrade upg = _upgradePool[idx];
        upg.Apply(this);

        if (upg.isUnique)
            _upgradePool.RemoveAt(idx);
    }

    // ── Death / banish ────────────────────────────────────────────────────────

    private void HandleDeath()
    {
        if (IsVulnerable)
        {
            OnPermanentlyKilled?.Invoke();
            Deactivate();
        }
        else
        {
            // Banish: partial health restore, will re-emerge next night via coffin
            CurrentHealth = MaxHealth * 0.5f;
            OnBanished?.Invoke();
            Deactivate();
        }
    }

    // ── Combat ────────────────────────────────────────────────────────────────

    private bool CheckLinecastForTarget()
    {
        var filter = new ContactFilter2D();
        filter.SetLayerMask(~(1 << gameObject.layer));
        filter.useTriggers = true;

        int count = Physics2D.Linecast(_rb.position, _navTarget, filter, _castBuffer);

        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = _castBuffer[i];
            if (hit.distance > attackRange) break;

            var col = hit.collider;
            if (col.gameObject == gameObject) continue;
            if (col.CompareTag("Enemy"))       continue;
            if (!ShouldTarget(col.gameObject)) continue;
            if (!col.TryGetComponent(out IEnemyAttackable atk)) continue;
            if (atk.IsDestroyed || atk.CurrentHealth <= 0f) continue;

            _attackTarget      = atk;
            _attackTargetMB    = atk as MonoBehaviour;
            _attackTimer       = 0f;
            _rb.linearVelocity = Vector2.zero;
            return true;
        }
        return false;
    }

    private bool ShouldTarget(GameObject go)
    {
        if (go.CompareTag("Player"))
            return Level >= 5;

        // Level 1-2: IEnemyAttackable only if it's a Barricade
        if (Level <= 2)
            return go.TryGetComponent<Barricade>(out _);

        // Level 3+: any building (IEnemyAttackable)
        return go.TryGetComponent<IEnemyAttackable>(out _);
    }

    private void TickAttack()
    {
        _attackTimer -= Time.fixedDeltaTime;
        if (_attackTimer > 0f) return;

        _attackTimer = attackInterval;
        OnAttackPerformed?.Invoke();
        _attackTarget.ReceiveEnemyAttack(attackDamage, attackInterval);
        GainXP(xpPerAttackHit);

        if (_lifestealPercent > 0f)
        {
            CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + attackDamage * _lifestealPercent);
            OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
        }
    }

    private void ClearAttackTarget()
    {
        _attackTarget   = null;
        _attackTargetMB = null;
        UpdateNavigation();
    }

    // ── Navigation (room-BFS) ─────────────────────────────────────────────────

    private void Navigate()
    {
        Vector2 dir = (_navTarget - _rb.position).normalized;
        _rb.linearVelocity = dir * _moveSpeed;
    }

    private void UpdateNavigation()
    {
        if (_player == null) return;

        HouseManager hm = HouseManager.Instance;
        if (hm == null)
        {
            _nextDoor  = null;
            _navTarget = GetPrimaryTarget();
            return;
        }

        _myRoom     = hm.GetRoomAt(_rb.position) ?? _myRoom;
        _playerRoom = hm.PlayerRoom;

        if (_myRoom == null || _playerRoom == null || _myRoom == _playerRoom)
        {
            _nextDoor  = null;
            _navTarget = GetPrimaryTarget();
            return;
        }

        Door previous = _nextDoor;
        _nextDoor = BFSNextDoor(_myRoom, _playerRoom);

        if (_nextDoor != null)
        {
            if (_nextDoor != previous)
            {
                Vector2 through  = DoorThroughDir(_nextDoor);
                _doorApproachPt  = _nextDoor.WorldPosition - through;
                _doorExitPt      = _nextDoor.WorldPosition + through;
                _approachingDoor = true;
                _navTarget       = _doorApproachPt;
            }
            else
            {
                _navTarget = _approachingDoor ? _doorApproachPt : _doorExitPt;
            }
        }
        else
        {
            _approachingDoor = false;
            _navTarget = GetPrimaryTarget();
        }
    }

    private Vector2 GetPrimaryTarget() =>
        _player != null ? (Vector2)_player.position : _rb.position;

    private static Door BFSNextDoor(Room start, Room goal)
    {
        if (start == goal) return null;
        var queue     = new Queue<Room>();
        var visited   = new HashSet<Room>();
        var firstDoor = new Dictionary<Room, Door>();

        visited.Add(start);
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            Room cur = queue.Dequeue();
            foreach (Door door in cur.Doors)
            {
                Room nb = door.GetOtherRoom(cur);
                if (visited.Contains(nb)) continue;
                visited.Add(nb);
                firstDoor[nb] = cur == start ? door : firstDoor[cur];
                if (nb == goal) return firstDoor[nb];
                queue.Enqueue(nb);
            }
        }
        return null;
    }

    private Vector2 DoorThroughDir(Door door)
    {
        if (door.IsHorizontalWall)
        {
            float fromY = _myRoom != null ? _myRoom.WorldCenter.y : _rb.position.y;
            return new Vector2(0f, fromY < door.WorldPosition.y ? 1f : -1f);
        }
        float fromX = _myRoom != null ? _myRoom.WorldCenter.x : _rb.position.x;
        return new Vector2(fromX < door.WorldPosition.x ? 1f : -1f, 0f);
    }

    // ── Periodic systems ──────────────────────────────────────────────────────

    private void TickPeriodicAoes()
    {
        for (int i = 0; i < _periodicAoes.Count; i++)
        {
            AoeEntry e = _periodicAoes[i];
            e.timer -= Time.deltaTime;
            if (e.timer <= 0f)
            {
                e.timer = e.interval;
                TriggerAoe(e.radius, e.damage);
            }
            _periodicAoes[i] = e;
        }
    }

    private void TriggerAoe(float radius, float damage)
    {
        var filter = new ContactFilter2D().NoFilter();
        int count  = Physics2D.OverlapCircle(transform.position, radius, filter, _overlapBuffer);
        for (int i = 0; i < count; i++)
        {
            var col = _overlapBuffer[i];
            if (col.gameObject == gameObject) continue;
            if (col.TryGetComponent(out IDamageable d) && d is not VampireEnemy)
                d.TakeDamage(damage);
        }
    }

    private void TickPhaseDash()
    {
        if (!_phaseDashEnabled || _player == null) return;
        _phaseDashTimer -= Time.deltaTime;
        if (_phaseDashTimer > 0f) return;

        _phaseDashTimer = _phaseDashInterval;

        Vector2 dir  = ((Vector2)_player.position - _rb.position).normalized;
        float   dist = Vector2.Distance(_rb.position, _player.position);
        _rb.position = _rb.position + dir * Mathf.Min(dist * 0.8f, _phaseDashRange);
    }

    private void TickThrallSpawn()
    {
        if (!_thrallSpawnEnabled) return;
        _thrallTimer -= Time.deltaTime;
        if (_thrallTimer > 0f) return;

        _thrallTimer = _thrallInterval;
        GameManager.Instance?.SpawnExtraEnemies(_thrallCount);
    }

    // ── Contact damage ────────────────────────────────────────────────────────

    private void OnCollisionStay2D(Collision2D col)
    {
        if (!_isActive) return;
        if (_contactTimer > 0f) return;
        if (!col.gameObject.CompareTag("Player")) return;

        if (col.gameObject.TryGetComponent(out IDamageable target))
        {
            target.TakeDamage(contactDamage);
            _contactTimer = contactCooldown;

            if (_lifestealPercent > 0f)
            {
                CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + contactDamage * _lifestealPercent);
                OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
            }
        }
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (!_isActive) return;
        Gizmos.color = IsVulnerable ? Color.red : new Color(0.6f, 0f, 1f);
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, _navTarget);
    }
}
