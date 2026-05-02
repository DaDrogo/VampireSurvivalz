using UnityEngine;

/// <summary>
/// Singleton. Tracks which tents the player has purchased and exposes what
/// those tents unlock (building cards, characters, resource bonuses).
/// Survives scene loads — place on the MainMenuScene manager object.
/// Assign the same TentDefinition[] array that CampSceneManager uses.
/// </summary>
public class CampManager : MonoBehaviour
{
    public static CampManager Instance { get; private set; }

    [SerializeField] private TentDefinition[] _allTents;

    private const string KeyPrefix = "Camp_";

    private void Awake()
    {
        if (Instance != null) { Destroy(this); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── Query — purchase state ─────────────────────────────────────────────────

    public bool IsPurchased(TentDefinition tent)
    {
        if (tent == null || string.IsNullOrEmpty(tent.unlockKey)) return false;
        return PlayerPrefs.GetInt(KeyPrefix + tent.unlockKey, 0) == 1;
    }

    // ── Query — gameplay unlocks ──────────────────────────────────────────────

    /// <summary>
    /// Returns true if the building card is available for the loadout.
    /// A card with requiresCampUnlock=false is always available.
    /// A card with requiresCampUnlock=true is available only if a purchased tent unlocks it.
    /// </summary>
    public bool IsCardAvailable(BuildingCard card)
    {
        if (card == null) return false;
        if (!card.requiresCampUnlock) return true;
        if (_allTents == null) return false;

        foreach (var tent in _allTents)
        {
            if (!IsPurchased(tent) || tent.unlocksBuildingCards == null) continue;
            foreach (var c in tent.unlocksBuildingCards)
                if (c == card) return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if the character is selectable in the setup screen.
    /// A character with requiresCampUnlock=false is always available.
    /// A character with requiresCampUnlock=true is available only if a purchased tent unlocks it.
    /// </summary>
    public bool IsCharacterAvailable(CharacterDefinition def)
    {
        if (def == null) return false;
        if (!def.requiresCampUnlock) return true;
        if (_allTents == null) return false;

        foreach (var tent in _allTents)
        {
            if (!IsPurchased(tent) || tent.unlocksCharacters == null) continue;
            foreach (var c in tent.unlocksCharacters)
                if (c == def) return true;
        }
        return false;
    }

    /// <summary>Sum of startingWoodBonus from all purchased tents.</summary>
    public int GetTotalWoodBonus()
    {
        int total = 0;
        if (_allTents == null) return total;
        foreach (var tent in _allTents)
            if (IsPurchased(tent)) total += tent.startingWoodBonus;
        return total;
    }

    /// <summary>Sum of startingMetalBonus from all purchased tents.</summary>
    public int GetTotalMetalBonus()
    {
        int total = 0;
        if (_allTents == null) return total;
        foreach (var tent in _allTents)
            if (IsPurchased(tent)) total += tent.startingMetalBonus;
        return total;
    }

    // ── Purchase ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Spends coins and marks the tent as purchased.
    /// Returns false if already purchased or the player can't afford it.
    /// </summary>
    public bool Purchase(TentDefinition tent)
    {
        if (tent == null || string.IsNullOrEmpty(tent.unlockKey)) return false;
        if (IsPurchased(tent)) return false;

        var pdm = PersistentDataManager.Instance;
        if (pdm == null || !pdm.SpendCurrency(tent.cost)) return false;

        PlayerPrefs.SetInt(KeyPrefix + tent.unlockKey, 1);
        PlayerPrefs.Save();
        return true;
    }
}
