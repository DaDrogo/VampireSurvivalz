using System;
using UnityEngine;

/// <summary>
/// Base class for all placeable buildings.
/// Handles health, damage, enemy attacks, hit/destroy audio, and destruction.
/// Override OnDied() for custom death behaviour (pathfinding cleanup, game over, etc.).
/// </summary>
public class Building : MonoBehaviour, IDamageable, IEnemyAttackable, IHoldInteractable
{
    [Header("Health")]
    [SerializeField] protected float maxHealth = 100f;

    [Header("Repair")]
    [SerializeField] private float repairDuration    = 3f;
    [Tooltip("% of max HP healed per second while holding (0.05 = 5 %/s).")]
    [SerializeField] private float repairTickPercent  = 0.05f;
    [Tooltip("% of max HP healed as a flat bonus when the hold completes (0.30 = 30 %).")]
    [SerializeField] private float repairBonusPercent = 0.30f;

    public float CurrentHealth { get; protected set; }
    public float MaxHealth     => maxHealth;
    public bool  IsDestroyed   => this == null || CurrentHealth <= 0f;

    public event Action<float, float> OnHealthChanged;

    protected void RaiseHealthChanged() => OnHealthChanged?.Invoke(CurrentHealth, maxHealth);

    protected virtual void Awake()
    {
        SpriteColliderAutoFit.Fit(gameObject);
        CurrentHealth = maxHealth;
    }

#if UNITY_EDITOR
    protected virtual void OnValidate() => SpriteColliderAutoFit.Fit(gameObject);
#endif

    public virtual void TakeDamage(float damage)
    {
        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        if (CurrentHealth <= 0f)
            OnDied();
        else
            AudioManager.Instance?.PlayBuildingHit();
    }

    public void ReceiveEnemyAttack(float damage, float _) => TakeDamage(damage);

    protected virtual void OnDied()
    {
        AudioManager.Instance?.PlayBuildingDestroyed();
        Destroy(gameObject);
    }

    // ── IHoldInteractable — repair ────────────────────────────────────────────

    public virtual float HoldDuration => repairDuration;

    public virtual void OnHoldStart()     { }
    public virtual void OnHoldTick(float progress)
    {
        if (CurrentHealth >= maxHealth) return;
        CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + repairTickPercent * maxHealth * Time.deltaTime);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    public virtual void OnHoldCancelled() { }

    public virtual void OnHoldCompleted()
    {
        if (CurrentHealth >= maxHealth) return;
        CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + repairBonusPercent * maxHealth);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        AudioManager.Instance?.PlayBuildingRepaired();
    }
}
