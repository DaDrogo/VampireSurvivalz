using System;
using System.Collections.Generic;
using UnityEngine;

public enum CitadelAuraType
{
    None,
    BarricadeHealth,    // walls in the citadel room gain bonus HP
    TurretFireRate,     // turrets in the citadel room shoot faster
    TurretRange,        // turrets in the citadel room see farther
    ResourceProduction, // resource producers in the citadel room produce faster
}

[Serializable]
public class PassiveEffect
{
    public string effectName = "Passive";
    [TextArea(1, 2)]
    public string effectDescription;
}

[CreateAssetMenu(menuName = "VampireSurvivalz/Character Definition", fileName = "NewCharacter")]
public class CharacterDefinition : ScriptableObject, ILexikonSource
{
    public string characterName = "Character";
    [TextArea(1, 3)]
    public string description;
    public Color  color = Color.white;

    [Header("Camp")]
    [Tooltip("If true, this character only appears after being unlocked via a Camp tent. Leave false for starter characters.")]
    public bool requiresCampUnlock = false;

    [Header("Stat Modifiers (1 = no change)")]
    public float healthMultiplier = 1f;
    public float speedMultiplier  = 1f;
    public float damageMultiplier = 1f;

    [Header("Starting Resources")]
    public int startingWood  = 0;
    public int startingMetal = 0;

    [Header("Loadout")]
    [Tooltip("Buildings available in the loadout picker for this character. Fixed buildings (Citadel, basics) always appear regardless.")]
    public BuildingCard[] availableBuildings;

    [Header("Passive Effects")]
    public PassiveEffect[] passiveEffects;

    [Header("Audio")]
    [Tooltip("Played when the player taps this character card in the setup screen.")]
    public AudioClip selectSound;

    [Header("Animation")]
    [Tooltip("Override controller that swaps animation clips for this character's sprite sheet.")]
    public AnimatorOverrideController animatorOverride;

    [Header("Citadel Aura")]
    [Tooltip("Which building stat this character's citadel aura boosts.")]
    public CitadelAuraType auraType = CitadelAuraType.None;
    [Tooltip("Multiplier applied per citadel tier upgrade (e.g. 0.25 = +25% per tier).")]
    [Min(0f)] public float auraStrengthPerTier = 0.25f;
    [TextArea(1, 2)]
    public string auraDescription = "";

    public List<StatLine> GetLexikonStats()
    {
        var lines = new List<StatLine>
        {
            new("HP",     $"×{healthMultiplier:F1}"),
            new("Speed",  $"×{speedMultiplier:F1}"),
            new("Damage", $"×{damageMultiplier:F1}"),
        };
        if (startingWood  > 0) lines.Add(new("Wood",  startingWood.ToString()));
        if (startingMetal > 0) lines.Add(new("Metal", startingMetal.ToString()));
        if (passiveEffects != null)
            foreach (var p in passiveEffects)
                if (!string.IsNullOrEmpty(p.effectName))
                    lines.Add(new("Passive", p.effectName));
        return lines;
    }
}
