using UnityEngine;

public enum LexikonCategory { Turret, Building, Enemy }

[CreateAssetMenu(menuName = "VampireSurvivalz/Lexikon Entry", fileName = "NewLexikonEntry")]
public class LexikonEntry : ScriptableObject
{
    public string          entryName   = "Entry";
    public LexikonCategory category    = LexikonCategory.Turret;
    public Color           color       = Color.white;
    [TextArea(2, 5)]
    public string          description;
    [TextArea(1, 3)]
    public string          stats;
}

/// <summary>Runtime tag placed on lexikon row GameObjects so filter buttons can toggle them.</summary>
public class LexikonCategoryTag : MonoBehaviour
{
    public LexikonCategory Category;
}
