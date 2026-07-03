using Mirror;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Orchestriert die Rollen-Initialisierung beim Betreten der Gameplay-Scene.
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
             " Wird in der Lobby komplett deaktiviert.")]
    [SerializeField] private GameObject playerVisuals;

    [Header("Rollen-Modelle")]
    [Tooltip("Modell/Hierarchie die nur beim Hunter aktiv sein soll (z.B. Hunter-Mesh, Arme, Ausrüstung).")]
    [SerializeField] private GameObject hunterModel;
    [Tooltip("Modell/Hierarchie die nur beim Vandalist aktiv sein soll (z.B. Vandalist-Mesh, Arme, Ausrüstung).")]
    [SerializeField] private GameObject vandalistModel;

    private string GameplayScene =>
        (NetworkManager.singleton as CustomNetworkManager)?.GameplayScene ?? string.Empty;

    private bool initialized = false;

    // ── Start: Lobby-Zustand ─────────────────────────────────────────────────

    private void Start()
    {
        // Alles ausblenden bis die Gameplay-Scene geladen ist.
        // Der Root bleibt aktiv – Mirror braucht das für Netzwerk-Sync.
        SetLobbyState();
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

        // Visuals und Aktionen auf allen Clients aktivieren
        if (playerVisuals != null)
            playerVisuals.SetActive(true);

        ActivateRoleModel(role);
        ActivateRoleAction(role);

        // Config und Spawn nur für den lokalen Spieler
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

    private void TeleportToSpawn(PlayerRole role)
    {
        Transform[] spawnPoints = role == PlayerRole.Hunter
            ? LevelManager.Instance.hunterSpawnPositions
            : LevelManager.Instance.vandalistSpawnPositions;

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning($"[PlayerRoleSetup] Keine Spawn-Positionen für {role}!");
            return;
        }

        movement.TeleportTo(spawnPoints[Random.Range(0, spawnPoints.Length)].position);
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

        if (ok) Debug.Log("[PlayerRoleSetup] Setup vollständig.");
    }
#endif
}