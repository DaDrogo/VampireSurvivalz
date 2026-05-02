using UnityEngine;

/// <summary>
/// Defines a purchasable tent/NPC in the Camp scene.
/// Create via: Assets ► right-click ► Create ► Camp ► Tent Definition
/// </summary>
[CreateAssetMenu(fileName = "TentDefinition", menuName = "Camp/Tent Definition")]
public class TentDefinition : ScriptableObject
{
    [Header("Identity")]
    public string tentName;
    [TextArea(2, 4)]
    public string description;

    [Header("Economy")]
    public int cost;

    [Header("Visuals")]
    [Tooltip("Sprite shown on the camp map for this tent.")]
    public Sprite tentSprite;
    [Tooltip("Portrait shown in the purchase / dialogue popup.")]
    public Sprite npcSprite;

    [Header("Dialogue")]
    [Tooltip("Lines shown when the player clicks the NPC after purchase.")]
    [TextArea(1, 3)]
    public string[] dialogueLines;

    [Header("Game Unlocks")]
    [Tooltip("Building cards made available in the loadout picker when this tent is purchased.")]
    public BuildingCard[] unlocksBuildingCards;
    [Tooltip("Characters made selectable in the setup screen when this tent is purchased.")]
    public CharacterDefinition[] unlocksCharacters;

    [Header("Starting Resource Bonus")]
    [Tooltip("Extra Wood granted at the start of every game run while this tent is owned.")]
    public int startingWoodBonus;
    [Tooltip("Extra Metal granted at the start of every game run while this tent is owned.")]
    public int startingMetalBonus;

    [Header("Map Position")]
    [Tooltip("Normalized position (0–1 on each axis) of this tent's icon within the camp map. (0,0)=bottom-left, (1,1)=top-right.")]
    public Vector2 campPosition = new Vector2(0.5f, 0.5f);

    [Header("Persistence")]
    [Tooltip("Unique key used for PlayerPrefs. Must not change after first save.")]
    public string unlockKey;
}
