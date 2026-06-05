using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;
using Sirenix.OdinInspector;

public class SoundBoxSpawner : MonoBehaviour
{
    public static SoundBoxSpawner Instance { get; private set; }

    [Header("References")]
    [SerializeField] private List<SoundBoxWave> soundBoxWaves = new List<SoundBoxWave>();
    [SerializeField] private Transform spawnParent;
    [SerializeField] private List<SoundBoxSpawnPoint> spawnPoints = new List<SoundBoxSpawnPoint>();

    [Header("Settings")]
    [SerializeField] private float waveTransitionDelay;

    [SerializeField, ReadOnly] private int currentWaveIndex = 0;

    private List<SoundBox> activeInstances = new List<SoundBox>();
    private bool gameWon = false;
    private bool isTransitioningWave = false;


    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        ResetWaveData();
        SpawnCurrentWave();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (isTransitioningWave || gameWon) return;

        if (activeInstances.Count == 0)
        {
            isTransitioningWave = true;
            StartCoroutine(TransitionToNextWave());
        }
    }


    private void ResetWaveData()
    {
        currentWaveIndex = 0;
        gameWon = false;
        isTransitioningWave = false;
        activeInstances.Clear();
    }

    private void SpawnCurrentWave()
    {
        if (currentWaveIndex >= soundBoxWaves.Count)
        {
            WinGame();
            return;
        }

        SoundBoxWave wave = soundBoxWaves[currentWaveIndex];

        if (wave.Boxes == null || wave.Boxes.Count == 0)
        {
            Debug.LogWarning($"SoundBoxSpawner: Wave {currentWaveIndex} has no boxes.");
            return;
        }

        if (spawnPoints == null || spawnPoints.Count == 0)
        {
            Debug.LogWarning("SoundBoxSpawner: No spawn points assigned.");
            return;
        }

        activeInstances.Clear();

        for (int i = 0; i < wave.Boxes.Count; i++)
        {
            SoundBox box = wave.Boxes[i];
            if (box == null) continue;

            SoundBoxSpawnPoint spawnPoint = spawnPoints[wave.SpawnPosNumbers[i]];
            SoundBox spawned = Instantiate(box, spawnPoint.transform.position, Quaternion.identity, spawnParent);
            activeInstances.Add(spawned);
        }

        SoundManager.Instance.InitializeSoundboxEmitters();

        Debug.Log($"Wave {currentWaveIndex} spawned with {wave.Boxes.Count} boxes.");
    }

    // Wird von SoundBox aufgerufen wenn sie zerstört wird
    public void NotifySoundBoxDestroyed(SoundBox _soundBox)
    {
        if (_soundBox == null) return;

        if (activeInstances.Remove(_soundBox))
        {
            Destroy(_soundBox.gameObject);
            return; // Wellen-Abschluss wird in Update() erkannt
        }

        Debug.LogWarning($"NotifySoundBoxDestroyed: '{_soundBox.name}' not found in active wave.");
    }

    private IEnumerator TransitionToNextWave()
    {
        RuntimeManager.PlayOneShot("event:/SFX/AllSpeakersDestroyed");

        yield return new WaitForSeconds(waveTransitionDelay);

        currentWaveIndex++;
        isTransitioningWave = false;

        SpawnCurrentWave();
    }

    private void WinGame()
    {
        if (gameWon) return;

        Debug.Log("All box-waves destroyed! -> WinGame is not implemented here right now.");

        gameWon = true;
        //GameManager.Instance.WinGame();
    }
}