using System;
using UnityEngine;

public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    // Events for UI binding — subscribe in UI scripts to react to changes
    public event Action<int> OnWoodChanged;
    public event Action<int> OnMetalChanged;

    private int _wood;
    private int _metal;

    public int Wood
    {
        get => _wood;
        private set
        {
            _wood = Mathf.Max(0, value);
            OnWoodChanged?.Invoke(_wood);
        }
    }

    public int Metal
    {
        get => _metal;
        private set
        {
            _metal = Mathf.Max(0, value);
            OnMetalChanged?.Invoke(_metal);
        }
    }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Adds (or subtracts if negative) the given amount to the specified resource.
    /// </summary>
    /// <param name="type">"Wood" or "Metal" (case-insensitive)</param>
    /// <param name="amount">Amount to add; use negative to spend</param>
    public void AddResource(string type, int amount)
    {
        switch (type.ToLowerInvariant())
        {
            case "wood":  Wood  += amount; break;
            case "metal": Metal += amount; break;
            default:
                Debug.LogWarning($"ResourceManager: unknown resource type '{type}'");
                break;
        }
    }

    /// <summary>Resets all resources to zero. Called by GameManager on new game / restart.</summary>
    public void ResetResources()
    {
        Wood  = 0;
        Metal = 0;
    }

    /// <summary>Returns current amount of a resource, or -1 if the type is unknown.</summary>
    public int GetResource(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "wood"  => Wood,
            "metal" => Metal,
            _       => -1,
        };
    }
}
