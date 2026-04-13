using System;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class Barricade : MonoBehaviour, IInteractable, IDamageable
{
    public enum BarricadeState { Ghost, Built }

    [Header("Cost")]
    [SerializeField] private int woodCost = 5;

    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;

    [Header("Sprites")]
    [SerializeField] private Sprite ghostSprite;
    [SerializeField] private Sprite builtSprite;

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
        _collider = GetComponent<BoxCollider2D>();
        _renderer = GetComponent<SpriteRenderer>();

        ApplyGhostState();
    }

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

    // ── Combat ────────────────────────────────────────────────────────────────

    /// <summary>Called by enemies to damage this barricade.</summary>
    public void TakeDamage(float damage)
    {
        if (State != BarricadeState.Built) return;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);

        if (CurrentHealth <= 0f)
            DestroyBarricade();
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void Build()
    {
        State = BarricadeState.Built;
        CurrentHealth = maxHealth;

        _collider.enabled = true;
        if (builtSprite != null) _renderer.sprite = builtSprite;

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
    }

    private void DestroyBarricade()
    {
        OnDestroyed?.Invoke();
        Destroy(gameObject);
    }
}
