using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class Barricade : Building, IInteractable, ILexikonSource
{
    public enum BarricadeState { Ghost, Built }

    [Header("Cost")]
    [SerializeField] private int woodCost = 5;

    [Header("Sprites")]
    [SerializeField] private Sprite ghostSprite;
    [SerializeField] private Sprite builtSprite;

    [Header("Regeneration")]
    [Tooltip("Health regenerated per second as a fraction of max health (e.g. 0.02 = 2%/s).")]
    [SerializeField] private float regenPercentPerSecond = 0.02f;

    [Header("Audio")]
    [SerializeField] private AudioClip buildSfx;

    // Read-only state exposed for UI / enemy AI
    public BarricadeState State { get; private set; } = BarricadeState.Ghost;

    public event Action<BarricadeState> OnStateChanged;
    public event Action OnDestroyed;

    private BoxCollider2D _collider;
    private SpriteRenderer _renderer;

    protected override void Awake()
    {
        _collider = GetComponent<BoxCollider2D>();
        _renderer = GetComponent<SpriteRenderer>();
        base.Awake();
        ApplyGhostState();
    }

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

    // ── Regeneration ─────────────────────────────────────────────────────────

    private void Update()
    {
        if (State != BarricadeState.Built) return;
        if (CurrentHealth >= maxHealth) return;

        CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + maxHealth * regenPercentPerSecond * Time.deltaTime);
        RaiseHealthChanged();
    }

    // ── Combat ────────────────────────────────────────────────────────────────

    public override void TakeDamage(float damage)
    {
        if (State != BarricadeState.Built) return;
        base.TakeDamage(damage);
    }

    protected override void OnDied()
    {
        PathfindingGrid.Instance?.UnregisterBarricade(transform.position);
        OnDestroyed?.Invoke();
        base.OnDied();
    }

    /// <summary>Called by <see cref="PlacedBuilding.TryUpgrade"/> to scale max health.</summary>
    public void ApplyUpgrade(float healthMult)
    {
        maxHealth     *= healthMult;
        CurrentHealth  = maxHealth;
        RaiseHealthChanged();
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
        RaiseHealthChanged();
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
}
