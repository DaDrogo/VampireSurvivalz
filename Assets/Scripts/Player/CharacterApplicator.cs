using UnityEngine;

/// <summary>
/// Reads the CharacterDefinition chosen in SetupScene from PersistentDataManager
/// and applies it to the player when they spawn. No local array needed.
/// </summary>
[DefaultExecutionOrder(1)]
public class CharacterApplicator : MonoBehaviour
{
    public static CharacterApplicator Instance { get; private set; }

    public CharacterDefinition ActiveCharacter { get; private set; }

    private void Awake()
    {
        if (Instance != null) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnPlayerSpawned += Apply;
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnPlayerSpawned -= Apply;
        if (Instance == this) Instance = null;
    }

    private void Apply(PlayerController player)
    {
        GameManager.Instance.OnPlayerSpawned -= Apply;

        CharacterDefinition def = PersistentDataManager.Instance?.SelectedCharacterDefinition;
        Debug.Log($"[CharacterApplicator] Applying: {(def != null ? def.characterName : "NULL — no character selected")}");
        if (def == null) return;

        ActiveCharacter = def;
        player.ApplyCharacter(def);
        player.GetComponent<PlayerAnimationController>()?.ApplyOverride(def.animatorOverride);

        ResourceManager.Instance?.AddResource("Wood",  def.startingWood  + (CampManager.Instance?.GetTotalWoodBonus()  ?? 0));
        ResourceManager.Instance?.AddResource("Metal", def.startingMetal + (CampManager.Instance?.GetTotalMetalBonus() ?? 0));
    }
}
