using UnityEngine;
using UnityEngine.UI;

public class MenuUI : MonoBehaviour
{
    [SerializeField] private Button hostLobbyButton;

    private void Start()
    {
        if (hostLobbyButton != null && SteamLobby.instance != null)
            hostLobbyButton.onClick.AddListener(SteamLobby.instance.HostLobby);
    }
}