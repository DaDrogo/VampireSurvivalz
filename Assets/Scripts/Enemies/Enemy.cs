using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(Rigidbody2D))]
public class Enemy : MonoBehaviour, IDamageable, ILexikonSource
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Stats")]
    [SerializeField] private float maxHealth  = 50f;
    [SerializeField] private float moveSpeed  = 2.5f;

    [Header("Attack")]
    [SerializeField] private float attackDamage   = 10f;
    [SerializeField] private float attackInterval = 1.5f;
    [Tooltip("Maximum distance at which the enemy stops to attack a blocker.")]
    [SerializeField] private float attackRange    = 1.2f;

    [Header("Linecast Block")]
    [Tooltip("Optional Animator — the bool 'IsAttacking' is set while the enemy is stopped attacking a blocker.")]
    [SerializeField] private Animator enemyAnimator;

    [Header("Contact Damage")]
    [SerializeField] private float contactDamage   = 5f;
    [SerializeField] private float contactCooldown = 1f;
    [Tooltip("Seconds the enemy must stay in contact with a resource before destroying it.")]
    [SerializeField] private float resourceDestroyTime = 3f;

    [Header("Navigation")]
    [Tooltip("How often (seconds) the room-BFS navigation is re-evaluated.")]
    [SerializeField] private float pathRefreshInterval = 0.4f;

    // ── IDamageable ───────────────────────────────────────────────────────────

    public float CurrentHealth { get; private set; }
    public float MaxHealth     => maxHealth;

    // ── Constants ─────────────────────────────────────────────────────────────

    private const float DOOR_REACH      = 0.8f;
    private const float STUCK_THRESHOLD = 3f;
    private const float STUCK_MIN_MOVE  = 0.20f;
    private const float STUCK_ATTACK_RADIUS = 1.5f;

    // Wall-break stuck system
    private const float WALL_BREAK_DURATION  = 10f;   // seconds against a wall before it breaks
    private const float WALL_BREAK_SCAN_DIST = 0.7f;  // distance ahead to probe for wall tiles
    private const float WALL_STUCK_SPEED_SQ  = 0.09f; // (0.3 m/s)² — below this = "stuck"

    private static readonly Vector3Int InvalidTile = new Vector3Int(int.MinValue, int.MinValue, 0);
    private static readonly Vector2[]  ScanDirs    =
        { Vector2.up, Vector2.down, Vector2.left, Vector2.right };

    // ── Private ───────────────────────────────────────────────────────────────

    private Rigidbody2D _rb;
    private Transform   _player;

    // Room-BFS navigation
    private Room    _myRoom;
    private Room    _playerRoom;
    private Door    _nextDoor;          // door to walk toward; null = go straight to player
    private Vector2 _navTarget;         // current movement destination

    // Two-waypoint door traversal
    private Vector2 _doorApproachPt;    // 1 tile before the door, on the approaching side
    private Vector2 _doorExitPt;        // 1 tile past the door, on the far side
    private bool    _approachingDoor;   // true = heading to approach pt; false = heading to exit pt

    private float   _navTimer;
    private Vector2 _lastMoveDir;

    // Stuck detection
    private Vector2 _stuckCheckPos;
    private float   _stuckTimer;

    // Attack
    private IEnemyAttackable _attackTarget;
    private MonoBehaviour    _attackTargetMB;
    private float            _attackTimer;

    private float _contactTimer;

    // Resource collision destruction
    private GameObject _resourceContact;
    private float      _resourceContactTimer;

    // Wall-slide state (populated by OnCollisionStay2D, consumed at the start of FixedUpdate)
    private bool    _wallContact;
    private Vector2 _wallNormal;   // accumulated (un-normalized) contact normals from wall tiles

    // Wall-break system
    private MapGenerator _mapGenerator;
    private float        _wallBreakTimer;
    private Vector3Int   _wallBreakTile;
    private bool         _hasWallBreakTarget;

    // Status effects (component added on demand by Projectile)
    private StatusEffectHandler _statusEffects;

    // Reusable physics buffers — avoid per-frame allocations
    private readonly RaycastHit2D[] _castBuffer    = new RaycastHit2D[16];
    private readonly Collider2D[]   _overlapBuffer = new Collider2D[8];

    // Base stats preserved for pool reset (ApplyWaveScaling mutates the fields)
    private float _baseMaxHealth;
    private float _baseMoveSpeed;
    private float _baseAttackDamage;
    private float _baseContactDamage;

    // Day/night runtime multipliers (applied on top of wave-scaled stats)
    private float _nightSpeedMult    = 1f;
    private float _nightDamageMult   = 1f;
    private float _permanentNightMult = 1f;   // grows each full cycle, never reset

    private float EffectiveMoveSpeed    => moveSpeed    * _nightSpeedMult  * _permanentNightMult;
    private float EffectiveAttackDamage => attackDamage * _nightDamageMult * _permanentNightMult;
    private float EffectiveContactDamage => contactDamage * _nightDamageMult * _permanentNightMult;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        SpriteColliderAutoFit.Fit(gameObject);
        _rb = GetComponent<Rigidbody2D>();
        _rb.gravityScale   = 0f;
        _rb.freezeRotation = true;
        _rb.linearDamping  = 0f;
        CurrentHealth      = maxHealth;

        _baseMaxHealth     = maxHealth;
        _baseMoveSpeed     = moveSpeed;
        _baseAttackDamage  = attackDamage;
        _baseContactDamage = contactDamage;

        // Enemies must not physically block each other — otherwise they pile up
        // at narrow door openings and jam.  One call disables all enemy↔enemy
        // rigidbody collisions globally for whatever layer this object is on.
        // REQUIREMENT: Enemy prefabs must be on a dedicated layer (e.g. "Enemy"),
        // NOT on the Default layer (0), or this would disable all default collisions.
        int layer = gameObject.layer;
        if (layer != 0)
            Physics2D.IgnoreLayerCollision(layer, layer, true);
    }

    private void OnEnable()
    {
        // Restore base stats so ApplyWaveScaling starts from clean values each reuse
        if (_baseMaxHealth > 0f)
        {
            maxHealth     = _baseMaxHealth;
            moveSpeed     = _baseMoveSpeed;
            attackDamage  = _baseAttackDamage;
            contactDamage = _baseContactDamage;
        }
        CurrentHealth = maxHealth;

        // Reset runtime navigation / combat state
        _attackTarget    = null;
        _attackTargetMB  = null;
        _attackTimer     = 0f;
        _contactTimer    = 0f;
        _resourceContact = null;
        _resourceContactTimer = 0f;
        _wallContact     = false;
        _wallNormal      = Vector2.zero;
        _stuckTimer      = 0f;
        _navTimer        = 0f;
        _myRoom          = null;
        _playerRoom      = null;
        _nextDoor        = null;
        _approachingDoor = false;

        if (_rb != null)
            _rb.linearVelocity = Vector2.zero;

        if (_statusEffects != null)
            _statusEffects.ClearAll();

        // Cancel any in-progress wall-break without un-tinting (tile may be gone / reused)
        _wallBreakTimer     = 0f;
        _hasWallBreakTarget = false;

        // Reset night mults — DayNightManager will re-apply them after registration
        _nightSpeedMult    = 1f;
        _nightDamageMult   = 1f;
        _permanentNightMult = 1f;

        DayNightManager.Instance?.RegisterEnemy(this);
    }

    private void OnDisable()
    {
        DayNightManager.Instance?.UnregisterEnemy(this);
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

        _statusEffects = GetComponent<StatusEffectHandler>();
        _mapGenerator  = FindAnyObjectByType<MapGenerator>();

        // Stagger so spawned enemies don't all recalculate on the same frame
        _navTimer = Random.Range(0f, pathRefreshInterval);
        UpdateNavigation();
    }

    private void FixedUpdate()
    {
        // Capture wall-contact state from the previous physics step's collision callbacks,
        // then clear so OnCollisionStay2D can repopulate for the next step.
        bool    wallContact = _wallContact;
        Vector2 wallNormal  = _wallNormal.sqrMagnitude > 0.001f
            ? _wallNormal.normalized
            : Vector2.zero;
        _wallContact = false;
        _wallNormal  = Vector2.zero;

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

        // ── Two-phase door traversal ──────────────────────────────────────────
        if (_nextDoor != null)
        {
            if (_approachingDoor &&
                Vector2.Distance(_rb.position, _doorApproachPt) < DOOR_REACH)
            {
                // Aligned in front of the door — drive straight through
                _approachingDoor = false;
                _navTarget       = _doorExitPt;
            }
            else if (!_approachingDoor &&
                     Vector2.Distance(_rb.position, _doorExitPt) < DOOR_REACH)
            {
                // Fully through — re-evaluate next destination
                UpdateNavigation();
            }
        }

        // ── Active attack target ──────────────────────────────────────────────
        if (_attackTarget != null)
        {
            // Target was destroyed — check for an adjacent blocker immediately.
            // A plain linecast misses colliders whose interior contains the cast origin
            // (e.g. a second barricade the enemy is already touching), so we use the
            // overlap check here rather than waiting for the next linecast or the 5-s
            // stuck timer.
            if (_attackTargetMB == null || _attackTarget.IsDestroyed || _attackTarget.CurrentHealth <= 0f)
            {
                ClearAttackTarget();
                if (TryAttackNearbyBlocker()) return;
                // Target gone and no adjacent blocker — fall through to
                // CheckLinecastBlock / Navigate so movement resumes this frame.
            }
            else
            {
                _rb.linearVelocity = Vector2.zero;
                TickAttack();
                return;
            }
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
                _stuckTimer    = 0f;
                _stuckCheckPos = _rb.position;

                // Attack whatever is physically blocking us before trying to reposition.
                if (TryAttackNearbyBlocker()) return;

                // Nothing attackable — push 1 tile away from nearby walls and reroute.
                PushAwayFromWalls();
            }
        }

        // ── Status-effect movement overrides ─────────────────────────────────
        // Lazy-init in case the handler was added by a projectile after Start().
        if (_statusEffects == null)
            _statusEffects = GetComponent<StatusEffectHandler>();

        if (_statusEffects != null && _statusEffects.IsStunned)
        {
            _rb.linearVelocity = Vector2.zero;
            // Reset stuck detection so the stun itself doesn't trigger a push.
            _stuckTimer    = 0f;
            _stuckCheckPos = _rb.position;
            return;
        }

        // ── Wall-break stuck system ───────────────────────────────────────────
        TickWallBreak();

        // ── Linecast block check then move ────────────────────────────────────
        if (CheckLinecastBlock()) return;
        Navigate(wallContact, wallNormal);
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
        Door previousDoor = _nextDoor;
        _nextDoor = BFSNextDoor(_myRoom, _playerRoom);

        if (_nextDoor != null)
        {
            if (_nextDoor != previousDoor)
            {
                // New door — compute fresh perpendicular waypoints and start approach phase.
                // Direction is baked here from the current _myRoom so it never flips mid-crossing.
                Vector2 through = DoorThroughDirection(_nextDoor);
                _doorApproachPt  = _nextDoor.WorldPosition - through;
                _doorExitPt      = _nextDoor.WorldPosition + through;
                _approachingDoor = true;
                _navTarget       = _doorApproachPt;
            }
            else
            {
                // Same door — the enemy is still in the middle of this traversal.
                // Re-assert the correct target for the current phase without touching
                // the baked waypoints or flipping the direction as _myRoom changes.
                _navTarget = _approachingDoor ? _doorApproachPt : _doorExitPt;
            }
        }
        else
        {
            _approachingDoor = false;
            _navTarget       = _player.position;   // no path found — direct chase
        }
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

    // ── Door traversal helpers ────────────────────────────────────────────────

    /// <summary>
    /// Returns a cardinal unit vector pointing FROM the enemy's current room
    /// THROUGH the door to the far room.
    /// Always axis-aligned (never diagonal) so the approach and exit waypoints
    /// land exactly 1 tile from the door centre with no lateral drift.
    /// Uses <see cref="_myRoom"/> as the stable "came from" reference so the
    /// sign can't flip once the enemy starts crossing.
    /// </summary>
    private Vector2 DoorThroughDirection(Door door)
    {
        if (door.IsHorizontalWall)
        {
            // Wall runs horizontally → cross in Y
            float fromY = _myRoom != null ? _myRoom.WorldCenter.y : _rb.position.y;
            return new Vector2(0f, fromY < door.WorldPosition.y ? 1f : -1f);
        }
        else
        {
            // Wall runs vertically → cross in X
            float fromX = _myRoom != null ? _myRoom.WorldCenter.x : _rb.position.x;
            return new Vector2(fromX < door.WorldPosition.x ? 1f : -1f, 0f);
        }
    }

    // ── Movement ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Moves the enemy toward <see cref="_navTarget"/>.
    ///
    /// Wall-contact behaviour:
    ///
    ///   Door target present (approach OR crossing phase):
    ///     Slide axis-aligned toward the door centre so the enemy lines up with
    ///     the gap.  Active in BOTH phases — without this, an enemy that switches
    ///     to the crossing phase while slightly off-centre will move directly into
    ///     the wall, making the generic projection zero out its velocity entirely
    ///     (dot product cancellation when movement and wall normal are antiparallel).
    ///
    ///   No door target (same room as player, or generic wall contact):
    ///     Use wall-plane projection — remove the into-wall component so the enemy
    ///     glides off corner tiles rather than getting pinned.
    /// </summary>
    private void Navigate(bool wallContact = false, Vector2 wallNormal = default)
    {
        float   speed = EffectiveMoveSpeed * (_statusEffects != null ? _statusEffects.SpeedMultiplier : 1f);
        Vector2 dir   = (_navTarget - _rb.position).normalized;

        if (wallContact && wallNormal.sqrMagnitude > 0.001f)
        {
            // ── Door-alignment slide ──────────────────────────────────────────
            // Active whenever a door is the current target, regardless of phase.
            // Slides the enemy along the wall toward the door centre so it can
            // pass through the opening cleanly.
            if (_nextDoor != null)
            {
                Vector2 slideDir;

                if (_nextDoor.IsHorizontalWall)
                {
                    // Dividing wall runs left-right → align in X with door centre
                    float dx = _nextDoor.WorldPosition.x - _rb.position.x;
                    slideDir = Mathf.Abs(dx) > 0.05f
                        ? new Vector2(Mathf.Sign(dx), 0f)
                        : Vector2.zero;
                }
                else
                {
                    // Dividing wall runs top-bottom → align in Y with door centre
                    float dy = _nextDoor.WorldPosition.y - _rb.position.y;
                    slideDir = Mathf.Abs(dy) > 0.05f
                        ? new Vector2(0f, Mathf.Sign(dy))
                        : Vector2.zero;
                }

                if (slideDir.sqrMagnitude > 0.001f)
                {
                    _lastMoveDir       = slideDir;
                    _rb.linearVelocity = slideDir * speed;
                    return;
                }
                // Already aligned — fall through to generic projection.
            }

            // ── Generic wall-plane projection ─────────────────────────────────
            // Used when no door target exists or when already aligned with the gap.
            // Removes the into-wall component so the enemy slides off corner tiles.
            Vector2 projected = dir - Vector2.Dot(dir, wallNormal) * wallNormal;
            if (projected.sqrMagnitude > 0.001f)
            {
                _lastMoveDir       = projected.normalized;
                _rb.linearVelocity = projected.normalized * speed;
                return;
            }
        }

        _lastMoveDir       = dir;
        _rb.linearVelocity = dir * speed;
    }

    // ── Wall push ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when the enemy has been stationary for <see cref="STUCK_THRESHOLD"/> seconds
    /// and no attackable blocker was found.
    /// Samples all solid (non-trigger) colliders within ~1.5 tiles, computes a push
    /// direction away from them using <see cref="Collider2D.ClosestPoint"/> (works
    /// correctly with tilemaps), and sets <see cref="_navTarget"/> to a tile-snapped
    /// position 1 tile in that direction.  Falls back to a direct player chase if
    /// no nearby wall is detected.
    /// </summary>
    private void PushAwayFromWalls()
    {
        int count = Physics2D.OverlapCircleNonAlloc(_rb.position, 1.5f, _overlapBuffer);

        Vector2 pushDir   = Vector2.zero;
        int     wallCount = 0;

        for (int i = 0; i < count; i++)
        {
            Collider2D col = _overlapBuffer[i];
            if (col.gameObject == gameObject) continue;
            if (col.isTrigger)               continue;
            if (col.CompareTag("Enemy"))      continue;
            if (col.CompareTag("Player"))     continue;

            // ClosestPoint handles composite / tilemap colliders correctly
            Vector2 closest = col.ClosestPoint(_rb.position);
            Vector2 away    = _rb.position - closest;

            if (away.sqrMagnitude < 0.0001f)
                away = Random.insideUnitCircle;   // fallback when perfectly overlapping

            pushDir += away.normalized;
            wallCount++;
        }

        _nextDoor        = null;
        _approachingDoor = false;

        if (wallCount > 0)
        {
            pushDir = (pushDir / wallCount).normalized;

            // Snap the target to the nearest tile centre so the enemy lands
            // cleanly on the grid rather than at a partial-unit offset.
            Vector2 raw    = _rb.position + pushDir;
            _navTarget     = new Vector2(
                Mathf.Floor(raw.x) + 0.5f,
                Mathf.Floor(raw.y) + 0.5f);
        }
        else
        {
            // No walls nearby — just head straight for the player
            _navTarget = _player.position;
        }

        // Force a full nav re-evaluation once the enemy reaches the push target
        UpdateNavigation();
    }

    // ── Wall-break stuck system ───────────────────────────────────────────────

    /// <summary>
    /// Accumulates a timer while the enemy is nearly motionless against an
    /// interior wall tile.  At <see cref="WALL_BREAK_DURATION"/> seconds the tile
    /// is removed and the pathfinding grid is updated so other enemies re-route.
    /// A crack tint (white → orange → red) gives the player visual warning.
    /// </summary>
    private void TickWallBreak()
    {
        // Only tick while truly stuck (velocity near zero)
        if (_rb.linearVelocity.sqrMagnitude > WALL_STUCK_SPEED_SQ)
        {
            CancelWallBreak();
            return;
        }

        Vector3Int tile = FindAdjacentInteriorWall();
        if (tile == InvalidTile)
        {
            CancelWallBreak();
            return;
        }

        // If the targeted tile changed, restart (enemy shifted position)
        if (_hasWallBreakTarget && tile != _wallBreakTile)
            CancelWallBreak();

        _wallBreakTile      = tile;
        _hasWallBreakTarget = true;
        _wallBreakTimer    += Time.fixedDeltaTime;

        ApplyWallCrackTint(_wallBreakTile, _wallBreakTimer / WALL_BREAK_DURATION);

        if (_wallBreakTimer >= WALL_BREAK_DURATION)
        {
            _mapGenerator?.TryBreakInteriorWall(_wallBreakTile);
            _hasWallBreakTarget = false;
            _wallBreakTimer     = 0f;
        }
    }

    private Vector3Int FindAdjacentInteriorWall()
    {
        if (_mapGenerator == null) return InvalidTile;

        Tilemap wt = _mapGenerator.WallTilemap;
        if (wt == null) return InvalidTile;

        foreach (Vector2 dir in ScanDirs)
        {
            Vector2    probe    = _rb.position + dir * WALL_BREAK_SCAN_DIST;
            Vector3Int tilePos  = wt.WorldToCell(probe);
            tilePos.z = 0;
            if (_mapGenerator.IsInteriorWall(tilePos))
                return tilePos;
        }
        return InvalidTile;
    }

    private void ApplyWallCrackTint(Vector3Int tilePos, float t)
    {
        if (_mapGenerator?.WallTilemap == null) return;
        Tilemap wt = _mapGenerator.WallTilemap;

        // white → orange (t<0.5) → red (t=1)
        Color crackColor = t < 0.5f
            ? Color.Lerp(Color.white,             new Color(1f, 0.55f, 0f), t * 2f)
            : Color.Lerp(new Color(1f, 0.55f, 0f), new Color(1f, 0.1f,  0f), (t - 0.5f) * 2f);

        wt.SetTileFlags(tilePos, TileFlags.None);
        wt.SetColor(tilePos, crackColor);
    }

    private void CancelWallBreak()
    {
        if (!_hasWallBreakTarget) return;

        // Restore the tile's original colour if it still exists
        if (_mapGenerator?.WallTilemap != null
            && _mapGenerator.IsInteriorWall(_wallBreakTile))
        {
            Tilemap wt = _mapGenerator.WallTilemap;
            wt.SetTileFlags(_wallBreakTile, TileFlags.None);
            wt.SetColor(_wallBreakTile, Color.white);
        }

        _hasWallBreakTarget = false;
        _wallBreakTimer     = 0f;
    }

    // ── Stuck-blocker attack ──────────────────────────────────────────────────

    /// <summary>
    /// Called when the stuck timer expires.  Searches for any <see cref="IEnemyAttackable"/>
    /// within <see cref="STUCK_ATTACK_RADIUS"/> of the enemy and locks on to it.
    /// Handles objects that the linecast misses because the enemy is already
    /// pressed against them (cast origin inside the collider).
    /// </summary>
    /// <returns>True if a target was found and attack mode was entered.</returns>
    private bool TryAttackNearbyBlocker()
    {
        // Exclude triggers (room bounds, etc.) so they can't fill the buffer and
        // crowd out the barricades we actually want to find.
        var filter = new ContactFilter2D();
        filter.useTriggers = false;
        int count = Physics2D.OverlapCircle(_rb.position, STUCK_ATTACK_RADIUS, filter, _overlapBuffer);

        for (int i = 0; i < count; i++)
        {
            Collider2D col = _overlapBuffer[i];

            if (col.gameObject == gameObject) continue;
            if (col.CompareTag("Enemy"))       continue;
            if (col.CompareTag("Player"))      continue;

            if (!col.TryGetComponent(out IEnemyAttackable attackable)) continue;
            if (attackable.IsDestroyed || attackable.CurrentHealth <= 0f) continue;

            _attackTarget      = attackable;
            _attackTargetMB    = attackable as MonoBehaviour;
            _attackTimer       = 0f;
            _rb.linearVelocity = Vector2.zero;

            if (enemyAnimator != null)
                enemyAnimator.SetBool("IsAttacking", true);

            return true;
        }

        return false;
    }

    // ── Linecast block detection ──────────────────────────────────────────────

    /// <summary>
    /// Casts a line from the enemy toward the current nav target and walks
    /// through every hit in order.  The first hit that is an <see cref="IEnemyAttackable"/>
    /// (and is not an enemy or the player) becomes the attack target.
    ///
    /// Uses <see cref="ContactFilter2D"/> so that trigger colliders are also
    /// detected — important because some structures use trigger volumes.
    /// Results are written into a pre-allocated buffer to avoid per-frame GC.
    /// </summary>
    /// <returns>True when a blocker was found (caller should skip Navigate).</returns>
    private bool CheckLinecastBlock()
    {
        // Build filter: exclude this enemy's own layer; include trigger colliders.
        var filter = new ContactFilter2D();
        filter.SetLayerMask(~(1 << gameObject.layer));
        filter.useTriggers = true;

        int count = Physics2D.Linecast(_rb.position, _navTarget, filter, _castBuffer);

        for (int i = 0; i < count; i++)
        {
            RaycastHit2D rayHit = _castBuffer[i];

            // Hits are sorted nearest-first; once beyond attack range there is no point continuing.
            if (rayHit.distance > attackRange) break;

            Collider2D col = rayHit.collider;

            if (col.gameObject == gameObject)  continue;   // self
            if (col.CompareTag("Enemy"))        continue;   // other enemies
            if (col.CompareTag("Player"))       continue;   // player — handled by contact damage

            if (!col.TryGetComponent(out IEnemyAttackable attackable)) continue;  // not a blocker
            if (attackable.IsDestroyed || attackable.CurrentHealth <= 0f) continue;  // already dead

            _attackTarget      = attackable;
            _attackTargetMB    = attackable as MonoBehaviour;
            _attackTimer       = 0f;
            _rb.linearVelocity = Vector2.zero;

            if (enemyAnimator != null)
                enemyAnimator.SetBool("IsAttacking", true);

            return true;
        }

        return false;
    }

    // ── Attack ────────────────────────────────────────────────────────────────

    /// <summary>Advances the attack cooldown; deals damage when it expires.</summary>
    private void TickAttack()
    {
        _attackTimer -= Time.fixedDeltaTime;
        if (_attackTimer > 0f) return;

        _attackTimer = attackInterval;
        _attackTarget.ReceiveEnemyAttack(EffectiveAttackDamage, attackInterval);
    }

    private void ClearAttackTarget()
    {
        _attackTarget   = null;
        _attackTargetMB = null;

        if (enemyAnimator != null)
            enemyAnimator.SetBool("IsAttacking", false);

        UpdateNavigation();
    }

    // ── IDamageable ───────────────────────────────────────────────────────────

    public void TakeDamage(float damage)
    {
        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
        if (CurrentHealth <= 0f) Die();
    }

    public List<StatLine> GetLexikonStats() => new()
    {
        new("HP",           maxHealth.ToString("F0")),
        new("Speed",        moveSpeed.ToString("F1")),
        new("Atk Dmg",      attackDamage.ToString("F0")),
        new("Atk Rate",     (1f / attackInterval).ToString("F1") + "/s"),
        new("Contact Dmg",  contactDamage.ToString("F0")),
    };

    public void ApplyWaveScaling(float healthMult, float speedMult, float damageMult)
    {
        maxHealth     *= healthMult;
        moveSpeed     *= speedMult;
        attackDamage  *= damageMult;
        contactDamage *= damageMult;
        CurrentHealth  = maxHealth;
    }

    // ── Day/night buff API (called by DayNightManager) ────────────────────────

    public void ApplyNightBuff(float speedMult, float damageMult)
    {
        _nightSpeedMult  = speedMult;
        _nightDamageMult = damageMult;
    }

    public void ClearNightBuff()
    {
        _nightSpeedMult  = 1f;
        _nightDamageMult = 1f;
    }

    /// <summary>Permanently increases the stat multiplier. Stacks each full cycle.</summary>
    public void AddPermanentCycleBuff(float bonus) => _permanentNightMult += bonus;

    private void Die()
    {
        GameManager.Instance?.OnEnemyDied();
        if (PoolManager.Instance != null)
            PoolManager.Instance.Release(gameObject);
        else
            Destroy(gameObject);
    }

    // ── Contact damage & resource destruction ────────────────────────────────

    private void OnCollisionEnter2D(Collision2D col)
    {
        // Start the destruction timer when we first touch a resource,
        // but only if we are not already engaged with one.
        if (_resourceContact != null)   return;
        if (!IsResource(col.gameObject)) return;

        _resourceContact      = col.gameObject;
        _resourceContactTimer = 0f;
    }

    private void OnCollisionStay2D(Collision2D col)
    {
        // ── Player contact damage ─────────────────────────────────────────────
        if (_contactTimer <= 0f && col.gameObject.CompareTag("Player"))
        {
            if (col.gameObject.TryGetComponent(out IDamageable damageable))
            {
                damageable.TakeDamage(EffectiveContactDamage);
                _contactTimer = contactCooldown;
            }
        }

        // ── Resource destruction timer ────────────────────────────────────────
        // Only accumulate while we are not already locked on to an attack target,
        // so enemies that are attacking a barricade don't also erode nearby resources.
        if (_resourceContact == col.gameObject && _attackTarget == null)
        {
            _resourceContactTimer += Time.fixedDeltaTime;
            if (_resourceContactTimer >= resourceDestroyTime && _resourceContact != null)
            {
                Destroy(_resourceContact);
                _resourceContact      = null;
                _resourceContactTimer = 0f;
            }
        }

        // ── Wall-slide normal accumulation ────────────────────────────────────
        // Accumulate outward normals from wall tiles so Navigate() can slide the
        // enemy along the surface toward the nearest door.
        if (IsWallCollider(col.collider))
        {
            foreach (ContactPoint2D cp in col.contacts)
                _wallNormal += cp.normal;
            _wallContact = true;
        }
    }

    private void OnCollisionExit2D(Collision2D col)
    {
        if (_resourceContact == col.gameObject)
        {
            _resourceContact      = null;
            _resourceContactTimer = 0f;
        }
    }

    /// <summary>
    /// Returns true for objects that are interactable world props (resources) but
    /// are NOT damageable structures.  Barricades and turrets implement
    /// <see cref="IDamageable"/> and are excluded; resource nodes do not.
    /// </summary>
    private static bool IsResource(GameObject go)
        => go.TryGetComponent<IInteractable>(out _) && !go.TryGetComponent<IDamageable>(out _);

    /// <summary>
    /// Returns true when <paramref name="col"/> is a solid, non-trigger geometry
    /// collider (e.g. wall tilemap) that the enemy should slide along rather than
    /// attack or interact with.
    /// </summary>
    private static bool IsWallCollider(Collider2D col)
    {
        if (col.isTrigger)                                return false;
        if (col.CompareTag("Enemy"))                      return false;
        if (col.CompareTag("Player"))                     return false;
        if (col.TryGetComponent<IEnemyAttackable>(out _)) return false;
        return true;
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        // Linecast toward nav target — red when blocked, white when clear
        bool isBlocked = _attackTarget != null;
        Gizmos.color = isBlocked ? Color.red : Color.white;
        Gizmos.DrawLine(transform.position, _navTarget);

        // Highlight the blocker position
        if (isBlocked && _attackTargetMB != null)
        {
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(_attackTargetMB.transform.position, 0.4f);
        }

        // Navigation target
        Gizmos.color = _nextDoor != null ? Color.yellow : Color.green;
        Gizmos.DrawWireSphere(_navTarget, 0.2f);

        // Door traversal waypoints
        if (_nextDoor != null)
        {
            // Door centre
            Gizmos.color = new Color(1f, 0.85f, 0f, 1f);
            Gizmos.DrawWireSphere(_nextDoor.WorldPosition, 0.25f);

            // Approach point (cyan) — where the enemy aligns before crossing
            Gizmos.color = _approachingDoor ? Color.cyan : new Color(0f, 1f, 1f, 0.35f);
            Gizmos.DrawWireSphere(_doorApproachPt, 0.2f);
            Gizmos.DrawLine(_doorApproachPt, _nextDoor.WorldPosition);

            // Exit point (magenta) — where the enemy heads after crossing
            Gizmos.color = !_approachingDoor ? Color.magenta : new Color(1f, 0f, 1f, 0.35f);
            Gizmos.DrawWireSphere(_doorExitPt, 0.2f);
            Gizmos.DrawLine(_nextDoor.WorldPosition, _doorExitPt);
        }
    }
}
