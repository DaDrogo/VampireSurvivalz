using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lightweight player controller for the CampScene.
/// WASD / left-stick movement. Detects nearby CampTentObjects and shows an
/// interact prompt; pressing E / South-button triggers Interact().
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class CampPlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed    = 4f;
    [SerializeField] private float interactRadius = 1.6f;

    private Rigidbody2D      _rb;
    private Vector2          _moveInput;
    private CampTentObject   _nearestTent;
    private SpriteRenderer   _sr;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _rb.gravityScale = 0f;
        _rb.constraints  = RigidbodyConstraints2D.FreezeRotation;
        _sr = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        ReadInput();
        ScanForTent();
        UpdatePrompt();
        HandleInteract();
    }

    private void FixedUpdate()
    {
        _rb.linearVelocity = _moveInput * moveSpeed;
        if (_sr != null && _moveInput.x != 0f)
            _sr.flipX = _moveInput.x < 0f;
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    private void ReadInput()
    {
        var kb = Keyboard.current;
        var gp = Gamepad.current;

        float x = 0f, y = 0f;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  y -= 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
        }
        if (gp != null)
        {
            var stick = gp.leftStick.ReadValue();
            x += stick.x; y += stick.y;
        }
        _moveInput = new Vector2(x, y).normalized;
    }

    private void HandleInteract()
    {
        if (_nearestTent == null) return;
        bool pressed = (Keyboard.current?.eKey.wasPressedThisFrame ?? false)
                    || (Gamepad.current?.buttonSouth.wasPressedThisFrame ?? false);
        if (pressed) _nearestTent.Interact();
    }

    // ── Proximity detection ───────────────────────────────────────────────────

    private void ScanForTent()
    {
        var hits = Physics2D.OverlapCircleAll(transform.position, interactRadius);
        CampTentObject best  = null;
        float          bestD = float.MaxValue;

        foreach (var col in hits)
        {
            var tent = col.GetComponent<CampTentObject>();
            if (tent == null) tent = col.GetComponentInParent<CampTentObject>();
            if (tent == null) continue;

            float d = Vector2.Distance(transform.position, tent.transform.position);
            if (d < bestD) { bestD = d; best = tent; }
        }

        if (best != _nearestTent)
        {
            _nearestTent = best;
            CampSceneManager.Instance?.SetPrompt(best != null ? best.GetInteractPrompt() : string.Empty);
        }
    }

    private void UpdatePrompt() { /* prompt driven by ScanForTent */ }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactRadius);
    }
}
