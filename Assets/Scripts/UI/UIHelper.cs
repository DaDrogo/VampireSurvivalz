using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared helpers used by all procedural UI scripts to apply a <see cref="UITheme"/>.
/// If a sprite is provided the Image uses it (Sliced type, white tint so the sprite
/// shows true colour).  If the sprite is null the Image keeps its flat fallback colour.
/// </summary>
public static class UIHelper
{
    /// <summary>
    /// Applies <paramref name="sprite"/> to <paramref name="img"/> if non-null,
    /// otherwise sets <paramref name="fallback"/> as the solid colour.
    /// </summary>
    public static void ApplyImage(Image img, Sprite sprite, Color fallback)
    {
        if (sprite != null)
        {
            img.sprite = sprite;
            img.type   = Image.Type.Sliced;
            img.color  = Color.white;
        }
        else
        {
            img.color = fallback;
        }
    }

    /// <summary>
    /// Returns a <see cref="ColorBlock"/> suitable for a <see cref="Button"/>.
    /// When a sprite is in use the normal colour is white (full sprite colour)
    /// with subtle tint shifts for hover and press.
    /// When no sprite is used the provided colours are applied directly.
    /// </summary>
    public static ColorBlock BtnColors(Sprite sprite,
        Color normal, Color highlighted, Color pressed)
    {
        ColorBlock cb       = ColorBlock.defaultColorBlock;
        cb.colorMultiplier  = 1f;

        if (sprite != null)
        {
            cb.normalColor      = Color.white;
            cb.highlightedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            cb.pressedColor     = new Color(0.70f, 0.70f, 0.70f, 1f);
            cb.disabledColor    = new Color(0.50f, 0.50f, 0.50f, 0.50f);
        }
        else
        {
            cb.normalColor      = normal;
            cb.highlightedColor = highlighted;
            cb.pressedColor     = pressed;
            cb.disabledColor    = normal * 0.5f;
        }

        return cb;
    }
}
