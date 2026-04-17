using System;
using UnityEngine;

/// <summary>Which debuff a projectile applies on hit.</summary>
public enum StatusEffectType
{
    None   = 0,
    Slow   = 1,   // reduces move speed for a duration
    Stun   = 2,   // freezes movement entirely for a duration
    Poison = 3,   // constant damage per tick over a duration
    Burn   = 4,   // damage-per-tick that escalates each tick (re-ignite resets escalation)
}

/// <summary>
/// Serialisable data object embedded directly in a <see cref="Projectile"/> to describe
/// the debuff it applies.  Assign in the Projectile Inspector under "On-Hit Effect".
/// </summary>
[Serializable]
public class OnHitEffect
{
    [Tooltip("Which debuff to apply.  None = no effect.")]
    public StatusEffectType type = StatusEffectType.None;

    [Tooltip("How many seconds the effect lasts.")]
    public float duration = 3f;

    [Tooltip("Slow: fraction of speed removed (0.5 = 50 % slower).\n" +
             "Poison / Burn: damage dealt per tick.")]
    public float strength = 0.4f;

    [Tooltip("Seconds between damage ticks (Poison and Burn only).")]
    public float tickInterval = 0.5f;
}
