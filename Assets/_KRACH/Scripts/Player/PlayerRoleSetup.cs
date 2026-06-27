using Mirror;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Liest die zugewiesene Rolle aus PlayerObjectController (SyncVar),
/// konfiguriert PlayerMovement mit dem passenden RoleMovementConfig und
/// aktiviert die rollenspezifische Aktion (PlayerInteract oder HunterAccuse).
///
/// Läuft auf ALLEN Clients (nicht nur Owner):
///   – Komponentenaktivierung gilt für alle (Mirror-RPCs brauchen aktive Komponenten).
///   – Config, Spawn und Modell-Aktivierung nur für den Owner.
/// </summary>
public class PlayerRoleSetup : NetworkBehaviour
{
    [Header("Scene")]
    [Scene][SerializeField] private string gameplayScene;

    [Header("Movement Configs")]
    [Tooltip("RoleMovementConfig Asset für die Hunter-Rolle.")]
    [SerializeField] private RoleMovementConfig hunterConfig;
    [Tooltip("RoleMovementConfig Asset für die Vandalist-Rolle.")]
    [SerializeField] private RoleMovementConfig vandalistConfig;

    [Header("References")]
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private PlayerInteract interact;        // Vandalist-Aktion
    [SerializeField] private HunterAccuse accuse;            // Hunter-Aktion
    [SerializeField] private GameObject playerModel;

    private bool initialized = false;

    // ── Update: warten bis Scene + LevelManager bereit ───────────────────────

    void Update()
    {
        if (initialized) return;
        if (SceneManager.GetActiveScene().path != gameplayScene) return;
        if (LevelManager.Instance == null) return;

        InitializeRole();
    }

    // ── Rolle initialisieren ──────────────────────────────────────────────────

    private void InitializeRole()
    {
        initialized = true;

        PlayerObjectController poc = GetComponent<PlayerObjectController>();
        if (poc == null)
        {
            Debug.LogError("[PlayerRoleSetup] Kein PlayerObjectController auf diesem GameObject gefunden!");
            return;
        }

        PlayerRole role = poc.playerRole;

        // Rollenspezifische Aktion aktivieren – auf allen Clients
        ActivateRoleAction(role);

        // Modell einblenden – auf allen Clients (andere Spieler sollen das Modell sehen)
        if (playerModel != null)
            playerModel.SetActive(true);

        // Bewegungskonfiguration und Spawn nur für den lokalen Spieler
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
        // IRoleAction-Callbacks aufrufen (Komponenten aktivieren/deaktivieren)
        if (interact != null)
        {
            if (role == PlayerRole.Vandalist) ((IRoleAction)interact).OnRoleActivated();
            else ((IRoleAction)interact).OnRoleDeactivated();
        }

        if (accuse != null)
        {
            if (role == PlayerRole.Hunter) ((IRoleAction)accuse).OnRoleActivated();
            else ((IRoleAction)accuse).OnRoleDeactivated();
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
            Debug.LogWarning($"[PlayerRoleSetup] Keine Spawn-Positionen für Rolle {role} konfiguriert!");
            return;
        }

        Vector3 spawnPosition = spawnPoints[Random.Range(0, spawnPoints.Length)].position;
        movement.TeleportTo(spawnPosition);
    }

    // ── Debug ─────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    [Button("Validate Setup")]
    private void ValidateSetup()
    {
        if (hunterConfig == null) Debug.LogError("[PlayerRoleSetup] hunterConfig fehlt!");
        if (vandalistConfig == null) Debug.LogError("[PlayerRoleSetup] vandalistConfig fehlt!");
        if (movement == null) Debug.LogError("[PlayerRoleSetup] PlayerMovement-Referenz fehlt!");
        if (interact == null) Debug.LogWarning("[PlayerRoleSetup] PlayerInteract-Referenz fehlt (Vandalist schlägt nicht).");
        if (accuse == null) Debug.LogWarning("[PlayerRoleSetup] HunterAccuse-Referenz fehlt (Hunter kann nicht anklagen).");
        if (playerModel == null) Debug.LogWarning("[PlayerRoleSetup] playerModel-Referenz fehlt.");
        if (string.IsNullOrEmpty(gameplayScene)) Debug.LogError("[PlayerRoleSetup] gameplayScene nicht gesetzt!");
        else Debug.Log("[PlayerRoleSetup] Setup sieht gut aus.");
    }
#endif
}