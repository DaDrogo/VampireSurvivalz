using UnityEngine;

[CreateAssetMenu(menuName = "VampireUpgrades/SummonThralls", fileName = "SummonThrallsUpgrade")]
public class SummonThrallsUpgrade : VampireUpgrade
{
    [Tooltip("Number of thralls summoned each interval.")]
    public int   count    = 2;
    [Tooltip("Seconds between summoning waves.")]
    public float interval = 20f;

    public override void Apply(VampireEnemy vampire) =>
        vampire.AddThrallSpawn(count, interval);
}
