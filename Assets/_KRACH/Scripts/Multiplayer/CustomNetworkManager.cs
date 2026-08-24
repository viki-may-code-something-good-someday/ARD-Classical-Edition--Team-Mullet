using Mirror;
using Steamworks;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CustomNetworkManager : NetworkManager
{
    [Header("References")]
    [SerializeField] private PlayerObjectController gamePlayerPrefab;

    [Scene]
    [SerializeField]
    private string lobbyScene;

    [Scene]
    [SerializeField]
    private string gameplayScene;

    /// <summary>
    /// Gibt den Pfad der Gameplay-Scene zurück.
    /// Wird von CameraController und PlayerRoleSetup verwendet.
    /// </summary>
    public string GameplayScene => gameplayScene;

    /// <summary>
    /// Gibt den Pfad der Lobby-Scene zurück. Wird für ReturnToLobby() benötigt,
    /// da OnServerAddPlayer nur in der onlineScene neue Spieler zulässt und wir
    /// beim Zurückwechseln keine neuen Player-Objekte erzeugen wollen.
    /// </summary>
    public string LobbyScene => lobbyScene;


    public List<PlayerObjectController> gamePlayers { get; } = new List<PlayerObjectController>();

    /// <summary>
    /// Server-autoritativer Spiegel von LobbyController.IsTestMode zum Startzeitpunkt.
    /// Wird über StartGame() gesetzt (LobbyController existiert nur client-seitig in der
    /// Lobby-Scene und kann daher nicht direkt vom Server ausgelesen werden – der Wert muss
    /// über die bestehende Command-Kette durchgereicht werden).
    /// Von TestDummySpawner genutzt um zu entscheiden ob Dummies gespawnt werden.
    /// </summary>
    public bool IsTestMode { get; private set; }


    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        if (SceneManager.GetActiveScene().path == onlineScene)
        {
            PlayerObjectController gamePlayerInstance = Instantiate(gamePlayerPrefab);

            gamePlayerInstance.connectionID = conn.connectionId;
            gamePlayerInstance.playerIdNumber = gamePlayers.Count + 1;
            gamePlayerInstance.playerSteamID = (ulong)SteamMatchmaking.GetLobbyMemberByIndex((CSteamID)SteamLobby.instance.currentLobbyID, gamePlayers.Count);

            NetworkServer.AddPlayerForConnection(conn, gamePlayerInstance.gameObject);
        }
    }

    /// <param name="testMode">
    /// Spiegelt LobbyController.IsTestMode zum Startzeitpunkt. Muss über die Command-Kette
    /// (PlayerObjectController.CanStartGame → hier) durchgereicht werden.
    /// </param>
    public void StartGame(string sceneName, bool useCustomNetworkGameplazScene, bool testMode)
    {
        if (useCustomNetworkGameplazScene)
        {
            sceneName = gameplayScene;
        }

        IsTestMode = testMode;
        ServerChangeScene(sceneName);
    }

    // ── Rückkehr zur Lobby ───────────────────────────────────────────────────

    /// <summary>
    /// Wechselt alle verbundenen Spieler zurück in die Lobby-Scene.
    /// Die vorhandenen PlayerObjectController-Instanzen (gamePlayers) bleiben erhalten –
    /// ServerChangeScene zerstört nur szenenspezifische Objekte, keine NetworkIdentities
    /// mit DontDestroyOnLoad-Flag bzw. Player-Objekte laufen über die Connection weiter.
    /// Ready-Status und Caught-Zustand werden zurückgesetzt, damit die Lobby-UI und
    /// PlayerRoleSetup sauber neu starten.
    /// </summary>
    [Server]
    public void ReturnToLobby()
    {
        if (string.IsNullOrEmpty(lobbyScene))
        {
            Debug.LogError("[CustomNetworkManager] lobbyScene ist nicht gesetzt!");
            return;
        }

        foreach (PlayerObjectController player in gamePlayers)
        {
            if (player == null) continue;

            player.ResetForLobby();

            PlayerRoleSetup roleSetup = player.GetComponent<PlayerRoleSetup>();
            roleSetup?.ResetForLobby();
        }

        ServerChangeScene(lobbyScene);
    }

    /// <summary>
    /// Läuft auf dem Server, sobald die neue Scene vollständig geladen ist.
    /// Wichtig für Late-Join-artige Situationen: hier könnten z.B. gamePlayers
    /// neu validiert werden, falls sich während des Wechsels Connections trennen.
    /// </summary>
    public override void OnServerSceneChanged(string sceneName)
    {
        base.OnServerSceneChanged(sceneName);

        if (sceneName == lobbyScene)
        {
            gamePlayers.RemoveAll(p => p == null);
        }
    }
}