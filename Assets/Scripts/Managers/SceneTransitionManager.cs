using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Survives scene loads. Provides a fade-to-black transition between scenes.
/// Place on the MainMenuScene manager object.
/// </summary>
public class SceneTransitionManager : MonoBehaviour
{
    public static SceneTransitionManager Instance { get; private set; }

    [SerializeField] private float fadeDuration = 0.35f;

    private Image _fadeImage;
    private bool  _isTransitioning;

    private void Awake()
    {
        if (Instance != null) { Destroy(this); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildFadeOverlay();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Fade out → load scene → fade in.</summary>
    public void LoadScene(string sceneName)
    {
        if (!_isTransitioning)
            StartCoroutine(TransitionRoutine(sceneName));
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private IEnumerator TransitionRoutine(string sceneName)
    {
        _isTransitioning         = true;
        _fadeImage.raycastTarget = true;

        yield return Fade(0f, 1f);

        // Always restore timescale before loading (handles coming from a paused state)
        Time.timeScale = 1f;

        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
        while (!load.isDone) yield return null;

        yield return Fade(1f, 0f);

        _fadeImage.raycastTarget = false;
        _isTransitioning         = false;
    }

    private IEnumerator Fade(float from, float to)
    {
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            _fadeImage.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, elapsed / fadeDuration));
            yield return null;
        }
        _fadeImage.color = new Color(0f, 0f, 0f, to);
    }

    private void BuildFadeOverlay()
    {
        GameObject canvasGO     = new GameObject("FadeCanvas");
        canvasGO.transform.SetParent(transform);
        DontDestroyOnLoad(canvasGO);

        Canvas canvas           = canvasGO.AddComponent<Canvas>();
        canvas.renderMode       = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder     = 999;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        GameObject imgGO        = new GameObject("FadeImage");
        imgGO.transform.SetParent(canvasGO.transform, false);
        RectTransform rt        = imgGO.AddComponent<RectTransform>();
        rt.anchorMin            = Vector2.zero;
        rt.anchorMax            = Vector2.one;
        rt.offsetMin            = Vector2.zero;
        rt.offsetMax            = Vector2.zero;

        _fadeImage              = imgGO.AddComponent<Image>();
        _fadeImage.color        = new Color(0f, 0f, 0f, 0f);
        _fadeImage.raycastTarget = false;
    }
}
