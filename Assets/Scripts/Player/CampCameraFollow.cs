using UnityEngine;

/// <summary>
/// Smooth camera follow for the Camp scene.
/// Clamps to the walkable map bounds supplied by CampMapGenerator.
/// Attach to the Main Camera in CampScene.
/// </summary>
public class CampCameraFollow : MonoBehaviour
{
    [SerializeField] private float smoothSpeed   = 6f;
    [SerializeField] private float orthoSize     = 7f;
    [SerializeField] private CampMapGenerator mapGen;

    private Transform _target;
    private Camera    _cam;

    private void Awake()
    {
        _cam          = GetComponent<Camera>();
        if (_cam != null) _cam.orthographicSize = orthoSize;
    }

    private void Start()
    {
        // Find the player spawned by CampMapGenerator
        var ctrl = Object.FindAnyObjectByType<CampPlayerController>();
        if (ctrl != null) _target = ctrl.transform;
    }

    private void LateUpdate()
    {
        if (_target == null) return;

        Vector3 goal = new Vector3(_target.position.x, _target.position.y, transform.position.z);

        if (mapGen != null && _cam != null)
        {
            float halfH = _cam.orthographicSize;
            float halfW = halfH * _cam.aspect;

            float minX = mapGen.MapCenter.x - mapGen.MapWidth  * 0.5f + halfW + 1f;
            float maxX = mapGen.MapCenter.x + mapGen.MapWidth  * 0.5f - halfW - 1f;
            float minY = mapGen.MapCenter.y - mapGen.MapHeight * 0.5f + halfH + 1f;
            float maxY = mapGen.MapCenter.y + mapGen.MapHeight * 0.5f - halfH - 1f;

            // Only clamp if the map is actually larger than the view
            if (minX < maxX) goal.x = Mathf.Clamp(goal.x, minX, maxX);
            if (minY < maxY) goal.y = Mathf.Clamp(goal.y, minY, maxY);
        }

        transform.position = Vector3.Lerp(transform.position, goal, smoothSpeed * Time.deltaTime);
    }
}
