using UnityEngine;

public enum LexikonCategory { Turret, Building, Enemy, Character }

[CreateAssetMenu(menuName = "VampireSurvivalz/Lexikon Entry", fileName = "NewLexikonEntry")]
public class LexikonEntry : ScriptableObject
{
    public string          entryName   = "Entry";
    public LexikonCategory category    = LexikonCategory.Turret;
    public Color           color       = Color.white;
    public Sprite          sprite;
    [TextArea(2, 5)]
    public string          description;

    [Header("Stat Sources")]
    [Tooltip("Prefab with ILexikonSource components (Enemy, Turret, Barricade).")]
    public GameObject          linkedPrefab;
    [Tooltip("Building card — provides wood/metal cost (Buildings and Turrets).")]
    public BuildingCard        linkedBuildingCard;
    [Tooltip("Character definition — provides stat multipliers (Character entries).")]
    public CharacterDefinition linkedCharacter;

    [Header("Unlock")]
    public bool isUnlockedByDefault = true;
    public int  unlockCost          = 50;
}

/// <summary>Runtime tag on lexikon row GameObjects so filter buttons can toggle them.</summary>
public class LexikonCategoryTag : MonoBehaviour
{
    public LexikonCategory Category;
}
