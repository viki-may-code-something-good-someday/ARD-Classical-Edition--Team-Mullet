using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using static FootprintUtility;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using Sirenix.OdinInspector;
#endif

/// <summary>
/// Editor-time procedural furniture/billboard placement tool. The walkable NavMesh is
/// bucketed into a coarse 2D grid; each grid cell gets its own object budget so small or
/// isolated rooms are guaranteed a minimum amount of coverage instead of being starved by
/// pure global area-weighted sampling. Within a cell, triangles are still sampled
/// area-weighted for a uniform distribution. Placement rejects any footprint that overlaps
/// a Wall footprint or an already-placed object footprint. Generated objects become normal
/// scene GameObjects — this does not run at runtime.
/// </summary>
public class FurnitureGenerator : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private List<GameObject> furniturePrefabs = new List<GameObject>();
    [SerializeField] private List<GameObject> billboardPrefabs = new List<GameObject>();

    [Header("Density")]
    [Tooltip("Target objects per square meter of walkable NavMesh area, per grid cell.")]
    [SerializeField] private float density = 0.05f;
    [Tooltip("0 = only furniture, 1 = only billboards.")]
    [Range(0f, 1f)]
    [SerializeField] private float billboardRatio = 0.3f;

    [Header("Grid Quota (guarantees coverage in small rooms)")]
    [Tooltip("Size of the coarse grid cells (meters) used to guarantee minimum coverage in small/isolated rooms. Not true room detection — just spatial buckets. Should roughly match your typical small room size.")]
    [SerializeField] private float gridCellSize = 2f;
    [Tooltip("Minimum objects guaranteed per non-empty grid cell, regardless of density-derived count.")]
    [SerializeField] private int minObjectsPerCell = 1;

    [Header("Spacing")]
    [Tooltip("Extra gap enforced between placed objects, on top of their own footprint.")]
    [SerializeField] private float objectSpacing = 0.1f;
    [Tooltip("Minimum distance a footprint must keep from any Wall footprint.")]
    [SerializeField] private float wallClearance = 0.15f;
    [Tooltip("No object will spawn within this radius (meters, XZ distance) of any Door's position — keeps doorways clear.")]
    [SerializeField] private float doorClearanceRadius = 1.5f;

    [Header("Placement")]
    [SerializeField] private int maxAttemptsPerObject = 30;
    [SerializeField] private int randomSeed = 12345;
    [Tooltip("Generated objects are parented here. Required — also used by Clear Generated.")]
    [SerializeField] private Transform generatedParent;

    private readonly List<OBB2D> placedFootprints = new List<OBB2D>();
    private readonly List<OBB2D> wallFootprints = new List<OBB2D>();
    private readonly List<Vector3> doorPositions = new List<Vector3>();

    /// <summary>
    /// Triangles bucketed into one coarse grid cell, plus a precomputed cumulative-area
    /// distribution so a point can be sampled uniformly (area-weighted) within the cell.
    /// </summary>
    private class GridCell
    {
        public readonly List<int> triangleIndices = new List<int>();
        public readonly List<float> triangleAreas = new List<float>();
        public float[] cumulativeAreas;
        public float totalArea;
    }

#if UNITY_EDITOR
    [Button("Generate Furniture")]
    private void GenerateFurniture()
    {
        if (generatedParent == null)
        {
            Debug.LogError("[FurnitureGenerator] Assign generatedParent before generating.");
            return;
        }

        if (furniturePrefabs.Count == 0 && billboardPrefabs.Count == 0)
        {
            Debug.LogError("[FurnitureGenerator] No prefabs assigned.");
            return;
        }

        NavMeshTriangulation navData = NavMesh.CalculateTriangulation();
        if (navData.vertices.Length == 0)
        {
            Debug.LogError("[FurnitureGenerator] NavMesh is empty — bake it first (Window > AI > Navigation).");
            return;
        }

        ClearGenerated();
        Random.InitState(randomSeed);

        wallFootprints.Clear();
        foreach (Wall wall in FindObjectsByType<Wall>(FindObjectsSortMode.InstanceID))
        {
            if (TryComputeWallFootprintOBB(wall, out OBB2D wallObb))
                wallFootprints.Add(wallObb);
        }

        doorPositions.Clear();
        foreach (Door door in FindObjectsByType<Door>(FindObjectsSortMode.InstanceID))
        {
            doorPositions.Add(door.transform.position);
        }

        Dictionary<Vector2Int, GridCell> grid = BuildTriangleGrid(navData);
        List<GridCell> cells = new List<GridCell>(grid.Values);
        Shuffle(cells); // avoid any scan-order bias between cells

        placedFootprints.Clear();
        int placedCount = 0;
        int targetTotal = 0;

        foreach (GridCell cell in cells)
        {
            int cellTarget = Mathf.Max(minObjectsPerCell, Mathf.RoundToInt(cell.totalArea * density));
            targetTotal += cellTarget;

            for (int i = 0; i < cellTarget; i++)
            {
                if (TryPlaceOneObject(navData, cell))
                    placedCount++;
            }
        }

        Debug.Log($"[FurnitureGenerator] {grid.Count} grid cells covering NavMesh — targeting {targetTotal} objects total. " +
                  $"Placed {placedCount}/{targetTotal} ({targetTotal - placedCount} skipped — no valid spot found within attempt limit).");

        EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    private bool TryPlaceOneObject(NavMeshTriangulation navData, GridCell cell)
    {
        bool useBillboard = billboardPrefabs.Count > 0 &&
                             (furniturePrefabs.Count == 0 || Random.value < billboardRatio);

        List<GameObject> pool = useBillboard ? billboardPrefabs : furniturePrefabs;
        if (pool.Count == 0) pool = useBillboard ? furniturePrefabs : billboardPrefabs;
        if (pool.Count == 0) return false;

        GameObject prefab = pool[Random.Range(0, pool.Count)];

        for (int attempt = 0; attempt < maxAttemptsPerObject; attempt++)
        {
            Vector3 samplePoint = SampleRandomPointInCell(navData, cell);

            if (TooCloseToAnyDoor(samplePoint))
                continue;

            float rotationY = Random.Range(0f, 360f);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, generatedParent);
            instance.transform.position = samplePoint;
            instance.transform.rotation = Quaternion.Euler(0f, rotationY, 0f);

            Physics.SyncTransforms();

            if (!TryComputeFootprintForPlacement(instance, useBillboard, out OBB2D candidateObb))
            {
                Debug.LogWarning($"[FurnitureGenerator] '{prefab.name}' has no active Renderer/Collider — cannot compute a footprint, skipping entirely.");
                DestroyImmediate(instance);
                return false;
            }

            if (OverlapsAnyWall(candidateObb) || OverlapsAnyPlacedObject(candidateObb))
            {
                DestroyImmediate(instance);
                continue;
            }

            Undo.RegisterCreatedObjectUndo(instance, "Generate Furniture");
            placedFootprints.Add(candidateObb);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Computes the placement footprint for an instance. For billboards, only the small
    /// collider on the InteractionTrigger child is used — every other collider on the
    /// instance (e.g. the large interaction/visual collider) is temporarily disabled so
    /// TryComputeWorldOBB measures against the small collider only, then all colliders are
    /// restored to their original enabled state so runtime behavior is unaffected. This
    /// keeps billboards from being rejected far more often than furniture just because of
    /// their larger physical collider.
    /// </summary>
    private bool TryComputeFootprintForPlacement(GameObject instance, bool useBillboard, out OBB2D obb)
    {
        if (useBillboard)
        {
            InteractionTrigger trigger = instance.GetComponentInChildren<InteractionTrigger>();
            Collider triggerCollider = trigger != null ? trigger.GetComponent<Collider>() : null;

            if (triggerCollider != null)
            {
                Collider[] allColliders = instance.GetComponentsInChildren<Collider>(true);
                var previousStates = new Dictionary<Collider, bool>();

                foreach (Collider col in allColliders)
                {
                    previousStates[col] = col.enabled;
                    col.enabled = (col == triggerCollider);
                }

                Physics.SyncTransforms();
                bool success = TryComputeWorldOBB(instance, out obb);

                foreach (var kvp in previousStates)
                    kvp.Key.enabled = kvp.Value;

                Physics.SyncTransforms();
                return success;
            }
        }

        return TryComputeWorldOBB(instance, out obb);
    }

    /// <summary>
    /// XZ-distance check against every Door position — keeps a clear radius around doorways
    /// so furniture/billboards can't block them. Checked against the sample point before
    /// anything is instantiated, so a rejected point costs nothing.
    /// </summary>
    private bool TooCloseToAnyDoor(Vector3 position)
    {
        float radiusSqr = doorClearanceRadius * doorClearanceRadius;
        foreach (Vector3 doorPos in doorPositions)
        {
            float dx = position.x - doorPos.x;
            float dz = position.z - doorPos.z;
            if (dx * dx + dz * dz <= radiusSqr)
                return true;
        }
        return false;
    }

    private bool OverlapsAnyWall(OBB2D candidate)
    {
        foreach (OBB2D wallObb in wallFootprints)
            if (Overlaps(candidate, wallObb, wallClearance)) return true;
        return false;
    }

    private bool OverlapsAnyPlacedObject(OBB2D candidate)
    {
        foreach (OBB2D placedObb in placedFootprints)
            if (Overlaps(candidate, placedObb, objectSpacing)) return true;
        return false;
    }

    /// <summary>
    /// Buckets every NavMesh triangle into a coarse grid cell based on its centroid (X/Z),
    /// and precomputes a per-cell cumulative-area distribution for area-weighted sampling
    /// within that cell. This is what lets each cell get its own independent object budget
    /// instead of one global budget spread over the whole NavMesh.
    /// </summary>
    private Dictionary<Vector2Int, GridCell> BuildTriangleGrid(NavMeshTriangulation navData)
    {
        var grid = new Dictionary<Vector2Int, GridCell>();
        int triCount = navData.indices.Length / 3;

        for (int i = 0; i < triCount; i++)
        {
            Vector3 a = navData.vertices[navData.indices[i * 3]];
            Vector3 b = navData.vertices[navData.indices[i * 3 + 1]];
            Vector3 c = navData.vertices[navData.indices[i * 3 + 2]];

            float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            Vector3 centroid = (a + b + c) / 3f;

            var cellCoord = new Vector2Int(
                Mathf.FloorToInt(centroid.x / gridCellSize),
                Mathf.FloorToInt(centroid.z / gridCellSize));

            if (!grid.TryGetValue(cellCoord, out GridCell cell))
            {
                cell = new GridCell();
                grid[cellCoord] = cell;
            }

            cell.triangleIndices.Add(i);
            cell.triangleAreas.Add(area);
            cell.totalArea += area;
        }

        foreach (GridCell cell in grid.Values)
        {
            cell.cumulativeAreas = new float[cell.triangleAreas.Count];
            float running = 0f;
            for (int i = 0; i < cell.triangleAreas.Count; i++)
            {
                running += cell.triangleAreas[i];
                cell.cumulativeAreas[i] = running;
            }
        }

        return grid;
    }

    private Vector3 SampleRandomPointInCell(NavMeshTriangulation navData, GridCell cell)
    {
        float target = Random.value * cell.totalArea;
        int idx = System.Array.BinarySearch(cell.cumulativeAreas, target);
        if (idx < 0) idx = ~idx;
        idx = Mathf.Clamp(idx, 0, cell.cumulativeAreas.Length - 1);

        int triIndex = cell.triangleIndices[idx];
        Vector3 a = navData.vertices[navData.indices[triIndex * 3]];
        Vector3 b = navData.vertices[navData.indices[triIndex * 3 + 1]];
        Vector3 c = navData.vertices[navData.indices[triIndex * 3 + 2]];

        // Uniform random point inside the triangle via barycentric coordinates.
        float r1 = Mathf.Sqrt(Random.value);
        float r2 = Random.value;
        return (1 - r1) * a + (r1 * (1 - r2)) * b + (r1 * r2) * c;
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    [Button("Clear Generated")]
    private void ClearGenerated()
    {
        if (generatedParent == null) return;

        for (int i = generatedParent.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(generatedParent.GetChild(i).gameObject);

        placedFootprints.Clear();

        EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }
#endif
}