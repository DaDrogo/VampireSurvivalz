using UnityEngine;

/// <summary>
/// Special building that permanently weakens the vampire while it remains intact.
/// Registers with VampireEnemy on placement/enable and unregisters on destruction/disable.
/// While at least one HolyWell is active, the vampire is always vulnerable and can be
/// permanently killed without requiring a silver bullet.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class HolyWell : MonoBehaviour, IDamageable, IEnemyAttackable
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 80f;

    // ── IDamageable / IEnemyAttackable ────────────────────────────────────────

    public float CurrentHealth { get; private set; }
    public float MaxHealth     => maxHealth;
    public bool  IsDestroyed   => this == null || CurrentHealth <= 0f;

    public void ReceiveEnemyAttack(float damage, float _) => TakeDamage(damage);

    public void TakeDamage(float damage)
    {
        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
        if (CurrentHealth <= 0f)
            Destroy(gameObject);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private bool _registered = false;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        SpriteColliderAutoFit.Fit(gameObject);
        CurrentHealth = maxHealth;
    }

    private void OnEnable()  => TryRegister();
    private void OnDisable() => TryUnregister();
    private void OnDestroy() => TryUnregister();

    private void Update()
    {
        // VampireEnemy may not exist yet when the well is first placed (first night not started).
        // Keep polling until registration succeeds.
        if (!_registered)
            TryRegister();
    }

    // ── Registration ──────────────────────────────────────────────────────────

    private void TryRegister()
    {
        if (_registered) return;
        if (VampireEnemy.Instance == null) return;
        VampireEnemy.Instance.RegisterHolyWell();
        _registered = true;
    }

    private void TryUnregister()
    {
        if (!_registered) return;
        VampireEnemy.Instance?.UnregisterHolyWell();
        _registered = false;
    }
}
