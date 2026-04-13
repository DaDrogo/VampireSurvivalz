using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Enemy : MonoBehaviour, IDamageable
{
    [Header("Stats")]
    [SerializeField] private float maxHealth = 50f;
    [SerializeField] private float moveSpeed = 2.5f;

    [Header("Barricade Attack")]
    [SerializeField] private float attackDamage = 10f;
    [SerializeField] private float attackInterval = 2f;
    [SerializeField] private float barricadeDetectDistance = 0.6f;
    [SerializeField] private LayerMask barricadeLayer;

    // IDamageable
    public float CurrentHealth { get; private set; }
    public float MaxHealth => maxHealth;

    private Rigidbody2D _rb;
    private Transform _player;
    private Barricade _targetBarricade;
    private float _attackTimer;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _rb.gravityScale = 0f;
        _rb.freezeRotation = true;
        CurrentHealth = maxHealth;
    }

    private void Start()
    {
        // Cache player once — if it can be destroyed mid-game, null-check each frame instead
        GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
        if (playerGO != null) _player = playerGO.transform;
    }

    private void FixedUpdate()
    {
        if (_player == null) return;

        Vector2 toPlayer = ((Vector2)_player.position - _rb.position).normalized;

        // Raycast toward the player to detect a barricade blocking the path
        RaycastHit2D hit = Physics2D.Raycast(
            _rb.position,
            toPlayer,
            barricadeDetectDistance,
            barricadeLayer
        );

        if (hit.collider != null && hit.collider.TryGetComponent(out Barricade barricade))
        {
            // Blocked — switch to attacking this barricade
            if (_targetBarricade != barricade)
            {
                _targetBarricade = barricade;
                _attackTimer = attackInterval; // attack immediately on first contact
            }

            AttackBarricade();
        }
        else
        {
            _targetBarricade = null;
            MoveTowardPlayer();
        }
    }

    private void MoveTowardPlayer()
    {
        Vector2 nextPos = Vector2.MoveTowards(
            _rb.position,
            _player.position,
            moveSpeed * Time.fixedDeltaTime
        );
        _rb.MovePosition(nextPos);
    }

    private void AttackBarricade()
    {
        _attackTimer -= Time.fixedDeltaTime;
        if (_attackTimer > 0f) return;

        _attackTimer = attackInterval;

        if (_targetBarricade != null)
            _targetBarricade.TakeDamage(attackDamage);
    }

    // ── IDamageable ───────────────────────────────────────────────────────────

    public void TakeDamage(float damage)
    {
        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);

        if (CurrentHealth <= 0f)
            Die();
    }

    private void Die()
    {
        // Notify SpawnManager so it can decrement its live-enemy count
        SpawnManager.Instance?.OnEnemyDied();
        Destroy(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        if (_player == null) return;
        Vector2 toPlayer = ((Vector2)_player.position - (Vector2)transform.position).normalized;
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position, toPlayer * barricadeDetectDistance);
    }
}
