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

    // Hold progress bar (world-space, shown above the player while harvesting)
    private GameObject _holdBarRoot;
    private Transform  _holdBarFill;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        SpriteColliderAutoFit.Fit(gameObject);
        _rb = GetComponent<Rigidbody2D>();
        _rb.gravityScale   = 0f;
        _rb.freezeRotation = true;

        CurrentHealth   = maxHealth;
        _interactAction = GetComponent<PlayerInput>().actions.FindAction("Interact");
        BuildHoldBar();
    }

#if UNITY_EDITOR
    private void OnValidate() => SpriteColliderAutoFit.Fit(gameObject);
#endif

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
            _holdBarRoot.SetActive(true);
            SetHoldBarFill(0f);
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
        SetHoldBarFill(progress);

        if (progress >= 1f)
        {
            IHoldInteractable completed = _holdTarget;
            _holdTarget = null; _holdTargetTransform = null; _holdTimer = 0f;
            _holdBarRoot.SetActive(false);
            completed.OnHoldCompleted();
        }
    }

    private void CancelHold()
    {
        _holdTarget?.OnHoldCancelled();
        _holdTarget = null; _holdTargetTransform = null; _holdTimer = 0f;
        _holdBarRoot.SetActive(false);
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

    // ── Hold progress bar ─────────────────────────────────────────────────────

    private void BuildHoldBar()
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        Sprite white = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

        _holdBarRoot = new GameObject("HoldProgressBar");
        _holdBarRoot.transform.SetParent(transform);
        _holdBarRoot.transform.localPosition = new Vector3(0f, -0.2f, 0f);
        _holdBarRoot.transform.localScale    = new Vector3(0.5f, 0.05f, 1f);

        SpriteRenderer bg = _holdBarRoot.AddComponent<SpriteRenderer>();
        bg.sprite       = white;
        bg.color        = new Color(0.15f, 0.15f, 0.15f, 0.85f);
        bg.sortingOrder = 10;

        GameObject fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(_holdBarRoot.transform);
        fillGO.transform.localPosition = new Vector3(-0.5f, 0f, 0f);
        fillGO.transform.localScale    = new Vector3(0f, 1f, 1f);
        _holdBarFill = fillGO.transform;

        SpriteRenderer fill = fillGO.AddComponent<SpriteRenderer>();
        fill.sprite       = white;
        fill.color        = new Color(0.2f, 0.85f, 0.3f, 1f);
        fill.sortingOrder = 11;

        _holdBarRoot.SetActive(false);
    }

    private void SetHoldBarFill(float t)
    {
        t = Mathf.Clamp01(t);
        _holdBarFill.localPosition = new Vector3(-0.5f + t * 0.5f, 0f, 0f);
        _holdBarFill.localScale    = new Vector3(t, 1f, 1f);
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactRadius);
    }
}
