using UnityEngine;

[CreateAssetMenu(menuName = "VampireUpgrades/PhaseDash", fileName = "PhaseDashUpgrade")]
public class PhaseDashUpgrade : VampireUpgrade
{
    [Tooltip("Seconds between phase dashes.")]
    public float interval = 8f;
    [Tooltip("Maximum distance of the teleport toward the target.")]
    public float range    = 6f;

    public override void Apply(VampireEnemy vampire) =>
        vampire.SetupPhaseDash(interval, range);
}
