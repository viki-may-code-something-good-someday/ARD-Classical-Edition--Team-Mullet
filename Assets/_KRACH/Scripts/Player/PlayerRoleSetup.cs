using Mirror;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerRoleSetup : NetworkBehaviour
{
    [Header("Movement Configs")]
    [SerializeField] private RoleMovementConfig hunterConfig;
    [SerializeField] private RoleMovementConfig vandalistConfig;

    [Header("References")]
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private PlayerInteract interact;
    [SerializeField] private HunterAccuse accuse;
    [SerializeField] private GameObject playerModel;

    // Gameplay-Scene-Pfad kommt direkt vom NetworkManager – kein doppeltes Feld nötig.
    private string GameplayScene => (NetworkManager.singleton as CustomNetworkManager)?.GameplayScene ?? string.Empty;

    private bool initialized = false;

    // ── Start: Lobby-Zustand herstellen ──────────────────────────────────────

    private void Start()
    {
        // Modell sofort verstecken – InitializeRole() blendet es wieder ein
        // sobald die Gameplay-Scene geladen ist.
        if (playerModel != null)
            playerModel.SetActive(false);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    void Update()
    {
        bool inGameplay = SceneManager.GetActiveScene().path == GameplayScene;

        if (initialized)
        {
            // Gameplay-Scene verlassen → zurücksetzen für nächsten Spielstart
            if (!inGameplay)
                ResetToLobbyState();
            return;
        }

        if (!inGameplay) return;
        if (LevelManager.Instance == null) return;

        InitializeRole();
    }

    // ── Lobby-Reset ───────────────────────────────────────────────────────────

    private void ResetToLobbyState()
    {
        initialized = false;

        if (playerModel != null)
            playerModel.SetActive(false);

        if (interact != null) interact.OnRoleDeactivated();
        if (accuse != null) accuse.OnRoleDeactivated();
    }

    // ── Rolle initialisieren ──────────────────────────────────────────────────

    private void InitializeRole()
    {
        initialized = true;

        PlayerObjectController poc = GetComponent<PlayerObjectController>();
        if (poc == null)
        {
            Debug.LogError("[PlayerRoleSetup] Kein PlayerObjectController auf diesem GameObject!");
            return;
        }

        PlayerRole role = poc.playerRole;

        // Rollenspezifische Aktion – auf allen Clients
        ActivateRoleAction(role);

        // Modell für alle Clients einblenden
        if (playerModel != null)
            playerModel.SetActive(true);

        // Config und Spawn nur für den lokalen Spieler
        if (!isOwned) return;

        RoleMovementConfig config = role == PlayerRole.Hunter ? hunterConfig : vandalistConfig;
        if (config == null)
        {
            Debug.LogError($"[PlayerRoleSetup] Kein RoleMovementConfig für Rolle {role} zugewiesen!");
            return;
        }

        movement.ApplyConfig(config);
        TeleportToSpawn(role);
    }

    // ── Rollenspezifische Aktion ein-/ausschalten ─────────────────────────────

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

    // ── Spawn ─────────────────────────────────────────────────────────────────

    private void TeleportToSpawn(PlayerRole role)
    {
        Transform[] spawnPoints = role == PlayerRole.Hunter
            ? LevelManager.Instance.hunterSpawnPositions
            : LevelManager.Instance.vandalistSpawnPositions;

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning($"[PlayerRoleSetup] Keine Spawn-Positionen für {role} im LevelManager!");
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
        if (mgr == null) { Debug.LogWarning("[PlayerRoleSetup] NetworkManager nicht gefunden (nur im Play Mode verfügbar)."); }
        else if (string.IsNullOrEmpty(mgr.GameplayScene)) { Debug.LogError("[PlayerRoleSetup] GameplayScene im NetworkManager nicht gesetzt!"); ok = false; }

        if (hunterConfig == null) { Debug.LogError("[PlayerRoleSetup] hunterConfig fehlt!"); ok = false; }
        if (vandalistConfig == null) { Debug.LogError("[PlayerRoleSetup] vandalistConfig fehlt!"); ok = false; }
        if (movement == null) { Debug.LogError("[PlayerRoleSetup] PlayerMovement fehlt!"); ok = false; }
        if (playerModel == null) { Debug.LogWarning("[PlayerRoleSetup] playerModel nicht gesetzt."); }
        if (interact == null) { Debug.LogWarning("[PlayerRoleSetup] PlayerInteract fehlt."); }
        if (accuse == null) { Debug.LogWarning("[PlayerRoleSetup] HunterAccuse fehlt."); }

        if (ok) Debug.Log("[PlayerRoleSetup] Setup ist vollständig.");
    }
#endif
}