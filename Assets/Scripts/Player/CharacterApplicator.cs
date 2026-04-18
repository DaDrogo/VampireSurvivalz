using UnityEngine;

/// <summary>
/// Applies the character chosen in SetupScene to the player when they spawn.
/// Place on any GameObject in SampleScene and assign the same CharacterDefinition[]
/// array that SetupManager uses (in the same index order).
/// </summary>
[DefaultExecutionOrder(1)]  // Awake/Start before GameManager (5) so we subscribe before StartGame fires
public class CharacterApplicator : MonoBehaviour
{
    [SerializeField] private CharacterDefinition[] characters;

    private void Start()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnPlayerSpawned += Apply;
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnPlayerSpawned -= Apply;
    }

    private void Apply(PlayerController player)
    {
        GameManager.Instance.OnPlayerSpawned -= Apply;

        var pdm = PersistentDataManager.Instance;
        if (pdm == null || characters == null || characters.Length == 0) return;

        int idx = Mathf.Clamp(pdm.SelectedCharacterIndex, 0, characters.Length - 1);
        CharacterDefinition def = characters[idx];
        if (def == null) return;

        player.ApplyCharacter(def);

        // Starting resources are added after GameManager.StartGame() calls ResetResources(),
        // so they arrive on top of the clean slate.
        ResourceManager.Instance?.AddResource("Wood",  def.startingWood);
        ResourceManager.Instance?.AddResource("Metal", def.startingMetal);
    }
}
