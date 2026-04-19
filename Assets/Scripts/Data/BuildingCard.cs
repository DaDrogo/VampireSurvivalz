using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight ScriptableObject used by SetupManager to display a building/turret in the
/// loadout picker. The index of this asset in SetupManager's buildingCards[] array must match
/// the corresponding entry in BuildingManager's buildings[] array.
/// </summary>
[CreateAssetMenu(menuName = "VampireSurvivalz/Building Card", fileName = "NewBuildingCard")]
public class BuildingCard : ScriptableObject, ILexikonSource
{
    public string displayName = "Building";
    [TextArea(1, 2)]
    public string description;
    public Color  color       = Color.white;
    public int    woodCost;
    public int    metalCost;

    [Header("Roster Flags")]
    [Tooltip("Always included in the loadout — player cannot deselect it.")]
    public bool isBasic    = false;
    [Tooltip("The Citadel — always pre-equipped in slot 1, cannot be removed.")]
    public bool isCitadel  = false;
    [Tooltip("Short stats string shown on the card, e.g. \"HP: 120  Range: 9  Fire: 2.5/s\"")]
    [TextArea(1, 2)]
    public string statsSummary;

    public List<StatLine> GetLexikonStats()
    {
        var lines = new List<StatLine>();
        if (woodCost  > 0) lines.Add(new("Wood",  woodCost.ToString()));
        if (metalCost > 0) lines.Add(new("Metal", metalCost.ToString()));
        return lines;
    }
}
