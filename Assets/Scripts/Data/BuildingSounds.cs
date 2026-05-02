using UnityEngine;

/// <summary>
/// All building lifecycle audio clips in one place.
/// Create via: right-click in Project → Create → Audio → Building Sounds
/// Assign the asset to AudioManager's "Building Sounds" field.
/// </summary>
[CreateAssetMenu(fileName = "BuildingSounds", menuName = "Audio/Building Sounds")]
public class BuildingSounds : ScriptableObject
{
    [Header("Lifecycle")]
    public AudioClip built;
    public AudioClip repaired;
    public AudioClip destroyed;

    [Header("Combat")]
    public AudioClip hit;
}
