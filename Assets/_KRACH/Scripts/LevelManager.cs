using UnityEngine;

public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    public Transform[] hunterSpawnPositions;
    public Transform[] vandalistSpawnPositions;

    private void Awake()
    {
        Instance = this;
    }
}