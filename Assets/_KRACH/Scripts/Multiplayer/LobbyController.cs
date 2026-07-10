using Steamworks;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyController : MonoBehaviour
{
    public static LobbyController instance;

    // ── UI ───────────────────────────────────────────────────────────────────

    public TextMeshProUGUI lobbyNameText;

    // ── Player Data ──────────────────────────────────────────────────────────

    public GameObject hunterPlayerListViewContent;
    public GameObject vandalistPlayerListViewContent;
    public GameObject playerListItemPrefab;

    // ── State ────────────────────────────────────────────────────────────────

    public ulong currentLobbyID;
    public bool playerItemCreated = false;

    private List<PlayerListItem> totalPlayerbaseListItems = new List<PlayerListItem>();
    private List<PlayerListItem> hunterPlayerListItems = new List<PlayerListItem>();
    private List<PlayerListItem> vandalistPlayerListItems = new List<PlayerListItem>();

    [HideInInspector] public PlayerObjectController localPlayerController;

    // ── Ready & Start ────────────────────────────────────────────────────────

    public Button startGameButton;
    public Button switchSidesButton;
    public Toggle readyToggle;
    public TextMeshProUGUI readyToggleText;

    // ── Test-Modus ────────────────────────────────────────────────────────────

    [Header("Test Mode")]
    [Tooltip("Überspringt Rollenvalidierung und Ready-Check. Erlaubt Solo-Start mit COM-Dummies.")]
    [SerializeField] private bool isTestMode = false;
    [Tooltip("Wird aktiviert wenn Test-Modus an ist (z.B. ein Text oder Panel).")]
    [SerializeField] private GameObject testModeIndicator;

    // ── Rollenlimits ─────────────────────────────────────────────────────────

    private const int hunterMaxNumber = 1;
    private const int vandalistMaxNumber = 4;

    public TextMeshProUGUI hunterCurrent;
    public TextMeshProUGUI hunterMax;

    public TextMeshProUGUI vandalistCurrent;
    public TextMeshProUGUI vandalistMax;

    public Color defaultTextColor;
    public Color overshootTextColor;

    // ── Manager ──────────────────────────────────────────────────────────────

    private CustomNetworkManager manager;
    private CustomNetworkManager Manager
    {
        get
        {
            if (manager != null) return manager;
            return manager = CustomNetworkManager.singleton as CustomNetworkManager;
        }
    }

    // ── Awake ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (instance == null) instance = this;

        // Buttons sofort deaktivieren – werden durch die jeweiligen Update-Methoden freigeschaltet
        if (startGameButton != null)
            startGameButton.interactable = false;
        if (switchSidesButton != null)
            switchSidesButton.interactable = false;

        UpdateTestModeIndicator();
    }

    // ── Spielstart ────────────────────────────────────────────────────────────

    /// <summary>
    /// Wird vom Start-Game-Button aufgerufen.
    /// Nutzt die Gameplay-Scene aus dem CustomNetworkManager.
    /// </summary>
    public void StartGame()
    {
        StartGameInternal(useManagerScene: true, sceneName: "");
    }

    /// <summary>
    /// Startet das Spiel mit einer expliziten Scene (z.B. für Tests mit anderer Scene).
    /// </summary>
    public void StartGame(string sceneName)
    {
        StartGameInternal(useManagerScene: false, sceneName: sceneName);
    }

    private void StartGameInternal(bool useManagerScene, string sceneName)
    {
        if (!ValidateRoleLimits()) return;
        AssignRolesToAllPlayers();

        // isTestMode wird hier explizit durchgereicht, da CustomNetworkManager (Server) diesen
        // Wert nicht direkt von LobbyController (nur client-seitig, nur Lobby-Scene) lesen kann.
        localPlayerController.CanStartGame(useManagerScene ? "" : sceneName, useManagerScene, isTestMode);
    }

    private bool ValidateRoleLimits()
    {
        if (isTestMode)
        {
            Debug.Log("[Lobby] Test-Modus – Rollenvalidierung übersprungen.");
            return true;
        }

        if (hunterPlayerListItems.Count == 0)
        {
            Debug.LogWarning("[Lobby] Kein Hunter zugewiesen.");
            return false;
        }
        if (vandalistPlayerListItems.Count == 0)
        {
            Debug.LogWarning("[Lobby] Kein Vandalist zugewiesen.");
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

    private void AssignRolesToAllPlayers()
    {
        foreach (PlayerListItem item in hunterPlayerListItems)
        {
            PlayerObjectController poc = GetPlayerControllerByConnectionID(item.connectionID);
            poc?.SetPlayerRole(PlayerRole.Hunter);
        }

        foreach (PlayerListItem item in vandalistPlayerListItems)
        {
            PlayerObjectController poc = GetPlayerControllerByConnectionID(item.connectionID);
            poc?.SetPlayerRole(PlayerRole.Vandalist);
        }

        // Test-Modus: Spieler die in keiner Liste sind bekommen Standardrolle Vandalist
        if (isTestMode)
        {
            foreach (PlayerObjectController poc in Manager.gamePlayers)
            {
                bool assigned = hunterPlayerListItems.Any(i => i.connectionID == poc.connectionID)
                             || vandalistPlayerListItems.Any(i => i.connectionID == poc.connectionID);
                if (!assigned)
                    poc.SetPlayerRole(PlayerRole.Vandalist);
            }
        }
    }

    private PlayerObjectController GetPlayerControllerByConnectionID(int connectionID)
    {
        return Manager.gamePlayers.FirstOrDefault(p => p.connectionID == connectionID);
    }

    // ── Lobby-Name ────────────────────────────────────────────────────────────

    public void UpdateLobbyName()
    {
        currentLobbyID = Manager.GetComponent<SteamLobby>().currentLobbyID;
        lobbyNameText.text = SteamMatchmaking.GetLobbyData(new CSteamID(currentLobbyID), "name");
    }

    // ── Lokalen Spieler finden ────────────────────────────────────────────────

    public void FindLocalPlayer()
    {
        GameObject localPlayerObject = GameObject.Find("LocalGamePlayer");
        if (localPlayerObject == null)
        {
            Debug.LogError("[Lobby] LocalGamePlayer nicht gefunden!");
            return;
        }
        localPlayerController = localPlayerObject.GetComponent<PlayerObjectController>();
    }

    // ── Spielerliste ──────────────────────────────────────────────────────────

    public void UpdatePlayerList()
    {
        if (!playerItemCreated)
            CreateHostPlayerItem();
        else if (totalPlayerbaseListItems.Count < Manager.gamePlayers.Count)
            CreateClientPlayerItem();
        else if (totalPlayerbaseListItems.Count > Manager.gamePlayers.Count)
            RemovePlayerItem();
        else
            UpdatePlayerItem();
    }

    public void CreateHostPlayerItem()
    {
        foreach (PlayerObjectController player in Manager.gamePlayers)
        {
            PlayerListItem item = CreatePlayerListItem(player);

            // Erster Spieler (Host) → Hunter, alle weiteren → Vandalist
            int role = (hunterPlayerListItems.Count == 0) ? 0 : 1;
            AddPlayerToList(role, item);
        }

        playerItemCreated = true;
    }

    public void CreateClientPlayerItem()
    {
        foreach (PlayerObjectController player in Manager.gamePlayers)
        {
            if (totalPlayerbaseListItems.Any(i => i.connectionID == player.connectionID))
                continue;

            PlayerListItem item = CreatePlayerListItem(player);
            AddPlayerToList(1, item); // neue Spieler → Vandalist
        }
    }

    private PlayerListItem CreatePlayerListItem(PlayerObjectController player)
    {
        GameObject obj = Instantiate(playerListItemPrefab);
        PlayerListItem item = obj.GetComponent<PlayerListItem>();

        item.playerName = player.playerName;
        item.connectionID = player.connectionID;
        item.playerSteamID = player.playerSteamID;
        item.isReady = player.ready;

        return item;
    }

    /// <param name="role">0 = Hunter, 1 = Vandalist</param>
    public void AddPlayerToList(int role, PlayerListItem item)
    {
        switch (role)
        {
            case 0:
                item.transform.SetParent(hunterPlayerListViewContent.transform);
                hunterPlayerListItems.Add(item);
                item.SetPlayerValues(PlayerRole.Hunter);
                break;
            default:
                item.transform.SetParent(vandalistPlayerListViewContent.transform);
                vandalistPlayerListItems.Add(item);
                item.SetPlayerValues(PlayerRole.Vandalist);
                break;
        }

        item.transform.localScale = Vector3.one;
        totalPlayerbaseListItems.Add(item);
        UpdateRoleCountTexts();
    }

    public void RemovePlayerFromList(PlayerListItem item)
    {
        totalPlayerbaseListItems.Remove(item);
        hunterPlayerListItems.Remove(item);
        vandalistPlayerListItems.Remove(item);
        Destroy(item.gameObject);
        UpdateRoleCountTexts();
    }

    public void UpdatePlayerItem()
    {
        foreach (PlayerObjectController player in Manager.gamePlayers)
        {
            PlayerListItem item = totalPlayerbaseListItems
                .FirstOrDefault(i => i.connectionID == player.connectionID);

            if (item == null) continue;

            item.playerName = player.playerName;
            item.isReady = player.ready;

            PlayerRole role = hunterPlayerListItems.Contains(item)
                ? PlayerRole.Hunter
                : PlayerRole.Vandalist;

            item.SetPlayerValues(role);
            item.UpdateReadyStatusText();

            if (player == localPlayerController)
                UpdateReadyText();
        }

        CheckIfAllReady();
    }

    public void RemovePlayerItem()
    {
        List<PlayerListItem> toRemove = totalPlayerbaseListItems
            .Where(item => item == null || !Manager.gamePlayers.Any(p => p.connectionID == item.connectionID))
            .ToList();

        foreach (PlayerListItem item in toRemove)
        {
            if (item == null)
            {
                totalPlayerbaseListItems.Remove(item);
                hunterPlayerListItems.Remove(item);
                vandalistPlayerListItems.Remove(item);
            }
            else
            {
                RemovePlayerFromList(item);
            }
        }

        UpdateRoleCountTexts();
    }

    // ── Ready ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wird vom Ready-Toggle im UI aufgerufen.
    /// </summary>
    public void ToggleReady()
    {
        if (localPlayerController == null) return;
        localPlayerController.ChangeReady();
    }

    public void UpdateReadyText()
    {
        if (localPlayerController == null || readyToggleText == null) return;
        readyToggleText.text = localPlayerController.ready ? "Bereit!" : "Bereit?";
    }

    public void CheckIfAllReady()
    {
        if (localPlayerController == null || startGameButton == null) return;

        bool isHost = localPlayerController.playerIdNumber == 1;

        if (isTestMode)
        {
            startGameButton.interactable = isHost;
            return;
        }

        bool allReady = Manager.gamePlayers.Count > 0
                     && Manager.gamePlayers.All(p => p.ready);

        startGameButton.interactable = allReady && isHost;
    }

    // ── Test-Modus ────────────────────────────────────────────────────────────

    /// <summary>
    /// Wird vom Test-Mode-Button im UI aufgerufen.
    /// </summary>
    public void ToggleTestMode()
    {
        isTestMode = !isTestMode;
        UpdateTestModeIndicator();
        CheckIfAllReady();
        Debug.Log($"[Lobby] Test-Modus: {(isTestMode ? "AN" : "AUS")}");
    }

    private void UpdateTestModeIndicator()
    {
        if (testModeIndicator != null)
            testModeIndicator.SetActive(isTestMode);
    }

    public bool IsTestMode => isTestMode;

    // ── Rollenwechsel ─────────────────────────────────────────────────────────

    /// <summary>
    /// Wechselt die Rolle des lokalen Spielers zwischen Hunter und Vandalist.
    /// Wird von einem UI-Button aufgerufen.
    /// Button wird durch UpdateSwitchSidesButton() ausgegraut wenn die Zielseite voll ist.
    /// </summary>
    public void ToggleLocalPlayerRole()
    {
        if (localPlayerController == null) return;
        if (!CanLocalPlayerSwitch()) return; // Sicherheitscheck auch ohne Button-Guard

        PlayerListItem localItem = totalPlayerbaseListItems
            .FirstOrDefault(i => i.connectionID == localPlayerController.connectionID);

        if (localItem != null)
            SwitchPlayerRole(localItem);
    }

    /// <summary>
    /// Gibt an ob der lokale Spieler die Seite wechseln kann.
    /// Nicht möglich wenn die Zielseite bereits voll ist.
    /// </summary>
    private bool CanLocalPlayerSwitch()
    {
        if (localPlayerController == null) return false;

        PlayerListItem localItem = totalPlayerbaseListItems
            .FirstOrDefault(i => i.connectionID == localPlayerController.connectionID);

        if (localItem == null) return false;

        if (hunterPlayerListItems.Contains(localItem))
            return vandalistPlayerListItems.Count < vandalistMaxNumber;

        if (vandalistPlayerListItems.Contains(localItem))
            return hunterPlayerListItems.Count < hunterMaxNumber;

        return false;
    }

    /// <summary>
    /// Aktualisiert den interactable-State des Switch-Sides-Buttons.
    /// Wird immer aufgerufen wenn sich die Spielerlisten ändern.
    /// </summary>
    private void UpdateSwitchSidesButton()
    {
        if (switchSidesButton == null) return;
        switchSidesButton.interactable = CanLocalPlayerSwitch();
    }

    public void SwitchPlayerRole(PlayerListItem item)
    {
        if (hunterPlayerListItems.Contains(item))
        {
            hunterPlayerListItems.Remove(item);
            item.transform.SetParent(vandalistPlayerListViewContent.transform);
            vandalistPlayerListItems.Add(item);
            item.SetPlayerValues(PlayerRole.Vandalist);
        }
        else if (vandalistPlayerListItems.Contains(item))
        {
            vandalistPlayerListItems.Remove(item);
            item.transform.SetParent(hunterPlayerListViewContent.transform);
            hunterPlayerListItems.Add(item);
            item.SetPlayerValues(PlayerRole.Hunter);
        }

        item.transform.localScale = Vector3.one;
        UpdateRoleCountTexts();
    }

    // ── Rollenanzeige ─────────────────────────────────────────────────────────

    public void UpdateRoleCountTexts()
    {
        int hCount = hunterPlayerListItems.Count;
        int vCount = vandalistPlayerListItems.Count;

        hunterCurrent.text = hCount.ToString();
        hunterMax.text = hunterMaxNumber.ToString();
        vandalistCurrent.text = vCount.ToString();
        vandalistMax.text = vandalistMaxNumber.ToString();

        Color hColor = hCount > hunterMaxNumber ? overshootTextColor : defaultTextColor;
        Color vColor = vCount > vandalistMaxNumber ? overshootTextColor : defaultTextColor;

        hunterCurrent.color = hColor;
        hunterMax.color = hColor;
        vandalistCurrent.color = vColor;
        vandalistMax.color = vColor;

        // Switch-Sides-Button nach jeder Listenänderung neu bewerten
        UpdateSwitchSidesButton();
    }
}