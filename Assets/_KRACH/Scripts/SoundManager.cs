using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using System.Collections;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    public EventReference classicSchubertEvent;
    public EventReference remixSchubertEvent;
    public EventReference neighbourEvent;
    public EventReference neightbourlistensEvent;

    private EventInstance classicSchubertInstance;
    private EventInstance remixSchubertInstance;
    private EventInstance neighbourInstance;
    private GameObject neighbourGO;

    public EventReference[] soundboxEvents;
    StudioEventEmitter[] soundboxEmitters;

    public int currentLoudness = 0;

    // LAUTHEIT: Musik-Lautstärke Reduktion mit Lerp
    private Bus musicBus;
    private float nextReductionTime;
    [SerializeField] private float minNormalInterval = 4f;   // Minimum Sekunden wenn laut
    [SerializeField] private float maxNormalInterval = 11f;   // Maximum Sekunden wenn laut
    [SerializeField] private float minReducedInterval = 2f;   // Minimum Sekunden wenn reduziert
    [SerializeField] private float maxReducedInterval = 6f;   // Maximum Sekunden wenn reduziert
    [SerializeField] private float reductionMultiplier = 0.2f; // Lautstärke-Reduktion (0.0 - 1.0)
    private float currentVolumeMultiplier = 1f; // Aktuelle Lautstärke-Multiplikator
    private float targetVolumeMultiplier = 1f; // Ziel Lautstärke-Multiplikator
    private float timeSinceVolumeChange = 0f;
    [SerializeField] private float volumeLerpDuration = 1f; // Dauer des Lerp in Sekunden
    bool activeUpdate = false;


    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Music Bus initialisieren
        musicBus = RuntimeManager.GetBus("bus:/Music");
        ScheduleNextReduction();

        // Get neighbour GameObject
        neighbourGO = GameObject.FindWithTag("Neighbour");

        //InitializeSoundboxEmitters();
        classicSchubertInstance = RuntimeManager.CreateInstance(classicSchubertEvent);
        remixSchubertInstance = RuntimeManager.CreateInstance(remixSchubertEvent);
    }

    private IEnumerator Start()
    {


        yield return new WaitForSeconds(10f); // Wait a bit to ensure all other objects are initialized
        activeUpdate = true;
    }

    void Update()
    {
        if (!activeUpdate) return;
        // Musik-Lautstärke Reduktion mit Random-Intervallen
        if (Time.time >= nextReductionTime)
        {
            // Wechsle zwischen Reduktion und Normal (1.0)
            targetVolumeMultiplier = (targetVolumeMultiplier == 1f) ? reductionMultiplier : 1f;
            timeSinceVolumeChange = 0f;

            // Play Neighbour Sound only when reducing (not when volume goes back up)
            if (targetVolumeMultiplier == reductionMultiplier)
            {
                RuntimeManager.PlayOneShot(neightbourlistensEvent, neighbourGO.transform.position);
            }

            ScheduleNextReduction();
        }

        // Lerpe die Lautstärke-Änderung
        timeSinceVolumeChange += Time.deltaTime;
        if (timeSinceVolumeChange < volumeLerpDuration)
        {
            float lerpProgress = timeSinceVolumeChange / volumeLerpDuration;
            currentVolumeMultiplier = Mathf.Lerp(currentVolumeMultiplier, targetVolumeMultiplier, lerpProgress);
        }
        else
        {
            currentVolumeMultiplier = targetVolumeMultiplier;
        }

        // Setze die Music Bus Lautstärke
        musicBus.setVolume(currentVolumeMultiplier);

        // Update currentLoudness basierend auf Reduktion
        currentLoudness = (targetVolumeMultiplier == reductionMultiplier) ? 0 : 1;


        // Debug Info
        //Debug.Log($"Current Loudness: {currentLoudness}, Volume Multiplier: {currentVolumeMultiplier:F2}");
    }

    public RMF_State GetCurrentRMFState()
    {
        if (currentLoudness == 0)
        {
            return RMF_State.Low;
        }
        else
        {
            return RMF_State.High;
        }
    }

    private void ScheduleNextReduction()
    {
        float randomInterval;
        if (targetVolumeMultiplier == 1f)
        {
            // Gerade normal -> nächste Reduktion
            randomInterval = Random.Range(minNormalInterval, maxNormalInterval);
            Debug.Log($"Nächste Musik-Reduktion in {randomInterval:F1} Sekunden");
        }
        else
        {
            // Gerade reduziert -> nächste Normal
            randomInterval = Random.Range(minReducedInterval, maxReducedInterval);
            Debug.Log($"Musik wieder normal in {randomInterval:F1} Sekunden");
        }
        nextReductionTime = Time.time + randomInterval;
    }

    public void InitializeSoundboxEmitters()
    {
        // Find all SoundBox Objects and then take the soundemitter component and fill in the array.
        GameObject[] soundboxes = GameObject.FindGameObjectsWithTag("SoundBox");

        soundboxEmitters = new StudioEventEmitter[soundboxes.Length];

        for (int i = 0; i < soundboxes.Length; i++)
        {
            soundboxEmitters[i] = soundboxes[i].GetComponent<StudioEventEmitter>();
            if (i < soundboxEvents.Length)
            {
                soundboxEmitters[i].EventReference = soundboxEvents[i];
                PlaySoundBoxEvent(i);    // play soundbox event on start
                //Debug.Log($"SoundManager: Assigned event {soundboxEvents[i].Path} to SoundBox {soundboxes[i].name}");
            }
        }
    }

    public void PlaySoundBoxEvent(int index)
    {
        if (index >= 0 && index < soundboxEmitters.Length && soundboxEmitters[index] != null)
        {
            soundboxEmitters[index].Play();
        }
    }

    public void PlayClassicMusic()
    {
        classicSchubertInstance.getPlaybackState(out PLAYBACK_STATE state);
        if (state != PLAYBACK_STATE.PLAYING)
        {
            classicSchubertInstance.start();
        }
    }

    public void StopClassicMusic()
    {
        classicSchubertInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
    }

    public void PlayRemixMusic()
    {
        remixSchubertInstance.getPlaybackState(out PLAYBACK_STATE state);
        if (state != PLAYBACK_STATE.PLAYING)
        {
            remixSchubertInstance.start();
        }
    }

    public void StopRemixMusic()
    {
        remixSchubertInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
    }
}
