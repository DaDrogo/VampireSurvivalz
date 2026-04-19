using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Scene-level singleton. Provides GameObject pools keyed by prefab.
/// Place in the game scene. Does not survive scene loads by design.
/// </summary>
public class PoolManager : MonoBehaviour
{
    public static PoolManager Instance { get; private set; }

    [SerializeField] private int defaultCapacity = 20;
    [SerializeField] private int maxSize         = 200;

    private readonly Dictionary<int, ObjectPool<GameObject>> _pools          = new();
    private readonly Dictionary<int, ObjectPool<GameObject>> _instanceToPool = new();

    private void Awake()
    {
        if (Instance != null) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        var pool = GetOrCreate(prefab);
        var go   = pool.Get();
        go.transform.SetPositionAndRotation(position, rotation);
        _instanceToPool[go.GetInstanceID()] = pool;
        return go;
    }

    public void Release(GameObject instance)
    {
        if (instance == null) return;
        if (_instanceToPool.TryGetValue(instance.GetInstanceID(), out var pool))
            pool.Release(instance);
        else
            Destroy(instance);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private ObjectPool<GameObject> GetOrCreate(GameObject prefab)
    {
        int key = prefab.GetInstanceID();
        if (!_pools.TryGetValue(key, out var pool))
        {
            pool = new ObjectPool<GameObject>(
                createFunc:      () => Instantiate(prefab),
                actionOnGet:     go => go.SetActive(true),
                actionOnRelease: go => go.SetActive(false),
                actionOnDestroy: go => Destroy(go),
                collectionCheck: false,
                defaultCapacity: defaultCapacity,
                maxSize:         maxSize
            );
            _pools[key] = pool;
        }
        return pool;
    }
}
