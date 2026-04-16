using UnityEngine;

/// <summary>
/// Attached at runtime to every building placed by <see cref="BuildingManager"/>.
/// Frees the tile in the occupancy set when the GameObject is destroyed,
/// so the player can build again on that spot.
/// </summary>
public class PlacedBuilding : MonoBehaviour
{
    private Vector2Int _tile;

    public void Init(Vector2Int tile) => _tile = tile;

    private void OnDestroy()
    {
        BuildingManager.Instance?.FreeTile(_tile);
    }
}
