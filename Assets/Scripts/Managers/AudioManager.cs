using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Survives scene loads. Manages one music AudioSource and one SFX AudioSource.
/// Volume is driven by PersistentDataManager.
/// Place on the MainMenuScene manager object.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource sfxSource;

    [Header("UI Sounds")]
    [SerializeField] private AudioClip buttonClickSfx;

    private void Awake()
    {
        if (Instance != null) { Destroy(this); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (musicSource == null)
        {
            musicSource             = gameObject.AddComponent<AudioSource>();
            musicSource.loop        = true;
            musicSource.playOnAwake = false;
        }
        if (sfxSource == null)
        {
            sfxSource               = gameObject.AddComponent<AudioSource>();
            sfxSource.loop          = false;
            sfxSource.playOnAwake   = false;
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        if (PersistentDataManager.Instance != null)
        {
            SetMusicVolume(PersistentDataManager.Instance.MusicVolume);
            SetSFXVolume(PersistentDataManager.Instance.SFXVolume);
        }
    }

    // ── Volume ────────────────────────────────────────────────────────────────

    public void SetMusicVolume(float v) => musicSource.volume = Mathf.Clamp01(v);
    public void SetSFXVolume(float v)   => sfxSource.volume   = Mathf.Clamp01(v);

    // ── Playback ──────────────────────────────────────────────────────────────

    public void PlayMusic(AudioClip clip)
    {
        if (clip == null || musicSource.clip == clip) return;
        musicSource.clip = clip;
        musicSource.Play();
    }

    public void StopMusic() => musicSource.Stop();

    public void PlaySFX(AudioClip clip)
    {
        if (clip == null) return;
        sfxSource.PlayOneShot(clip);
    }

    // ── Button sounds ─────────────────────────────────────────────────────────

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StartCoroutine(HookButtonsNextFrame());
    }

    private IEnumerator HookButtonsNextFrame()
    {
        // Wait one frame so procedural UIs built in Start() are ready
        yield return null;
        HookAllButtons();
    }

    private void HookAllButtons()
    {
        if (buttonClickSfx == null) return;
        foreach (Button btn in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            btn.onClick.AddListener(() => PlaySFX(buttonClickSfx));
    }
}
