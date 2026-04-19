using UnityEngine;

[CreateAssetMenu(menuName = "VampireUpgrades/BatSwarm", fileName = "BatSwarmUpgrade")]
public class BatSwarmUpgrade : VampireUpgrade
{
    [Tooltip("Radius of the bat explosion around the vampire.")]
    public float radius   = 3f;
    [Tooltip("Damage dealt to each target in the swarm.")]
    public float damage   = 12f;
    [Tooltip("Seconds between swarm bursts.")]
    public float interval = 5f;

    public override void Apply(VampireEnemy vampire) =>
        vampire.AddPeriodicAoe(radius, damage, interval);
}
