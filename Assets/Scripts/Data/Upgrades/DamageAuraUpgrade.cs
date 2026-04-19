using UnityEngine;

[CreateAssetMenu(menuName = "VampireUpgrades/DamageAura", fileName = "DamageAuraUpgrade")]
public class DamageAuraUpgrade : VampireUpgrade
{
    [Tooltip("Radius of the continuous damage aura.")]
    public float radius        = 1.8f;
    [Tooltip("Damage per tick.")]
    public float damagePerTick = 5f;
    [Tooltip("Seconds between ticks (keep >= 0.3 to avoid spam).")]
    public float tickInterval  = 0.5f;

    public override void Apply(VampireEnemy vampire) =>
        vampire.AddPeriodicAoe(radius, damagePerTick, tickInterval);
}
