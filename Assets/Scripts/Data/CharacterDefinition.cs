using System;
using UnityEngine;

[Serializable]
public class PassiveEffect
{
    public string effectName = "Passive";
    [TextArea(1, 2)]
    public string effectDescription;
}

[CreateAssetMenu(menuName = "VampireSurvivalz/Character Definition", fileName = "NewCharacter")]
public class CharacterDefinition : ScriptableObject
{
    public string characterName = "Character";
    [TextArea(1, 3)]
    public string description;
    public Color  color = Color.white;

    [Header("Stat Modifiers (1 = no change)")]
    public float healthMultiplier = 1f;
    public float speedMultiplier  = 1f;
    public float damageMultiplier = 1f;

    [Header("Starting Resources")]
    public int startingWood  = 0;
    public int startingMetal = 0;

    [Header("Passive Effects")]
    public PassiveEffect[] passiveEffects;
}
