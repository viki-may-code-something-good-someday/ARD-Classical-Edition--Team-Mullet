using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;


public class PauseMenuController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject pausePanel;
    [Tooltip("Button/GameObject only the host should be able to use, e.g. 'End Game for Everyone'.")]
    [SerializeField] private GameObject hostOnlyControls;

    [Header("Settings")]
    [Tooltip("Only allow pausing while in the gameplay scene, not in the lobby.")]
    [SerializeField] private bool restrictToGameplayScene = true;

    private bool isPaused;

    private string GameplayScene =>
        (NetworkManager.singleton as CustomNetworkManager)?.GameplayScene ?? string.Empty;

    private bool InGameplayScene => SceneManager.GetActiveScene().path == GameplayScene;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Start()
    {
        SetPaused(false);
    }

    private void Update()
    {
        if (restrictToGameplayScene && !InGameplayScene) return;

        if (Input.GetKeyDown(KeyCode.Escape))
            TogglePause();
    }

    // ── Pause Toggle ─────────────────────────────────────────────────────────

    public void TogglePause() => SetPaused(!isPaused);

    public void SetPaused(bool paused)
    {
        isPaused = paused;

        if (pausePanel != null) pausePanel.SetActive(paused);

        // Only host-relevant controls (e.g. "End Game") are shown to the host.
        // NetworkServer.active is true for both dedicated server and host.
        if (hostOnlyControls != null)
            hostOnlyControls.SetActive(paused && NetworkServer.active);

        Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = paused;
    }

    /// <summary>Called by the "Resume" button.</summary>
    public void Resume() => SetPaused(false);

    // ── Session Control ──────────────────────────────────────────────────────

    public void EndGameAsHost()
    {
        if (!NetworkServer.active)
        {
            Debug.LogWarning("[PauseMenuController] EndGameAsHost() aufgerufen, aber kein Host/Server – ignoriert.");
            return;
        }

        CustomNetworkManager manager = NetworkManager.singleton as CustomNetworkManager;
        if (manager == null)
        {
            Debug.LogError("[PauseMenuController] Kein CustomNetworkManager gefunden – kann nicht zur Lobby zurückkehren.");
            return;
        }

        manager.ReturnToLobby();
        SetPaused(false);
    }

    /// <summary>
    /// Called by the "Leave Game" button for non-host clients: disconnects only
    /// the local player, the session continues for everyone else.
    /// </summary>
    public void LeaveGameAsClient()
    {
        if (NetworkServer.active)
        {
            Debug.LogWarning("[PauseMenuController] LeaveGameAsClient() als Host aufgerufen – " +
                             "nutze stattdessen EndGameAsHost().");
            return;
        }

        NetworkManager manager = NetworkManager.singleton;
        if (manager == null)
        {
            Debug.LogError("[PauseMenuController] Kein NetworkManager gefunden – kann Session nicht verlassen.");
            return;
        }

        manager.StopClient();
    }

    /// <summary>Called by the "Quit to Desktop" button.</summary>
    public void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}