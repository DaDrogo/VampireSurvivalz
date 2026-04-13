using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PlayerInput))]
public class PlayerController : MonoBehaviour, IDamageable
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("Interaction")]
    [SerializeField] private float interactRadius = 1.5f;
    [SerializeField] private LayerMask interactableMask;

    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;
    [Tooltip("Seconds of invincibility after taking a hit, preventing damage spam")]
    [SerializeField] private float iFrameDuration = 0.5f;

    // ── IDamageable ───────────────────────────────────────────────────────────

    public float CurrentHealth { get; private set; }
    public float MaxHealth     => maxHealth;

    /// <summary>Fired whenever health changes. Args: (currentHealth, maxHealth).</summary>
    public event Action<float, float> OnHealthChanged;

    /// <summary>Fired once when health reaches zero, before GameOver is triggered.</summary>
    public event Action OnDied;

    // ── Private ───────────────────────────────────────────────────────────────

    private Rigidbody2D _rb;
    private Vector2     _moveInput;
    private float       _iFrameTimer;

    // Hold-interaction state
    private InputAction       _interactAction;
    private IHoldInteractable _holdTarget;
    private Transform         _holdTargetTransform;
    private float             _holdTimer;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _rb.gravityScale   = 0f;
        _rb.freezeRotation = true;

        CurrentHealth   = maxHealth;
        _interactAction = GetComponent<PlayerInput>().actions.FindAction("Interact");
    }

    // ── Input callbacks (SendMessages) ────────────────────────────────────────

    private void OnMove(InputValue value) => _moveInput = value.Get<Vector2>();

    private void OnInteract(InputValue value)
    {
        if (!value.isPressed) return;

        Collider2D hit = Physics2D.OverlapCircle(transform.position, interactRadius, interactableMask);
        if (hit == null) return;

        if (hit.TryGetComponent(out IHoldInteractable holdTarget))
        {
            _holdTarget          = holdTarget;
            _holdTargetTransform = hit.transform;
            _holdTimer           = 0f;
            _holdTarget.OnHoldStart();
        }
        else if (hit.TryGetComponent(out IInteractable interactable))
        {
            interactable.Interact();
        }
    }

    // ── Per-frame ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (_iFrameTimer > 0f) _iFrameTimer -= Time.deltaTime;

        TickHoldInteraction();
    }

    private void TickHoldInteraction()
    {
        if (_holdTarget == null) return;

        bool outOfRange = Vector2.Distance(transform.position, _holdTargetTransform.position) > interactRadius;
        bool buttonUp   = _interactAction == null || !_interactAction.IsPressed();

        if (outOfRange || buttonUp) { CancelHold(); return; }

        _holdTimer += Time.deltaTime;
        float progress = Mathf.Clamp01(_holdTimer / _holdTarget.HoldDuration);
        _holdTarget.OnHoldTick(progress);

        if (progress >= 1f)
        {
            IHoldInteractable completed = _holdTarget;
            _holdTarget = null; _holdTargetTransform = null; _holdTimer = 0f;
            completed.OnHoldCompleted();
        }
    }

    private void CancelHold()
    {
        _holdTarget?.OnHoldCancelled();
        _holdTarget = null; _holdTargetTransform = null; _holdTimer = 0f;
    }

    // ── Physics ───────────────────────────────────────────────────────────────

    private void FixedUpdate() => _rb.linearVelocity = _moveInput.normalized * moveSpeed;

    // ── IDamageable ───────────────────────────────────────────────────────────

    public void TakeDamage(float damage)
    {
        if (_iFrameTimer > 0f) return;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
        _iFrameTimer  = iFrameDuration;
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);

        if (CurrentHealth <= 0f) Die();
    }

    private void Die()
    {
        OnDied?.Invoke();
        GameManager.Instance?.TriggerGameOver();
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactRadius);
    }
}
