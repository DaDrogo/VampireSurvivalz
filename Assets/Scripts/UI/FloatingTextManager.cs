using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Spawns screen-space floating resource popups (+5 in the resource colour).
/// Text size is fixed in screen pixels — unaffected by camera zoom.
/// Add this component to any persistent scene object (e.g. the GameManager GameObject).
/// </summary>
public class FloatingTextManager : MonoBehaviour
{
    public static FloatingTextManager Instance { get; private set; }

    public enum ResourceKind { Wood, Metal }

    [Header("UI Theme")]
    [SerializeField] private UITheme _theme;

    [Header("Animation")]
    [SerializeField] private float duration    = 1.2f;
    [SerializeField] private float floatPixels = 80f;   // screen-space rise (px)
    [SerializeField] private float driftPixels = 20f;   // random horizontal drift (px)
    [SerializeField] private float fontSize    = 48f;

    [Header("Colours")]
    [SerializeField] private Color woodColor  = new Color(0.72f, 0.54f, 0.28f);
    [SerializeField] private Color metalColor = new Color(0.70f, 0.70f, 0.82f);

    private TMP_FontAsset _font;
    private Canvas        _canvas;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(this); return; }
        Instance = this;

        _font = _theme?.font != null
            ? _theme.font
            : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        BuildCanvas();
    }

    private void BuildCanvas()
    {
        GameObject go = new GameObject("FloatingTextCanvas");
        go.transform.SetParent(transform);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 150;
        CanvasScaler scaler        = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 1f;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Spawn(Vector3 worldPos, int amount, ResourceKind kind)
    {
        Color color  = kind == ResourceKind.Wood ? woodColor : metalColor;
        float xDrift = Random.Range(-driftPixels, driftPixels);
        StartCoroutine(Animate(worldPos, xDrift, $"+{amount}", color));
    }

    // ── Animation coroutine ───────────────────────────────────────────────────

    private IEnumerator Animate(Vector3 worldPos, float xDrift, string text, Color color)
    {
        GameObject go = new GameObject("FloatingText");
        go.transform.SetParent(_canvas.transform, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(200f, 80f);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

        var tmp       = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = fontSize;
        tmp.color     = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        if (_font != null) tmp.font = _font;

        Camera cam    = Camera.main;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = elapsed / duration;

            // Project world position to screen pixels each frame (follows moving source)
            Vector2 screenPos = cam != null
                ? (Vector2)cam.WorldToScreenPoint(worldPos)
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            float scaleFactor = Screen.height / 1080f;
            rt.anchoredPosition = new Vector2(
                (screenPos.x - Screen.width  * 0.5f) / scaleFactor + xDrift,
                (screenPos.y - Screen.height * 0.5f) / scaleFactor + floatPixels * t);

            // Scale punch on spawn
            float scaleMult = 1f;
            if      (t < 0.15f) scaleMult = Mathf.Lerp(1f, 1.4f, t / 0.15f);
            else if (t < 0.30f) scaleMult = Mathf.Lerp(1.4f, 1f, (t - 0.15f) / 0.15f);
            rt.localScale = Vector3.one * scaleMult;

            // Fade out in last 40%
            float alpha = t < 0.6f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.6f) / 0.4f);
            tmp.color = new Color(color.r, color.g, color.b, alpha);

            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(go);
    }
}
