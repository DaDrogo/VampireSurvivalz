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
    public static void ApplyImage(Image img, Sprite sprite, Color fallback,
                                  Image.Type spriteType = Image.Type.Sliced)
    {
        if (sprite != null)
        {
            img.sprite = sprite;
            img.type   = spriteType;
            img.color  = Color.white;
        }
        else
        {
            img.color = fallback;
        }
    }

    /// <summary>
    /// Returns a white X sprite on a transparent background.
    /// Generated once and cached — safe to call repeatedly.
    /// </summary>
    public static Sprite MakeCancelIconSprite()
    {
        if (_cancelIcon != null) return _cancelIcon;

        const int   size      = 64;
        const float thickness = 5f;
        const float margin    = 0.18f;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float fx = x / (float)(size - 1);
            float fy = y / (float)(size - 1);
            float d1 = SegmentDist(fx, fy, margin, margin,        1f - margin, 1f - margin);
            float d2 = SegmentDist(fx, fy, margin, 1f - margin,   1f - margin, margin);
            float a  = Mathf.Clamp01(thickness - Mathf.Min(d1, d2) * size + 0.5f);
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        _cancelIcon = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return _cancelIcon;
    }

    private static Sprite _cancelIcon;

    private static float SegmentDist(float px, float py, float ax, float ay, float bx, float by)
    {
        float dx = bx - ax, dy = by - ay;
        float t  = Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy));
        float ex = px - (ax + t * dx), ey = py - (ay + t * dy);
        return Mathf.Sqrt(ex * ex + ey * ey);
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
            cb.selectedColor    = Color.white;
            cb.disabledColor    = new Color(0.50f, 0.50f, 0.50f, 0.50f);
        }
        else
        {
            cb.normalColor      = normal;
            cb.highlightedColor = highlighted;
            cb.pressedColor     = pressed;
            cb.selectedColor    = normal;
            cb.disabledColor    = normal * 0.5f;
        }

        return cb;
    }
}
