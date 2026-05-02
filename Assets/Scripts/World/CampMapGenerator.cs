using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

[DefaultExecutionOrder(-10)]
public class CampMapGenerator : MonoBehaviour
{
    [Header("Tilemaps")]
    [SerializeField] private Tilemap floorTilemap;
    [SerializeField] private Tilemap wallTilemap;

    [Header("Tiles")]
    [SerializeField] private TileBase[] floorTiles;
    [SerializeField] private TileBase[] wallTiles;

    [Header("Map Size")]
    [SerializeField] private int mapWidth  = 28;
    [SerializeField] private int mapHeight = 20;
    [SerializeField] private Vector2 mapCenter = Vector2.zero;

    [Header("Tents")]
    [SerializeField] private GameObject tentPrefab;
    [SerializeField] private TentDefinition[] tentDefinitions;
    [Tooltip("Sprite shown after purchase")]
    [SerializeField] private Sprite[] purchasedSprites;
    [Tooltip("Sprite shown before purchase (greyed-out)")]
    [SerializeField] private Sprite[] ghostSprites;

    [Header("Player")]
    [SerializeField] private GameObject playerPrefab;

    [Header("Forest (tilemap)")]
    [SerializeField] private Tilemap    treeTilemap;
    [SerializeField] private TileBase[] treeTiles;
    [Tooltip("Sprites layered on top of each placed tree tile.")]
    [SerializeField] private Sprite[]   treeSprites;
    [Tooltip("Width in tiles of the soft transition between clearing and forest.")]
    [SerializeField] private float treeEdgeBlend = 2f;

    [Header("River (tilemap)")]
    [SerializeField] private Tilemap    riverTilemap;
    [SerializeField] private TileBase[] riverTiles;
    [Tooltip("Tiles placed in the bridge crossing gap (on top of the river tilemap).")]
    [SerializeField] private TileBase[] bridgeTiles;
    [Tooltip("How many tiles tall the bridge gap is (centred on map).")]
    [SerializeField] private int bridgeHeightTiles = 4;

    [Header("Night Atmosphere")]
    [SerializeField] private Light2D globalLight;
    [SerializeField] private float moonOuterRadius = 9f;
    [SerializeField] private float moonInnerRadius = 2.5f;
    [SerializeField] private float moonIntensity   = 1.1f;
    [SerializeField] private Color moonColor       = new Color(0.85f, 0.90f, 1.00f);

    private static Sprite _cachedSquareSprite;
    private Transform _treeContainer;

    // ── Public accessors ──────────────────────────────────────────────────────

    public Vector2 MapCenter => mapCenter;
    public int     MapWidth  => mapWidth;
    public int     MapHeight => mapHeight;

    public Bounds WalkableBounds => new Bounds(
        mapCenter,
        new Vector3(mapWidth - 2f, mapHeight - 2f, 0f));

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        Generate();
    }

    // ── Generation ────────────────────────────────────────────────────────────

    [ContextMenu("Generate Now")]
    public void Generate()
    {
        if (!ValidateReferences()) return;

        floorTilemap.ClearAllTiles();
        wallTilemap.ClearAllTiles();

        int ox = Mathf.RoundToInt(mapCenter.x - mapWidth  * 0.5f);
        int oy = Mathf.RoundToInt(mapCenter.y - mapHeight * 0.5f);

        // ── Floor — one batch write ───────────────────────────────────────────
        var floorBounds = new BoundsInt(ox, oy, 0, mapWidth, mapHeight, 1);
        var floorData   = new TileBase[mapWidth * mapHeight];
        for (int i = 0; i < floorData.Length; i++) floorData[i] = Pick(floorTiles);
        floorTilemap.SetTilesBlock(floorBounds, floorData);

        // ── Perimeter walls — one batch write each ────────────────────────────
        int perimeterCount = mapWidth * 2 + (mapHeight - 2) * 2;
        var wallPos  = new Vector3Int[perimeterCount];
        var wallData = new TileBase[perimeterCount];
        int wi = 0;

        for (int x = ox; x < ox + mapWidth; x++)
        {
            wallPos[wi] = new Vector3Int(x, oy, 0);                  wallData[wi++] = Pick(wallTiles);
            wallPos[wi] = new Vector3Int(x, oy + mapHeight - 1, 0);  wallData[wi++] = Pick(wallTiles);
        }
        for (int y = oy + 1; y < oy + mapHeight - 1; y++)
        {
            wallPos[wi] = new Vector3Int(ox,               y, 0);    wallData[wi++] = Pick(wallTiles);
            wallPos[wi] = new Vector3Int(ox + mapWidth - 1, y, 0);   wallData[wi++] = Pick(wallTiles);
        }

        wallTilemap.SetTiles(wallPos, wallData);

        // Clear floor under walls in one pass
        var nullData = new TileBase[perimeterCount]; // all null by default
        floorTilemap.SetTiles(wallPos, nullData);

        floorTilemap.CompressBounds();
        wallTilemap.CompressBounds();

        SpawnTents();
        SpawnPlayer();
        GenerateForest();
        GenerateRiver();
        SetupNightAtmosphere();
    }

    // ── Forest ────────────────────────────────────────────────────────────────

    private void GenerateForest()
    {
        if (treeTilemap == null || treeTiles == null || treeTiles.Length == 0) return;

        treeTilemap.ClearAllTiles();

        if (_treeContainer != null) Destroy(_treeContainer.gameObject);
        _treeContainer = new GameObject("TreeSprites").transform;
        _treeContainer.SetParent(transform);

        int ox = Mathf.RoundToInt(mapCenter.x - mapWidth  * 0.5f);
        int oy = Mathf.RoundToInt(mapCenter.y - mapHeight * 0.5f);

        var positions = new List<Vector3Int>();
        var tiles     = new List<TileBase>();

        for (int x = ox + 1; x < ox + mapWidth  - 1; x++)
        for (int y = oy + 1; y < oy + mapHeight - 1; y++)
        {
            float dx   = (x + 0.5f) - mapCenter.x;
            float dy   = (y + 0.5f) - mapCenter.y;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);

            float t = Mathf.InverseLerp(moonOuterRadius - treeEdgeBlend, moonOuterRadius, dist);
            if (t <= 0f || Random.value > t) continue;

            positions.Add(new Vector3Int(x, y, 0));
            tiles.Add(Pick(treeTiles));
            SpawnTreeSprite(x, y);
        }

        if (positions.Count > 0)
            treeTilemap.SetTiles(positions.ToArray(), tiles.ToArray());

        treeTilemap.CompressBounds();
    }

    private void SpawnTreeSprite(int tileX, int tileY)
    {
        if (treeSprites == null || treeSprites.Length == 0) return;
        var sprite = treeSprites[Random.Range(0, treeSprites.Length)];
        if (sprite == null) return;

        var go = new GameObject("T");
        go.transform.SetParent(_treeContainer);
        go.transform.position = new Vector3(tileX + 0.5f, tileY + 0.5f, 0f);
        float s = Random.Range(0.75f, 1.35f);
        go.transform.localScale = new Vector3(s, s, 1f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = sprite;
        sr.sortingOrder = 3;

        float shade = Random.Range(0.30f, 0.55f);
        sr.color = new Color(shade * 0.70f, shade * 0.80f, shade);
    }

    // ── River ─────────────────────────────────────────────────────────────────

    private void GenerateRiver()
    {
        if (riverTilemap == null || riverTiles == null || riverTiles.Length == 0) return;

        riverTilemap.ClearAllTiles();

        int ox = Mathf.RoundToInt(mapCenter.x - mapWidth  * 0.5f);
        int oy = Mathf.RoundToInt(mapCenter.y - mapHeight * 0.5f);

        int rx         = Mathf.RoundToInt(mapCenter.x) - 1;
        int bridgeMid  = Mathf.RoundToInt(mapCenter.y);
        int bridgeHalf = bridgeHeightTiles / 2;

        var positions = new List<Vector3Int>();
        var tiles     = new List<TileBase>();

        var bridgePositions = new List<Vector3Int>();
        var bridgeData      = new List<TileBase>();

        for (int col = rx; col <= rx + 1; col++)
        for (int y = oy; y < oy + mapHeight; y++)
        {
            bool isBridge = y >= bridgeMid - bridgeHalf && y <= bridgeMid + bridgeHalf - 1;
            if (isBridge)
            {
                if (bridgeTiles != null && bridgeTiles.Length > 0)
                {
                    bridgePositions.Add(new Vector3Int(col, y, 0));
                    bridgeData.Add(Pick(bridgeTiles));
                }
            }
            else
            {
                positions.Add(new Vector3Int(col, y, 0));
                tiles.Add(Pick(riverTiles));
            }
        }

        if (positions.Count > 0)
            riverTilemap.SetTiles(positions.ToArray(), tiles.ToArray());

        // Bridge tiles go on the floor tilemap (no TilemapCollider2D) so the player can walk across
        if (bridgePositions.Count > 0 && floorTilemap != null)
            floorTilemap.SetTiles(bridgePositions.ToArray(), bridgeData.ToArray());

        riverTilemap.CompressBounds();
    }

    // ── Night atmosphere ──────────────────────────────────────────────────────

    private void SetupNightAtmosphere()
    {
        var gl = globalLight;
        if (gl == null)
            foreach (var l in FindObjectsByType<Light2D>(FindObjectsSortMode.None))
                if (l.lightType == Light2D.LightType.Global) { gl = l; break; }

        if (gl != null)
        {
            gl.intensity = 0.05f;
            gl.color     = new Color(0.20f, 0.24f, 0.40f);
        }

        var moonGO = new GameObject("MoonLight");
        moonGO.transform.SetParent(transform);
        moonGO.transform.position = new Vector3(mapCenter.x, mapCenter.y, 0f);

        var moon = moonGO.AddComponent<Light2D>();
        moon.lightType             = Light2D.LightType.Point;
        moon.pointLightInnerRadius = moonInnerRadius;
        moon.pointLightOuterRadius = moonOuterRadius;
        moon.intensity             = moonIntensity;
        moon.color                 = moonColor;

        if (Camera.main != null)
            Camera.main.backgroundColor = new Color(0.01f, 0.01f, 0.04f);
    }

    // ── Tent spawning ─────────────────────────────────────────────────────────

    private void SpawnTents()
    {
        if (tentDefinitions == null || tentDefinitions.Length == 0) return;

        float innerLeft   = mapCenter.x - mapWidth  * 0.5f + 1.5f;
        float innerBottom = mapCenter.y - mapHeight * 0.5f + 1.5f;
        float innerW      = mapWidth  - 3f;
        float innerH      = mapHeight - 3f;

        for (int i = 0; i < tentDefinitions.Length; i++)
        {
            var def = tentDefinitions[i];
            if (def == null) continue;

            float wx  = innerLeft   + def.campPosition.x * innerW;
            float wy  = innerBottom + def.campPosition.y * innerH;
            var   pos = new Vector3(Mathf.Round(wx) + 0.5f, Mathf.Round(wy) + 0.5f, 0f);

            GameObject go = tentPrefab != null
                ? Instantiate(tentPrefab, pos, Quaternion.identity)
                : BuildProceduralTent(pos, def);

            go.name = $"Tent_{def.tentName}";

            Sprite purchased = purchasedSprites != null && i < purchasedSprites.Length ? purchasedSprites[i] : null;
            Sprite ghost     = ghostSprites     != null && i < ghostSprites.Length     ? ghostSprites[i]     : null;

            var tentObj = go.GetComponent<CampTentObject>() ?? go.AddComponent<CampTentObject>();
            tentObj.Init(def, purchased, ghost);

            if (go.GetComponent<Collider2D>() == null)
            {
                var col = go.AddComponent<CircleCollider2D>();
                col.radius    = 0.8f;
                col.isTrigger = true;
            }
        }
    }

    private static GameObject BuildProceduralTent(Vector3 pos, TentDefinition def)
    {
        bool owned = CampManager.Instance != null && CampManager.Instance.IsPurchased(def);

        var go = new GameObject();
        go.transform.position = pos;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = GetCachedSquareSprite();
        sr.color        = owned ? new Color(0.22f, 0.55f, 0.22f) : new Color(0.40f, 0.40f, 0.40f, 0.70f);
        sr.sortingOrder = 2;

        return go;
    }

    // ── Player spawning ───────────────────────────────────────────────────────

    private void SpawnPlayer()
    {
        // Spawn on the right bank — river occupies x = -1 and x = 0 in tile space
        var pos = new Vector3(mapCenter.x + 4f, mapCenter.y, 0f);

        GameObject player = playerPrefab != null
            ? Instantiate(playerPrefab, pos, Quaternion.identity)
            : BuildProceduralPlayer(pos);

        if (player.GetComponent<Collider2D>() == null)
        {
            var col = player.AddComponent<CircleCollider2D>();
            col.radius = 0.35f;
        }

        if (player.GetComponent<CampPlayerController>() == null)
            player.AddComponent<CampPlayerController>();
    }

    private static GameObject BuildProceduralPlayer(Vector3 pos)
    {
        var go = new GameObject("CampPlayer");
        go.transform.position = pos;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = GetCachedSquareSprite();
        sr.color        = new Color(1f, 0.85f, 0.1f);
        sr.sortingOrder = 5;

        var rb          = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.constraints  = RigidbodyConstraints2D.FreezeRotation;

        return go;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Sprite GetCachedSquareSprite()
    {
        if (_cachedSquareSprite != null) return _cachedSquareSprite;

        var tex    = new Texture2D(32, 32);
        var pixels = new Color[32 * 32];
        for (int p = 0; p < pixels.Length; p++) pixels[p] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        _cachedSquareSprite = Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 32f);
        return _cachedSquareSprite;
    }

    private TileBase Pick(TileBase[] arr) =>
        arr != null && arr.Length > 0 ? arr[Random.Range(0, arr.Length)] : null;

    private bool ValidateReferences()
    {
        bool ok = true;
        if (floorTilemap == null) { Debug.LogError("[CampMapGenerator] floorTilemap not assigned."); ok = false; }
        if (wallTilemap  == null) { Debug.LogError("[CampMapGenerator] wallTilemap not assigned.");  ok = false; }
        if (floorTiles   == null || floorTiles.Length == 0) { Debug.LogError("[CampMapGenerator] floorTiles empty."); ok = false; }
        if (wallTiles    == null || wallTiles.Length  == 0) { Debug.LogError("[CampMapGenerator] wallTiles empty.");  ok = false; }
        if (riverTilemap == null) Debug.LogWarning("[CampMapGenerator] riverTilemap not assigned — river skipped.");
        if (riverTiles   == null || riverTiles.Length == 0) Debug.LogWarning("[CampMapGenerator] riverTiles empty — river skipped.");
        if (bridgeTiles  == null || bridgeTiles.Length == 0) Debug.LogWarning("[CampMapGenerator] bridgeTiles empty — bridge gap will be invisible.");
        if (treeTilemap  == null) Debug.LogWarning("[CampMapGenerator] treeTilemap not assigned — forest skipped.");
        if (treeTiles    == null || treeTiles.Length  == 0) Debug.LogWarning("[CampMapGenerator] treeTiles empty — forest skipped.");
        return ok;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.15f);
        Gizmos.DrawCube(mapCenter, new Vector3(mapWidth, mapHeight, 0f));
        Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.8f);
        Gizmos.DrawWireCube(mapCenter, new Vector3(mapWidth, mapHeight, 0f));
    }
}
