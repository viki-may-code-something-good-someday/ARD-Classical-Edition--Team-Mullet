using Steamworks;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyController : MonoBehaviour
{
    public static LobbyController instance;

    //UI 
    public TextMeshProUGUI lobbyNameText;

    //Player Data
    public GameObject hunterPlayerListViewContent;
    public GameObject vandalistPlayerListViewContent;
    public GameObject playerListItemPrefab;
    public GameObject localPlayerObject;

    //Data
    public ulong currentLobbyID;
    public bool playerItemCreated = false;
    private List<PlayerListItem> totalPlayerbaseListItems = new List<PlayerListItem>();
    private List<PlayerListItem> hunterPlayerListItems = new List<PlayerListItem>();
    private List<PlayerListItem> vandalistPlayerListItems = new List<PlayerListItem>();
    public PlayerObjectController localPlayerController;

    //Ready
    public Button startGameButton;
    public Toggle readyToggle;
    public TextMeshProUGUI readyToggleText;

    //Player lists
    private const int hunterMaxNumber = 1;
    private const int vandalistMaxNumber = 4;

    public TextMeshProUGUI hunterCurrent;
    public TextMeshProUGUI hunterSlash;
    public TextMeshProUGUI hunterMax;

    public TextMeshProUGUI vandalistCurrent;
    public TextMeshProUGUI vandalistSlash;
    public TextMeshProUGUI vandalistMax;

    public Color defaultTextColor;
    public Color overshootTextColor;

    //Manager
    private CustomNetworkManager manager;

    private CustomNetworkManager Manager
    {
        get
        {
            if (manager != null)
            {
                return manager;
            }
            return manager = CustomNetworkManager.singleton as CustomNetworkManager;
        }
    }


    private void Awake()
    {
        if (instance == null) { instance = this; }
    }

    public void StartGame(string sceneName)
    {
        localPlayerController.CanStartGame(sceneName);
    }

    public void UpdateLobbyName()
    {
        currentLobbyID = Manager.GetComponent<SteamLobby>().currentLobbyID;
        lobbyNameText.text = SteamMatchmaking.GetLobbyData(new CSteamID(currentLobbyID), "name");
    }

    public void UpdatePlayerList()
    {
        if (!playerItemCreated)
        {
            CreateHostPlayerItem();
        }

        if (totalPlayerbaseListItems.Count < Manager.gamePlayers.Count)
        {
            CreateClientPlayerItem();
        }

        if (totalPlayerbaseListItems.Count > Manager.gamePlayers.Count)
        {
            RemovePlayerItem();
        }

        if (totalPlayerbaseListItems.Count == Manager.gamePlayers.Count)
        {
            UpdatePlayerItem();
        }
    }

    public void FindLocalPlayer()
    {
        localPlayerObject = GameObject.Find("LocalGamePlayer");
        localPlayerController = localPlayerObject.GetComponent<PlayerObjectController>();
    }

    public void CreateHostPlayerItem()
    {
        foreach (PlayerObjectController player in Manager.gamePlayers)
        {
            GameObject newPlayerItem = Instantiate(playerListItemPrefab) as GameObject;
            PlayerListItem newPlayerItemScript = newPlayerItem.GetComponent<PlayerListItem>();

            newPlayerItemScript.playerName = player.playerName;
            newPlayerItemScript.connectionID = player.connectionID;
            newPlayerItemScript.playerSteamID = player.playerSteamID;
            newPlayerItemScript.isReady = player.ready;



            AddPlayerToListAndSetValues(1, newPlayerItemScript);

            playerItemCreated = true;
        }

        ReadyPlayer();
    }

    public void AddPlayerToListAndSetValues(int isHunterOrVandalist, PlayerListItem playerItem) // 0 = Hunter, 1 = Vandalist - maybe in the future 2 = Viewer
    {
        switch (isHunterOrVandalist)
        {
            case 0:
                playerItem.transform.SetParent(hunterPlayerListViewContent.transform);
                hunterPlayerListItems.Add(playerItem);
                playerItem.SetPlayerValues(PlayerRole.Hunter);
                break;
            case 1:
                playerItem.transform.SetParent(vandalistPlayerListViewContent.transform);
                vandalistPlayerListItems.Add(playerItem);
                playerItem.SetPlayerValues(PlayerRole.Vandalist);
                break;
        }
        playerItem.transform.localScale = Vector3.one;
        totalPlayerbaseListItems.Add(playerItem);
        UpdateRoleCountTexts();
    }

    public void RemovePlayerFromList(PlayerListItem playerItem, GameObject objToRemove)
    {
        totalPlayerbaseListItems.Remove(playerItem);
        if (hunterPlayerListItems.Contains(playerItem))
        {
            hunterPlayerListItems.Remove(playerItem);
        }
        if (vandalistPlayerListItems.Contains(playerItem))
        {
            vandalistPlayerListItems.Remove(playerItem);
        }
        Destroy(objToRemove);
        UpdateRoleCountTexts();
    }

    public void ReadyPlayer()
    {
        localPlayerController.ChangeReady();
    }

    public void UpdateReadyText()
    {
        if (localPlayerController.ready)
        {
            readyToggleText.text = "Bereit!";
        }
        else
        {
            readyToggleText.text = "Bereit?";
        }
    }

    public void CheckIfAllReady()
    {
        bool allReady = false;

        foreach (PlayerObjectController player in Manager.gamePlayers)
        {
            if (player.ready)
            {
                allReady = true;
            }
            else
            {
                allReady = false;
                break;
            }
        }

        if (allReady)
        {
            if (localPlayerController.playerIdNumber == 1)
            {
                startGameButton.interactable = true;
            }
            else
            {
                startGameButton.interactable = false;
            }
        }
        else
        {
            startGameButton.interactable = false;
        }
    }

    public void SwapRoleButton()
    {
        // Nur den lokalen Spieler switchen
        foreach (PlayerListItem playerListItemScript in totalPlayerbaseListItems)
        {
            if (playerListItemScript.connectionID == localPlayerController.connectionID)
            {
                SwitchPlayerToOtherRole(playerListItemScript);
                return;
            }
        }
    }

    public void SwitchPlayerToOtherRole(PlayerListItem playerItem)
    {
        // Aktuelle Rolle bestimmen und zur anderen wechseln
        if (hunterPlayerListItems.Contains(playerItem))
        {
            // Hunter → Vandalist
            hunterPlayerListItems.Remove(playerItem);
            playerItem.transform.SetParent(vandalistPlayerListViewContent.transform);
            vandalistPlayerListItems.Add(playerItem);
            playerItem.SetPlayerValues(PlayerRole.Vandalist);
        }
        else if (vandalistPlayerListItems.Contains(playerItem))
        {
            // Vandalist → Hunter
            vandalistPlayerListItems.Remove(playerItem);
            playerItem.transform.SetParent(hunterPlayerListViewContent.transform);
            hunterPlayerListItems.Add(playerItem);
            playerItem.SetPlayerValues(PlayerRole.Hunter);
        }

        playerItem.transform.localScale = Vector3.one;
        UpdateRoleCountTexts();
    }

    public void UpdateRoleCountTexts()
    {
        int hunterCount = hunterPlayerListItems.Count;
        int vandalistCount = vandalistPlayerListItems.Count;

        hunterCurrent.text = hunterCount.ToString();
        hunterMax.text = hunterMaxNumber.ToString();

        vandalistCurrent.text = vandalistCount.ToString();
        vandalistMax.text = vandalistMaxNumber.ToString();

        bool hunterOvershoot = hunterCount > hunterMaxNumber;
        bool vandalistOvershoot = vandalistCount > vandalistMaxNumber;

        Color hunterColor = hunterOvershoot ? overshootTextColor : defaultTextColor;
        Color vandalistColor = vandalistOvershoot ? overshootTextColor : defaultTextColor;

        hunterCurrent.color = hunterColor;
        hunterMax.color = hunterColor;

        vandalistCurrent.color = vandalistColor;
        vandalistMax.color = vandalistColor;
    }

    public void CreateClientPlayerItem()
    {
        foreach (PlayerObjectController player in Manager.gamePlayers)
        {
            if (!totalPlayerbaseListItems.Any(b => b.connectionID == player.connectionID))
            {
                GameObject newPlayerItem = Instantiate(playerListItemPrefab) as GameObject;
                PlayerListItem newPlayerItemScript = newPlayerItem.GetComponent<PlayerListItem>();

                newPlayerItemScript.playerName = player.playerName;
                newPlayerItemScript.connectionID = player.connectionID;
                newPlayerItemScript.playerSteamID = player.playerSteamID;
                newPlayerItemScript.isReady = player.ready;

                AddPlayerToListAndSetValues(1, newPlayerItemScript);
            }
        }
    }

    public void UpdatePlayerItem()
    {
        foreach (PlayerObjectController player in Manager.gamePlayers)
        {
            foreach (PlayerListItem playerListItemScript in totalPlayerbaseListItems)
            {
                if (playerListItemScript.connectionID == player.connectionID)
                {
                    playerListItemScript.playerName = player.playerName;

                    PlayerRole currentRole = hunterPlayerListItems.Contains(playerListItemScript)
                        ? PlayerRole.Hunter
                        : PlayerRole.Vandalist;

                    playerListItemScript.isReady = player.ready; // echten Wert übernehmen
                    playerListItemScript.SetPlayerValues(currentRole);
                    playerListItemScript.UpdateReadyStatusText(); // NEU: Status aktualisieren

                    if (player == localPlayerController)
                    {
                        UpdateReadyText();
                    }
                }
            }
        }
        CheckIfAllReady();
    }

    public void RemovePlayerItem()
    {
        List<PlayerListItem> playerListItemToRemove = new List<PlayerListItem>();

        foreach (PlayerListItem playerListItem in totalPlayerbaseListItems)
        {
            // null-check: item könnte bereits destroyed sein (z.B. durch OnStopClient)
            if (playerListItem == null)
            {
                playerListItemToRemove.Add(playerListItem);
                continue;
            }

            if (!Manager.gamePlayers.Any(b => b.connectionID == playerListItem.connectionID))
            {
                playerListItemToRemove.Add(playerListItem);
            }
        }

        if (playerListItemToRemove.Count > 0)
        {
            foreach (PlayerListItem playerlistItemToRemove in playerListItemToRemove)
            {
                // nochmal prüfen, da zwischen den zwei loops Destroys passieren können
                if (playerlistItemToRemove == null)
                {
                    totalPlayerbaseListItems.Remove(playerlistItemToRemove);
                    hunterPlayerListItems.Remove(playerlistItemToRemove);
                    vandalistPlayerListItems.Remove(playerlistItemToRemove);
                    continue;
                }

                GameObject objectToRemove = playerlistItemToRemove.gameObject;
                RemovePlayerFromList(playerlistItemToRemove, objectToRemove);
                objectToRemove = null;
            }
        }

        UpdateRoleCountTexts();
    }
}
