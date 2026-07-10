using UnityEngine;

public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    [Header("Spawn Positions")]
    public Transform[] vandalistSpawnPositions;
    public Transform[] hunterSpawnPositions;

    private void Awake()
    {
        Instance = this;
    }
}