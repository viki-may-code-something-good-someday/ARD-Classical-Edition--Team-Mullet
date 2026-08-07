using Mirror;
using Steamworks;
using TMPro;
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

    /// <summary>
    /// Feuert auf jedem Client sobald die Rolle über die SyncVar ankommt oder sich ändert.
    /// PlayerRoleSetup hängt sich hier ein, damit eine spät eintreffende Rolle (Lag, Late Join)
    /// nicht verschluckt wird.
    /// </summary>
    public event System.Action<PlayerRole> RoleChanged;


    private CustomNetworkManager networkManager;

    public TMP_Text nameText;
    public Canvas nameCanvas;

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
        if (isClient && LobbyController.instance != null)
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

        // Der LobbyController lebt nur in der Lobby-Scene. Test-Dummies werden erst in der
        // Gameplay-Scene gespawnt – ohne diese Prüfung wirft jeder Dummy-Spawn hier.
        if (LobbyController.instance != null)
        {
            LobbyController.instance.UpdateLobbyName();
            LobbyController.instance.UpdatePlayerList();
        }

        if ((isOwned || isLocalPlayer) && nameCanvas != null)
        {
            nameCanvas.gameObject.SetActive(false);
        }
    }

    public override void OnStopClient()
    {
        if (networkManager != null) networkManager.gamePlayers.Remove(this);
        if (LobbyController.instance != null) LobbyController.instance.UpdatePlayerList();
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
            if (LobbyController.instance != null) LobbyController.instance.UpdatePlayerList();

            if (nameText != null)
            {
                nameText.text = newValue;
            }
        }
    }


    /// <param name="testMode">
    /// Spiegelt LobbyController.IsTestMode zum Startzeitpunkt. Wird bis zum Server
    /// (CustomNetworkManager.StartGame) durchgereicht, da LobbyController nur client-seitig
    /// existiert und der Server diesen Wert nicht direkt auslesen kann.
    /// </param>
    public void CanStartGame(string sceneName, bool useCustomNetworkGameplayScene, bool testMode)
    {
        if (isOwned)
        {
            CmdCanStartGame(sceneName, useCustomNetworkGameplayScene, testMode);
        }
    }

    [Command]
    public void CmdCanStartGame(string sceneName, bool useCustomNetworkGameplayScene, bool testMode)
    {
        NetworkManager.StartGame(sceneName, useCustomNetworkGameplayScene, testMode);
    }

    [Command]
    public void CmdChangeRole(PlayerRole newRole)
    {
        SetPlayerRole(newRole);
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
        Debug.Log($"[PlayerObjectController] {playerName}: {oldRole} → {newRole}");

        if (isClient && LobbyController.instance != null)
        {
            // Wenn die Rolle im Netzwerk geändert wurde, UI aktualisieren!
            LobbyController.instance.UpdatePlayerList();
        }

        RoleChanged?.Invoke(newRole);
    }
}