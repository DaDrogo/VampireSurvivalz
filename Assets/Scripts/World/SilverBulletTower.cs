using System.Collections;
using UnityEngine;

/// <summary>
/// Special tower that fires silver bullets at the VampireEnemy.
/// Each hit applies a timed vulnerability window during which the vampire can be
/// permanently killed.  Deals no regular damage — purely a setup tool for the player.
///
/// Fires using the same Turret projectile system for visuals; the vulnerability is
/// applied after a travel-time delay calculated from the bullet's speed.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class SilverBulletTower : MonoBehaviour, IDamageable, IEnemyAttackable
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 120f;

    [Header("Detection & Firing")]
    [SerializeField] private float detectionRange        = 10f;
    [SerializeField] private float fireRate              = 0.5f;   // shots per second
    [SerializeField] private float bulletSpeed           = 12f;

    [Header("Vulnerability Window")]
    [Tooltip("How long the vampire remains vulnerable after a silver bullet hit.")]
    [SerializeField] private float vulnerabilityDuration = 15f;

    [Header("Visuals")]
    [SerializeField] private GameObject bulletPrefab;    // any simple projectile visual
    [SerializeField] private Transform  firePoint;
    [SerializeField] private Transform  head;

    // ── IDamageable / IEnemyAttackable ────────────────────────────────────────

    public float CurrentHealth { get; private set; }
    public float MaxHealth     => maxHealth;
    public bool  IsDestroyed   => this == null || CurrentHealth <= 0f;

    public void ReceiveEnemyAttack(float damage, float _) => TakeDamage(damage);

    public void TakeDamage(float damage)
    {
        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
        if (CurrentHealth <= 0f) Destroy(gameObject);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private float _fireCooldown = 0f;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        SpriteColliderAutoFit.Fit(gameObject);
        CurrentHealth = maxHealth;
    }

    private void Update()
    {
        VampireEnemy vampire = VampireEnemy.Instance;
        if (vampire == null || !vampire.gameObject.activeSelf) return;

        float dist = Vector2.Distance(transform.position, vampire.transform.position);
        if (dist > detectionRange) return;

        RotateHeadToward(vampire.transform.position);

        _fireCooldown -= Time.deltaTime;
        if (_fireCooldown <= 0f)
        {
            _fireCooldown = 1f / fireRate;
            Fire(vampire);
        }
    }

    private void RotateHeadToward(Vector3 target)
    {
        if (head == null) return;
        Vector2 dir   = target - head.position;
        float   angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
        head.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void Fire(VampireEnemy vampire)
    {
        Transform origin = firePoint != null ? firePoint : head != null ? head : transform;
        float dist       = Vector2.Distance(origin.position, vampire.transform.position);
        float travelTime = bulletSpeed > 0f ? dist / bulletSpeed : 0.1f;

        // Spawn visual bullet
        if (bulletPrefab != null)
        {
            GameObject go = PoolManager.Instance != null
                ? PoolManager.Instance.Get(bulletPrefab, origin.position, Quaternion.identity)
                : Instantiate(bulletPrefab, origin.position, Quaternion.identity);

            // Initialize standard Projectile if present — deals 0 damage, just for visuals
            if (go.TryGetComponent(out Projectile proj))
                proj.Initialize(vampire.transform.position);
        }

        StartCoroutine(ApplyVulnerabilityAfterDelay(travelTime));
    }

    private IEnumerator ApplyVulnerabilityAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        VampireEnemy.Instance?.MakeVulnerable(vulnerabilityDuration);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.8f, 0.8f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
}
