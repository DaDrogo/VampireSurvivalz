using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages active status effects (Slow, Stun, Poison, Burn) on any entity.
///
/// Added automatically at runtime by <see cref="Projectile"/> the first time an
/// effect-bearing projectile hits the entity.  No manual scene setup required.
///
/// Exposes two properties that <see cref="Enemy"/> reads each FixedUpdate:
///   <see cref="SpeedMultiplier"/> — combined speed reduction from all active slows.
///   <see cref="IsStunned"/>       — true while any Stun effect has remaining time.
///
/// Applies DoT ticks via <see cref="IDamageable.TakeDamage"/> on the same GameObject.
/// Tints the SpriteRenderer as visual feedback; restores original colour when all
/// effects expire.
/// </summary>
public class StatusEffectHandler : MonoBehaviour
{
    // ── Effect instance ───────────────────────────────────────────────────────

    private class ActiveEffect
    {
        public StatusEffectType Type;
        public float Remaining;
        public float Strength;
        public float TickInterval;
        public float TickTimer;
        public float BurnMult;     // escalation multiplier, grows each Burn tick
    }

    // ── Effect tint colours (blended 50 % with original) ─────────────────────

    private static readonly Color SlowTint   = new Color(0.35f, 0.65f, 1.00f);
    private static readonly Color PoisonTint = new Color(0.25f, 0.90f, 0.25f);
    private static readonly Color BurnTint   = new Color(1.00f, 0.38f, 0.05f);
    private static readonly Color StunTint   = new Color(1.00f, 0.95f, 0.15f);

    // ── Private state ─────────────────────────────────────────────────────────

    private readonly List<ActiveEffect> _active = new List<ActiveEffect>(4);
    private IDamageable    _damageable;
    private SpriteRenderer _renderer;
    private Color          _originalColor;

    // ── Derived state (read by Enemy) ─────────────────────────────────────────

    /// <summary>Multiplicative speed factor. 1 = full speed; 0.5 = half speed.</summary>
    public float SpeedMultiplier { get; private set; } = 1f;

    /// <summary>True while any Stun is active.</summary>
    public bool IsStunned { get; private set; }

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        _damageable    = GetComponent<IDamageable>();
        _renderer      = GetComponent<SpriteRenderer>();
        _originalColor = _renderer != null ? _renderer.color : Color.white;
    }

    private void Update()
    {
        if (_active.Count == 0) return;

        float dt = Time.deltaTime;

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            ActiveEffect fx = _active[i];
            fx.Remaining -= dt;

            if (fx.Remaining <= 0f)
            {
                _active.RemoveAt(i);
                continue;
            }

            // Tick damage for Poison and Burn
            if (fx.Type == StatusEffectType.Poison || fx.Type == StatusEffectType.Burn)
            {
                fx.TickTimer -= dt;
                if (fx.TickTimer <= 0f)
                {
                    fx.TickTimer += fx.TickInterval;

                    _damageable?.TakeDamage(fx.Strength * fx.BurnMult);

                    // Burn escalates: each tick deals more than the last
                    if (fx.Type == StatusEffectType.Burn)
                        fx.BurnMult *= 1.4f;
                }
            }
        }

        RecalcDerivedState();

        // Restore colour when all effects have expired
        if (_active.Count == 0 && _renderer != null)
            _renderer.color = _originalColor;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies (or refreshes / stacks) an effect on this entity.
    ///
    /// Stacking rules:
    ///   Slow   — keeps whichever application has the higher strength;
    ///            refreshes duration.
    ///   Stun   — refreshes (extends) the remaining duration.
    ///   Poison — refreshes duration; tick rate and strength unchanged.
    ///   Burn   — re-ignites: removes the existing burn and restarts
    ///            with full duration and fresh escalation.
    /// </summary>
    public void Apply(OnHitEffect data)
    {
        if (data == null || data.type == StatusEffectType.None) return;

        switch (data.type)
        {
            case StatusEffectType.Slow:
                ApplySlow(data);
                break;

            case StatusEffectType.Stun:
            case StatusEffectType.Poison:
                RefreshOrAdd(data);
                break;

            case StatusEffectType.Burn:
                ReIgnite(data);
                break;
        }

        RecalcDerivedState();
    }

    public void ClearAll()
    {
        _active.Clear();
        SpeedMultiplier = 1f;
        IsStunned       = false;
        if (_renderer != null) _renderer.color = _originalColor;
    }

    // ── Stacking helpers ──────────────────────────────────────────────────────

    private void ApplySlow(OnHitEffect data)
    {
        foreach (ActiveEffect fx in _active)
        {
            if (fx.Type != StatusEffectType.Slow) continue;
            if (data.strength > fx.Strength) fx.Strength = data.strength;
            if (data.duration  > fx.Remaining) fx.Remaining = data.duration;
            return;
        }
        AddNew(data);
    }

    private void RefreshOrAdd(OnHitEffect data)
    {
        foreach (ActiveEffect fx in _active)
        {
            if (fx.Type != data.type) continue;
            fx.Remaining = Mathf.Max(fx.Remaining, data.duration);
            return;
        }
        AddNew(data);
    }

    private void ReIgnite(OnHitEffect data)
    {
        for (int i = _active.Count - 1; i >= 0; i--)
            if (_active[i].Type == StatusEffectType.Burn)
                _active.RemoveAt(i);

        AddNew(data);
    }

    private void AddNew(OnHitEffect data)
    {
        float interval = data.tickInterval > 0f ? data.tickInterval : 0.5f;
        _active.Add(new ActiveEffect
        {
            Type         = data.type,
            Remaining    = data.duration,
            Strength     = data.strength,
            TickInterval = interval,
            TickTimer    = interval,
            BurnMult     = 1f,
        });
    }

    // ── Derived state + tint ──────────────────────────────────────────────────

    private void RecalcDerivedState()
    {
        float slowReduction = 0f;
        bool  stunned       = false;
        bool  poisoned      = false;
        bool  burning       = false;

        foreach (ActiveEffect fx in _active)
        {
            switch (fx.Type)
            {
                case StatusEffectType.Slow:   slowReduction = Mathf.Max(slowReduction, fx.Strength); break;
                case StatusEffectType.Stun:   stunned  = true; break;
                case StatusEffectType.Poison: poisoned = true; break;
                case StatusEffectType.Burn:   burning  = true; break;
            }
        }

        SpeedMultiplier = Mathf.Clamp01(1f - slowReduction);
        IsStunned       = stunned;

        // Tint: highest-priority effect wins (Stun > Burn > Poison > Slow)
        if (_renderer != null)
        {
            Color tint = _originalColor;
            float blend = 0.55f;
            if (slowReduction > 0f) tint = Color.Lerp(_originalColor, SlowTint,   blend);
            if (poisoned)           tint = Color.Lerp(_originalColor, PoisonTint, blend);
            if (burning)            tint = Color.Lerp(_originalColor, BurnTint,   blend + 0.1f);
            if (stunned)            tint = Color.Lerp(_originalColor, StunTint,   blend + 0.1f);
            _renderer.color = tint;
        }
    }
}
