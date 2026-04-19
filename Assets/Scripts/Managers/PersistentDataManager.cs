using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Survives scene loads. Stores settings (volume), selections (character/level),
/// milestone counters, currency, and unlock state via PlayerPrefs.
/// Place on the MainMenuScene manager object alongside AudioManager and SceneTransitionManager.
/// </summary>
public class PersistentDataManager : MonoBehaviour
{
    public static PersistentDataManager Instance { get; private set; }

    // ── Settings ──────────────────────────────────────────────────────────────
    public float MusicVolume { get; private set; } = 0.8f;
    public float SFXVolume   { get; private set; } = 0.8f;

    // ── Selections ────────────────────────────────────────────────────────────
    public int   SelectedCharacterIndex   { get; private set; } = 0;
    public int   SelectedLevelIndex       { get; private set; } = 0;
    public int[] SelectedBuildingIndices  { get; private set; } = new int[] { 0, 1, 2, 3, 4 };

    // ── Milestone counters ────────────────────────────────────────────────────
    public int BestWave           { get; private set; } = 0;
    public int TotalEnemiesKilled { get; private set; } = 0;
    public int TotalBuildings     { get; private set; } = 0;
    public int TotalGamesPlayed   { get; private set; } = 0;

    // ── Currency & unlocks ────────────────────────────────────────────────────
    public int TotalCurrency { get; private set; } = 0;

    /// <summary>Fired whenever TotalCurrency changes so HUD and menu can refresh.</summary>
    public event Action<int> OnCurrencyChanged;

    private readonly HashSet<string> _unlockedItems = new HashSet<string>();

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    public void SetMusicVolume(float v)
    {
        MusicVolume = Mathf.Clamp01(v);
        AudioManager.Instance?.SetMusicVolume(MusicVolume);
        PlayerPrefs.SetFloat("MusicVolume", MusicVolume);
        PlayerPrefs.Save();
    }

    public void SetSFXVolume(float v)
    {
        SFXVolume = Mathf.Clamp01(v);
        AudioManager.Instance?.SetSFXVolume(SFXVolume);
        PlayerPrefs.SetFloat("SFXVolume", SFXVolume);
        PlayerPrefs.Save();
    }

    // ── Selections ────────────────────────────────────────────────────────────

    public void SelectCharacter(int index)
    {
        SelectedCharacterIndex = index;
        PlayerPrefs.SetInt("SelectedCharacter", index);
        PlayerPrefs.Save();
    }

    public void SelectLevel(int index)
    {
        SelectedLevelIndex = index;
        PlayerPrefs.SetInt("SelectedLevel", index);
        PlayerPrefs.Save();
    }

    public void SetBuildingLoadout(int[] indices)
    {
        SelectedBuildingIndices = indices ?? new int[0];
        PlayerPrefs.SetString("BuildingLoadout", string.Join(",", SelectedBuildingIndices));
        PlayerPrefs.Save();
    }

    // ── Milestone recording ───────────────────────────────────────────────────

    public void RecordGameOver(int waveSurvived)
    {
        TotalGamesPlayed++;
        PlayerPrefs.SetInt("TotalGamesPlayed", TotalGamesPlayed);

        if (waveSurvived > BestWave)
        {
            BestWave = waveSurvived;
            PlayerPrefs.SetInt("BestWave", BestWave);
        }
        PlayerPrefs.Save();
    }

    public void AddKills(int count)
    {
        TotalEnemiesKilled += count;
        PlayerPrefs.SetInt("TotalKills", TotalEnemiesKilled);
        // Batch-save deferred — high frequency; GameManager calls Save on wave end
    }

    public void AddBuilding()
    {
        TotalBuildings++;
        PlayerPrefs.SetInt("TotalBuildings", TotalBuildings);
        PlayerPrefs.Save();
    }

    public void SaveKills() => PlayerPrefs.Save();

    // ── Currency ──────────────────────────────────────────────────────────────

    public void AddCurrency(int amount)
    {
        if (amount <= 0) return;
        TotalCurrency += amount;
        PlayerPrefs.SetInt("TotalCurrency", TotalCurrency);
        OnCurrencyChanged?.Invoke(TotalCurrency);
        // Batched — caller is responsible for PlayerPrefs.Save() at a natural checkpoint
    }

    /// <summary>Returns true and deducts cost if the player can afford it.</summary>
    public bool SpendCurrency(int cost)
    {
        if (TotalCurrency < cost) return false;
        TotalCurrency -= cost;
        PlayerPrefs.SetInt("TotalCurrency", TotalCurrency);
        PlayerPrefs.Save();
        OnCurrencyChanged?.Invoke(TotalCurrency);
        return true;
    }

    // ── Unlock tracking ───────────────────────────────────────────────────────

    public bool IsUnlocked(LexikonEntry entry)
    {
        if (entry == null) return false;
        return entry.isUnlockedByDefault || _unlockedItems.Contains(entry.entryName);
    }

    /// <summary>Spends coins and unlocks the entry. Returns false if already unlocked or can't afford.</summary>
    public bool UnlockItem(LexikonEntry entry)
    {
        if (entry == null || IsUnlocked(entry)) return false;
        if (!SpendCurrency(entry.unlockCost)) return false;
        _unlockedItems.Add(entry.entryName);
        PlayerPrefs.SetString("UnlockedItems",
            string.Join(",", _unlockedItems));
        PlayerPrefs.Save();
        return true;
    }

    // ── Milestone query ───────────────────────────────────────────────────────

    public bool IsMilestoneComplete(MilestoneDefinition m)
    {
        if (m == null) return false;
        return GetProgress(m) >= m.requiredValue;
    }

    public int GetProgress(MilestoneDefinition m)
    {
        if (m == null) return 0;
        return m.type switch
        {
            MilestoneType.BestWave         => BestWave,
            MilestoneType.TotalKills       => TotalEnemiesKilled,
            MilestoneType.TotalBuildings   => TotalBuildings,
            MilestoneType.TotalGamesPlayed => TotalGamesPlayed,
            _                              => 0
        };
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    public void ResetProgress()
    {
        BestWave           = 0;
        TotalEnemiesKilled = 0;
        TotalBuildings     = 0;
        TotalGamesPlayed   = 0;
        TotalCurrency      = 0;
        _unlockedItems.Clear();

        PlayerPrefs.DeleteKey("BestWave");
        PlayerPrefs.DeleteKey("TotalKills");
        PlayerPrefs.DeleteKey("TotalBuildings");
        PlayerPrefs.DeleteKey("TotalGamesPlayed");
        PlayerPrefs.DeleteKey("TotalCurrency");
        PlayerPrefs.DeleteKey("UnlockedItems");
        PlayerPrefs.Save();

        OnCurrencyChanged?.Invoke(TotalCurrency);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void Load()
    {
        MusicVolume            = PlayerPrefs.GetFloat("MusicVolume",       0.8f);
        SFXVolume              = PlayerPrefs.GetFloat("SFXVolume",         0.8f);
        SelectedCharacterIndex = PlayerPrefs.GetInt("SelectedCharacter",   0);
        SelectedLevelIndex     = PlayerPrefs.GetInt("SelectedLevel",       0);
        BestWave               = PlayerPrefs.GetInt("BestWave",            0);
        TotalEnemiesKilled     = PlayerPrefs.GetInt("TotalKills",          0);
        TotalBuildings         = PlayerPrefs.GetInt("TotalBuildings",      0);
        TotalGamesPlayed       = PlayerPrefs.GetInt("TotalGamesPlayed",    0);

        string loadoutStr      = PlayerPrefs.GetString("BuildingLoadout",  "0,1,2,3,4");
        try
        {
            string[] parts = loadoutStr.Split(',');
            var parsed     = new List<int>();
            foreach (string p in parts)
                if (int.TryParse(p.Trim(), out int v)) parsed.Add(v);
            if (parsed.Count > 0) SelectedBuildingIndices = parsed.ToArray();
        }
        catch { /* keep default */ }

        TotalCurrency = PlayerPrefs.GetInt("TotalCurrency", 0);

        string unlockedStr = PlayerPrefs.GetString("UnlockedItems", "");
        if (!string.IsNullOrEmpty(unlockedStr))
        {
            foreach (string s in unlockedStr.Split(','))
            {
                string t = s.Trim();
                if (t.Length > 0) _unlockedItems.Add(t);
            }
        }
    }
}
