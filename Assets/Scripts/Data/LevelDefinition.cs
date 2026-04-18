using UnityEngine;

[CreateAssetMenu(menuName = "VampireSurvivalz/Level Definition", fileName = "NewLevel")]
public class LevelDefinition : ScriptableObject
{
    public string levelName   = "Level";
    [TextArea(1, 3)]
    public string description;
    public Color  previewColor = new Color(0.2f, 0.4f, 0.2f);
    [Tooltip("Exact scene name as it appears in Build Settings")]
    public string sceneName   = "SampleScene";
    public bool   isUnlockedByDefault = true;
    [Tooltip("Unlock when BestWave reaches this value. Ignored when isUnlockedByDefault is true.")]
    public int    unlockAtBestWave = 5;
}
