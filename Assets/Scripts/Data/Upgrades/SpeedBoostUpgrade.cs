using UnityEngine;

[CreateAssetMenu(menuName = "VampireUpgrades/SpeedBoost", fileName = "SpeedBoostUpgrade")]
public class SpeedBoostUpgrade : VampireUpgrade
{
    [Tooltip("Flat speed added to the vampire's move speed.")]
    public float speedBonus = 0.8f;

    public override void Apply(VampireEnemy vampire) =>
        vampire.AddSpeedBonus(speedBonus);
}
