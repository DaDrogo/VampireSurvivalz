using UnityEngine;

/// <summary>
/// Base ScriptableObject for all vampire upgrades.
/// Create concrete subclasses, mark with [CreateAssetMenu], and drag assets
/// into the VampireEnemy prefab's "Available Upgrades" list.
///
/// isUnique = true  → removed from the pool once picked (can still be re-added
///                    if the pool empties and resets).
/// isUnique = false → always stays in the pool; Apply is called every time.
/// </summary>
public abstract class VampireUpgrade : ScriptableObject
{
    [Header("Upgrade Info")]
    public string upgradeName    = "Unnamed Upgrade";
    [TextArea(2, 4)]
    public string description    = "";
    public bool   isUnique       = true;

    /// <summary>Called once when this upgrade is selected on level-up.</summary>
    public abstract void Apply(VampireEnemy vampire);
}
