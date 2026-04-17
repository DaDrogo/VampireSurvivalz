using UnityEngine;

/// <summary>
/// A multi-harvest world object (tree, rock, crate, …) that yields resources
/// each time the player completes a hold interaction.
/// Each harvest takes <see cref="amountPerHarvest"/> from a finite pool
/// (<see cref="totalAmount"/>); the object is destroyed when the pool runs out.
/// A world-space resource bar shows the remaining pool at all times.
/// Enemies can also destroy it after spending <see cref="enemyDestroyTime"/> seconds attacking it.
/// </summary>
public class HarvestableObject : MonoBehaviour, IHoldInteractable, IEnemyAttackable
{
    public enum ResourceType { Wood, Metal, Random }

    [Header("Harvesting")]
    [SerializeField] private float holdDuration = 2f;

    [Header("Enemy Destruction")]
    [SerializeField] private float enemyDestroyTime = 2f;

    [Header("Loot")]
    [SerializeField] private ResourceType resourceType  = ResourceType.Wood;
    [Tooltip("Total resources available in this node.")]
    [SerializeField] private int totalAmount      = 30;
    [Tooltip("Resources given to the player on each completed harvest.")]
    [SerializeField] private int amountPerHarvest = 10;

    // ── IHoldInteractable ─────────────────────────────────────────────────────

    public float HoldDuration => holdDuration;
    public float CurrentHealth => _enemyDestroyProgressRemaining;
    public float MaxHealth => enemyDestroyTime;
    public bool IsDestroyed => this == null || _isDestroyed;

    // ── Private state ─────────────────────────────────────────────────────────

    private int   _remainingAmount;
    private float _enemyDestroyProgressRemaining;
    private bool  _isDestroyed;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        SpriteColliderAutoFit.Fit(gameObject);
        _remainingAmount               = Mathf.Max(1, totalAmount);
        _enemyDestroyProgressRemaining = Mathf.Max(0.01f, enemyDestroyTime);
    }

#if UNITY_EDITOR
    private void OnValidate() => SpriteColliderAutoFit.Fit(gameObject);
#endif

    // ── IHoldInteractable callbacks ───────────────────────────────────────────

    public void OnHoldStart()              { }
    public void OnHoldTick(float progress) { }
    public void OnHoldCancelled()          { }

    public void OnHoldCompleted()
    {
        if (_isDestroyed) return;

        GiveResource();
        _remainingAmount -= amountPerHarvest;

        if (_remainingAmount <= 0)
            DestroySelf();
    }

    public void ReceiveEnemyAttack(float damage, float attackInterval)
    {
        if (_isDestroyed) return;

        _enemyDestroyProgressRemaining = Mathf.Max(0f, _enemyDestroyProgressRemaining - Mathf.Max(0.01f, attackInterval));
        if (_enemyDestroyProgressRemaining <= 0f)
            DestroySelf();
    }

    // ── Resource logic ────────────────────────────────────────────────────────

    private void GiveResource()
    {
        if (ResourceManager.Instance == null)
        {
            Debug.LogWarning("HarvestableObject: no ResourceManager in scene.");
            return;
        }

        string type = resourceType switch
        {
            ResourceType.Wood   => "Wood",
            ResourceType.Metal  => "Metal",
            ResourceType.Random => Random.value < 0.5f ? "Wood" : "Metal",
            _                   => "Wood"
        };

        // Give exactly amountPerHarvest, clamped so we never overpay on the last hit
        int actual = Mathf.Min(amountPerHarvest, _remainingAmount);
        ResourceManager.Instance.AddResource(type, actual);
        Debug.Log($"Harvested {actual} {type} from {name}. ({Mathf.Max(0, _remainingAmount - actual)} remaining)");
    }

    private void DestroySelf()
    {
        if (_isDestroyed) return;

        _isDestroyed = true;
        Destroy(gameObject);
    }
}
