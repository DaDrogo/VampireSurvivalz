using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Survives scene loads.  Manages two music AudioSources (day + night crossfade),
/// one SFX source, and global button-click sounds.
///
/// Menu music:  call PlayMusic(clip) — loops on the day source at full volume.
/// Game music:  call StartGameMusic() — runs shuffled day/night playlists whose
///              volumes crossfade continuously based on DayNightManager.NightT.
///              Both playlists keep playing (silently when inactive) so each
///              resumes exactly where it left off when its phase returns.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [SerializeField] private AudioSource musicSource;   // day / menu source
    [SerializeField] private AudioSource sfxSource;

    [Header("Menu Playlist")]
    [SerializeField] private AudioClip[] menuPlaylist;

    [Header("Game Playlists")]
    [SerializeField] private AudioClip[] dayPlaylist;
    [SerializeField] private AudioClip[] nightPlaylist;
    [Tooltip("Seconds to fade game music in from silence when a game starts.")]
    [SerializeField] private float musicFadeInDuration = 2f;

    [Header("Game State SFX")]
    [SerializeField] private AudioClip waveStartSfx;
    [SerializeField] private AudioClip waveSurvivedSfx;
    [SerializeField] private AudioClip gameOverSfx;
    [SerializeField] private AudioClip victorySfx;

    [Header("End Screen Music")]
    [SerializeField] private AudioClip gameOverMusic;
    [SerializeField] private AudioClip victoryMusic;

    [Header("Camp Playlist")]
    [SerializeField] private AudioClip[] campPlaylist;
    [Tooltip("Seconds to crossfade between menu/camp music.")]
    [SerializeField] private float campFadeDuration = 1f;

    [Header("Building Sounds")]
    [SerializeField] private BuildingSounds buildingSounds;

    [Header("UI Sounds")]
    [SerializeField] private AudioClip buttonClickSfx;

    // ── Private ───────────────────────────────────────────────────────────────

    private AudioSource      _nightMusicSource;
    private AudioSource      _endScreenSource;
    private float            _masterMusicVolume = 0.8f;

    private bool             _gameplayMusicActive;
    private bool             _menuPlaylistActive;
    private bool             _campPlaylistActive;
    private float            _fadeInTimer;

    private ShuffledPlaylist _menuList;
    private ShuffledPlaylist _campList;
    private ShuffledPlaylist _dayList;
    private ShuffledPlaylist _nightList;

    private Coroutine        _crossfadeCoroutine;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(this); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (musicSource == null)
        {
            musicSource             = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
        }

        // Night source is always created at runtime (not a serialised field)
        _nightMusicSource             = gameObject.AddComponent<AudioSource>();
        _nightMusicSource.loop        = false;
        _nightMusicSource.playOnAwake = false;
        _nightMusicSource.volume      = 0f;

        _endScreenSource             = gameObject.AddComponent<AudioSource>();
        _endScreenSource.loop        = true;
        _endScreenSource.playOnAwake = false;
        _endScreenSource.volume      = _masterMusicVolume;

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

    private void Update()
    {
        if (_menuPlaylistActive && !_gameplayMusicActive)
        {
            _menuList?.Tick(musicSource);
            return;
        }

        if (_campPlaylistActive && !_gameplayMusicActive)
        {
            _campList?.Tick(musicSource);
            return;
        }

        if (!_gameplayMusicActive) return;

        // Fade-in ramp (0 → 1 over fadeInDuration)
        if (_fadeInTimer < musicFadeInDuration)
            _fadeInTimer = Mathf.Min(_fadeInTimer + Time.unscaledDeltaTime, musicFadeInDuration);
        float fadeScale = musicFadeInDuration > 0f ? _fadeInTimer / musicFadeInDuration : 1f;

        float nightT = GetNightT();
        musicSource.volume       = _masterMusicVolume * (1f - nightT) * fadeScale;
        _nightMusicSource.volume = _masterMusicVolume *       nightT  * fadeScale;

        // Advance tracks when the current clip ends naturally
        _dayList?.Tick(musicSource);
        _nightList?.Tick(_nightMusicSource);
    }

    // ── Volume ────────────────────────────────────────────────────────────────

    public void SetMusicVolume(float v)
    {
        _masterMusicVolume = Mathf.Clamp01(v);
        // During gameplay Update drives the volumes; in menu set directly.
        if (!_gameplayMusicActive)
            musicSource.volume = _masterMusicVolume;
        if (_endScreenSource.isPlaying)
            _endScreenSource.volume = _masterMusicVolume;
    }

    public void SetSFXVolume(float v) => sfxSource.volume = Mathf.Clamp01(v);

    // ── Menu playback ─────────────────────────────────────────────────────────

    /// <summary>Plays a looping clip on the day/menu source at master volume.</summary>
    public void PlayMusic(AudioClip clip)
    {
        if (clip == null || musicSource.clip == clip) return;
        musicSource.loop   = true;
        musicSource.clip   = clip;
        musicSource.volume = _masterMusicVolume;
        musicSource.Play();
    }

    public void StopMusic() => musicSource.Stop();

    /// <summary>
    /// Plays a shuffled playlist on the menu source.
    /// Pass clips to override; omit to use the Inspector-assigned menuPlaylist.
    /// </summary>
    public void PlayMenuPlaylist(AudioClip[] clips = null)
    {
        AudioClip[] list = (clips != null && clips.Length > 0) ? clips : menuPlaylist;
        if (list == null || list.Length == 0) return;

        if (_crossfadeCoroutine != null) { StopCoroutine(_crossfadeCoroutine); _crossfadeCoroutine = null; }
        _gameplayMusicActive = false;
        _campPlaylistActive  = false;
        _menuPlaylistActive  = true;

        musicSource.loop   = false;
        musicSource.Stop();
        musicSource.volume = _masterMusicVolume;

        _menuList = new ShuffledPlaylist(list);
        _menuList.StartOn(musicSource);
    }

    // ── Camp playlist ─────────────────────────────────────────────────────────

    /// <summary>Crossfades from whatever is playing to the camp playlist.</summary>
    public void PlayCampPlaylist()
    {
        if (campPlaylist == null || campPlaylist.Length == 0) return;
        if (_crossfadeCoroutine != null) StopCoroutine(_crossfadeCoroutine);
        _crossfadeCoroutine = StartCoroutine(CrossfadeTo(campPlaylist, camp: true));
    }

    private IEnumerator CrossfadeTo(AudioClip[] clips, bool camp)
    {
        // Fade out
        float startVol = musicSource.volume;
        for (float t = 0f; t < campFadeDuration; t += Time.unscaledDeltaTime)
        {
            musicSource.volume = Mathf.Lerp(startVol, 0f, t / campFadeDuration);
            yield return null;
        }
        musicSource.Stop();
        _nightMusicSource.Stop();
        _gameplayMusicActive = false;
        _menuPlaylistActive  = !camp;
        _campPlaylistActive  = camp;

        var list = new ShuffledPlaylist(clips);
        if (camp) _campList  = list;
        else      _menuList  = list;
        list.StartOn(musicSource);

        // Fade in
        for (float t = 0f; t < campFadeDuration; t += Time.unscaledDeltaTime)
        {
            musicSource.volume = Mathf.Lerp(0f, _masterMusicVolume, t / campFadeDuration);
            yield return null;
        }
        musicSource.volume  = _masterMusicVolume;
        _crossfadeCoroutine = null;
    }

    // ── Game playlist ─────────────────────────────────────────────────────────

    /// <summary>
    /// Call from GameManager.StartGame().
    /// Stops any menu track, initialises fresh shuffled playlists for this run,
    /// and starts both sources (volumes are driven by Update + DayNightManager).
    /// </summary>
    public void StartGameMusic()
    {
        if (_crossfadeCoroutine != null) { StopCoroutine(_crossfadeCoroutine); _crossfadeCoroutine = null; }
        _campPlaylistActive = false;
        // Stop menu loop and silence both sources before starting playlists
        musicSource.loop = false;
        musicSource.Stop();
        _nightMusicSource.Stop();

        _dayList  = new ShuffledPlaylist(dayPlaylist);
        _nightList = new ShuffledPlaylist(nightPlaylist);

        // Both sources start playing immediately; Update() controls their volumes.
        // Playing both simultaneously means each playlist resumes exactly where it
        // left off when its phase returns — no explicit save/restore needed.
        _dayList.StartOn(musicSource);
        _nightList.StartOn(_nightMusicSource);

        _fadeInTimer         = 0f;
        _menuPlaylistActive  = false;
        _gameplayMusicActive = true;
    }

    /// <summary>
    /// Call when returning to the main menu.
    /// Stops playlist sources; MainMenuManager.Start() will restore menu music.
    /// </summary>
    public void StopGameMusic()
    {
        _gameplayMusicActive = false;
        musicSource.Stop();
        _nightMusicSource.Stop();
        musicSource.volume       = _masterMusicVolume;
        _nightMusicSource.volume = 0f;
    }

    // ── SFX ───────────────────────────────────────────────────────────────────

    public void PlaySFX(AudioClip clip)
    {
        if (clip == null) return;
        sfxSource.PlayOneShot(clip);
    }

    public void PlayWaveStart()    => PlaySFX(waveStartSfx);
    public void PlayWaveSurvived() => PlaySFX(waveSurvivedSfx);
    public void PlayGameOver()     => PlaySFX(gameOverSfx);
    public void PlayVictory()      => PlaySFX(victorySfx);

    public void PlayGameOverMusic()
    {
        StopGameMusic();
        if (gameOverMusic == null) return;
        _endScreenSource.volume = _masterMusicVolume;
        _endScreenSource.clip   = gameOverMusic;
        _endScreenSource.Play();
    }

    public void PlayVictoryMusic()
    {
        StopGameMusic();
        if (victoryMusic == null) return;
        _endScreenSource.volume = _masterMusicVolume;
        _endScreenSource.clip   = victoryMusic;
        _endScreenSource.Play();
    }

    public void StopEndScreenMusic() => _endScreenSource.Stop();

    public void PlayBuildingBuilt()     => PlaySFX(buildingSounds?.built);
    public void PlayBuildingRepaired()  => PlaySFX(buildingSounds?.repaired);
    public void PlayBuildingDestroyed() => PlaySFX(buildingSounds?.destroyed);
    public void PlayBuildingHit()       => PlaySFX(buildingSounds?.hit);

    // ── Button sounds ─────────────────────────────────────────────────────────

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "CampScene")
        {
            // Crossfade from whatever is playing to the camp playlist
            if (_gameplayMusicActive) StopGameMusic();
            PlayCampPlaylist();
        }
        else if (scene.name == "MainMenuScene")
        {
            // Stop game or camp music — MainMenuManager.Start() will restart menu music
            if (_gameplayMusicActive) StopGameMusic();
            if (_campPlaylistActive)
            {
                if (_crossfadeCoroutine != null) { StopCoroutine(_crossfadeCoroutine); _crossfadeCoroutine = null; }
                _campPlaylistActive = false;
                musicSource.Stop();
                musicSource.volume = _masterMusicVolume;
            }
        }

        StartCoroutine(HookButtonsNextFrame());
    }

    private IEnumerator HookButtonsNextFrame()
    {
        yield return null;
        HookAllButtons();
    }

    private void HookAllButtons()
    {
        if (buttonClickSfx == null) return;
        foreach (Button btn in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            btn.onClick.AddListener(() => PlaySFX(buttonClickSfx));
    }

    // ── NightT (mirrors DayNightManager.NightT without coupling) ─────────────

    private static float GetNightT()
    {
        DayNightManager dm = DayNightManager.Instance;
        if (dm == null) return 0f;
        return dm.CurrentPhase switch
        {
            DayNightManager.Phase.Night          => 1f,
            DayNightManager.Phase.DuskTransition => dm.PhaseProgress,
            DayNightManager.Phase.DawnTransition => 1f - dm.PhaseProgress,
            _                                    => 0f
        };
    }

    // ── ShuffledPlaylist ──────────────────────────────────────────────────────

    /// <summary>
    /// Tracks a shuffled playback order for an array of clips.
    /// Calling Tick() each frame auto-advances when a clip ends.
    /// Re-shuffles each time the full list loops so every cycle has a fresh order.
    /// </summary>
    private sealed class ShuffledPlaylist
    {
        private readonly AudioClip[] _clips;
        private readonly int[]       _order;
        private int                  _cursor;

        public ShuffledPlaylist(AudioClip[] clips)
        {
            _clips  = (clips != null && clips.Length > 0) ? clips : System.Array.Empty<AudioClip>();
            _order  = new int[_clips.Length];
            for (int i = 0; i < _order.Length; i++) _order[i] = i;
            Shuffle();
        }

        /// <summary>Starts the first track on <paramref name="src"/>.</summary>
        public void StartOn(AudioSource src)
        {
            if (_clips.Length == 0) return;
            src.clip = _clips[_order[_cursor]];
            src.Play();
        }

        /// <summary>
        /// Called every frame.  When the current clip finishes playing, advances
        /// to the next track.  Re-shuffles when the list wraps around so each
        /// cycle has a different order.
        /// </summary>
        public void Tick(AudioSource src)
        {
            if (_clips.Length == 0 || src.isPlaying) return;

            _cursor++;
            if (_cursor >= _clips.Length)
            {
                _cursor = 0;
                Shuffle();
            }

            src.clip = _clips[_order[_cursor]];
            src.Play();
        }

        private void Shuffle()
        {
            for (int i = _order.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }
        }
    }
}
