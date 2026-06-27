using Mirror;
using Steamworks;
using UnityEngine;

public class PlayerObjectController : NetworkBehaviour
{
    //Player Data
    [SyncVar] public int connectionID;
    [SyncVar] public int playerIdNumber;
    [SyncVar] public ulong playerSteamID;

    [SyncVar(hook = nameof(PlayerNameUpdate))] public string playerName;
    [SyncVar(hook = nameof(PlayerReadyUpdate))] public bool ready;

    [SyncVar(hook = nameof(OnPlayerRoleChanged))]

    public PlayerRole playerRole = PlayerRole.Vandalist;


    private CustomNetworkManager networkManager;

    private CustomNetworkManager NetworkManager
    {
        get
        {
            if (networkManager != null)
            {
                return networkManager;
            }
            return networkManager = CustomNetworkManager.singleton as CustomNetworkManager;
        }
    }

    private void Start()
    {
        DontDestroyOnLoad(this.gameObject);
    }

    private void PlayerReadyUpdate(bool oldValue, bool newValue)
    {
        if (isServer)
        {
            this.ready = newValue;
        }
        if (isClient)
        {
            LobbyController.instance.UpdatePlayerList();
        }
    }

    [Command]
    private void CmdSetPlayerReady()
    {
        this.PlayerReadyUpdate(this.ready, !this.ready);
    }

    [Command]
    private void CmdSetReadyState(bool newState)
    {
        this.PlayerReadyUpdate(this.ready, newState);
    }

    public void ChangeReady()
    {
        if (isOwned)
        {
            CmdSetPlayerReady();
        }
    }

    public override void OnStartAuthority()
    {
        CmdSetPlayerName(SteamFriends.GetPersonaName().ToString());
        gameObject.name = "LocalGamePlayer";
        LobbyController.instance.FindLocalPlayer();
        LobbyController.instance.UpdateLobbyName();

        if (isServer)
        {
            CmdSetReadyState(true);
        }
    }

    public override void OnStartClient()
    {
        if (NetworkManager == null)
        {
            Debug.LogError("CustomNetworkManager not found!");
            return;
        }

        networkManager.gamePlayers.Add(this);
        LobbyController.instance.UpdateLobbyName();
        LobbyController.instance.UpdatePlayerList();
    }

    public override void OnStopClient()
    {
        networkManager.gamePlayers.Remove(this);
        LobbyController.instance.UpdatePlayerList();
    }

    [Command]
    private void CmdSetPlayerName(string playerName)
    {
        this.PlayerNameUpdate(this.playerName, playerName);
    }

    public void PlayerNameUpdate(string oldValue, string newValue)
    {
        if (isServer)
        {
            this.playerName = newValue;
        }
        if (isClient)
        {
            LobbyController.instance.UpdatePlayerList();
        }
    }


    public void CanStartGame(string sceneName, bool useCustomNetworkGameplayScene)
    {
        if (isOwned)
        {
            CmdCanStartGame(sceneName, useCustomNetworkGameplayScene);
        }
    }

    [Command]
    public void CmdCanStartGame(string sceneName, bool useCustomNetworkGameplayScene)
    {
        NetworkManager.StartGame(sceneName, useCustomNetworkGameplayScene);
    }

    public void SetPlayerRole(PlayerRole role)
    {
        if (!NetworkServer.active)
        {
            Debug.LogWarning("[PlayerObjectController] SetPlayerRole darf nur auf dem Server aufgerufen werden.");
            return;
        }
        playerRole = role; // SyncVar → wird automatisch an alle Clients repliziert
    }

    private void OnPlayerRoleChanged(PlayerRole oldRole, PlayerRole newRole)
    {
        // Wird auf allen Clients ausgeführt wenn sich die Rolle ändert.
        // Kann hier genutzt werden um UI oder Visuals zu aktualisieren.
        Debug.Log($"[PlayerObjectController] {playerName}: {oldRole} → {newRole}");
    }
}
