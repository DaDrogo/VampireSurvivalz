using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class Turret : MonoBehaviour, IDamageable, IEnemyAttackable
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 150f;

    public float CurrentHealth { get; private set; }
    public float MaxHealth     => maxHealth;

    [Header("Detection")]
    [SerializeField] private float detectionRange = 8f;

    [Header("Firing")]
    [SerializeField] private float fireRate = 1f;          // shots per second
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform firePoint;          // child empty at barrel tip

    [Header("References")]
    [SerializeField] private Transform head;               // child transform that rotates

    private float _fireCooldown;

    private void Awake()
    {
        SpriteColliderAutoFit.Fit(gameObject);
        CurrentHealth = maxHealth;
    }

    public void TakeDamage(float damage)
    {
        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
        if (CurrentHealth <= 0f) Destroy(gameObject);
    }

    public bool IsDestroyed => this == null || CurrentHealth <= 0f;

    public void ReceiveEnemyAttack(float damage, float attackInterval)
    {
        TakeDamage(damage);
    }

#if UNITY_EDITOR
    private void OnValidate() => SpriteColliderAutoFit.Fit(gameObject);
#endif

    private void Update()
    {
        Transform target = FindClosestEnemy();

        if (target == null) return;

        RotateHeadToward(target.position);

        _fireCooldown -= Time.deltaTime;
        if (_fireCooldown <= 0f)
        {
            Fire(target.position);
            _fireCooldown = 1f / fireRate;
        }
    }

    private Transform FindClosestEnemy()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, detectionRange);

        Transform closest = null;
        float closestDist = float.MaxValue;

        foreach (Collider2D hit in hits)
        {
            if (!hit.CompareTag("Enemy")) continue;

            float dist = Vector2.Distance(transform.position, hit.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = hit.transform;
            }
        }

        return closest;
    }

    private void RotateHeadToward(Vector3 targetPos)
    {
        if (head == null) return;

        Vector2 dir = targetPos - head.position;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
        head.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void Fire(Vector3 targetPos)
    {
        if (projectilePrefab == null) return;

        Transform spawnPoint = firePoint != null ? firePoint : head != null ? head : transform;
        GameObject proj = Instantiate(projectilePrefab, spawnPoint.position, Quaternion.identity);

        if (proj.TryGetComponent(out Projectile projectile))
        {
            projectile.Initialize(targetPos);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
}
