using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Enemy : MonoBehaviour, IDamageable
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Stats")]
    [SerializeField] private float maxHealth  = 50f;
    [SerializeField] private float moveSpeed  = 2.5f;

    [Header("Attack")]
    [SerializeField] private float attackDamage   = 10f;
    [SerializeField] private float attackInterval = 1.5f;

    [Header("Forward Detection")]
    [Tooltip("Radius of the overlap circle in front of the enemy.")]
    [SerializeField] private float detectionRadius = 0.7f;
    [Tooltip("How far ahead to centre the detection circle.")]
    [SerializeField] private float detectionOffset = 0.5f;
    [Tooltip("Layers checked for IDamageable targets. Enemies are always excluded.")]
    [SerializeField] private LayerMask attackableLayers = ~0;

    [Header("Contact Damage")]
    [SerializeField] private float contactDamage   = 5f;
    [SerializeField] private float contactCooldown = 1f;

    [Header("Navigation")]
    [Tooltip("How often (seconds) the room-BFS navigation is re-evaluated.")]
    [SerializeField] private float pathRefreshInterval = 0.4f;

    // ── IDamageable ───────────────────────────────────────────────────────────

    public float CurrentHealth { get; private set; }
    public float MaxHealth     => maxHealth;

    // ── Constants ─────────────────────────────────────────────────────────────

    private const float DOOR_REACH      = 0.8f;   // distance at which the enemy "crosses" a door
    private const float STUCK_THRESHOLD = 1.5f;   // seconds before declaring stuck
    private const float STUCK_MIN_MOVE  = 0.08f;  // minimum movement per check interval

    // ── Private ───────────────────────────────────────────────────────────────

    private Rigidbody2D _rb;
    private Transform   _player;

    // Room-BFS navigation
    private Room    _myRoom;
    private Room    _playerRoom;
    private Door    _nextDoor;     // door to walk toward; null = go straight to player
    private Vector2 _navTarget;    // current movement destination

    private float   _navTimer;
    private Vector2 _lastMoveDir;

    // Stuck detection
    private Vector2 _stuckCheckPos;
    private float   _stuckTimer;

    // Attack
    private IDamageable   _attackTarget;
    private MonoBehaviour _attackTargetMB;
    private float         _attackTimer;

    private float _contactTimer;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        SpriteColliderAutoFit.Fit(gameObject);
        _rb = GetComponent<Rigidbody2D>();
        _rb.gravityScale   = 0f;
        _rb.freezeRotation = true;
        _rb.linearDamping  = 0f;
        CurrentHealth      = maxHealth;

        // Enemies must not physically block each other — otherwise they pile up
        // at narrow door openings and jam.  One call disables all enemy↔enemy
        // rigidbody collisions globally for whatever layer this object is on.
        // REQUIREMENT: Enemy prefabs must be on a dedicated layer (e.g. "Enemy"),
        // NOT on the Default layer (0), or this would disable all default collisions.
        int layer = gameObject.layer;
        if (layer != 0)
            Physics2D.IgnoreLayerCollision(layer, layer, true);
    }

#if UNITY_EDITOR
    private void OnValidate() => SpriteColliderAutoFit.Fit(gameObject);
#endif

    private void Start()
    {
        GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
        if (playerGO != null)
            _player = playerGO.transform;
        else
            Debug.LogWarning("[Enemy] No GameObject tagged 'Player' found.", this);

        _lastMoveDir   = _player != null
            ? ((Vector2)_player.position - _rb.position).normalized
            : Vector2.down;
        _stuckCheckPos = _rb.position;
        _navTarget     = _player != null ? (Vector2)_player.position : _rb.position;

        // Stagger so spawned enemies don't all recalculate on the same frame
        _navTimer = Random.Range(0f, pathRefreshInterval);
        UpdateNavigation();
    }

    private void FixedUpdate()
    {
        if (_contactTimer > 0f) _contactTimer -= Time.fixedDeltaTime;

        if (_player == null)
        {
            GameObject go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) _player = go.transform;
        }
        if (_player == null) return;

        // ── Periodic navigation refresh ───────────────────────────────────────
        _navTimer -= Time.fixedDeltaTime;
        if (_navTimer <= 0f)
        {
            _navTimer = pathRefreshInterval;
            UpdateNavigation();
        }

        // ── If close to the current door, force an immediate nav update ───────
        if (_nextDoor != null &&
            Vector2.Distance(_rb.position, _nextDoor.WorldPosition) < DOOR_REACH)
        {
            UpdateNavigation();
        }

        // ── Active attack target ──────────────────────────────────────────────
        if (_attackTarget != null)
        {
            if (_attackTargetMB == null || _attackTarget.CurrentHealth <= 0f)
            {
                ClearAttackTarget();
                return;
            }
            _rb.linearVelocity = Vector2.zero;
            bool justFired = TickAttack();
            // After each hit, re-check whether the target is still in range.
            // If it has moved away, resume movement immediately.
            if (justFired && !IsAttackTargetInRange())
                ClearAttackTarget();
            return;
        }

        // ── Stuck detection ───────────────────────────────────────────────────
        if (Vector2.Distance(_rb.position, _stuckCheckPos) > STUCK_MIN_MOVE)
        {
            _stuckTimer    = 0f;
            _stuckCheckPos = _rb.position;
        }
        else
        {
            _stuckTimer += Time.fixedDeltaTime;
            if (_stuckTimer >= STUCK_THRESHOLD)
            {
                // Bypass door routing for this cycle — head directly for the player
                _nextDoor  = null;
                _navTarget = _player.position;
                _stuckTimer    = 0f;
                _stuckCheckPos = _rb.position;
            }
        }

        // ── Move and scan ─────────────────────────────────────────────────────
        Navigate();
        CheckForwardDetection();
    }

    // ── Room-BFS navigation ───────────────────────────────────────────────────

    /// <summary>
    /// Determines where the enemy should move next:
    ///   • Same room as player  → move directly to player.
    ///   • Different room       → BFS the room graph, aim at the first Door in the sequence.
    ///   • HouseManager absent  → fall back to direct movement.
    /// </summary>
    private void UpdateNavigation()
    {
        if (_player == null) return;

        HouseManager hm = HouseManager.Instance;
        if (hm == null)
        {
            // Room system not available — just chase the player directly
            _nextDoor  = null;
            _navTarget = _player.position;
            return;
        }

        // Preserve last known room while the enemy is inside a wall gap (GetRoomAt returns null)
        _myRoom     = hm.GetRoomAt(_rb.position) ?? _myRoom;
        _playerRoom = hm.PlayerRoom;

        // Same room, or room data not ready → go straight to the player
        if (_myRoom == null || _playerRoom == null || _myRoom == _playerRoom)
        {
            _nextDoor  = null;
            _navTarget = _player.position;
            return;
        }

        // Different rooms: BFS to find the first door to step through
        _nextDoor = BFSNextDoor(_myRoom, _playerRoom);

        _navTarget = _nextDoor != null
            ? DoorPassThroughPoint(_nextDoor)   // aim past the door so we walk fully through
            : (Vector2)_player.position;         // no path found — direct chase
    }

    /// <summary>
    /// Breadth-first search on the room graph.
    /// Returns the Door leaving <paramref name="start"/> that lies on the
    /// shortest path to <paramref name="goal"/>, or null if unreachable.
    /// </summary>
    private static Door BFSNextDoor(Room start, Room goal)
    {
        if (start == goal) return null;

        // For each room we reach, remember which door from 'start' put us on this path
        var queue     = new Queue<Room>();
        var visited   = new HashSet<Room>();
        var firstDoor = new Dictionary<Room, Door>();

        visited.Add(start);
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            Room current = queue.Dequeue();

            foreach (Door door in current.Doors)
            {
                Room neighbor = door.GetOtherRoom(current);
                if (visited.Contains(neighbor)) continue;

                visited.Add(neighbor);

                // Track which door from 'start' leads to this neighbor
                firstDoor[neighbor] = current == start
                    ? door
                    : firstDoor[current];

                if (neighbor == goal)
                    return firstDoor[neighbor];

                queue.Enqueue(neighbor);
            }
        }

        return null; // rooms are disconnected
    }

    // ── Door pass-through ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns a point <c>PASS_OFFSET</c> units past the door centre on the far
    /// side of the dividing wall.  Aiming beyond the door prevents the enemy from
    /// stopping at the wall edge and getting wedged by the wall tiles.
    /// </summary>
    private Vector2 DoorPassThroughPoint(Door door)
    {
        const float PASS_OFFSET = 1.5f;
        Vector2 doorPos = door.WorldPosition;

        // Use the room centre as the stable "came from" reference.
        // Basing direction on _rb.position would flip the sign the moment the
        // enemy crosses the door midpoint, causing it to reverse and stall.
        if (_myRoom != null)
        {
            Vector2 dir = (doorPos - _myRoom.WorldCenter).normalized;
            return doorPos + dir * PASS_OFFSET;
        }

        // Fallback (no room data yet)
        if (door.IsHorizontalWall)
            return doorPos + new Vector2(0f, (_rb.position.y < doorPos.y ? 1f : -1f) * PASS_OFFSET);
        else
            return doorPos + new Vector2((_rb.position.x < doorPos.x ? 1f : -1f) * PASS_OFFSET, 0f);
    }

    // ── Movement ──────────────────────────────────────────────────────────────

    private void Navigate()
    {
        Vector2 dir = (_navTarget - _rb.position).normalized;
        _lastMoveDir       = dir;
        _rb.linearVelocity = dir * moveSpeed;
    }

    // ── Forward detection ─────────────────────────────────────────────────────

    private void CheckForwardDetection()
    {
        Vector2    origin = _rb.position + _lastMoveDir * detectionOffset;
        Collider2D[] hits = Physics2D.OverlapCircleAll(origin, detectionRadius, attackableLayers);

        foreach (Collider2D hit in hits)
        {
            if (hit.gameObject == gameObject) continue;
            if (hit.CompareTag("Enemy"))      continue;

            if (!hit.TryGetComponent(out IDamageable damageable)) continue;
            if (damageable.CurrentHealth <= 0f)                   continue;

            _attackTarget      = damageable;
            _attackTargetMB    = damageable as MonoBehaviour;
            _attackTimer       = 0f;
            _rb.linearVelocity = Vector2.zero;
            return;
        }
    }

    // ── Attack ────────────────────────────────────────────────────────────────

    /// <returns>True the frame the attack actually fires.</returns>
    private bool TickAttack()
    {
        _attackTimer -= Time.fixedDeltaTime;
        if (_attackTimer > 0f) return false;

        _attackTimer = attackInterval;
        _attackTarget.TakeDamage(attackDamage);
        return true;
    }

    /// <summary>
    /// Returns true if the current attack target is still inside the forward
    /// detection circle (i.e. the enemy should keep standing still to attack).
    /// </summary>
    private bool IsAttackTargetInRange()
    {
        if (_attackTargetMB == null) return false;
        Vector2    origin = _rb.position + _lastMoveDir * detectionOffset;
        Collider2D[] hits = Physics2D.OverlapCircleAll(origin, detectionRadius, attackableLayers);
        foreach (Collider2D hit in hits)
            if (hit.gameObject == _attackTargetMB.gameObject) return true;
        return false;
    }

    private void ClearAttackTarget()
    {
        _attackTarget   = null;
        _attackTargetMB = null;
        UpdateNavigation();
    }

    // ── IDamageable ───────────────────────────────────────────────────────────

    public void TakeDamage(float damage)
    {
        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
        if (CurrentHealth <= 0f) Die();
    }

    public void ApplyWaveScaling(float healthMult, float speedMult, float damageMult)
    {
        maxHealth     *= healthMult;
        moveSpeed     *= speedMult;
        attackDamage  *= damageMult;
        contactDamage *= damageMult;
        CurrentHealth  = maxHealth;
    }

    private void Die()
    {
        GameManager.Instance?.OnEnemyDied();
        Destroy(gameObject);
    }

    // ── Contact damage ────────────────────────────────────────────────────────

    private void OnCollisionStay2D(Collision2D col)
    {
        if (_contactTimer > 0f)                   return;
        if (!col.gameObject.CompareTag("Player")) return;
        if (!col.gameObject.TryGetComponent(out IDamageable damageable)) return;

        damageable.TakeDamage(contactDamage);
        _contactTimer = contactCooldown;
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        // Forward detection circle
        Gizmos.color = _attackTarget != null ? Color.red : new Color(1f, 0.5f, 0f, 0.8f);
        Gizmos.DrawWireSphere(
            (Vector2)transform.position + _lastMoveDir * detectionOffset,
            detectionRadius);

        // Navigation target
        Gizmos.color = _nextDoor != null ? Color.yellow : Color.green;
        Gizmos.DrawLine(transform.position, _navTarget);
        Gizmos.DrawWireSphere(_navTarget, 0.2f);

        // Next door highlight
        if (_nextDoor != null)
        {
            Gizmos.color = new Color(1f, 0.85f, 0f, 1f);
            Gizmos.DrawWireSphere(_nextDoor.WorldPosition, DOOR_REACH);
        }
    }
}
