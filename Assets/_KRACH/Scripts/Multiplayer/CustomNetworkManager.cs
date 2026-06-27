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
    [Scene][SerializeField] private string gameplayScene;


    public List<PlayerObjectController> gamePlayers { get; } = new List<PlayerObjectController>();


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

    public void StartGame(string sceneName, bool useCustomNetworkGameplazScene)
    {
        if (useCustomNetworkGameplazScene)
        {
            sceneName = gameplayScene;
        }

        ServerChangeScene(sceneName);
    }
}