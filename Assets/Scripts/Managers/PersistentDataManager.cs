using UnityEngine;

/// <summary>
/// Survives scene loads. Stores settings (volume), selections (character/level),
/// and milestone counters via PlayerPrefs.
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
            var parsed     = new System.Collections.Generic.List<int>();
            foreach (string p in parts)
                if (int.TryParse(p.Trim(), out int v)) parsed.Add(v);
            if (parsed.Count > 0) SelectedBuildingIndices = parsed.ToArray();
        }
        catch { /* keep default */ }
    }
}
