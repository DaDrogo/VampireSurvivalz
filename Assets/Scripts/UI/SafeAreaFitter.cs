using UnityEngine;

/// <summary>
/// Adjusts this RectTransform's anchors every frame to match Screen.safeArea,
/// keeping UI elements away from notches, status bars, and system gesture areas.
/// Attach to a full-stretch child of any ScreenSpaceOverlay Canvas.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    private RectTransform _rt;
    private Rect          _lastSafeArea = Rect.zero;

    private void Awake()
    {
        _rt = GetComponent<RectTransform>();
        Apply();
    }

    private void Update()
    {
        if (Screen.safeArea != _lastSafeArea)
            Apply();
    }

    private void Apply()
    {
        Rect sa = Screen.safeArea;
        _lastSafeArea = sa;

        var anchorMin = new Vector2(sa.x / Screen.width,  sa.y / Screen.height);
        var anchorMax = new Vector2((sa.x + sa.width) / Screen.width,
                                   (sa.y + sa.height) / Screen.height);
        _rt.anchorMin = anchorMin;
        _rt.anchorMax = anchorMax;
        _rt.offsetMin = _rt.offsetMax = Vector2.zero;
    }
}
