using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class Turret : Building, ILexikonSource
{

    [Header("Detection")]
    [SerializeField] private float detectionRange = 8f;
    public float DetectionRange => detectionRange;

    [Header("Firing")]
    [SerializeField] private float fireRate = 1f;          // shots per second
    public float FireRate => fireRate;
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform firePoint;          // child empty at barrel tip

    [Header("References")]
    [SerializeField] private Transform head;               // child transform that rotates

    [Header("Audio")]
    [SerializeField] private AudioClip fireSfx;

    private float _fireCooldown;

    public List<StatLine> GetLexikonStats() => new()
    {
        new("HP",    maxHealth.ToString("F0")),
        new("Range", detectionRange.ToString("F1")),
        new("Rate",  fireRate.ToString("F1") + "/s"),
    };

    /// <summary>Called by <see cref="PlacedBuilding.TryUpgrade"/> to scale stats.</summary>
    public void ApplyUpgrade(float healthMult, float fireRateMult, float rangeMult)
    {
        float prevMax  = maxHealth;
        maxHealth      = Mathf.Max(1f,   maxHealth      * healthMult);
        CurrentHealth  = Mathf.Min(CurrentHealth / prevMax * maxHealth, maxHealth);
        fireRate       = Mathf.Max(0.1f, fireRate       * fireRateMult);
        detectionRange = Mathf.Max(1f,   detectionRange * rangeMult);
    }

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
        GameObject proj = PoolManager.Instance != null
            ? PoolManager.Instance.Get(projectilePrefab, spawnPoint.position, Quaternion.identity)
            : Instantiate(projectilePrefab, spawnPoint.position, Quaternion.identity);

        if (proj.TryGetComponent(out Projectile projectile))
            projectile.Initialize(targetPos);

        AudioManager.Instance?.PlaySFX(fireSfx);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
}
