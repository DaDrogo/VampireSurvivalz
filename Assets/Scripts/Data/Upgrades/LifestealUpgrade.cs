using UnityEngine;

[CreateAssetMenu(menuName = "VampireUpgrades/Lifesteal", fileName = "LifestealUpgrade")]
public class LifestealUpgrade : VampireUpgrade
{
    [Range(0.05f, 0.50f)]
    [Tooltip("Fraction of dealt damage restored as health.")]
    public float lifestealPercent = 0.15f;

    public override void Apply(VampireEnemy vampire) =>
        vampire.AddLifestealPercent(lifestealPercent);
}
