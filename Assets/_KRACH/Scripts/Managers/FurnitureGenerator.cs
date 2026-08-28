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
/// Editor-time procedural furniture/billboard placement tool. Samples random points across
/// the baked NavMesh (area-weighted, so density is uniform regardless of triangle size),
/// then places furniture or billboard prefabs while rejecting any placement that overlaps
/// a Wall footprint or an already-placed object footprint. Generated objects become normal
/// scene GameObjects — this does not run at runtime.
/// </summary>
public class FurnitureGenerator : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private List<GameObject> furniturePrefabs = new List<GameObject>();
    [SerializeField] private List<GameObject> billboardPrefabs = new List<GameObject>();

    [Header("Density")]
    [Tooltip("Target objects per square meter of walkable NavMesh area.")]
    [SerializeField] private float density = 0.05f;
    [Tooltip("0 = only furniture, 1 = only billboards.")]
    [Range(0f, 1f)]
    [SerializeField] private float billboardRatio = 0.3f;

    [Header("Spacing")]
    [Tooltip("Extra gap enforced between placed objects, on top of their own footprint.")]
    [SerializeField] private float objectSpacing = 0.1f;
    [Tooltip("Minimum distance a footprint must keep from any Wall footprint.")]
    [SerializeField] private float wallClearance = 0.15f;

    [Header("Placement")]
    [SerializeField] private int maxAttemptsPerObject = 30;
    [SerializeField] private int randomSeed = 12345;
    [Tooltip("Generated objects are parented here. Required — also used by Clear Generated.")]
    [SerializeField] private Transform generatedParent;

    private readonly List<OBB2D> placedFootprints = new List<OBB2D>();
    private readonly List<OBB2D> wallFootprints = new List<OBB2D>();

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

        BuildTriangleDistribution(navData, out float[] cumulativeAreas, out float totalArea);
        int targetCount = Mathf.RoundToInt(totalArea * density);

        Debug.Log($"[FurnitureGenerator] NavMesh area: {totalArea:F1} m² — targeting {targetCount} objects.");

        placedFootprints.Clear();
        int placedCount = 0;

        for (int i = 0; i < targetCount; i++)
        {
            if (TryPlaceOneObject(navData, cumulativeAreas, totalArea))
                placedCount++;
        }

        Debug.Log($"[FurnitureGenerator] Placed {placedCount}/{targetCount} objects " +
                  $"({targetCount - placedCount} skipped — no valid spot found within attempt limit).");

        EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    private bool TryPlaceOneObject(NavMeshTriangulation navData, float[] cumulativeAreas, float totalArea)
    {
        bool useBillboard = billboardPrefabs.Count > 0 &&
                             (furniturePrefabs.Count == 0 || Random.value < billboardRatio);

        List<GameObject> pool = useBillboard ? billboardPrefabs : furniturePrefabs;
        if (pool.Count == 0) pool = useBillboard ? furniturePrefabs : billboardPrefabs;
        if (pool.Count == 0) return false;

        GameObject prefab = pool[Random.Range(0, pool.Count)];

        for (int attempt = 0; attempt < maxAttemptsPerObject; attempt++)
        {
            Vector3 samplePoint = SampleRandomPointOnNavMesh(navData, cumulativeAreas, totalArea);
            float rotationY = Random.Range(0f, 360f);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, generatedParent);
            instance.transform.position = samplePoint;
            instance.transform.rotation = Quaternion.Euler(0f, rotationY, 0f);

            if (!TryComputeWorldOBB(instance, out OBB2D candidateObb))
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
    /// Precomputes per-triangle area and a cumulative distribution so triangle selection can
    /// be area-weighted. Without this, small triangles (common near complex geometry) and
    /// large triangles (common in open rooms) would be equally likely, clustering points in
    /// geometrically complex areas instead of distributing them uniformly across the floor.
    /// </summary>
    private void BuildTriangleDistribution(NavMeshTriangulation navData, out float[] cumulativeAreas, out float totalArea)
    {
        int triCount = navData.indices.Length / 3;
        cumulativeAreas = new float[triCount];
        totalArea = 0f;

        for (int i = 0; i < triCount; i++)
        {
            Vector3 a = navData.vertices[navData.indices[i * 3]];
            Vector3 b = navData.vertices[navData.indices[i * 3 + 1]];
            Vector3 c = navData.vertices[navData.indices[i * 3 + 2]];

            totalArea += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            cumulativeAreas[i] = totalArea;
        }
    }

    private Vector3 SampleRandomPointOnNavMesh(NavMeshTriangulation navData, float[] cumulativeAreas, float totalArea)
    {
        float target = Random.value * totalArea;
        int triIndex = System.Array.BinarySearch(cumulativeAreas, target);
        if (triIndex < 0) triIndex = ~triIndex;
        triIndex = Mathf.Clamp(triIndex, 0, cumulativeAreas.Length - 1);

        Vector3 a = navData.vertices[navData.indices[triIndex * 3]];
        Vector3 b = navData.vertices[navData.indices[triIndex * 3 + 1]];
        Vector3 c = navData.vertices[navData.indices[triIndex * 3 + 2]];

        // Uniform random point inside the triangle via barycentric coordinates.
        float r1 = Mathf.Sqrt(Random.value);
        float r2 = Random.value;
        return (1 - r1) * a + (r1 * (1 - r2)) * b + (r1 * r2) * c;
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