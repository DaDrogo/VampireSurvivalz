using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

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

    public event Action<float, float> OnHealthChanged;
    public event Action               OnDied;

    // ── Private ───────────────────────────────────────────────────────────────

    private Rigidbody2D _rb;
    private Vector2     _moveInput;
    private float       _iFrameTimer;

    // Manual hold-interaction (keyboard Interact button)
    private InputAction       _interactAction;
    private IHoldInteractable _holdTarget;
    private Transform         _holdTargetTransform;
    private float             _holdTimer;

    // Point-to-move state
    private List<Vector2>     _path             = new List<Vector2>();
    private int               _pathIndex;
    private IHoldInteractable _pendingHoldTarget;
    private Transform         _pendingHoldTransform;
    private bool              _autoHolding;

    private const float WaypointDist = 0.2f;   // how close = "reached" a waypoint

    // Hold progress bar
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

        EnhancedTouchSupport.Enable();   // required for Touch.activeTouches on mobile
        BuildHoldBar();
    }

    private void OnDestroy() => EnhancedTouchSupport.Disable();

#if UNITY_EDITOR
    private void OnValidate() => SpriteColliderAutoFit.Fit(gameObject);
#endif

    // ── Input callbacks (New Input System SendMessages) ───────────────────────

    private void OnMove(InputValue value) => _moveInput = value.Get<Vector2>();

    /// Keyboard/gamepad instant-interact (unchanged behaviour)
    private void OnInteract(InputValue value)
    {
        if (!value.isPressed) return;

        Collider2D hit = Physics2D.OverlapCircle(transform.position, interactRadius, interactableMask);
        if (hit == null) return;

        if (hit.TryGetComponent(out IHoldInteractable holdTarget))
        {
            ClearPointToMove();
            BeginHold(holdTarget, hit.transform, autoHold: false);
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

        HandlePointerInput();
        TickHoldInteraction();
    }

    // ── Physics ───────────────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        if (_autoHolding)
        {
            // Stand still while auto-harvesting
            _rb.linearVelocity = Vector2.zero;
            return;
        }

        if (_moveInput.sqrMagnitude > 0.01f)
        {
            // Keyboard/stick takes priority — cancel any click-to-move
            ClearPointToMove();
            _rb.linearVelocity = _moveInput.normalized * moveSpeed;
            return;
        }

        if (_path.Count > 0 && _pathIndex < _path.Count)
        {
            FollowPath();
            return;
        }

        _rb.linearVelocity = Vector2.zero;
    }

    // ── Point-to-move ─────────────────────────────────────────────────────────

    private void HandlePointerInput()
    {
        Vector2 screenPos = Vector2.zero;
        bool    clicked   = false;
        int     pointerId = -1;   // -1 = mouse

        // Mouse
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            screenPos = Mouse.current.position.ReadValue();
            clicked   = true;
        }

        // Touch (mobile — first finger that just touched down)
        if (!clicked)
        {
            foreach (Touch touch in Touch.activeTouches)
            {
                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    screenPos = touch.screenPosition;
                    pointerId = touch.touchId;
                    clicked   = true;
                    break;
                }
            }
        }

        if (!clicked) return;

        // Don't navigate when tapping a UI element
        if (EventSystem.current != null)
        {
            bool overUI = pointerId < 0
                ? EventSystem.current.IsPointerOverGameObject()
                : EventSystem.current.IsPointerOverGameObject(pointerId);
            if (overUI) return;
        }

        ProcessWorldClick(screenPos);
    }

    private void ProcessWorldClick(Vector2 screenPos)
    {
        if (Camera.main == null) return;

        Vector2 worldPos = Camera.main.ScreenToWorldPoint(screenPos);

        // Cancel any running hold first
        if (_holdTarget != null) CancelHold();

        // Check for an interactable at the tapped position
        Collider2D hit = Physics2D.OverlapCircle(worldPos, 0.4f, interactableMask);

        if (hit != null && hit.TryGetComponent(out IHoldInteractable holdTarget))
        {
            _pendingHoldTarget    = holdTarget;
            _pendingHoldTransform = hit.transform;
            SetPathTo(hit.transform.position);
        }
        else
        {
            ClearPendingInteract();
            SetPathTo(worldPos);
        }
    }

    private void SetPathTo(Vector2 worldTarget)
    {
        List<Vector2> path = Pathfinder.FindPath(transform.position, worldTarget, avoidBarricades: true);

        if (path == null || path.Count == 0)
            path = new List<Vector2> { worldTarget };   // fallback: straight line

        _path      = path;
        _pathIndex = path.Count > 1 ? 1 : 0;   // skip start node (already there)
    }

    private void FollowPath()
    {
        // Guard: pending target may have been destroyed en route
        if (_pendingHoldTransform == null)
            ClearPendingInteract();

        // Check range every frame — the player's collider may physically block
        // reaching the exact tile center, so don't wait for the final waypoint.
        if (_pendingHoldTarget != null &&
            Vector2.Distance(transform.position, _pendingHoldTransform.position) <= interactRadius)
        {
            _rb.linearVelocity = Vector2.zero;
            _path.Clear();
            StartAutoHold();
            return;
        }

        Vector2 waypoint = _path[_pathIndex];
        Vector2 dir      = waypoint - (Vector2)transform.position;

        if (dir.magnitude <= WaypointDist)
        {
            _pathIndex++;

            if (_pathIndex >= _path.Count)
            {
                _rb.linearVelocity = Vector2.zero;
                _path.Clear();
            }
            return;
        }

        _rb.linearVelocity = dir.normalized * moveSpeed;
    }

    private void ClearPointToMove()
    {
        _path.Clear();
        ClearPendingInteract();
        if (_autoHolding) CancelHold();
    }

    private void ClearPendingInteract()
    {
        _pendingHoldTarget    = null;
        _pendingHoldTransform = null;
    }

    // ── Hold interaction ──────────────────────────────────────────────────────

    private void StartAutoHold()
    {
        if (_pendingHoldTarget == null) return;
        BeginHold(_pendingHoldTarget, _pendingHoldTransform, autoHold: true);
        ClearPendingInteract();
    }

    private void BeginHold(IHoldInteractable target, Transform targetTransform, bool autoHold)
    {
        _holdTarget          = target;
        _holdTargetTransform = targetTransform;
        _holdTimer           = 0f;
        _autoHolding         = autoHold;
        _holdBarRoot.SetActive(true);
        SetHoldBarFill(0f);
        _holdTarget.OnHoldStart();
    }

    private void TickHoldInteraction()
    {
        if (_holdTarget == null) return;

        bool outOfRange = Vector2.Distance(transform.position, _holdTargetTransform.position) > interactRadius;
        // Auto-hold doesn't require a button to be held — only manual hold does
        bool buttonUp   = !_autoHolding && (_interactAction == null || !_interactAction.IsPressed());

        if (outOfRange || buttonUp) { CancelHold(); return; }

        _holdTimer += Time.deltaTime;
        float progress = Mathf.Clamp01(_holdTimer / _holdTarget.HoldDuration);
        _holdTarget.OnHoldTick(progress);
        SetHoldBarFill(progress);

        if (progress >= 1f)
        {
            IHoldInteractable completed = _holdTarget;
            _holdTarget          = null;
            _holdTargetTransform = null;
            _holdTimer           = 0f;
            _autoHolding         = false;
            _holdBarRoot.SetActive(false);
            completed.OnHoldCompleted();
        }
    }

    private void CancelHold()
    {
        _holdTarget?.OnHoldCancelled();
        _holdTarget          = null;
        _holdTargetTransform = null;
        _holdTimer           = 0f;
        _autoHolding         = false;
        _holdBarRoot.SetActive(false);
    }

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

        if (_path == null || _path.Count == 0) return;
        Gizmos.color = Color.cyan;
        for (int i = _pathIndex; i < _path.Count - 1; i++)
            Gizmos.DrawLine(_path[i], _path[i + 1]);
    }
}
