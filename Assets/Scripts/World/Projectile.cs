using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How the projectile behaves on impact.
/// </summary>
public enum ProjectileType
{
    Arrow      = 0,  // Fast, single-target — destroys on first enemy hit
    Cannonball = 1,  // Slow, high-damage — explodes in an AoE on contact or on reaching target
    Chain      = 2,  // Redirects to the nearest unhit enemy after each hit (damage fades per hop)
}

/// <summary>
/// Flexible projectile fired by <see cref="Turret"/> (or any other launcher that calls Initialize).
///
/// Type-specific inspector sections:
///   Arrow      — speed / damage / maxLifetime only
///   Cannonball — also configure aoeRadius
///   Chain      — also configure chainCount / chainRadius / chainDamageMult
///
/// Assign different prefabs (each with a different ProjectileType set) to the Turret's
/// projectilePrefab field to change what it fires.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class Projectile : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Type")]
    [SerializeField] private ProjectileType type = ProjectileType.Arrow;

    [Header("Common")]
    [SerializeField] private float speed       = 12f;
    [SerializeField] private float damage      = 25f;
    [SerializeField] private float maxLifetime = 5f;

    [Header("Cannonball — AoE")]
    [Tooltip("World-space explosion radius on detonation.")]
    [SerializeField] private float aoeRadius = 2.5f;

    [Header("Chain")]
    [Tooltip("Number of additional targets to chain to after the first hit.")]
    [SerializeField] private int   chainCount      = 3;
    [Tooltip("World-space search radius when looking for the next chain target.")]
    [SerializeField] private float chainRadius     = 5f;
    [Tooltip("Damage multiplier applied on each hop (e.g. 0.7 = 70 % of previous hit).")]
    [SerializeField] [Range(0.1f, 1f)]
                     private float chainDamageMult = 0.7f;

    [Header("On-Hit Effect")]
    [Tooltip("Optional status effect applied to every enemy this projectile damages.\n" +
             "Leave type = None for a plain damage projectile.")]
    [SerializeField] private OnHitEffect onHitEffect = new OnHitEffect();

    // ── Private ───────────────────────────────────────────────────────────────

    private Rigidbody2D _rb;
    private Vector2     _targetPos;
    private bool        _initialized;
    private bool        _detonated;
    private float       _currentDamage;
    private int         _chainsLeft;

    // Tracks already-hit colliders so a single projectile never damages the same
    // enemy twice (important for chain hops and fast-moving AoE splash).
    private readonly HashSet<Collider2D> _hitSet = new HashSet<Collider2D>();

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        SpriteColliderAutoFit.Fit(gameObject);
        _rb = GetComponent<Rigidbody2D>();
        _rb.gravityScale = 0f;
        GetComponent<Collider2D>().isTrigger = true;
    }

#if UNITY_EDITOR
    private void OnValidate() => SpriteColliderAutoFit.Fit(gameObject);
#endif

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by the launcher immediately after Instantiate.
    /// Aims the projectile at <paramref name="targetWorldPos"/> and starts it moving.
    /// </summary>
    public void Initialize(Vector3 targetWorldPos)
    {
        _targetPos     = targetWorldPos;
        _currentDamage = damage;
        _chainsLeft    = chainCount;

        Vector2 dir = ((Vector2)targetWorldPos - _rb.position).normalized;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        _rb.linearVelocity = dir * speed;
        _initialized = true;

        Destroy(gameObject, maxLifetime);
    }

    // ── Per-frame ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_initialized || _detonated) return;

        // Cannonball: detonate automatically when it reaches the aim point so it
        // always explodes even if no enemy collider lies exactly at that position.
        if (type == ProjectileType.Cannonball &&
            Vector2.Distance(transform.position, _targetPos) < 0.35f)
        {
            Detonate(transform.position);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!_initialized || _detonated) return;
        if (_hitSet.Contains(other))     return;
        if (!other.CompareTag("Enemy"))  return;

        switch (type)
        {
            case ProjectileType.Arrow:
                HitSingleTarget(other);
                break;

            case ProjectileType.Cannonball:
                Detonate(transform.position);
                break;

            case ProjectileType.Chain:
                HitAndChain(other);
                break;
        }
    }

    // ── Arrow ─────────────────────────────────────────────────────────────────

    private void HitSingleTarget(Collider2D other)
    {
        if (other.TryGetComponent(out IDamageable d))
            d.TakeDamage(_currentDamage);
        ApplyEffect(other);
        Destroy(gameObject);
    }

    // ── Cannonball ────────────────────────────────────────────────────────────

    private void Detonate(Vector2 centre)
    {
        _detonated = true;
        _rb.linearVelocity = Vector2.zero;

        Collider2D[] hits = Physics2D.OverlapCircleAll(centre, aoeRadius);
        foreach (Collider2D col in hits)
        {
            if (!col.CompareTag("Enemy")) continue;
            if (_hitSet.Contains(col))    continue;    // don't double-hit corner cases
            _hitSet.Add(col);
            if (col.TryGetComponent(out IDamageable d))
                d.TakeDamage(_currentDamage);
            ApplyEffect(col);
        }

        Destroy(gameObject);
    }

    // ── Chain ─────────────────────────────────────────────────────────────────

    private void HitAndChain(Collider2D first)
    {
        _hitSet.Add(first);

        if (first.TryGetComponent(out IDamageable d))
            d.TakeDamage(_currentDamage);
        ApplyEffect(first);

        if (_chainsLeft <= 0)
        {
            Destroy(gameObject);
            return;
        }

        Collider2D next = FindNextChainTarget();
        if (next == null)
        {
            Destroy(gameObject);
            return;
        }

        // Reduce damage and redirect toward the next target
        _currentDamage *= chainDamageMult;
        _chainsLeft--;

        Vector2 dir = ((Vector2)next.transform.position - _rb.position).normalized;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
        _rb.linearVelocity = dir * speed;
    }

    private Collider2D FindNextChainTarget()
    {
        Collider2D[] candidates = Physics2D.OverlapCircleAll(transform.position, chainRadius);
        Collider2D   best       = null;
        float        bestDist   = float.MaxValue;

        foreach (Collider2D col in candidates)
        {
            if (!col.CompareTag("Enemy")) continue;
            if (_hitSet.Contains(col))    continue;

            float dist = Vector2.Distance(transform.position, col.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best     = col;
            }
        }

        return best;
    }

    // ── Effect application ────────────────────────────────────────────────────

    /// <summary>
    /// Applies <see cref="onHitEffect"/> to <paramref name="target"/> if the effect
    /// type is not None.  Adds a <see cref="StatusEffectHandler"/> component to the
    /// target on demand so no pre-wiring is required.
    /// </summary>
    private void ApplyEffect(Collider2D target)
    {
        if (onHitEffect == null || onHitEffect.type == StatusEffectType.None) return;

        StatusEffectHandler handler = target.GetComponent<StatusEffectHandler>();
        if (handler == null)
            handler = target.gameObject.AddComponent<StatusEffectHandler>();

        handler.Apply(onHitEffect);
    }

    // ── Editor gizmos ─────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (type == ProjectileType.Cannonball)
        {
            UnityEditor.Handles.color = new Color(1f, 0.4f, 0f, 0.25f);
            UnityEditor.Handles.DrawSolidDisc(transform.position, Vector3.forward, aoeRadius);
            UnityEditor.Handles.color = new Color(1f, 0.4f, 0f, 0.9f);
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, aoeRadius);
        }
        else if (type == ProjectileType.Chain)
        {
            UnityEditor.Handles.color = new Color(0.2f, 0.85f, 1f, 0.2f);
            UnityEditor.Handles.DrawSolidDisc(transform.position, Vector3.forward, chainRadius);
        }
    }
#endif
}
