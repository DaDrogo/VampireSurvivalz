using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Enemy : MonoBehaviour, IDamageable
{
    [Header("Stats")]
    [SerializeField] private float maxHealth = 50f;
    [SerializeField] private float moveSpeed = 2.5f;

    [Header("Barricade Attack")]
    [SerializeField] private float attackDamage    = 10f;
    [SerializeField] private float attackInterval  = 2f;

    [Header("Contact Damage")]
    [SerializeField] private float contactDamage  = 10f;
    [Tooltip("Seconds between contact hits — prevents draining the player in one frame")]
    [SerializeField] private float contactCooldown = 1f;

    [Header("Pathfinding")]
    [Tooltip("How often (seconds) the enemy recalculates its path")]
    [SerializeField] private float pathRefreshInterval = 0.5f;

    // IDamageable
    public float CurrentHealth { get; private set; }
    public float MaxHealth => maxHealth;

    private Rigidbody2D _rb;
    private Transform   _player;

    // ── Path following ────────────────────────────────────────────────────────

    private List<Vector2> _path;
    private int           _pathIndex;
    private float         _pathRefreshTimer;

    // ── Attack / contact timers ───────────────────────────────────────────────

    private Barricade _targetBarricade;
    private float     _attackTimer;
    private float     _contactTimer;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _rb.gravityScale   = 0f;
        _rb.freezeRotation = true;
        CurrentHealth      = maxHealth;
    }

    private void Start()
    {
        GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
        if (playerGO != null) _player = playerGO.transform;

        // Stagger path refreshes across enemies to avoid all recalculating on the same frame
        _pathRefreshTimer = Random.Range(0f, pathRefreshInterval);
    }

    private void FixedUpdate()
    {
        if (_contactTimer > 0f) _contactTimer -= Time.fixedDeltaTime;
        if (_player == null) return;

        // ── Path refresh ──────────────────────────────────────────────────────
        _pathRefreshTimer -= Time.fixedDeltaTime;
        if (_pathRefreshTimer <= 0f)
        {
            _pathRefreshTimer = pathRefreshInterval;
            RefreshPath();
        }

        FollowPath();
    }

    // ── Pathfinding ───────────────────────────────────────────────────────────

    private void RefreshPath()
    {
        if (PathfindingGrid.Instance == null)
        {
            // No grid in scene — fall back to straight-line movement
            _path = null;
            return;
        }

        Vector2 start = _rb.position;
        Vector2 goal  = _player.position;

        // Pass 1 — try to avoid barricades entirely
        List<Vector2> path = Pathfinder.FindPath(start, goal, avoidBarricades: true);

        // Pass 2 — if blocked, route through barricades (enemies will damage them)
        if (path == null)
            path = Pathfinder.FindPath(start, goal, avoidBarricades: false);

        _path      = path;
        _pathIndex = 0;
        _targetBarricade = null;
    }

    private void FollowPath()
    {
        // No grid — direct movement
        if (PathfindingGrid.Instance == null)
        {
            MoveToward(_player.position);
            return;
        }

        // No valid path yet
        if (_path == null || _pathIndex >= _path.Count)
        {
            // If standing on a barricade node, attack it
            TryAttackBarricadeAtCurrentNode();
            return;
        }

        Vector2 target    = _path[_pathIndex];
        PathNode targetNode = PathfindingGrid.Instance.NodeFromWorld(target);

        // Check if the next node is now a barricade (built after path was calculated)
        if (targetNode != null && targetNode.IsBarricade && targetNode.BarricadeRef != null)
        {
            AttackBarricade(targetNode.BarricadeRef);
            return; // Stand still while attacking
        }

        // Advance along path
        _targetBarricade = null;
        MoveToward(target);

        if (Vector2.Distance(_rb.position, target) < 0.1f)
            _pathIndex++;
    }

    private void TryAttackBarricadeAtCurrentNode()
    {
        PathNode node = PathfindingGrid.Instance?.NodeFromWorld(_rb.position);
        if (node != null && node.IsBarricade && node.BarricadeRef != null)
            AttackBarricade(node.BarricadeRef);
    }

    private void MoveToward(Vector2 target)
    {
        Vector2 next = Vector2.MoveTowards(_rb.position, target, moveSpeed * Time.fixedDeltaTime);
        _rb.MovePosition(next);
    }

    private void AttackBarricade(Barricade barricade)
    {
        if (_targetBarricade != barricade)
        {
            _targetBarricade = barricade;
            _attackTimer     = 0f; // attack immediately on first contact
        }

        _attackTimer -= Time.fixedDeltaTime;
        if (_attackTimer > 0f) return;

        _attackTimer = attackInterval;
        barricade.TakeDamage(attackDamage);
    }

    // ── IDamageable ───────────────────────────────────────────────────────────

    public void TakeDamage(float damage)
    {
        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
        if (CurrentHealth <= 0f) Die();
    }

    /// <summary>
    /// Applies per-wave stat scaling. Called by GameManager immediately after Instantiate.
    /// </summary>
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
        if (_contactTimer > 0f) return;
        if (!col.gameObject.CompareTag("Player")) return;

        if (col.gameObject.TryGetComponent(out IDamageable damageable))
        {
            damageable.TakeDamage(contactDamage);
            _contactTimer = contactCooldown;
        }
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (_path == null || _path.Count < 2) return;
        Gizmos.color = Color.cyan;
        for (int i = _pathIndex; i < _path.Count - 1; i++)
            Gizmos.DrawLine(_path[i], _path[i + 1]);
    }
}
