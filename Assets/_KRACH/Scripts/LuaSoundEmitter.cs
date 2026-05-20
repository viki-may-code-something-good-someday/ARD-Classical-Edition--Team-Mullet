using System.Collections.Generic;
using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using Sirenix.OdinInspector;
#if UNITY_EDITOR
using UnityEditor;
#endif

[AddComponentMenu("Lua/LuaSoundEmitter")]
public class LuaSoundEmitter : EventHandler
{
    [Header("Event")]
    public EventReference EventReference;

    [Header("Emitter Settings")]
    public EmitterGameEvent EventPlayTrigger = EmitterGameEvent.None;
    public EmitterGameEvent EventStopTrigger = EmitterGameEvent.None;
    [Space(5)]
    public bool AllowFadeout = true;
    public bool TriggerOnce = false;
    public bool Preload = false;
    public bool NonRigidbodyVelocity = false;
    [Space(5)]
    public bool OverrideAttenuation = false;
    [ShowIf("OverrideAttenuation")]
    public float OverrideMinDistance = 2f;
    [ShowIf("OverrideAttenuation")]
    public float OverrideMaxDistance = 3f;
    [Space(5)]
    public ParamRef[] Params = new ParamRef[0];

    [Header("Occlusion")]
    [SerializeField] private bool enableOcclusion = true;
    [Space(5)]
    [ShowIf("enableOcclusion")]
    [SerializeField] private Transform playerTransform;
    [ShowIf("enableOcclusion")]
    [SerializeField] private LayerMask obstacleLayer;
    [Space(5)]
    [ShowIf("enableOcclusion")]
    [SerializeField] private float maxDistance = 20f;
    [ShowIf("enableOcclusion")]
    [SerializeField] private float maxParameterDistance = 30f;
    [Space(5)]
    [ShowIf("enableOcclusion")]
    [SerializeField] private bool invertFadeRange = false;
    [ShowIf("enableOcclusion")]
    [SerializeField] private bool stopAudioWhenOutOfRange = false;
    [ShowIf("enableOcclusion")]
    [SerializeField] private string nonOcclusionTag = "NoOcclusion";

    [Header("Reverb")]
    [SerializeField] private ReverbType selectedMaterial = ReverbType.Room;
    [Space(10)]

    [Header("One-Shot Cooldown")]
    [SerializeField] private bool enableOneShotCooldown = false;
    [ShowIf("enableOneShotCooldown")]
    [SerializeField] private float oneShotCooldownDuration = 1f;
    [Space(10)]

    [ShowIf("enableOcclusion")]
    [Header("Debug")]
    [SerializeField] private float scaledValue;
    [ShowIf("enableOcclusion")]
    [SerializeField] private bool isObstructed;

    // ── FMOD handles ──────────────────────────────────────────────────────────
    private EventInstance    audioSource;
    private EventDescription eventDescription;
    private List<ParamRef>   cachedParams = new List<ParamRef>();

    // ── State ─────────────────────────────────────────────────────────────────
    private bool hasStartedEvent;
    private bool hasTriggered;
    private bool isQuitting;
    private int  materialParameterValue;
    private float oneShotCooldownTimer = 0f;

    public bool IsActive  { get; private set; }
    public bool IsPlaying()
    {
        if (!audioSource.isValid()) return false;
        audioSource.getPlaybackState(out var state);
        return state != PLAYBACK_STATE.STOPPED;
    }

    // ── Constants ─────────────────────────────────────────────────────────────
    private const string OcclusionParam = "Occlusion";
    private const string FadeParam      = "OcclusionFade";
    private const string MaterialParam  = "ReverbType";
    private const float  MinDistance    = 0f;

    private enum ReverbType { None, Room, Hallway, Arena, Padded }

    // ═════════════════════════════════════════════════════════════════════════
    // Lifecycle
    // ═════════════════════════════════════════════════════════════════════════

    protected override void Start()
    {
        RuntimeUtils.EnforceLibraryOrder();

        materialParameterValue = selectedMaterial switch
        {
            ReverbType.None    => 0,
            ReverbType.Room    => 1,
            ReverbType.Hallway => 2,
            ReverbType.Arena   => 3,
            ReverbType.Padded  => 4,
            _                  => 1
        };

        if (Preload)
        {
            Lookup();
            eventDescription.loadSampleData();
        }

        HandleGameEvent(EmitterGameEvent.ObjectStart);
    }

    private void OnApplicationQuit() => isQuitting = true;

    protected override void OnDestroy()
    {
        if (isQuitting) return;

        HandleGameEvent(EmitterGameEvent.ObjectDestroy);
        StopAudio();

        if (Preload && eventDescription.isValid())
            eventDescription.unloadSampleData();
    }

    private void Update()
    {
        // Update One-Shot Cooldown timer
        if (enableOneShotCooldown && oneShotCooldownTimer > 0f)
        {
            oneShotCooldownTimer -= Time.deltaTime;
        }

        if (!IsActive) return;

        if (playerTransform == null)
        {
            StopAudio();
            return;
        }

        Vector3 dir = playerTransform.position - transform.position;

        // No raycast hit within maxDistance → player out of range
        if (!Physics.Raycast(transform.position, dir, out var hit, maxDistance, obstacleLayer))
        {
            isObstructed = false;
            if (stopAudioWhenOutOfRange) StopAudio();
            return;
        }

        float distToPlayer = Vector3.Distance(transform.position, playerTransform.position);
        float hitDist      = Vector3.Distance(transform.position, hit.point);
        float width        = hit.collider.bounds.size.x;

        float normalized = Mathf.Clamp01(1f - (distToPlayer - width) / (maxParameterDistance - MinDistance));
        float fadeValue  = invertFadeRange
            ? Mathf.Clamp01((distToPlayer - width) / (maxParameterDistance - MinDistance))
            : normalized;

        scaledValue = fadeValue;

        if (normalized > 0f)
        {
            EnsureInstanceStarted();
            audioSource.setParameterByName(FadeParam, fadeValue);
        }
        else
        {
            StopAudio();
            return;
        }

        // Occlusion — suppress if hit object carries the bypass tag
        bool suppressOcclusion = !string.IsNullOrEmpty(nonOcclusionTag)
                                 && hit.collider.CompareTag(nonOcclusionTag);
        isObstructed = !suppressOcclusion && (hitDist < distToPlayer);

        audioSource.setParameterByName(OcclusionParam, isObstructed ? 1f : 0f);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // EventHandler
    // ═════════════════════════════════════════════════════════════════════════

    protected override void HandleGameEvent(EmitterGameEvent gameEvent)
    {
        if (EventPlayTrigger == gameEvent) Play();
        if (EventStopTrigger == gameEvent) Stop();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Public API
    // ═════════════════════════════════════════════════════════════════════════

    public void Play()
    {
        if (TriggerOnce && hasTriggered) return;
        if (enableOneShotCooldown && oneShotCooldownTimer > 0f) return; // Cooldown active, cannot play
        if (EventReference.IsNull) return;
        if (!eventDescription.isValid()) Lookup();

        IsActive     = true;
        hasTriggered = true;
        
        // Start cooldown timer if enabled
        if (enableOneShotCooldown)
        {
            oneShotCooldownTimer = oneShotCooldownDuration;
        }
    }

    public void Stop()
    {
        IsActive = false;
        cachedParams.Clear();
        StopAudio();
    }

    /// <summary>Set a parameter by name. Value is cached and re-applied if the instance is recreated.</summary>
    public void SetParameter(string name, float value, bool ignoreSeekSpeed = false)
    {
        CacheParam(name, value);
        if (audioSource.isValid())
            audioSource.setParameterByName(name, value, ignoreSeekSpeed);
    }

    /// <summary>Set a parameter by ID. Value is cached and re-applied if the instance is recreated.</summary>
    public void SetParameter(PARAMETER_ID id, float value, bool ignoreSeekSpeed = false)
    {
        CacheParam(id, value);
        if (audioSource.isValid())
            audioSource.setParameterByID(id, value, ignoreSeekSpeed);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Private Helpers
    // ═════════════════════════════════════════════════════════════════════════

    private void Lookup()
    {
        eventDescription = RuntimeManager.GetEventDescription(EventReference);
        if (!eventDescription.isValid()) return;

        foreach (var p in Params)
        {
            eventDescription.getParameterDescriptionByName(p.Name, out var desc);
            p.ID = desc.id;
        }
    }

    private void EnsureInstanceStarted()
    {
        if (hasStartedEvent && audioSource.isValid()) return;

        if (!eventDescription.isValid()) Lookup();
        if (!eventDescription.isValid()) return;

        eventDescription.createInstance(out audioSource);
        audioSource.set3DAttributes(RuntimeUtils.To3DAttributes(gameObject));

        // Override FMOD spatialiser attenuation range
        if (OverrideAttenuation)
        {
            audioSource.setProperty(EVENT_PROPERTY.MINIMUM_DISTANCE, OverrideMinDistance);
            audioSource.setProperty(EVENT_PROPERTY.MAXIMUM_DISTANCE, OverrideMaxDistance);
        }

        // Static inspector params
        foreach (var p in Params)
            audioSource.setParameterByID(p.ID, p.Value);

        // Dynamic params set via SetParameter() before instance existed
        foreach (var p in cachedParams)
            audioSource.setParameterByName(p.Name, p.Value);

        // Reverb material
        audioSource.setParameterByName(MaterialParam, materialParameterValue);

        audioSource.start();
        hasStartedEvent = true;
    }

    private void StopAudio()
    {
        if (!hasStartedEvent || !audioSource.isValid()) return;

        audioSource.stop(AllowFadeout ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);
        audioSource.release();
        hasStartedEvent = false;
    }

    private void CacheParam(string name, float value)
    {
        if (!eventDescription.isValid()) Lookup();
        var entry = cachedParams.Find(p => p.Name == name);
        if (entry == null)
        {
            eventDescription.getParameterDescriptionByName(name, out var desc);
            entry = new ParamRef { ID = desc.id, Name = desc.name };
            cachedParams.Add(entry);
        }
        entry.Value = value;
    }

    private void CacheParam(PARAMETER_ID id, float value)
    {
        if (!eventDescription.isValid()) Lookup();
        var entry = cachedParams.Find(p => p.ID.Equals(id));
        if (entry == null)
        {
            eventDescription.getParameterDescriptionByID(id, out var desc);
            entry = new ParamRef { ID = desc.id, Name = desc.name };
            cachedParams.Add(entry);
        }
        entry.Value = value;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Gizmos
    // ═════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.2f, 0.35f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, maxDistance);

        Gizmos.color = new Color(0.2f, 0.4f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, maxParameterDistance);

        // Yellow gizmo for override attenuation
        if (OverrideAttenuation)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            if (OverrideMinDistance > 0f)
                Gizmos.DrawWireSphere(transform.position, OverrideMinDistance);
            if (OverrideMaxDistance > 0f)
                Gizmos.DrawWireSphere(transform.position, OverrideMaxDistance);
        }

#if UNITY_EDITOR
        var redStyle  = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(1f, 0.4f, 0.4f) } };
        var blueStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.4f, 0.6f, 1f) } };
        var yellowStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(1f, 1f, 0f) } };

        Handles.Label(transform.position + transform.forward * maxDistance,
            $"maxDistance: {maxDistance:F1}", redStyle);
        Handles.Label(transform.position + transform.right * maxParameterDistance,
            $"maxParameterDistance: {maxParameterDistance:F1}", blueStyle);

        if (OverrideAttenuation && OverrideMaxDistance > 0f)
        {
            Handles.Label(transform.position + Vector3.up * OverrideMaxDistance,
                $"Override Max: {OverrideMaxDistance:F1}", yellowStyle);
        }
#endif
    }
}