using TMPro;
using UnityEngine;

/// <summary>
/// Assign one of these assets to every UI script's "UI Theme" field.
/// Leave any sprite slot empty to keep the existing flat-colour fallback.
/// Create via: Assets ► right-click ► Create ► UI ► Theme
/// </summary>
[CreateAssetMenu(fileName = "UITheme", menuName = "UI/Theme")]
public class UITheme : ScriptableObject
{
    [Header("Font — leave empty to keep LiberationSans SDF fallback")]
    public TMP_FontAsset font;

    [Header("Button Sprites — leave empty to keep flat colours")]
    [Tooltip("Sidebar nav buttons and generic neutral buttons.")]
    public Sprite buttonNav;

    [Tooltip("Primary action: Play, Start, Confirm, Upgrade (green).")]
    public Sprite buttonPrimary;

    [Tooltip("Destructive action: Quit, Reset, Delete (red).")]
    public Sprite buttonDanger;

    [Tooltip("Secondary action: Settings, Back, Menu, Choice (blue/grey).")]
    public Sprite buttonSecondary;

    [Tooltip("Economy action: Unlock, Purchase (gold).")]
    public Sprite buttonGold;

    [Header("Card & Panel")]
    [Tooltip("Hotbar slots, character cards, loadout cards, level cards.")]
    public Sprite cardBackground;

    [Tooltip("Modal panel backdrop (pause, info panel, etc.).")]
    public Sprite panelBackground;

    [Header("Day/Night Widget")]
    [Tooltip("Button icon during full day.")]
    public Sprite dayNightDay;
    [Tooltip("Button icon during dusk transition.")]
    public Sprite dayNightDusk;
    [Tooltip("Button icon during full night.")]
    public Sprite dayNightNight;
    [Tooltip("Button icon during dawn transition.")]
    public Sprite dayNightDawn;

    [Header("Step Bar")]
    [Tooltip("Background of the step bar strip at the top of the setup screen.")]
    public Sprite stepBarBackground;
    [Tooltip("Number chip for each step (active/done/future states are colour-tinted on top).")]
    public Sprite stepChip;
    [Tooltip("1px divider between steps. Leave empty to keep a flat colour line.")]
    public Sprite stepDivider;

    [Header("Slider")]
    public Sprite sliderBackground;
    public Sprite sliderFill;
    public Sprite sliderHandle;
}
