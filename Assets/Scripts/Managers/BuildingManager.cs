using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Defines one placeable building type.
/// Assign in the BuildingManager Inspector — index 0 = key 1, index 1 = key 2, etc.
/// </summary>
[Serializable]
public class BuildingDefinition
{
    public string       buildingName;
    public GameObject   prefab;
    [Tooltip("World-space size used for the OverlapBox placement check")]
    public Vector2      footprint    = Vector2.one;
    public int          woodCost;
    public int          metalCost;
}

/// <summary>
/// Press 1 / 2 / … to enter placement mode.
/// A ghost follows the cursor — green = valid, red = blocked or too expensive.
/// Left-click to place, Right-click or Escape to cancel.
/// Stays in placement mode after placing so you can build multiple in a row.
/// </summary>
public class BuildingManager : MonoBehaviour
{
    public static BuildingManager Instance { get; private set; }

    [Header("Buildings  (index 0 → key 1,  index 1 → key 2, …)")]
    [SerializeField] private BuildingDefinition[] buildings;

    [Header("Placement")]
    [Tooltip("Layers that block placement — include your structure and wall layers")]
    [SerializeField] private LayerMask blockingLayers;
    [SerializeField] private Color validColor   = new Color(0.2f, 1f,  0.2f, 0.45f);
    [SerializeField] private Color invalidColor = new Color(1f,  0.2f, 0.2f, 0.45f);

    // ── State ─────────────────────────────────────────────────────────────────

    public bool IsPlacing => _isPlacing;

    private BuildingDefinition _active;
    private GameObject         _ghost;
    private SpriteRenderer     _ghostSR;
    private bool               _isPlacing;

    private static readonly Key[] Hotkeys =
        { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        // Don't process input during Game Over
        if (GameManager.Instance?.CurrentState == GameManager.GameState.GameOver) return;

        HandleHotkeys();

        if (!_isPlacing) return;

        MoveGhost();
        UpdateGhostColor();

        if (Mouse.current.leftButton.wasPressedThisFrame)  TryPlace();
        if (Mouse.current.rightButton.wasPressedThisFrame) CancelPlacement();
        if (Keyboard.current.escapeKey.wasPressedThisFrame) CancelPlacement();
    }

    // ── Hotkeys ───────────────────────────────────────────────────────────────

    private void HandleHotkeys()
    {
        for (int i = 0; i < Mathf.Min(buildings.Length, Hotkeys.Length); i++)
        {
            if (!Keyboard.current[Hotkeys[i]].wasPressedThisFrame) continue;

            // Pressing the active key again cancels placement (toggle)
            if (_isPlacing && _active == buildings[i])
                CancelPlacement();
            else
                BeginPlacement(buildings[i]);
            return;
        }
    }

    // ── Placement flow ────────────────────────────────────────────────────────

    private void BeginPlacement(BuildingDefinition def)
    {
        if (def.prefab == null)
        {
            Debug.LogWarning($"BuildingManager: '{def.buildingName}' has no prefab assigned.");
            return;
        }

        CancelPlacement();   // destroy any previous ghost first
        _active    = def;
        _isPlacing = true;
        _ghost     = SpawnGhost(def);
    }

    private void TryPlace()
    {
        Vector2 pos = MouseWorldPos();

        if (!CanAfford(_active))
        {
            Debug.Log($"BuildingManager: not enough resources to build {_active.buildingName}  " +
                      $"(need {_active.woodCost} Wood / {_active.metalCost} Metal).");
            return;
        }

        if (!IsAreaClear(pos, _active.footprint))
        {
            Debug.Log($"BuildingManager: placement blocked at {pos}.");
            return;
        }

        Instantiate(_active.prefab, pos, Quaternion.identity);
        SpendResources(_active);
        // Intentionally stay in placement mode so the player can keep building.
    }

    public void CancelPlacement()
    {
        if (_ghost != null) Destroy(_ghost);
        _ghost     = null;
        _ghostSR   = null;
        _active    = null;
        _isPlacing = false;
    }

    // ── Ghost ─────────────────────────────────────────────────────────────────

    private GameObject SpawnGhost(BuildingDefinition def)
    {
        var go = new GameObject($"Ghost_{def.buildingName}");
        go.layer = 2; // Ignore Raycast — must not hit our own OverlapBox

        _ghostSR              = go.AddComponent<SpriteRenderer>();
        _ghostSR.sortingOrder = 20;

        // Mirror the prefab's sprite and scale; fall back to a plain square
        var prefabSR = def.prefab.GetComponentInChildren<SpriteRenderer>();
        if (prefabSR != null)
        {
            _ghostSR.sprite         = prefabSR.sprite;
            go.transform.localScale = def.prefab.transform.localScale;
        }
        else
        {
            _ghostSR.sprite         = WhiteSquareSprite();
            go.transform.localScale = new Vector3(def.footprint.x, def.footprint.y, 1f);
        }

        return go;
    }

    private void MoveGhost() => _ghost.transform.position = MouseWorldPos();

    private void UpdateGhostColor()
    {
        Vector2 pos   = MouseWorldPos();
        bool    valid = CanAfford(_active) && IsAreaClear(pos, _active.footprint);
        _ghostSR.color = valid ? validColor : invalidColor;
    }

    // ── Validation ────────────────────────────────────────────────────────────

    private bool CanAfford(BuildingDefinition def)
    {
        if (ResourceManager.Instance == null) return false;
        return ResourceManager.Instance.Wood  >= def.woodCost
            && ResourceManager.Instance.Metal >= def.metalCost;
    }

    /// <summary>
    /// Returns true when no collider on <see cref="blockingLayers"/> overlaps
    /// the placement footprint. The box is shrunk by 10 % so objects can be
    /// placed flush against each other without being blocked by their own edges.
    /// </summary>
    private bool IsAreaClear(Vector2 center, Vector2 size)
    {
        Collider2D hit = Physics2D.OverlapBox(center, size * 0.9f, 0f, blockingLayers);
        return hit == null;
    }

    private static void SpendResources(BuildingDefinition def)
    {
        ResourceManager.Instance.AddResource("Wood",  -def.woodCost);
        ResourceManager.Instance.AddResource("Metal", -def.metalCost);
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private static Vector2 MouseWorldPos()
    {
        Vector3 p = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        p.z = 0f;
        return p;
    }

    private static Sprite WhiteSquareSprite()
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }

    // ── Editor gizmo ─────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (!_isPlacing || _active == null) return;
        Vector2 pos   = MouseWorldPos();
        bool    valid = CanAfford(_active) && IsAreaClear(pos, _active.footprint);
        Gizmos.color  = valid ? new Color(0f, 1f, 0f, 0.25f) : new Color(1f, 0f, 0f, 0.25f);
        Gizmos.DrawWireCube(pos, _active.footprint);
    }
}
