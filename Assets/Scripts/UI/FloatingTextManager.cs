using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Spawns world-space floating resource popups (+5 in the resource colour).
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
    [SerializeField] private float floatHeight = 1.5f;
    [SerializeField] private float driftRange  = 0.25f;
    [SerializeField] private float worldScale  = 0.025f;
    [SerializeField] private float fontSize    = 36f;

    [Header("Colours")]
    [SerializeField] private Color woodColor  = new Color(0.72f, 0.54f, 0.28f);
    [SerializeField] private Color metalColor = new Color(0.70f, 0.70f, 0.82f);

    private TMP_FontAsset _font;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(this); return; }
        Instance = this;

        _font = _theme?.font != null
            ? _theme.font
            : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Spawn(Vector3 worldPos, int amount, ResourceKind kind)
    {
        Color color = kind == ResourceKind.Wood ? woodColor : metalColor;
        float xDrift = Random.Range(-driftRange, driftRange);
        StartCoroutine(Animate(worldPos + new Vector3(xDrift, 0f, 0f), $"+{amount}", color));
    }

    // ── Animation coroutine ───────────────────────────────────────────────────

    private IEnumerator Animate(Vector3 startPos, string text, Color color)
    {
        GameObject go = new GameObject("FloatingText");
        go.transform.position   = startPos;
        go.transform.localScale = Vector3.one * worldScale;

        var tmp            = go.AddComponent<TextMeshPro>();
        tmp.text           = text;
        tmp.fontSize       = fontSize;
        tmp.color          = color;
        tmp.alignment      = TextAlignmentOptions.Center;
        tmp.fontStyle      = FontStyles.Bold;
        tmp.sortingOrder   = 10;
        if (_font != null) tmp.font = _font;

        RectTransform rt = tmp.GetComponent<RectTransform>();
        rt.pivot = new Vector2(0.5f, 0.5f);

        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = elapsed / duration;

            go.transform.position = startPos + new Vector3(0f, floatHeight * t, 0f);

            // Quick scale punch at spawn (1.0 → 1.4 → 1.0 over first 30% of lifetime)
            float scaleMult = 1f;
            if (t < 0.15f)       scaleMult = Mathf.Lerp(1f, 1.4f, t / 0.15f);
            else if (t < 0.30f)  scaleMult = Mathf.Lerp(1.4f, 1f, (t - 0.15f) / 0.15f);
            go.transform.localScale = Vector3.one * worldScale * scaleMult;

            // Hold full opacity, then fade in last 40%
            float alpha = t < 0.6f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.6f) / 0.4f);
            tmp.color = new Color(color.r, color.g, color.b, alpha);

            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(go);
    }
}
