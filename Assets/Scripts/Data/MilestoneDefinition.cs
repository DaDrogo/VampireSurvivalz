using UnityEngine;

public enum MilestoneType { BestWave, TotalKills, TotalBuildings, TotalGamesPlayed }

[CreateAssetMenu(menuName = "VampireSurvivalz/Milestone Definition", fileName = "NewMilestone")]
public class MilestoneDefinition : ScriptableObject
{
    public string        title;
    [TextArea(1, 2)]
    public string        description;
    public MilestoneType type;
    public int           requiredValue;
}
