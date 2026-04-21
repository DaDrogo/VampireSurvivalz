using System;
using UnityEngine;

/// <summary>
/// Base class for all placeable buildings.
/// Handles health, damage, enemy attacks, hit/destroy audio, and destruction.
/// Override OnDied() for custom death behaviour (pathfinding cleanup, game over, etc.).
/// </summary>
public class Building : MonoBehaviour, IDamageable, IEnemyAttackable
{
    [Header("Health")]
    [SerializeField] protected float maxHealth = 100f;

    [Header("Audio")]
    [SerializeField] private AudioClip hitSfx;
    [SerializeField] private AudioClip destroySfx;

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
            AudioManager.Instance?.PlaySFX(hitSfx);
    }

    public void ReceiveEnemyAttack(float damage, float _) => TakeDamage(damage);

    protected virtual void OnDied()
    {
        AudioManager.Instance?.PlaySFX(destroySfx);
        Destroy(gameObject);
    }
}
