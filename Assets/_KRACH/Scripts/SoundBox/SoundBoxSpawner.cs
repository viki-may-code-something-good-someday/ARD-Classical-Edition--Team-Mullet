using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;
using FMOD.Studio;

public class SoundBoxSpawner : MonoBehaviour
{
    public static SoundBoxSpawner Instance { get; private set; }

    [SerializeField] private List<SoundBoxWave> soundBoxWaves = new List<SoundBoxWave>();
    [SerializeField] private Transform spawnParent;
    [SerializeField] private List<SoundBoxSpawnPoint> spawnPoints = new List<SoundBoxSpawnPoint>();

    [SerializeField] private float waveSpawnDelay = 6f;   // delay between clearing a wave and spawning the next one

    private SoundManager soundManager;
    private bool wonGame = false;

    [SerializeField] private int currentWaveIndex = 0;
    private bool waitingForNextWave = false;
    bool activeUpdate = false;



    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }


    private IEnumerator Start()
    {
        SoundManager.Instance.PlayClassicMusic();
        yield return new WaitForSeconds(10f); // Wait a bit to ensure all other objects are initialized

        SoundManager.Instance.StopClassicMusic();
        SoundManager.Instance.PlayRemixMusic();


        soundManager = FindFirstObjectByType<SoundManager>();
        if (soundManager == null)
        {
            Debug.LogWarning("SoundBoxSpawner: SoundManager not found in scene.");
        }

        for (int i = soundBoxWaves.Count - 1; i >= 0; i--)
        {
            soundBoxWaves[i].hasSpawned = false;
            soundBoxWaves[i].activeInstances.Clear();
        }
        currentWaveIndex = 0;
        wonGame = false;

        activeUpdate = true;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!activeUpdate) return;

        if (currentWaveIndex >= soundBoxWaves.Count)
        {
            WinGame();
            return;
        }

        SoundBoxWave currentWave = soundBoxWaves[currentWaveIndex];

        if (!currentWave.hasSpawned)
        {
            SpawnSoundBoxWave(currentWave);
            currentWave.hasSpawned = true;

            SoundManager.Instance.InitializeSoundboxEmitters();

            return;
        }

        // IMPORTANT: only check clear if it had instances
        if (!waitingForNextWave &&
            currentWave.hasSpawned &&
            currentWave.activeInstances.Count == 0 &&
            currentWave.boxes.Count > 0)
        {
            Debug.Log("Wave " + currentWaveIndex + " cleared");

            waitingForNextWave = true;
            ClearWave();
        }
    }


    private void SpawnSoundBoxWave(SoundBoxWave wave)
    {
        if (wave == null || wave.boxes == null || wave.boxes.Count == 0)
        {
            Debug.LogWarning("SoundBoxSpawner: wave has no boxes to spawn.");
            return;
        }

        if (spawnPoints == null || spawnPoints.Count == 0)
        {
            Debug.LogWarning("SoundBoxSpawner: no spawn points assigned.");
            return;
        }

        wave.activeInstances.Clear();

        for (int i = 0; i < wave.boxes.Count; i++)
        {
            SoundBox thisBox = wave.boxes[i];
            if (thisBox == null) continue;

            SoundBoxSpawnPoint spawnPoint = spawnPoints[wave.spawnPosNumbers[i]];

            // Instantiate and keep the SoundBox component reference
            SoundBox spawned = Instantiate(thisBox, spawnPoint.transform.position, Quaternion.identity, spawnParent);
            wave.activeInstances.Add(spawned);
        }

        Debug.Log("Spawning wave " + currentWaveIndex + " with " + wave.boxes.Count + " boxes.!!!!!!!!!!!!!!!!!!!!!!");

        Debug.Log("!!!!!!!!!!!!!!!!!!!!!!!Spawned instances: " + wave.activeInstances.Count);

    }

    private void WinGame()
    {
        if (wonGame) return;

        wonGame = true;

        GameManager.Instance.WinGame();
    }

    public void DestroyingSoundBox(SoundBox soundBox)
    {
        if (soundBox == null) return;

        for (int i = 0; i < soundBoxWaves.Count; i++)
        {
            SoundBoxWave wave = soundBoxWaves[i];
            if (wave.activeInstances.Remove(soundBox))
            {
                // wave completion handled in Update
                return;
            }
        }

        Destroy(soundBox.gameObject);

        Debug.LogWarning("DestroyedSoundBox: instance not found in any active wave.");
    }

    private void ClearWave()
    {
        RuntimeManager.PlayOneShot("event:/SFX/AllSpeakersDestroyed");    // sound on all destroyed soundboxes in the wave

        // Start timer before next wave
        StartCoroutine(NextWaveSpawnDelay(waveSpawnDelay));
    }

    private IEnumerator NextWaveSpawnDelay(float delay)
    {
        soundManager.PlayClassicMusic();
        soundManager.StopRemixMusic();
        yield return new WaitForSeconds(5f);

        soundManager.StopClassicMusic();

        yield return new WaitForSeconds(3f);

        soundManager.PlayRemixMusic();

        currentWaveIndex++;        // <-- WAVE INDEX ERST HIER ERHÖHEN
        waitingForNextWave = false;
    }

}
