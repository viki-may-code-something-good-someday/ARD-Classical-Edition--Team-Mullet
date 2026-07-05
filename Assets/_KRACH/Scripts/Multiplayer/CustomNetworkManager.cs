using Mirror;
using Steamworks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CustomNetworkManager : NetworkManager
{
    [Header("References")]
    [SerializeField] private PlayerObjectController gamePlayerPrefab;
    //[Scene][SerializeField] private string lobbyScene;
    [Scene]
    [SerializeField]
    private string gameplayScene;

    /// <summary>
    /// Gibt den Pfad der Gameplay-Scene zurück.
    /// Wird von CameraController und PlayerRoleSetup verwendet.
    /// </summary>
    public string GameplayScene => gameplayScene;


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
}