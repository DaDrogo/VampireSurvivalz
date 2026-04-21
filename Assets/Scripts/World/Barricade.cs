using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class Barricade : MonoBehaviour, IInteractable, IDamageable, IEnemyAttackable, ILexikonSource
{
    public enum BarricadeState { Ghost, Built }

    [Header("Cost")]
    [SerializeField] private int woodCost = 5;

    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;

    [Header("Sprites")]
    [SerializeField] private Sprite ghostSprite;
    [SerializeField] private Sprite builtSprite;

    [Header("Audio")]
    [SerializeField] private AudioClip buildSfx;
    [SerializeField] private AudioClip hitSfx;
    [SerializeField] private AudioClip destroySfx;

    // Read-only state exposed for UI / enemy AI
    public BarricadeState State { get; private set; } = BarricadeState.Ghost;
    public float CurrentHealth { get; private set; }
    public float MaxHealth => maxHealth;

    // Raised so UI / effects can react without polling
    public event Action<BarricadeState> OnStateChanged;
    public event Action<float, float> OnHealthChanged; // (current, max)
    public event Action OnDestroyed;

    private BoxCollider2D _collider;
    private SpriteRenderer _renderer;

    private void Awake()
    {
        SpriteColliderAutoFit.Fit(gameObject);
        _collider = GetComponent<BoxCollider2D>();
        _renderer = GetComponent<SpriteRenderer>();

        ApplyGhostState();
    }

#if UNITY_EDITOR
    private void OnValidate() => SpriteColliderAutoFit.Fit(gameObject);
#endif

    public List<StatLine> GetLexikonStats() => new()
    {
        new("HP", maxHealth.ToString("F0")),
    };

    // ── IInteractable ─────────────────────────────────────────────────────────

    public void Interact()
    {
        if (State != BarricadeState.Ghost) return;

        if (ResourceManager.Instance == null)
        {
            Debug.LogWarning("Barricade: no ResourceManager in scene.");
            return;
        }

        if (ResourceManager.Instance.GetResource("Wood") < woodCost)
        {
            Debug.Log($"Barricade: not enough Wood (need {woodCost}).");
            return;
        }

        ResourceManager.Instance.AddResource("Wood", -woodCost);
        Build();
    }

    // ── Placement ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="BuildingManager"/> after placement.
    /// Skips the resource check — costs were already paid to BuildingManager.
    /// </summary>
    public void BuildImmediate() => Build();

    // ── Combat ────────────────────────────────────────────────────────────────

    /// <summary>Called by enemies to damage this barricade.</summary>
    public void TakeDamage(float damage)
    {
        if (State != BarricadeState.Built) return;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);

        if (CurrentHealth <= 0f)
            DestroyBarricade();
        else
            AudioManager.Instance?.PlaySFX(hitSfx);
    }

    public bool IsDestroyed => this == null || CurrentHealth <= 0f;

    public void ReceiveEnemyAttack(float damage, float attackInterval)
    {
        TakeDamage(damage);
    }

    /// <summary>Called by <see cref="PlacedBuilding.TryUpgrade"/> to scale max health.</summary>
    public void ApplyUpgrade(float healthMult)
    {
        maxHealth     *= healthMult;
        CurrentHealth  = maxHealth;
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void Build()
    {
        State = BarricadeState.Built;
        CurrentHealth = maxHealth;

        _collider.enabled = true;
        if (builtSprite != null) _renderer.sprite = builtSprite;
        SpriteColliderAutoFit.Fit(gameObject);

        PathfindingGrid.Instance?.RegisterBarricade(transform.position, this);

        AudioManager.Instance?.PlaySFX(buildSfx);
        OnStateChanged?.Invoke(State);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    private void ApplyGhostState()
    {
        State = BarricadeState.Ghost;
        CurrentHealth = 0f;

        // Ghost barricades are passable — only the built state blocks movement
        _collider.enabled = false;
        if (ghostSprite != null) _renderer.sprite = ghostSprite;
        SpriteColliderAutoFit.Fit(gameObject);
    }

    private void DestroyBarricade()
    {
        AudioManager.Instance?.PlaySFX(destroySfx);
        PathfindingGrid.Instance?.UnregisterBarricade(transform.position);
        OnDestroyed?.Invoke();
        Destroy(gameObject);
    }
}
