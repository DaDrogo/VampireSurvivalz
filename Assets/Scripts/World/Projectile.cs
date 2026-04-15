using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class Projectile : MonoBehaviour
{
    [SerializeField] private float speed = 12f;
    [SerializeField] private float damage = 25f;
    [SerializeField] private float maxLifetime = 5f;

    private Rigidbody2D _rb;
    private Vector2 _direction;
    private bool _initialized;

    private void Awake()
    {
        SpriteColliderAutoFit.Fit(gameObject);
        _rb = GetComponent<Rigidbody2D>();
        _rb.gravityScale = 0f;

        // Trigger so the projectile passes through and we handle the hit manually
        GetComponent<Collider2D>().isTrigger = true;
    }

#if UNITY_EDITOR
    private void OnValidate() => SpriteColliderAutoFit.Fit(gameObject);
#endif

    /// <summary>Called by Turret immediately after Instantiate.</summary>
    public void Initialize(Vector3 targetWorldPos)
    {
        _direction = (targetWorldPos - transform.position).normalized;

        // Face the travel direction
        float angle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg - 90f;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        _rb.linearVelocity = _direction * speed;
        _initialized = true;

        Destroy(gameObject, maxLifetime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Ignore the turret's own colliders
        if (!_initialized) return;
        if (!other.CompareTag("Enemy")) return;

        if (other.TryGetComponent(out IDamageable damageable))
        {
            damageable.TakeDamage(damage);
        }

        Destroy(gameObject);
    }
}
