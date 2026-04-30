using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Scene-level singleton. Runs a day/night cycle independent of the wave system.
///
/// Cycle layout (totals: day = 300 s, night = 120 s):
///   Full Day  270 s  → DuskTransition 30 s → Full Night 90 s → DawnTransition 30 s → repeat
///
/// Effects:
///   Day   — player gains a speed bonus (ramps in/out during transitions).
///   Night — all living enemies gain a temporary speed + damage buff (ramps with transition).
///           At the end of each full night, every enemy (living and future spawns) gains
///           a small permanent stat multiplier that stacks each cycle.
///
/// Place on the GameManager's scene object (or any object that lives for the whole game).
/// </summary>
public class DayNightManager : MonoBehaviour
{
    public static DayNightManager Instance { get; private set; }

    public void SetTheme(UITheme theme) { _theme = theme; }

    public enum Phase { Day, DuskTransition, Night, DawnTransition }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("UI Theme")]
    [SerializeField] private UITheme _theme;

    [Header("Durations (seconds)")]
    [Tooltip("Full-brightness day before dusk begins.")]
    [SerializeField] private float fullDayDuration    = 270f;
    [Tooltip("Crossfade window between day and night (and back).")]
    [SerializeField] private float transitionDuration = 30f;
    [Tooltip("Full-night duration before dawn begins.")]
    [SerializeField] private float fullNightDuration  = 90f;

    [Header("Night Enemy Buffs (at full night)")]
    [SerializeField] private float nightEnemySpeedMult  = 1.30f;
    [SerializeField] private float nightEnemyDamageMult = 1.50f;

    [Header("Day Player Buff (at full day)")]
    [SerializeField] private float dayPlayerSpeedMult = 1.25f;

    [Header("Permanent Buff — stacks each cycle")]
    [SerializeField] private float permanentBuffPerCycle = 0.05f;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired when the active phase changes (Day / transitions / Night).</summary>
    public event Action<Phase> OnPhaseChanged;

    /// <summary>Fired once when 10 seconds remain in DuskTransition (vampire warning).</summary>
    public event Action OnDuskWarning;

    /// <summary>
    /// Fired every frame with the player's current speed multiplier (1 = base,
    /// >1 during day).  PlayerController subscribes to this.
    /// </summary>
    public event Action<float> OnPlayerSpeedMultChanged;

    // ── Public state ──────────────────────────────────────────────────────────

    public Phase CurrentPhase  { get; private set; } = Phase.Day;
    public float PhaseProgress { get; private set; } = 0f;   // 0–1 within current phase
    public int   CycleCount    { get; private set; } = 0;

    // ── Private ───────────────────────────────────────────────────────────────

    private float _phaseTimer;
    private float _accumulatedPermanentBonus = 0f;  // total bonus added so far (e.g. 0.10 after 2 cycles)
    private bool  _duskWarnFired = false;

    private readonly List<Enemy> _registeredEnemies = new();

    // Visual
    private Image            _overlayImage;
    private Image            _cycleArc;
    private Image            _phaseImage;       // background of the widget
    private Image            _dayNightIcon;     // phase icon inside the strip
    private Image            _dimOverlay;
    private TextMeshProUGUI  _phaseNameLabel;
    private TextMeshProUGUI  _timeLabel;

    private static readonly Color DayOverlay   = new Color(1f,    0.95f, 0.80f, 0.00f);
    private static readonly Color NightOverlay = new Color(0.04f, 0.04f, 0.20f, 0.45f);
    private static readonly Color ArcDay       = new Color(1.00f, 0.85f, 0.20f, 0.90f);
    private static readonly Color ArcNight     = new Color(0.40f, 0.55f, 1.00f, 0.90f);

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(this); return; }
        Instance = this;
        BuildVisuals();
        EnterPhase(Phase.Day);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        _phaseTimer += Time.deltaTime;
        PhaseProgress = Mathf.Clamp01(_phaseTimer / CurrentPhaseDuration());

        UpdateBuffs();
        UpdateVisuals();

        // Fire dusk warning when 10s remain in DuskTransition (vampire incoming)
        if (CurrentPhase == Phase.DuskTransition && !_duskWarnFired
            && transitionDuration - _phaseTimer <= 10f)
        {
            _duskWarnFired = true;
            OnDuskWarning?.Invoke();
        }

        if (_phaseTimer >= CurrentPhaseDuration())
            AdvancePhase();
    }

    // ── Enemy registry ────────────────────────────────────────────────────────

    /// <summary>
    /// Called by Enemy.OnEnable so newly (re-)activated enemies immediately receive
    /// the current permanent and night buffs.
    /// </summary>
    public void RegisterEnemy(Enemy e)
    {
        if (_registeredEnemies.Contains(e)) return;
        _registeredEnemies.Add(e);

        // Apply permanent buff accumulated so far
        if (_accumulatedPermanentBonus > 0f)
            e.AddPermanentCycleBuff(_accumulatedPermanentBonus);

        // Apply current night buff immediately (important mid-night spawns)
        float t = NightT();
        if (t > 0f)
            e.ApplyNightBuff(
                Mathf.Lerp(1f, nightEnemySpeedMult,  t),
                Mathf.Lerp(1f, nightEnemyDamageMult, t));
    }

    /// <summary>Called by Enemy.OnDisable (pooled) or OnDestroy.</summary>
    public void UnregisterEnemy(Enemy e) => _registeredEnemies.Remove(e);

    // ── Phase logic ───────────────────────────────────────────────────────────

    private float CurrentPhaseDuration() => CurrentPhase switch
    {
        Phase.Day            => fullDayDuration,
        Phase.DuskTransition => transitionDuration,
        Phase.Night          => fullNightDuration,
        Phase.DawnTransition => transitionDuration,
        _                    => fullDayDuration
    };

    /// <summary>Immediately jumps to the next phase (Day → Dusk → Night → Dawn → Day).</summary>
    public void SkipToNextPhase() => AdvancePhase();

    private void AdvancePhase()
    {
        if (CurrentPhase == Phase.DawnTransition)
        {
            // Full cycle complete — apply permanent buff to all living and future enemies
            CycleCount++;
            _accumulatedPermanentBonus += permanentBuffPerCycle;
            foreach (Enemy e in _registeredEnemies)
                e.AddPermanentCycleBuff(permanentBuffPerCycle);
        }

        Phase next = CurrentPhase switch
        {
            Phase.Day            => Phase.DuskTransition,
            Phase.DuskTransition => Phase.Night,
            Phase.Night          => Phase.DawnTransition,
            Phase.DawnTransition => Phase.Day,
            _                    => Phase.Day
        };
        EnterPhase(next);
    }

    private void EnterPhase(Phase phase)
    {
        CurrentPhase   = phase;
        _phaseTimer    = 0f;
        PhaseProgress  = 0f;
        _duskWarnFired = false;
        OnPhaseChanged?.Invoke(phase);
    }

    // ── 0–1 night intensity (0 = full day, 1 = full night) ───────────────────

    private float NightT() => CurrentPhase switch
    {
        Phase.Night          => 1f,
        Phase.DuskTransition => PhaseProgress,
        Phase.DawnTransition => 1f - PhaseProgress,
        _                    => 0f
    };

    private float DayT() => 1f - NightT();

    // ── Buff application ──────────────────────────────────────────────────────

    private void UpdateBuffs()
    {
        float nightT     = NightT();
        float speedMult  = Mathf.Lerp(1f, nightEnemySpeedMult,  nightT);
        float damageMult = Mathf.Lerp(1f, nightEnemyDamageMult, nightT);

        // Iterate a copy so an enemy dying mid-loop doesn't break iteration
        for (int i = _registeredEnemies.Count - 1; i >= 0; i--)
        {
            Enemy e = _registeredEnemies[i];
            if (e == null) { _registeredEnemies.RemoveAt(i); continue; }

            if (nightT > 0f)
                e.ApplyNightBuff(speedMult, damageMult);
            else
                e.ClearNightBuff();
        }

        // Player speed
        float playerMult = Mathf.Lerp(1f, dayPlayerSpeedMult, DayT());
        OnPlayerSpeedMultChanged?.Invoke(playerMult);
    }

    // ── Visuals ───────────────────────────────────────────────────────────────

    private void UpdateVisuals()
    {
        float nightT = NightT();

        // Screen overlay
        if (_overlayImage != null)
            _overlayImage.color = Color.Lerp(DayOverlay, NightOverlay, nightT);

        // Arc fill: progress within the full cycle, clockwise
        if (_cycleArc != null)
        {
            float totalCycle     = fullDayDuration + transitionDuration * 2f + fullNightDuration;
            _cycleArc.fillAmount = 1f - (CycleElapsed() / totalCycle);
            _cycleArc.color      = Color.Lerp(ArcDay, ArcNight, nightT);
        }

        // Phase icon swap
        if (_dayNightIcon != null)
        {
            Sprite phaseSprite = CurrentPhase switch
            {
                Phase.Day            => _theme?.dayNightDay,
                Phase.DuskTransition => _theme?.dayNightDusk,
                Phase.Night          => _theme?.dayNightNight,
                Phase.DawnTransition => _theme?.dayNightDawn,
                _                    => null
            };

            if (phaseSprite != null) { _dayNightIcon.sprite = phaseSprite; _dayNightIcon.color = Color.white; }
            else _dayNightIcon.color = Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(0.4f, 0.5f, 1f), nightT);
        }

        // Phase name colour shifts day→night
        if (_phaseNameLabel != null)
            _phaseNameLabel.color = Color.Lerp(
                new Color(1f, 0.90f, 0.55f),
                new Color(0.65f, 0.75f, 1.00f),
                nightT);

        // Dim overlay: deepen slightly at night so text stays readable
        if (_dimOverlay != null)
            _dimOverlay.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.30f, 0.55f, nightT));

        // Phase name text
        if (_phaseNameLabel != null)
            _phaseNameLabel.text = CurrentPhase switch
            {
                Phase.Day            => "Day",
                Phase.DuskTransition => "Dusk",
                Phase.Night          => "Night",
                Phase.DawnTransition => "Dawn",
                _                    => ""
            };

        // Time remaining
        if (_timeLabel != null)
        {
            float remaining = CurrentPhaseDuration() - _phaseTimer;
            _timeLabel.text = $"{Mathf.CeilToInt(remaining)}s";
        }
    }

    private float CycleElapsed() => CurrentPhase switch
    {
        Phase.Day            => _phaseTimer,
        Phase.DuskTransition => fullDayDuration + _phaseTimer,
        Phase.Night          => fullDayDuration + transitionDuration + _phaseTimer,
        Phase.DawnTransition => fullDayDuration + transitionDuration + fullNightDuration + _phaseTimer,
        _                    => 0f
    };

    // ── UI construction ───────────────────────────────────────────────────────

    private void BuildVisuals()
    {
        TMP_FontAsset font = _theme?.font != null
            ? _theme.font
            : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        // ── Overlay canvas (below HUD so HUD stays readable) ─────────────────
        Canvas overlayCanvas = MakeCanvas("DayNightOverlayCanvas", 50);

        GameObject overlayGO = new GameObject("NightOverlay");
        overlayGO.transform.SetParent(overlayCanvas.transform, false);
        Stretch(overlayGO.AddComponent<RectTransform>());
        _overlayImage              = overlayGO.AddComponent<Image>();
        _overlayImage.color        = DayOverlay;
        _overlayImage.raycastTarget = false;

        // Widget is now built by UIManager.Start() into the HUD canvas so that
        // both share the same CanvasScaler — see UIManager.Start().
    }

    private Canvas MakeCanvas(string name, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform);

        Canvas c          = go.AddComponent<Canvas>();
        c.renderMode      = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder    = order;
        go.AddComponent<GraphicRaycaster>();
        return c;
    }

    public void BuildWidget(Transform parent, TMP_FontAsset font)
    {
        // Small icon + timer stacked vertically, anchored top-right below the 90 px top bar.
        const float topBarH = 90f;
        const float iconSz  = 32f;
        const float timerH  = 18f;
        const float spacing = 2f;
        const float totalW  = iconSz;
        const float totalH  = iconSz + spacing + timerH;

        GameObject root   = new GameObject("DayNightWidget");
        root.transform.SetParent(parent, false);
        RectTransform rrt = root.AddComponent<RectTransform>();
        rrt.anchorMin        = new Vector2(1f, 1f);
        rrt.anchorMax        = new Vector2(1f, 1f);
        rrt.pivot            = new Vector2(1f, 1f);
        rrt.anchoredPosition = new Vector2(-12f, -topBarH - 8f);
        rrt.sizeDelta        = new Vector2(totalW, totalH);

        VerticalLayoutGroup vlg  = root.AddComponent<VerticalLayoutGroup>();
        vlg.spacing              = spacing;
        vlg.childAlignment       = TextAnchor.UpperCenter;
        vlg.childControlWidth    = true;
        vlg.childControlHeight   = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        // Icon
        GameObject iconGO      = new GameObject("PhaseIcon");
        iconGO.transform.SetParent(root.transform, false);
        LayoutElement ile      = iconGO.AddComponent<LayoutElement>();
        ile.minHeight          = iconSz;
        ile.preferredHeight    = iconSz;
        ile.flexibleHeight     = 0f;
        _dayNightIcon          = iconGO.AddComponent<Image>();
        _dayNightIcon.raycastTarget = false;
        Sprite daySprite = _theme?.dayNightDay;
        if (daySprite != null) { _dayNightIcon.sprite = daySprite; _dayNightIcon.color = Color.white; }
        else _dayNightIcon.color = new Color(1f, 0.85f, 0.2f);

        // Timer below icon
        GameObject timeGO      = new GameObject("TimeLabel");
        timeGO.transform.SetParent(root.transform, false);
        LayoutElement tle      = timeGO.AddComponent<LayoutElement>();
        tle.minHeight          = timerH;
        tle.preferredHeight    = timerH;
        tle.flexibleHeight     = 0f;
        _timeLabel             = timeGO.AddComponent<TextMeshProUGUI>();
        _timeLabel.fontSize    = 13f;
        _timeLabel.fontStyle   = FontStyles.Bold;
        _timeLabel.color       = new Color(0.85f, 0.85f, 0.85f);
        _timeLabel.alignment   = TextAlignmentOptions.Center;
        _timeLabel.raycastTarget = false;
        if (font != null) _timeLabel.font = font;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
