using UnityEngine;

/// <summary>
/// A placed building that automatically delivers resources to <see cref="ResourceManager"/>
/// on a fixed interval. Supports health, enemy damage, and upgrade scaling.
///
/// Setup:
///   • Assign a <see cref="BuildingDefinition"/> whose component type includes this script.
///   • Set <c>outputType</c> to "Wood", "Metal", or "Both".
///   • Set <c>amountPerCycle</c> and <c>productionInterval</c> in the Inspector.
/// Upgrades scale via <c>ApplyUpgrade(healthMult, productionMult)</c>.
/// </summary>
[RequireComponent(typeof(CircleCollider2D))]
public class ResourceProducer : MonoBehaviour, IDamageable, IEnemyAttackable
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;

    [Header("Production")]
    [Tooltip("\"Wood\", \"Metal\", or \"Both\"")]
    [SerializeField] private string outputType = "Wood";

    [Tooltip("Resources added each cycle (per type if Both).")]
    [SerializeField] private int amountPerCycle = 5;

    [Tooltip("Seconds between each production tick.")]
    [SerializeField] private float productionInterval = 10f;

    // ── IDamageable ───────────────────────────────────────────────────────────

    public float CurrentHealth { get; private set; }
    public float MaxHealth     => maxHealth;

    // ── Read-only stats (for UI) ──────────────────────────────────────────────

    public string OutputType         => outputType;
    public int    AmountPerCycle     => amountPerCycle;
    public float  ProductionInterval => productionInterval;

    // ── Private ───────────────────────────────────────────────────────────────

    private float _timer;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        SpriteColliderAutoFit.Fit(gameObject);
        CurrentHealth = maxHealth;
    }

#if UNITY_EDITOR
    private void OnValidate() => SpriteColliderAutoFit.Fit(gameObject);
#endif

    private void Update()
    {
        if (ResourceManager.Instance == null) return;

        _timer += Time.deltaTime;
        if (_timer >= productionInterval)
        {
            _timer -= productionInterval;
            Produce();
        }
    }

    // ── Production ────────────────────────────────────────────────────────────

    private void Produce()
    {
        switch (outputType.ToLowerInvariant())
        {
            case "wood":
                ResourceManager.Instance.AddResource("Wood", amountPerCycle);
                break;
            case "metal":
                ResourceManager.Instance.AddResource("Metal", amountPerCycle);
                break;
            case "both":
                ResourceManager.Instance.AddResource("Wood",  amountPerCycle);
                ResourceManager.Instance.AddResource("Metal", amountPerCycle);
                break;
            default:
                Debug.LogWarning($"[ResourceProducer] Unknown outputType '{outputType}'.");
                break;
        }
    }

    // ── IDamageable ───────────────────────────────────────────────────────────

    public void TakeDamage(float damage)
    {
        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
        if (CurrentHealth <= 0f) Destroy(gameObject);
    }

    // ── IEnemyAttackable ──────────────────────────────────────────────────────

    public bool IsDestroyed => this == null || CurrentHealth <= 0f;

    public void ReceiveEnemyAttack(float damage, float attackInterval)
    {
        TakeDamage(damage);
    }

    // ── Upgrade ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="PlacedBuilding.TryUpgrade"/>.
    /// <paramref name="healthMult"/> scales max HP; <paramref name="productionMult"/> scales
    /// both <c>amountPerCycle</c> and shortens <c>productionInterval</c> (faster cycles).
    /// </summary>
    public void ApplyUpgrade(float healthMult, float productionMult)
    {
        float prevMax  = maxHealth;
        maxHealth      = Mathf.Max(1f, maxHealth * healthMult);
        CurrentHealth  = Mathf.Min(CurrentHealth / prevMax * maxHealth, maxHealth);

        amountPerCycle     = Mathf.Max(1, Mathf.RoundToInt(amountPerCycle * productionMult));
        productionInterval = Mathf.Max(1f, productionInterval / productionMult);
    }
}
