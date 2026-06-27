using Steamworks;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyController : MonoBehaviour
{
    public static LobbyController instance;

    // UI
    public TextMeshProUGUI lobbyNameText;

    // Player Data
    public GameObject hunterPlayerListViewContent;
    public GameObject vandalistPlayerListViewContent;
    public GameObject playerListItemPrefab;
    public GameObject localPlayerObject;

    // Data
    public ulong currentLobbyID;
    public bool playerItemCreated = false;
    private List<PlayerListItem> totalPlayerbaseListItems = new List<PlayerListItem>();
    private List<PlayerListItem> hunterPlayerListItems = new List<PlayerListItem>();
    private List<PlayerListItem> vandalistPlayerListItems = new List<PlayerListItem>();
    public PlayerObjectController localPlayerController;

    // Ready
    public Button startGameButton;
    public Toggle readyToggle;
    public TextMeshProUGUI readyToggleText;

    // Test-Modus
    [Header("Test Mode")]
    [Tooltip("Überspringt Rollenvalidierung und Ready-Check. Erlaubt Solo-Start mit COM-Dummies.")]
    [SerializeField] private bool isTestMode = false;
    [Tooltip("Optionale UI-Anzeige die zeigt ob Test-Modus aktiv ist (z.B. ein Text oder Panel).")]
    [SerializeField] private GameObject testModeIndicator;

    // Player limits
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

    // Manager
    private CustomNetworkManager manager;
    private CustomNetworkManager Manager
    {
        get
        {
            if (manager != null) return manager;
            return manager = CustomNetworkManager.singleton as CustomNetworkManager;
        }
    }

    // ── Awake ────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (instance == null) instance = this;
        UpdateTestModeIndicator();
    }

    // ── Test-Modus ────────────────────────────────────────────────────────────

    /// <summary>
    /// Wird von einem UI-Button aufgerufen um den Test-Modus ein-/auszuschalten.
    /// </summary>
    public void ToggleTestMode()
    {
        isTestMode = !isTestMode;
        UpdateTestModeIndicator();
        CheckIfAllReady(); // Start-Button-State sofort aktualisieren
        Debug.Log($"[Lobby] Test-Modus: {(isTestMode ? "AN" : "AUS")}");
    }

    private void UpdateTestModeIndicator()
    {
        if (testModeIndicator != null)
            testModeIndicator.SetActive(isTestMode);
    }

    public bool IsTestMode => isTestMode;

    // ── Spielstart ────────────────────────────────────────────────────────────

    public void StartGame(string sceneName, bool useCustomNetworkGameplayScene)
    {
        if (!ValidateRoleLimits()) return;
        AssignRolesToAllPlayers();
        localPlayerController.CanStartGame(sceneName, useCustomNetworkGameplayScene);
    }

    public void StartGame(bool useCustomNetworkGameplayScene)
    {
        if (!ValidateRoleLimits()) return;
        AssignRolesToAllPlayers();
        localPlayerController.CanStartGame("", useCustomNetworkGameplayScene);
    }

    /// <summary>
    /// Prüft ob die Rollenverteilung gültig ist (min. 1 Hunter, min. 1 Vandalist, keine Überschreitung).
    /// Im Test-Modus werden alle Checks übersprungen.
    /// </summary>
    private bool ValidateRoleLimits()
    {
        if (isTestMode)
        {
            Debug.Log("[Lobby] Test-Modus aktiv – Rollenvalidierung übersprungen.");
            return true;
        }

        if (hunterPlayerListItems.Count == 0)
        {
            Debug.LogWarning("[Lobby] Kein Hunter zugewiesen – Spiel kann nicht gestartet werden.");
            return false;
        }

        if (vandalistPlayerListItems.Count == 0)
        {
            Debug.LogWarning("[Lobby] Kein Vandalist zugewiesen – Spiel kann nicht gestartet werden.");
            return false;
        }

        if (hunterPlayerListItems.Count > hunterMaxNumber)
        {
            Debug.LogWarning($"[Lobby] Zu viele Hunter ({hunterPlayerListItems.Count}/{hunterMaxNumber}).");
            return false;
        }

        if (vandalistPlayerListItems.Count > vandalistMaxNumber)
        {
            Debug.LogWarning($"[Lobby] Zu viele Vandalists ({vandalistPlayerListItems.Count}/{vandalistMaxNumber}).");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Schreibt die im Lobby-UI gewählte Rolle in den jeweiligen PlayerObjectController.
    /// Wird direkt vor dem Spielstart aufgerufen, damit InitRole() im Character-Script
    /// die korrekte Rolle lesen kann.
    /// </summary>
    private void AssignRolesToAllPlayers()
    {
        foreach (PlayerListItem item in hunterPlayerListItems)
        {
            PlayerObjectController poc = GetPlayerControllerByConnectionID(item.connectionID);
            if (poc != null)
            {
                poc.SetPlayerRole(PlayerRole.Hunter);
                Debug.Log($"[Lobby] {item.playerName} → Hunter");
            }
        }

        foreach (PlayerListItem item in vandalistPlayerListItems)
        {
            PlayerObjectController poc = GetPlayerControllerByConnectionID(item.connectionID);
            if (poc != null)
            {
                poc.SetPlayerRole(PlayerRole.Vandalist);
                Debug.Log($"[Lobby] {item.playerName} → Vandalist");
            }
        }
    }

    private PlayerObjectController GetPlayerControllerByConnectionID(int connectionID)
    {
        foreach (PlayerObjectController poc in Manager.gamePlayers)
        {
            if (poc.connectionID == connectionID)
                return poc;
        }
        return null;
    }

    // ── Lobby-Name ────────────────────────────────────────────────────────────

    public void UpdateLobbyName()
    {
        currentLobbyID = Manager.GetComponent<SteamLobby>().currentLobbyID;
        lobbyNameText.text = SteamMatchmaking.GetLobbyData(new CSteamID(currentLobbyID), "name");
    }

    // ── Spielerliste ──────────────────────────────────────────────────────────

    public void UpdatePlayerList()
    {
        if (!playerItemCreated) CreateHostPlayerItem();
        if (totalPlayerbaseListItems.Count < Manager.gamePlayers.Count) CreateClientPlayerItem();
        if (totalPlayerbaseListItems.Count > Manager.gamePlayers.Count) RemovePlayerItem();
        if (totalPlayerbaseListItems.Count == Manager.gamePlayers.Count) UpdatePlayerItem();
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
            GameObject newPlayerItem = Instantiate(playerListItemPrefab);
            PlayerListItem newPlayerItemScript = newPlayerItem.GetComponent<PlayerListItem>();

            newPlayerItemScript.playerName = player.playerName;
            newPlayerItemScript.connectionID = player.connectionID;
            newPlayerItemScript.playerSteamID = player.playerSteamID;
            newPlayerItemScript.isReady = player.ready;

            // Standardrolle: Vandalist – erster Spieler (Host) wird Hunter
            int defaultRole = (vandalistPlayerListItems.Count == 0 && hunterPlayerListItems.Count == 0) ? 0 : 1;
            AddPlayerToListAndSetValues(defaultRole, newPlayerItemScript);

            playerItemCreated = true;
        }
    }

    /// <param name="isHunterOrVandalist">0 = Hunter, 1 = Vandalist</param>
    public void AddPlayerToListAndSetValues(int isHunterOrVandalist, PlayerListItem playerItem)
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
        hunterPlayerListItems.Remove(playerItem);
        vandalistPlayerListItems.Remove(playerItem);
        Destroy(objToRemove);
        UpdateRoleCountTexts();
    }

    // ── Ready ─────────────────────────────────────────────────────────────────

    public void ReadyPlayer()
    {
        localPlayerController.ChangeReady();
    }

    public void UpdateReadyText()
    {
        readyToggleText.text = localPlayerController.ready ? "Ready!" : "Ready?";
    }

    public void CheckIfAllReady()
    {
        bool isHost = localPlayerController.playerIdNumber == 1;

        if (isTestMode)
        {
            // Im Test-Modus: Host kann immer starten, Ready-Status wird ignoriert
            startGameButton.interactable = isHost;
            return;
        }

        bool allReady = Manager.gamePlayers.Count > 0 &&
                        Manager.gamePlayers.All(p => p.ready);

        startGameButton.interactable = allReady && isHost;
    }

    // ── Rollen-Wechsel ────────────────────────────────────────────────────────

    public void SwapRoleButton()
    {
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

    // ── Rollen-Anzeige ────────────────────────────────────────────────────────

    public void UpdateRoleCountTexts()
    {
        int hunterCount = hunterPlayerListItems.Count;
        int vandalistCount = vandalistPlayerListItems.Count;

        hunterCurrent.text = hunterCount.ToString();
        hunterMax.text = hunterMaxNumber.ToString();

        vandalistCurrent.text = vandalistCount.ToString();
        vandalistMax.text = vandalistMaxNumber.ToString();

        Color hunterColor = hunterCount > hunterMaxNumber ? overshootTextColor : defaultTextColor;
        Color vandalistColor = vandalistCount > vandalistMaxNumber ? overshootTextColor : defaultTextColor;

        hunterCurrent.color = hunterColor;
        hunterMax.color = hunterColor;

        vandalistCurrent.color = vandalistColor;
        vandalistMax.color = vandalistColor;
    }

    // ── Create / Update / Remove ──────────────────────────────────────────────

    public void CreateClientPlayerItem()
    {
        foreach (PlayerObjectController player in Manager.gamePlayers)
        {
            if (!totalPlayerbaseListItems.Any(b => b.connectionID == player.connectionID))
            {
                GameObject newPlayerItem = Instantiate(playerListItemPrefab);
                PlayerListItem newPlayerItemScript = newPlayerItem.GetComponent<PlayerListItem>();

                newPlayerItemScript.playerName = player.playerName;
                newPlayerItemScript.connectionID = player.connectionID;
                newPlayerItemScript.playerSteamID = player.playerSteamID;
                newPlayerItemScript.isReady = player.ready;

                AddPlayerToListAndSetValues(1, newPlayerItemScript); // neue Spieler → Vandalist
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

                    playerListItemScript.isReady = player.ready;
                    playerListItemScript.SetPlayerValues(currentRole);
                    playerListItemScript.UpdateReadyStatusText();

                    if (player == localPlayerController)
                        UpdateReadyText();
                }
            }
        }
        CheckIfAllReady();
    }

    public void RemovePlayerItem()
    {
        List<PlayerListItem> toRemove = new List<PlayerListItem>();

        foreach (PlayerListItem item in totalPlayerbaseListItems)
        {
            if (item == null)
            {
                toRemove.Add(item);
                continue;
            }
            if (!Manager.gamePlayers.Any(b => b.connectionID == item.connectionID))
                toRemove.Add(item);
        }

        foreach (PlayerListItem item in toRemove)
        {
            if (item == null)
            {
                totalPlayerbaseListItems.Remove(item);
                hunterPlayerListItems.Remove(item);
                vandalistPlayerListItems.Remove(item);
                continue;
            }
            RemovePlayerFromList(item, item.gameObject);
        }

        UpdateRoleCountTexts();
    }
}