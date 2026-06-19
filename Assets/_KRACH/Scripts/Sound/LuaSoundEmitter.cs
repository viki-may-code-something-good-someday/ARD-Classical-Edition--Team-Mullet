using System.Collections.Generic;
using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using Sirenix.OdinInspector;
using Mirror;

#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using Sirenix.OdinInspector.Editor;
[CustomEditor(typeof(LuaSoundEmitter))]
public class LuaSoundEmitterEditor : OdinEditor { }

// Hides the "CollisionTag" field inherited from FMODUnity.EventHandler in the inspector.
public class LuaSoundEmitterAttributeProcessor : OdinAttributeProcessor<LuaSoundEmitter>
{
    public override void ProcessChildMemberAttributes(InspectorProperty parentProperty, MemberInfo member, List<Attribute> attributes)
    {
        if (member.Name == nameof(FMODUnity.EventHandler.CollisionTag))
            attributes.Add(new HideInInspector());
    }
}
#endif

/// <summary>
/// When a LuaSoundEmitter trigger fires. Mirrors FMOD's EmitterGameEvent value-for-value
/// (so existing inspector/prefab data is preserved when the field type changed) and adds
/// <see cref="Script"/> for emitters that are driven purely from code — i.e. you call
/// Play() / Stop() / PlayOneShot() yourself; no game event auto-fires them.
/// </summary>
public enum SoundTrigger
{
    None             = 0,
    Script           = 100, // not a game event — triggered from code only
    ObjectStart      = 1,
    ObjectDestroy    = 2,
    TriggerEnter     = 3,
    TriggerExit      = 4,
    TriggerEnter2D   = 5,
    TriggerExit2D    = 6,
    CollisionEnter   = 7,
    CollisionExit    = 8,
    CollisionEnter2D = 9,
    CollisionExit2D  = 10,
    ObjectEnable     = 11,
    ObjectDisable    = 12,
    ObjectMouseEnter = 13,
    ObjectMouseExit  = 14,
    ObjectMouseDown  = 15,
    ObjectMouseUp    = 16,
    UIMouseEnter     = 17,
    UIMouseExit      = 18,
    UIMouseDown      = 19,
    UIMouseUp        = 20,
}

[AddComponentMenu("Lua/LuaSoundEmitter")]
public class LuaSoundEmitter : FMODUnity.EventHandler
{
    // ═════════════════════════════════════════════════════════════════════════
    // Multiplayer (Mirror) — optional. When disabled this behaves like a plain,
    // local-only emitter; the sync settings below are hidden and ignored.
    // ═════════════════════════════════════════════════════════════════════════

    [Header("Multiplayer (Mirror)")]
    [Tooltip("When enabled, this emitter respects the Mirror Sync Mode below. When disabled it plays locally like a normal emitter.")]
    [SerializeField] private bool enableMultiplayer = false;
    [ShowIf("IsMultiplayerEnabled")]
    [SerializeField] private MirrorSyncMode mirrorSyncMode = MirrorSyncMode.All;
    [ShowIf("IsMirrorTargetMode")]
    [InfoBox("Target = netId des Player-Avatars, der diesen Emitter hören soll. netIds werden erst zur Laufzeit vergeben — daher per Code setzen: SetTargetClient(playerNetworkIdentity). 0 = niemand.")]
    [SerializeField] private uint targetClientId = 0;

    private bool IsMultiplayerEnabled() => enableMultiplayer;
    private bool IsMirrorTargetMode()   => enableMultiplayer && mirrorSyncMode == MirrorSyncMode.Target;
    private bool IsOcclusionEnabled()   => enableOcclusion;
    private bool IsOneShotCooldownEnabled() => enableOneShotCooldown;

    public enum MirrorSyncMode
    {
        OnlyOwner,
        All,
        AllButOwner,
        Target
    }

    // NetworkIdentity for Mirror networking (only used when enableMultiplayer)
    private NetworkIdentity networkIdentity;

    // ═════════════════════════════════════════════════════════════════════════
    // Event Settings
    // ═════════════════════════════════════════════════════════════════════════

    [Header("Event")]
    public EventReference EventReference;

    [Header("Emitter Settings")]
    [InfoBox("'Script' = wird von keinem Game-Event ausgelöst, sondern nur per Code: Play() / Stop() / PlayOneShot().")]
    public SoundTrigger EventPlayTrigger = SoundTrigger.None;
    public SoundTrigger EventStopTrigger = SoundTrigger.None;
    [InfoBox("OneShotTrigger feuert PlayOneShot() — unabhängig vom persistenten Play()/Stop()-State. Für discrete Events (Collision, Punch-Hit, Pickup) statt EventPlayTrigger nutzen.")]
    public SoundTrigger OneShotTrigger = SoundTrigger.None;
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
    [ShowIf("IsOcclusionEnabled")]
    [SerializeField] private LayerMask obstacleLayer;
    [Space(5)]
    [ShowIf("IsOcclusionEnabled")]
    [SerializeField] private float maxDistance = 20f;
    [ShowIf("IsOcclusionEnabled")]
    [SerializeField] private float maxParameterDistance = 30f;
    [Space(5)]
    [ShowIf("IsOcclusionEnabled")]
    [SerializeField] private bool invertFadeRange = false;
    [ShowIf("IsOcclusionEnabled")]
    [SerializeField] private bool stopAudioWhenOutOfRange = false;
    [ShowIf("IsOcclusionEnabled")]
    [SerializeField] private string nonOcclusionTag = "NoOcclusion";
    private Transform playerTransform;

    [Header("Reverb")]
    [SerializeField] private ReverbType selectedMaterial = ReverbType.Room;
    [Space(10)]

    [Header("One-Shot Cooldown")]
    [SerializeField] private bool enableOneShotCooldown = false;
    [ShowIf("IsOneShotCooldownEnabled")]
    [SerializeField] private float oneShotCooldownDuration = 1f;
    [Space(10)]

    [ShowIf("IsOcclusionEnabled")]
    [Header("Debug")]
    [SerializeField] private float scaledValue;
    [ShowIf("IsOcclusionEnabled")]
    [SerializeField] private bool isObstructed;

    // ── FMOD handles (persistent/looping instance) ──────────────────────────────
    private EventInstance    audioSource;
    private EventDescription eventDescription;
    private List<ParamRef>   cachedParams = new List<ParamRef>();

    // ── State ─────────────────────────────────────────────────────────────────
    private bool hasStartedEvent;
    private bool hasTriggered;        // gates the persistent Play() path (TriggerOnce)
    private bool hasTriggeredOneShot; // gates the PlayOneShot() path (TriggerOnce)
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

        if (enableMultiplayer)
            networkIdentity = GetComponent<NetworkIdentity>();

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
        // One-shots are fire-and-forget and should continue playing even after this emitter is destroyed.

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

        // Mirror sync: clients that shouldn't hear this emitter never start/keep the loop.
        // (No-op when multiplayer is disabled — ShouldReceiveAudio() then always returns true.)
        if (!ShouldReceiveAudio())
        {
            StopAudio();
            return;
        }

        // Only do occlusion/distance-based logic if occlusion is enabled and a listener exists
        if (enableOcclusion)
        {
            // Auto-bind the listener when none is assigned in the inspector. In multiplayer
            // this resolves to THIS client's own local player, which is what makes occlusion
            // correct with multiple players (see ResolveListenerTransform).
            if (playerTransform == null)
                playerTransform = ResolveListenerTransform();

            if (playerTransform == null)
            {
                StopAudio();
                return;
            }

            float distToPlayer = Vector3.Distance(transform.position, playerTransform.position);

            // Player out of maxDistance → no audio
            if (distToPlayer > maxDistance)
            {
                isObstructed = false;
                if (stopAudioWhenOutOfRange) StopAudio();
                return;
            }

            Vector3 dir = playerTransform.position - transform.position;

            // ── Distance-based fade ───────────────────────────────────────────
            float normalized = Mathf.Clamp01(1f - distToPlayer / (maxParameterDistance - MinDistance));
            float fadeValue  = invertFadeRange
                ? Mathf.Clamp01(distToPlayer / (maxParameterDistance - MinDistance))
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

            // ── Occlusion via RaycastAll ──────────────────────────────────────
            // RaycastAll correctly handles destroyed obstacles: their colliders are
            // removed from the physics scene immediately on Destroy(), so they never
            // appear in the results — no phantom occlusion after destruction.
            RaycastHit[] hits = Physics.RaycastAll(transform.position, dir, distToPlayer, obstacleLayer);

            bool anyObstacle = false;
            foreach (RaycastHit h in hits)
            {
                // Skip destroyed or null colliders (safety guard)
                if (h.collider == null) continue;

                // Skip objects tagged to bypass occlusion
                if (!string.IsNullOrEmpty(nonOcclusionTag) && h.collider.CompareTag(nonOcclusionTag)) continue;

                // The hit must lie between emitter and player (small epsilon avoids self-hits)
                if (Vector3.Distance(transform.position, h.point) < distToPlayer - 0.05f)
                {
                    anyObstacle = true;
                    break;
                }
            }

            isObstructed = anyObstacle;
            audioSource.setParameterByName(OcclusionParam, isObstructed ? 1f : 0f);
        }
        else
        {
            // When occlusion is disabled, just ensure audio is started
            EnsureInstanceStarted();
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Event Handling
    // ═════════════════════════════════════════════════════════════════════════

    protected override void HandleGameEvent(EmitterGameEvent gameEvent)
    {
        if (TriggeredBy(EventPlayTrigger, gameEvent)) Play();
        if (TriggeredBy(EventStopTrigger, gameEvent)) Stop();
        if (TriggeredBy(OneShotTrigger, gameEvent)) PlayOneShot();
    }

    /// <summary>True if a game event matches this trigger. 'Script' (and 'None') never
    /// match a game event — those emitters are started from code instead.</summary>
    private static bool TriggeredBy(SoundTrigger trigger, EmitterGameEvent gameEvent)
        => trigger != SoundTrigger.None
        && trigger != SoundTrigger.Script
        && (EmitterGameEvent)trigger == gameEvent;

    // ═════════════════════════════════════════════════════════════════════════
    // Public API — Persistent / Loop Path
    // ═════════════════════════════════════════════════════════════════════════

    public void Play()
    {
        if (TriggerOnce && hasTriggered) return;
        if (enableOneShotCooldown && oneShotCooldownTimer > 0f) return;
        if (EventReference.IsNull) return;
        if (!eventDescription.isValid()) Lookup();

        IsActive = true;

        if (TriggerOnce)
        {
            hasTriggered = true;
        }

        if (enableOneShotCooldown)
        {
            oneShotCooldownTimer = oneShotCooldownDuration;
        }

        EnsureInstanceStarted();
    }

    public void Stop()
    {
        IsActive = false;
        cachedParams.Clear();
        StopAudio();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Public API — One-Shot Path (discrete, repeatable, fire-and-forget)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fires a brand-new, independent FMOD instance every call. Does NOT touch
    /// IsActive/hasStartedEvent — those belong exclusively to the persistent
    /// Play()/Stop() loop path. Use this for discrete repeatable triggers like
    /// OnCollisionEnter, punch hits, or pickup events.
    ///
    /// One-shots are fire-and-forget: they play from their creation point and continue
    /// playing even if this emitter is destroyed. They are NOT attached to this emitter.
    ///
    /// Optionally seeds the instance velocity so FMOD's built-in "Speed (Absolute)"
    /// parameter reflects it — e.g. impact speed for velocity-scaled prop collisions.
    /// The sound designer maps that built-in parameter to Volume/Pitch in FMOD Studio;
    /// the code only feeds the velocity (this also drives Doppler).
    /// </summary>
    public void PlayOneShot(Vector3 velocity = default)
    {
        if (!ShouldReceiveAudio()) return; // Mirror sync gate — don't consume TriggerOnce/cooldown on muted clients
        if (TriggerOnce && hasTriggeredOneShot) return;
        if (enableOneShotCooldown && oneShotCooldownTimer > 0f) return;
        if (EventReference.IsNull) return;

        if (TriggerOnce)
        {
            hasTriggeredOneShot = true;
        }

        if (enableOneShotCooldown)
        {
            oneShotCooldownTimer = oneShotCooldownDuration;
        }

        if (!eventDescription.isValid()) Lookup();
        if (!eventDescription.isValid()) return;

        eventDescription.createInstance(out var instance);
        // Set 3D position at creation time (not attached to transform)
        FMOD.ATTRIBUTES_3D attrs = RuntimeUtils.To3DAttributes(transform, velocity);
        instance.set3DAttributes(attrs);

        if (OverrideAttenuation)
        {
            instance.setProperty(EVENT_PROPERTY.MINIMUM_DISTANCE, OverrideMinDistance);
            instance.setProperty(EVENT_PROPERTY.MAXIMUM_DISTANCE, OverrideMaxDistance);
        }

        foreach (var p in Params)
            instance.setParameterByID(p.ID, p.Value);

        instance.setParameterByName(MaterialParam, materialParameterValue);

        instance.start();
        instance.release(); // FMOD owns and manages this instance from now on; it will release automatically when playback finishes
    }

    /// <summary>Set a parameter by name. Value is cached and re-applied if the persistent instance is recreated.
    /// Does not affect already-fired one-shots (they are independent and short-lived by design).</summary>
    public void SetParameter(string name, float value, bool ignoreSeekSpeed = false)
    {
        CacheParam(name, value);
        if (audioSource.isValid())
            audioSource.setParameterByName(name, value, ignoreSeekSpeed);
    }

    /// <summary>Set a parameter by ID. Value is cached and re-applied if the persistent instance is recreated.</summary>
    public void SetParameter(PARAMETER_ID id, float value, bool ignoreSeekSpeed = false)
    {
        CacheParam(id, value);
        if (audioSource.isValid())
            audioSource.setParameterByID(id, value, ignoreSeekSpeed);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Mirror Sync Helper
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// True if the local machine is the "owner" of this emitter's object.
    /// Works for both player avatars / client-authority objects (via isOwned) and
    /// server-spawned world objects that have no client authority (Billboard, SoundBox):
    /// in that case the host/server counts as the owner.
    ///
    /// NOTE: isLocalPlayer is ALWAYS false on non-player objects, so it cannot be used
    /// as the owner signal for world emitters.
    /// </summary>
    private bool IsOwner()
    {
        if (networkIdentity == null) return true; // no networking → treat as local

        // We hold authority over this object (player avatar or client-authority object).
        if (networkIdentity.isOwned) return true;

        // No client owns it → it is server-owned. The host/server is then the owner.
        if (networkIdentity.isServer && networkIdentity.connectionToClient == null) return true;

        return false;
    }

    private bool ShouldReceiveAudio()
    {
        // Single-player / multiplayer disabled → always play locally.
        if (!enableMultiplayer) return true;

        switch (mirrorSyncMode)
        {
            case MirrorSyncMode.OnlyOwner:
                return IsOwner();
            case MirrorSyncMode.All:
                return true;
            case MirrorSyncMode.AllButOwner:
                return !IsOwner();
            case MirrorSyncMode.Target:
                return targetClientId != 0 &&
                       NetworkClient.localPlayer != null &&
                       NetworkClient.localPlayer.netId == targetClientId;
            default:
                return true;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Mirror Sync — Target API (runtime)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Current target player netId for MirrorSyncMode.Target (0 = nobody).</summary>
    public uint TargetClientId => targetClientId;

    /// <summary>
    /// Points the Target sync mode at a specific player by netId. netIds are assigned at
    /// runtime, so this is the intended way to drive Target mode from gameplay code.
    /// Pass 0 to clear (nobody hears it). Takes effect immediately for the looping path
    /// and for the next PlayOneShot().
    /// </summary>
    public void SetTargetClient(uint netId) => targetClientId = netId;

    /// <summary>Points the Target sync mode at a specific player avatar. Null clears the target.</summary>
    public void SetTargetClient(NetworkIdentity targetPlayer)
        => targetClientId = targetPlayer != null ? targetPlayer.netId : 0u;

    /// <summary>Clears the Target sync mode target (nobody hears it).</summary>
    public void ClearTarget() => targetClientId = 0;

    // ═════════════════════════════════════════════════════════════════════════
    // Private Helpers
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the transform the occlusion raycast should aim at — the LOCAL listener.
    /// This is deliberately independent of the multiplayer sync flag: the listener is
    /// always "where this client hears from", which in any networked session is this
    /// client's own player (NetworkClient.localPlayer). That is what makes occlusion
    /// correct with multiple players — every client raycasts toward the avatar it
    /// actually controls, instead of one shared playerTransform that fits at most one
    /// client. Prefers the player's FMOD StudioListener (the real "ears"), else the
    /// player root. With no networked local player it falls back to an active scene
    /// FMOD listener, then Camera.main.
    /// Returns null if nothing suitable exists yet (e.g. local player not spawned).
    /// </summary>
    private Transform ResolveListenerTransform()
    {
        // Networked session → this client's own player is the listener.
        if (NetworkClient.active && NetworkClient.localPlayer != null)
        {
            NetworkIdentity localPlayer = NetworkClient.localPlayer;
            StudioListener listener = localPlayer.GetComponentInChildren<StudioListener>();
            return listener != null ? listener.transform : localPlayer.transform;
        }

        // Single-player / not yet connected → first ACTIVE FMOD listener, else Camera.main.
        foreach (StudioListener l in FindObjectsByType<StudioListener>(FindObjectsSortMode.None))
            if (l.isActiveAndEnabled) return l.transform;

        return Camera.main != null ? Camera.main.transform : null;
    }

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
        if (!ShouldReceiveAudio()) return; // Mirror sync gate — covers the direct Play() path too

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