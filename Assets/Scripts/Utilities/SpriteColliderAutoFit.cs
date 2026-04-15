using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fits 2D colliders to the assigned sprite in local space.
/// Supports Box, Circle, Capsule and Polygon colliders.
/// </summary>
public static class SpriteColliderAutoFit
{
    public static void Fit(GameObject target)
    {
        if (target == null) return;
        if (!target.TryGetComponent(out SpriteRenderer spriteRenderer)) return;
        if (spriteRenderer.sprite == null) return;

        Sprite sprite = spriteRenderer.sprite;
        Bounds bounds = sprite.bounds;

        if (target.TryGetComponent(out BoxCollider2D box))
        {
            box.size = bounds.size;
            box.offset = bounds.center;
        }

        if (target.TryGetComponent(out CircleCollider2D circle))
        {
            circle.radius = Mathf.Max(bounds.extents.x, bounds.extents.y);
            circle.offset = bounds.center;
        }

        if (target.TryGetComponent(out CapsuleCollider2D capsule))
        {
            capsule.size = bounds.size;
            capsule.offset = bounds.center;
        }

        if (target.TryGetComponent(out PolygonCollider2D polygon))
        {
            int shapeCount = sprite.GetPhysicsShapeCount();
            if (shapeCount <= 0) return;

            polygon.pathCount = shapeCount;
            var points = new List<Vector2>();
            for (int i = 0; i < shapeCount; i++)
            {
                points.Clear();
                sprite.GetPhysicsShape(i, points);
                polygon.SetPath(i, points);
            }
        }
    }
}
