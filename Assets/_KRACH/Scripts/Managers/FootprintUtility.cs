using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Shared 2D oriented-bounding-box math for the flat (single Y-level) building layout.
/// Used for wall clearance and object-overlap checks during procedural furniture placement.
/// Assumes objects only rotate around the world Y axis (no pitch/roll) — valid for a
/// building that sits entirely on one Y plane.
/// </summary>
public static class FootprintUtility
{
    public struct OBB2D
    {
        public Vector2 Center;       // world XZ position
        public Vector2 HalfExtents;  // half-size along the box's own rotated X/Z axes
        public float RotationDeg;    // rotation around world Y axis

        public Vector2 AxisX => new Vector2(Mathf.Cos(RotationDeg * Mathf.Deg2Rad), Mathf.Sin(RotationDeg * Mathf.Deg2Rad));
        public Vector2 AxisZ => new Vector2(-Mathf.Sin(RotationDeg * Mathf.Deg2Rad), Mathf.Cos(RotationDeg * Mathf.Deg2Rad));
    }

    /// <summary>
    /// Computes the world-space footprint of a prefab instance (furniture or billboard) by
    /// encapsulating all child colliders (preferred) or renderers (fallback) into the
    /// object's own local XZ frame, then re-expressing that as a world OBB using the
    /// object's Y rotation. Only active components are considered.
    /// </summary>
    public static bool TryComputeWorldOBB(GameObject go, out OBB2D obb)
    {
        Collider[] colliders = go.GetComponentsInChildren<Collider>(false)
            .Where(c => c.GetComponent<InteractionTrigger>() == null)
            .ToArray();

        IEnumerable<Bounds> sourceBounds = colliders.Length > 0
            ? colliders.Select(c => c.bounds)
            : go.GetComponentsInChildren<Renderer>(false).Select(r => r.bounds);

        return TryBuildOBB(go.transform, sourceBounds, out obb);
    }
    /// <summary>
    /// Computes a Wall's footprint for furniture-clearance checks, explicitly excluding any
    /// colliders that belong to Door children — doors extend well past the wall's own
    /// thickness and would otherwise blow up the wall's footprint far beyond its real shape.
    /// </summary>
    public static bool TryComputeWallFootprintOBB(Wall wall, out OBB2D obb)
    {
        Collider[] colliders = wall.GetComponentsInChildren<Collider>(false)
            .Where(c => c.GetComponentInParent<Door>() == null)
            .ToArray();

        return TryBuildOBB(wall.transform, colliders.Select(c => c.bounds), out obb);
    }

    private static bool TryBuildOBB(Transform root, IEnumerable<Bounds> worldBoundsList, out OBB2D obb)
    {
        obb = default;
        bool hasBounds = false;
        Bounds localBounds = default;

        foreach (Bounds worldBounds in worldBoundsList)
        {
            Vector3 c = worldBounds.center;
            Vector3 e = worldBounds.extents;

            for (int xi = -1; xi <= 1; xi += 2)
                for (int yi = -1; yi <= 1; yi += 2)
                    for (int zi = -1; zi <= 1; zi += 2)
                    {
                        Vector3 worldCorner = c + new Vector3(e.x * xi, e.y * yi, e.z * zi);
                        Vector3 localCorner = root.InverseTransformPoint(worldCorner);

                        if (!hasBounds)
                        {
                            localBounds = new Bounds(localCorner, Vector3.zero);
                            hasBounds = true;
                        }
                        else
                        {
                            localBounds.Encapsulate(localCorner);
                        }
                    }
        }

        if (!hasBounds) return false;

        Vector3 worldCenter = root.TransformPoint(new Vector3(localBounds.center.x, 0f, localBounds.center.z));

        obb = new OBB2D
        {
            Center = new Vector2(worldCenter.x, worldCenter.z),
            HalfExtents = new Vector2(localBounds.extents.x, localBounds.extents.z),
            RotationDeg = root.eulerAngles.y
        };
        return true;
    }

    /// <summary>
    /// Standard 2D Separating Axis Theorem test between two OBBs. extraMargin inflates
    /// both boxes symmetrically (e.g. wall clearance or minimum object spacing).
    /// </summary>
    public static bool Overlaps(OBB2D a, OBB2D b, float extraMargin = 0f)
    {
        Vector2 aHalf = a.HalfExtents + Vector2.one * (extraMargin * 0.5f);
        Vector2 bHalf = b.HalfExtents + Vector2.one * (extraMargin * 0.5f);

        Vector2[] axes = { a.AxisX, a.AxisZ, b.AxisX, b.AxisZ };
        Vector2 diff = b.Center - a.Center;

        foreach (Vector2 axis in axes)
        {
            float projA = Mathf.Abs(Vector2.Dot(a.AxisX, axis)) * aHalf.x + Mathf.Abs(Vector2.Dot(a.AxisZ, axis)) * aHalf.y;
            float projB = Mathf.Abs(Vector2.Dot(b.AxisX, axis)) * bHalf.x + Mathf.Abs(Vector2.Dot(b.AxisZ, axis)) * bHalf.y;
            float dist = Mathf.Abs(Vector2.Dot(diff, axis));

            if (dist > projA + projB) return false; // separating axis found
        }
        return true; // no separating axis -> overlap
    }
}