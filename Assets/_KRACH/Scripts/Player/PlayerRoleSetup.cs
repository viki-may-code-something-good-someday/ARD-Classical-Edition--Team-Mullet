using System.Collections;
using Mirror;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Orchestriert die Rollen-Initialisierung beim Betreten der Gameplay-Scene sowie das
/// Fangen/Respawnen von Vandalisten (reagiert auf HunterAccuse.OnVandalistCaught).
/// </summary>
public class PlayerRoleSetup : NetworkBehaviour
{
    [Header("Movement Configs")]
    [SerializeField] private RoleMovementConfig hunterConfig;
    [SerializeField] private RoleMovementConfig vandalistConfig;

    [Header("References")]
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private PlayerInteract interact;
    [SerializeField] private HunterAccuse accuse;

    [Tooltip("Parent aller sichtbaren Spielerelemente. Wird in der Lobby UND im Caught-Zustand deaktiviert.")]
    [SerializeField] private GameObject playerVisuals;

    [Header("Rollen-Modelle")]
    [SerializeField] private GameObject hunterModel;
    [SerializeField] private GameObject vandalistModel;
    [SerializeField] private GameObject hunterModelLocalOnlyShadow;
    [SerializeField] private GameObject vandalistModelLocalOnlyShadow;


    [Header("Respawn Settings")]
    [Tooltip("Ob gefangene Vandalisten respawnen. Wenn false, bleiben sie dauerhaft gefangen.")]
    [SerializeField] private bool allowRespawn = true;
    [SerializeField] private float respawnDelay = 5f;
    [Tooltip("Kurze Unverwundbarkeit nach dem Respawn, damit der Hunter nicht sofort wieder fängt.")]
    [SerializeField] private float respawnInvulnerability = 2f;
    [Tooltip("Catches bis zur endgültigen Elimination, auch bei allowRespawn. -1 = unbegrenzt.")]
    [SerializeField] private int maxCatches = -1;
    [Tooltip("Optionale UI (z.B. Respawn-Countdown), nur für den betroffenen Owner im Caught-Zustand.")]
    [SerializeField] private GameObject caughtStateVisual;

    [Tooltip("Nur für ownerlose Objekte (Test-Dummies): Layer die beim Respawn als Boden gelten." +
             " Echte Spieler brauchen das nicht, ihr CharacterController fällt von selbst.")]
    [SerializeField] private LayerMask ownerlessGroundMask = 8;

    // ── State ────────────────────────────────────────────────────────────────

    [SyncVar(hook = nameof(OnCaughtStateChanged))]
    private bool isCaught;
    public bool IsCaught => isCaught;

    // Als SyncVar statt als lokaler Zeitstempel: Time.time läuft auf Server und Clients
    // unterschiedlich, und der Hunter-Client muss den Zustand für sein Fadenkreuz kennen.
    [SyncVar]
    private bool isInvulnerable;
    public bool IsInvulnerable => isInvulnerable;

    private int catchCount;

    private PlayerObjectController poc;
    private PlayerRole assignedRole;
    private bool initialized;
    private Coroutine initRoutine;
    private Collider[] bodyColliders;

    private string GameplayScene =>
        (NetworkManager.singleton as CustomNetworkManager)?.GameplayScene ?? string.Empty;

    private bool InGameplayScene => SceneManager.GetActiveScene().path == GameplayScene;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        poc = GetComponent<PlayerObjectController>();

        // Nur die Collider erfassen die im Prefab aktiv sind – bewusst deaktivierte bleiben
        // deaktiviert. Der CharacterController ist ausgenommen: den verwaltet PlayerMovement
        // über Freeze/Unfreeze, sonst hätte dieser Zustand zwei Besitzer.
        bodyColliders = System.Array.FindAll(
            GetComponentsInChildren<Collider>(true),
            col => col.enabled && !(col is CharacterController));
    }

    private void Start()
    {
        // Alles ausblenden bis die Gameplay-Scene geladen ist.
        // Der Root bleibt aktiv – Mirror braucht das für den Netzwerk-Sync.
        SetLobbyState();
        EvaluateScene();
    }

    private void OnEnable()
    {
        HunterAccuse.OnVandalistCaught += HandleVandalistCaught;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        if (poc != null) poc.RoleChanged += OnRoleSyncedFromServer;
    }

    private void OnDisable()
    {
        HunterAccuse.OnVandalistCaught -= HandleVandalistCaught;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        if (poc != null) poc.RoleChanged -= OnRoleSyncedFromServer;
    }

    private void OnActiveSceneChanged(Scene from, Scene to) => EvaluateScene();

    private void EvaluateScene()
    {
        if (InGameplayScene) BeginInitialization();
        else SetLobbyState();
    }

    /// <summary>
    /// Die Rolle kommt als SyncVar an und kann den Szenenwechsel überholen oder ihm hinterherlaufen
    /// (Lag, Late Join). Trifft sie später ein als die Initialisierung, wird hier nachgezogen –
    /// sonst bliebe der Spieler dauerhaft auf dem Default-Wert des Prefabs hängen.
    /// </summary>
    private void OnRoleSyncedFromServer(PlayerRole newRole)
    {
        if (!initialized || newRole == assignedRole) return;
        if (!InGameplayScene) return;

        Debug.Log($"[PlayerRoleSetup] Rolle nachträglich geändert: {assignedRole} → {newRole}, Setup wird erneuert.");
        InitializeRole();
    }

    // ── Zustände ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Startet die Initialisierung sobald der LevelManager der Gameplay-Scene bereit ist.
    /// Der LevelManager wird beim Szenenwechsel neu erzeugt, kann also noch fehlen.
    /// </summary>
    private void BeginInitialization()
    {
        if (initialized || initRoutine != null) return;

        initRoutine = StartCoroutine(InitializeWhenLevelReady());
    }

    private IEnumerator InitializeWhenLevelReady()
    {
        while (LevelManager.Instance == null && InGameplayScene)
            yield return null;

        initRoutine = null;

        if (InGameplayScene) InitializeRole();
    }

    private void SetLobbyState()
    {
        initialized = false;

        if (initRoutine != null)
        {
            StopCoroutine(initRoutine);
            initRoutine = null;
        }

        if (playerVisuals != null) playerVisuals.SetActive(false);
        if (hunterModel != null) hunterModel.SetActive(false);
        if (vandalistModel != null) vandalistModel.SetActive(false);

        if (interact != null) interact.OnRoleDeactivated();
        if (accuse != null) accuse.OnRoleDeactivated();
    }

    private void InitializeRole()
    {
        initialized = true;

        if (poc == null)
        {
            Debug.LogError("[PlayerRoleSetup] Kein PlayerObjectController gefunden!");
            return;
        }

        assignedRole = poc.playerRole;

        // Visuals und Aktionen auf allen Clients
        if (playerVisuals != null) playerVisuals.SetActive(true);
        ActivateRoleModel(assignedRole);
        ActivateRoleAction(assignedRole);

        // Config und Spawn nur für den lokalen Spieler (client-autoritatives Movement).
        // Für ownerlose Objekte (Test-Dummies) übernimmt TestDummySpawner die Startposition.
        if (!isOwned) return;

        // Hide own body mesh for the local camera, but keep the shadow.
        // Scoped to this instance only (isOwned is per-instance), so other players' models stay visible.
        ApplyLocalPlayerVisibility();

        RoleMovementConfig config = assignedRole == PlayerRole.Hunter ? hunterConfig : vandalistConfig;
        if (config == null || movement == null)
        {
            Debug.LogError($"[PlayerRoleSetup] Config oder PlayerMovement fehlt für Rolle {assignedRole}!");
            return;
        }

        movement.ApplyConfig(config);
        movement.TeleportTo(PickSpawnPosition(assignedRole));
    }

    private void ActivateRoleModel(PlayerRole role)
    {
        bool isHunter = role == PlayerRole.Hunter;
        if (hunterModel != null) hunterModel.SetActive(isHunter);
        if (vandalistModel != null) vandalistModel.SetActive(!isHunter);
    }

    /// <summary>
    /// Hides this player's own renderers from their own view while keeping the shadow visible.
    /// Only ever called on the active local player
    /// </summary>
    private void ApplyLocalPlayerVisibility()
    {
        Renderer[] renderers;

        if (assignedRole == PlayerRole.Vandalist)
        {
            renderers = vandalistModelLocalOnlyShadow.GetComponentsInChildren<Renderer>(true);

        }
        else if (assignedRole == PlayerRole.Hunter)
        {
            renderers = hunterModelLocalOnlyShadow.GetComponentsInChildren<Renderer>(true);
        }
        else
        {
            Debug.LogWarning($"[PlayerRoleSetup] Unbekannte Rolle {assignedRole}, keine Modelle wurden local ausgeblendet.");
            return;
        }

        foreach (Renderer r in renderers)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }
    }

    private void ActivateRoleAction(PlayerRole role)
    {
        SetInteractActive(role == PlayerRole.Vandalist);

        if (accuse != null)
        {
            if (role == PlayerRole.Hunter) accuse.OnRoleActivated();
            else accuse.OnRoleDeactivated();
        }
    }

    private void SetInteractActive(bool active)
    {
        if (interact == null) return;

        if (active) interact.OnRoleActivated();
        else interact.OnRoleDeactivated();
    }

    // ── Spawn / Respawn ──────────────────────────────────────────────────────

    private Vector3 PickSpawnPosition(PlayerRole role)
    {
        Transform[] spawnPoints = role == PlayerRole.Hunter
            ? LevelManager.Instance.hunterSpawnPositions
            : LevelManager.Instance.vandalistSpawnPositions;

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning($"[PlayerRoleSetup] Keine Spawn-Positionen für {role}!");
            return transform.position;
        }

        return spawnPoints[Random.Range(0, spawnPoints.Length)].position;
    }

    /// <summary>
    /// Server-autoritativer Respawn. Echte Spieler bekommen einen TargetRpc (Movement ist
    /// client-authoritative), ownerlose Objekte wie Test-Dummies setzt der Server direkt.
    /// </summary>
    [Server]
    private void ServerRespawn(PlayerRole role)
    {
        Vector3 spawnPos = PickSpawnPosition(role);

        if (connectionToClient != null && movement != null)
        {
            TargetTeleport(connectionToClient, spawnPos);
            return;
        }

        // Ownerlos (Test-Dummy): kein CharacterController der den Spawnpunkt-Offset ausgleicht,
        // also selbst auf dem Boden absetzen. Sonst schwebt der Dummy nach dem Respawn.
        transform.position = SpawnPlacement.DropToGround(
            spawnPos, GetComponent<CapsuleCollider>(), ownerlessGroundMask);
    }

    [TargetRpc]
    private void TargetTeleport(NetworkConnectionToClient target, Vector3 position)
    {
        if (movement != null) movement.TeleportTo(position);
    }

    // ── Catch / Respawn-Logik ────────────────────────────────────────────────

    private void HandleVandalistCaught(NetworkIdentity caught)
    {
        if (caught != netIdentity) return;
        if (!isServer) return;  // nur der Server entscheidet über Catch/Respawn
        if (isCaught) return;   // bereits im Caught/Respawn-Prozess

        HandleCaughtServer();
    }

    [Server]
    private void HandleCaughtServer()
    {
        catchCount++;
        isInvulnerable = false;
        isCaught = true;      // SyncVar-Hook aktualisiert Visuals/Collider auf allen Clients
        ApplyCaughtState(true); // Dedicated Server: dort feuert der Hook nicht

        if (!allowRespawn || (maxCatches >= 0 && catchCount > maxCatches))
        {
            Debug.Log($"[PlayerRoleSetup] {name} wurde endgültig eliminiert (Catch #{catchCount}).");
            // TODO: an Round-/GameManager melden für den Win-Condition-Check.
            return;
        }

        StartCoroutine(RespawnAfterDelay());
    }

    [Server]
    private IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnDelay);

        ServerRespawn(PlayerRole.Vandalist);
        isInvulnerable = true;
        isCaught = false;
        ApplyCaughtState(false);

        yield return new WaitForSeconds(respawnInvulnerability);
        isInvulnerable = false;
    }

    /// <summary>Läuft auf allen Clients (inkl. Host), sobald sich isCaught ändert.</summary>
    private void OnCaughtStateChanged(bool oldValue, bool newValue) => ApplyCaughtState(newValue);

    /// <summary>
    /// Gleicht Sichtbarkeit, Collider und Steuerung an den Caught-Zustand an.
    /// Bewusst idempotent: der SyncVar-Hook deckt Clients und Host ab, der Server-Aufruf
    /// den Dedicated-Server-Fall, in dem Mirror den Hook nicht feuert.
    /// </summary>
    private void ApplyCaughtState(bool caught)
    {
        if (playerVisuals != null) playerVisuals.SetActive(!caught);

        // Ein gefangener Spieler darf kein unsichtbares Hindernis sein: keine Kollision,
        // kein Raycast-Ziel, keine Trigger. Läuft auf allen Clients UND dem Server, damit
        // auch die serverseitige Trefferprüfung ihn nicht mehr findet.
        SetBodyCollidersEnabled(!caught);

        if (isOwned && caughtStateVisual != null)
            caughtStateVisual.SetActive(caught);

        if (!isOwned) return;

        if (movement != null)
        {
            if (caught) movement.FreezeMovement();
            else movement.UnfreezeMovement();
        }

        if (assignedRole == PlayerRole.Vandalist)
            SetInteractActive(!caught);
    }

    private void SetBodyCollidersEnabled(bool value)
    {
        if (bodyColliders == null) return;

        foreach (Collider col in bodyColliders)
        {
            if (col != null) col.enabled = value;
        }
    }

    // ── Debug ─────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    [Button("Validate Setup")]
    private void ValidateSetup()
    {
        bool ok = true;

        CustomNetworkManager mgr = NetworkManager.singleton as CustomNetworkManager;
        if (mgr == null)
            Debug.LogWarning("[PlayerRoleSetup] NetworkManager nicht gefunden (nur im Play Mode prüfbar).");
        else if (string.IsNullOrEmpty(mgr.GameplayScene))
        { Debug.LogError("[PlayerRoleSetup] GameplayScene im NetworkManager nicht gesetzt!"); ok = false; }

        if (hunterConfig == null) { Debug.LogError("[PlayerRoleSetup] hunterConfig fehlt!"); ok = false; }
        if (vandalistConfig == null) { Debug.LogError("[PlayerRoleSetup] vandalistConfig fehlt!"); ok = false; }
        if (movement == null) { Debug.LogError("[PlayerRoleSetup] PlayerMovement fehlt!"); ok = false; }
        if (playerVisuals == null) Debug.LogWarning("[PlayerRoleSetup] playerVisuals nicht gesetzt – Lobby-Hiding funktioniert nicht.");
        if (hunterModel == null) Debug.LogWarning("[PlayerRoleSetup] hunterModel nicht gesetzt.");
        if (vandalistModel == null) Debug.LogWarning("[PlayerRoleSetup] vandalistModel nicht gesetzt.");
        if (interact == null) Debug.LogWarning("[PlayerRoleSetup] PlayerInteract fehlt.");
        if (accuse == null) Debug.LogWarning("[PlayerRoleSetup] HunterAccuse fehlt.");
        if (allowRespawn && respawnDelay <= 0f)
            Debug.LogWarning("[PlayerRoleSetup] respawnDelay ist 0 oder negativ – Spieler respawnt sofort.");

        if (ok) Debug.Log("[PlayerRoleSetup] Setup vollständig.");
    }

    [Button("Debug: Force Catch")]
    private void DebugForceCatch()
    {
        if (!Application.isPlaying || !isServer)
        {
            Debug.LogWarning("[PlayerRoleSetup] Nur im Play Mode als Server nutzbar.");
            return;
        }
        if (isCaught)
        {
            Debug.LogWarning("[PlayerRoleSetup] Spieler ist bereits gefangen.");
            return;
        }

        HandleCaughtServer();
    }
#endif
}
