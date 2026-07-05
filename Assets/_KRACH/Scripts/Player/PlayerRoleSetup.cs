using System.Collections;
using Mirror;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Orchestriert die Rollen-Initialisierung beim Betreten der Gameplay-Scene,
/// sowie das Fangen/Respawnen von Vandalisten (reagiert auf HunterAccuse.OnVandalistCaught).
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

    [Tooltip("Parent-GameObject aller sichtbaren Spielerelemente (Modell, Arme, etc.)." +
             " Wird in der Lobby UND während des Caught-Zustands deaktiviert.")]
    [SerializeField] private GameObject playerVisuals;

    [Header("Rollen-Modelle")]
    [Tooltip("Modell/Hierarchie die nur beim Hunter aktiv sein soll (z.B. Hunter-Mesh, Arme, Ausrüstung).")]
    [SerializeField] private GameObject hunterModel;
    [Tooltip("Modell/Hierarchie die nur beim Vandalist aktiv sein soll (z.B. Vandalist-Mesh, Arme, Ausrüstung).")]
    [SerializeField] private GameObject vandalistModel;

    [Header("Respawn Settings")]
    [Tooltip("Ob gefangene Vandalisten nach respawnDelay respawnen. Wenn false, bleiben sie dauerhaft gefangen.")]
    [SerializeField] private bool allowRespawn = true;
    [Tooltip("Wartezeit in Sekunden zwischen Fangen und Respawn.")]
    [SerializeField] private float respawnDelay = 5f;
    [Tooltip("Kurze Unverwundbarkeit nach dem Respawn, damit der Hunter nicht sofort wieder fängt.")]
    [SerializeField] private float respawnInvulnerability = 2f;
    [Tooltip("Maximale Anzahl an Catches bevor der Spieler dauerhaft eliminiert wird, auch wenn allowRespawn true ist. -1 = unbegrenzt.")]
    [SerializeField] private int maxCatches = -1;
    [Tooltip("Optionales GameObject (z.B. Respawn-Countdown-UI), das nur für den betroffenen Owner während des Caught-Zustands aktiviert wird.")]
    [SerializeField] private GameObject caughtStateVisual;

    // ── Netzwerk-State ───────────────────────────────────────────────────────

    [SyncVar(hook = nameof(OnCaughtStateChanged))]
    private bool isCaught = false;
    public bool IsCaught => isCaught;

    private int catchCount = 0;
    private float invulnerableUntil = -1f;
    public bool IsInvulnerable => Time.time < invulnerableUntil;

    private PlayerRole assignedRole;

    private string GameplayScene =>
        (NetworkManager.singleton as CustomNetworkManager)?.GameplayScene ?? string.Empty;

    private bool initialized = false;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Start()
    {
        // Alles ausblenden bis die Gameplay-Scene geladen ist.
        // Der Root bleibt aktiv – Mirror braucht das für Netzwerk-Sync.
        SetLobbyState();
    }

    private void OnEnable()
    {
        HunterAccuse.OnVandalistCaught += HandleVandalistCaught;
    }

    private void OnDisable()
    {
        HunterAccuse.OnVandalistCaught -= HandleVandalistCaught;
    }

    // ── Update ───────────────────────────────────────────────────────────────

    void Update()
    {
        bool inGameplay = SceneManager.GetActiveScene().path == GameplayScene;

        if (initialized)
        {
            if (!inGameplay)
                SetLobbyState();
            return;
        }

        if (!inGameplay) return;
        if (LevelManager.Instance == null) return;

        InitializeRole();
    }

    // ── Zustände ─────────────────────────────────────────────────────────────

    private void SetLobbyState()
    {
        initialized = false;

        if (playerVisuals != null)
            playerVisuals.SetActive(false);

        // Beide Rollen-Modelle in der Lobby ausblenden
        if (hunterModel != null) hunterModel.SetActive(false);
        if (vandalistModel != null) vandalistModel.SetActive(false);

        // Aktionen deaktivieren
        if (interact != null) interact.OnRoleDeactivated();
        if (accuse != null) accuse.OnRoleDeactivated();
    }

    private void InitializeRole()
    {
        initialized = true;

        PlayerObjectController poc = GetComponent<PlayerObjectController>();
        if (poc == null)
        {
            Debug.LogError("[PlayerRoleSetup] Kein PlayerObjectController gefunden!");
            return;
        }

        PlayerRole role = poc.playerRole;
        assignedRole = role;

        // Visuals und Aktionen auf allen Clients aktivieren
        if (playerVisuals != null)
            playerVisuals.SetActive(true);

        ActivateRoleModel(role);
        ActivateRoleAction(role);

        // Config und initialer Spawn nur für den lokalen Spieler (client-autoritatives Movement).
        // Für ownerlose Objekte (z.B. Test-Dummies) übernimmt TestDummySpawner die initiale Position.
        if (!isOwned) return;

        RoleMovementConfig config = role == PlayerRole.Hunter ? hunterConfig : vandalistConfig;
        if (config == null)
        {
            Debug.LogError($"[PlayerRoleSetup] Kein RoleMovementConfig für Rolle {role}!");
            return;
        }

        movement.ApplyConfig(config);
        TeleportToSpawn(role);
    }

    private void ActivateRoleModel(PlayerRole role)
    {
        bool isHunter = role == PlayerRole.Hunter;

        if (hunterModel != null) hunterModel.SetActive(isHunter);
        if (vandalistModel != null) vandalistModel.SetActive(!isHunter);
    }

    private void ActivateRoleAction(PlayerRole role)
    {
        if (interact != null)
        {
            if (role == PlayerRole.Vandalist) interact.OnRoleActivated();
            else interact.OnRoleDeactivated();
        }

        if (accuse != null)
        {
            if (role == PlayerRole.Hunter) accuse.OnRoleActivated();
            else accuse.OnRoleDeactivated();
        }
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
    /// Initialer Spawn beim Rollen-Setup. Läuft lokal auf dem Owner (client-autoritatives Movement).
    /// </summary>
    private void TeleportToSpawn(PlayerRole role)
    {
        movement.TeleportTo(PickSpawnPosition(role));
    }

    /// <summary>
    /// Server-autoritativer Respawn. Funktioniert sowohl für echte Spieler (Owner bekommt
    /// gezielten TargetRpc-Teleport-Befehl, da Movement client-authoritative ist) als auch für
    /// ownerlose Objekte wie Test-Dummies (Server setzt Position direkt, da er dort Authority ist –
    /// setzt voraus dass NetworkTransform für ownerlose Objekte Server-Authority nutzt).
    /// </summary>
    [Server]
    private void ServerRespawn(PlayerRole role)
    {
        Vector3 spawnPos = PickSpawnPosition(role);

        if (connectionToClient != null && movement != null)
        {
            TargetTeleport(connectionToClient, spawnPos);
        }
        else
        {
            transform.position = spawnPos;
        }
    }

    [TargetRpc]
    private void TargetTeleport(NetworkConnectionToClient target, Vector3 position)
    {
        if (movement != null)
            movement.TeleportTo(position);
    }

    // ── Catch / Respawn-Logik ────────────────────────────────────────────────

    private void HandleVandalistCaught(NetworkIdentity caught)
    {
        if (caught == null || caught != netIdentity) return;
        if (!isServer) return;      // Nur der Server entscheidet über Catch/Respawn
        if (isCaught) return;       // Bereits im Caught/Respawn-Prozess, Duplikat ignorieren

        HandleCaughtServer();
    }

    [Server]
    private void HandleCaughtServer()
    {
        catchCount++;
        isCaught = true; // SyncVar-Hook aktualisiert Visuals/Movement auf allen Clients automatisch

        bool eliminated = !allowRespawn || (maxCatches >= 0 && catchCount > maxCatches);
        if (eliminated)
        {
            Debug.Log($"[PlayerRoleSetup] {name} wurde endgültig eliminiert (Catch #{catchCount}).");
            // TODO: Hier später an einen Round-/GameManager melden für Win-Condition-Check (z.B.
            // "alle Vandalisten eliminiert" → Hunter gewinnt).
            return;
        }

        StartCoroutine(RespawnAfterDelay());
    }

    [Server]
    private IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnDelay);

        ServerRespawn(PlayerRole.Vandalist);
        invulnerableUntil = Time.time + respawnInvulnerability;
        isCaught = false; // SyncVar-Hook blendet Visuals wieder ein / entfriert Movement
    }

    /// <summary>
    /// Läuft automatisch auf allen Clients (inkl. Server), sobald sich isCaught ändert.
    /// </summary>
    private void OnCaughtStateChanged(bool oldValue, bool newValue)
    {
        // Modell während des Caught-Zustands für alle ausblenden
        if (playerVisuals != null)
            playerVisuals.SetActive(!newValue);

        // Optionales UI (z.B. Respawn-Countdown) nur für den betroffenen Spieler selbst
        if (isOwned && caughtStateVisual != null)
            caughtStateVisual.SetActive(newValue);

        // Movement/Interact nur lokal beim Owner (de-)aktivieren
        if (!isOwned) return;

        if (movement != null)
        {
            if (newValue) movement.FreezeMovement();
            else movement.UnfreezeMovement();
        }

        if (interact != null && assignedRole == PlayerRole.Vandalist)
        {
            if (newValue) interact.OnRoleDeactivated();
            else interact.OnRoleActivated();
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
        {
            Debug.LogError("[PlayerRoleSetup] GameplayScene im NetworkManager nicht gesetzt!");
            ok = false;
        }

        if (hunterConfig == null) { Debug.LogError("[PlayerRoleSetup] hunterConfig fehlt!"); ok = false; }
        if (vandalistConfig == null) { Debug.LogError("[PlayerRoleSetup] vandalistConfig fehlt!"); ok = false; }
        if (movement == null) { Debug.LogError("[PlayerRoleSetup] PlayerMovement fehlt!"); ok = false; }
        if (playerVisuals == null) { Debug.LogWarning("[PlayerRoleSetup] playerVisuals nicht gesetzt – Lobby-Hiding funktioniert nicht."); }
        if (hunterModel == null) { Debug.LogWarning("[PlayerRoleSetup] hunterModel nicht gesetzt – Hunter-Modell wird nicht umgeschaltet."); }
        if (vandalistModel == null) { Debug.LogWarning("[PlayerRoleSetup] vandalistModel nicht gesetzt – Vandalist-Modell wird nicht umgeschaltet."); }
        if (interact == null) { Debug.LogWarning("[PlayerRoleSetup] PlayerInteract fehlt."); }
        if (accuse == null) { Debug.LogWarning("[PlayerRoleSetup] HunterAccuse fehlt."); }
        if (allowRespawn && respawnDelay <= 0f) { Debug.LogWarning("[PlayerRoleSetup] respawnDelay ist 0 oder negativ – Spieler respawnt sofort."); }

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

        HandleCaughtServer();
    }
#endif
}