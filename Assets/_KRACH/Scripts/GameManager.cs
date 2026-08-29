using System.Collections;
using System.Collections.Generic;
using FMODUnity;
using Mirror;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum GameState
{
    EndMenu,
    Playing,
    Paused,
    Sequence,
    GameOver
}

public enum WinningSide
{
    None,
    Hunter,
    Vandalist
}

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [SyncVar(hook = nameof(OnGameStateChanged))]
    private GameState currentState;

    public GameState CurrentState => currentState;

    public float maxPlaytimeInSeconds;

    public bool testInEditor;
    public GameObject networkManager;

    //Timer
    [SyncVar]
    private double matchEndTime;

    public float CurrentPlaytime => currentState == GameState.Playing
        ? Mathf.Max(0f, maxPlaytimeInSeconds - (float)(matchEndTime - NetworkTime.time))
        : 0f;

    public float TimeRemaining => currentState == GameState.Playing
        ? Mathf.Max(0f, (float)(matchEndTime - NetworkTime.time))
        : maxPlaytimeInSeconds;

    //SoundBoxCounter
    [SyncVar] private int totalSoundBoxCount;
    [SyncVar] private int remainingSoundBoxCount;

    public int TotalSoundBoxCount => totalSoundBoxCount;
    public int RemainingSoundBoxCount => remainingSoundBoxCount;
    public int DestroyedSoundBoxCount => totalSoundBoxCount - remainingSoundBoxCount;

    // ── Win Condition: Soundboxes ────────────────────────────────────────────

    [Header("Win Condition - Soundboxes")]
    [Tooltip("Wird ein SoundBox aus dieser Liste zerstört, wird es entfernt. " +
             "Ist die Liste leer, haben die Vandalisten gewonnen.")]
    [SerializeField] private List<SoundBox> trackedSoundBoxes = new List<SoundBox>();

#if UNITY_EDITOR
    [Button("Find All SoundBoxes In Scene")]
    private void FindAllSoundBoxesInScene()
    {
        trackedSoundBoxes = new List<SoundBox>(
            FindObjectsByType<SoundBox>(FindObjectsSortMode.None));
        Debug.Log($"[GameManager] {trackedSoundBoxes.Count} SoundBox(es) in der Szene gefunden.");
    }
#endif

    // ── Win Screen ────────────────────────────────────────────────────────────

    [Header("Win Screen")]
    [Scene]
    [SerializeField] private string winScreenScene;
    [Tooltip("Wartezeit nach Spielende, bevor zur Win-Screen-Scene gewechselt wird " +
             "(lässt Sound/UI des Game-Over-Screens noch kurz laufen).")]
    [SerializeField] private float winScreenTransitionDelay = 3f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (testInEditor)
        {
            GameObject nm = GameObject.Instantiate(networkManager);
            nm.GetComponent<CustomNetworkManager>().onlineScene = SceneManager.GetActiveScene().ToString();
            nm.GetComponent<CustomNetworkManager>().StartHost();
            nm.GetComponent<SteamLobby>().HostLobby();
        }
    }

    private void OnEnable()
    {
        PlayerRoleSetup.OnCaughtStateChangedServer += HandlePlayerCaughtStateChanged;
        SoundBox.OnDestroyedServer += HandleSoundBoxDestroyed;
    }

    private void OnDisable()
    {
        PlayerRoleSetup.OnCaughtStateChangedServer -= HandlePlayerCaughtStateChanged;
        SoundBox.OnDestroyedServer -= HandleSoundBoxDestroyed;
    }

    private void Start()
    {
        if (isServer)
        {
            StartGame();
        }
    }

    private void Update()
    {
        if (!isServer) { return; }
        if (currentState == GameState.Playing)
        {
            UpdateInternalTimer();
        }
    }

    // ── Server API ─────────────────────────────────────────────────────────────

    [Server]
    public void StartGame()
    {
        matchEndTime = NetworkTime.time + maxPlaytimeInSeconds;

        totalSoundBoxCount = trackedSoundBoxes.Count;
        remainingSoundBoxCount = trackedSoundBoxes.Count;

        SetState(GameState.Playing);
    }

    [Server]
    public void PauseGame()
    {
        SetState(GameState.Paused);
        RpcSetTimeScale(0f);
    }

    [Server]
    public void ResumeGame()
    {
        SetState(GameState.Playing);
        RpcSetTimeScale(1f);
    }

    /// <summary>
    /// Beendet das Spiel server-autoritativ für die angegebene Siegerseite.
    /// Idempotent: läuft nach dem ersten Aufruf ins Leere, falls das Spiel bereits vorbei ist.
    /// Kann auch direkt aus dem Inspector (z.B. Debug-Button/UnityEvent) mit einem
    /// WinningSide-Dropdown-Parameter aufgerufen werden.
    /// </summary>
    [Server]
    public void EndGame(WinningSide winner)
    {
        if (currentState == GameState.GameOver) return;

        SetState(GameState.GameOver);
        RpcGameOver(winner);

        CustomNetworkManager manager = NetworkManager.singleton as CustomNetworkManager;
        if (manager != null)
            manager.SetLastWinner(winner);
        else
            Debug.LogError("[GameManager] Kein CustomNetworkManager gefunden – Gewinner kann nicht an die Win-Screen-Scene übergeben werden.");

        StartCoroutine(TransitionToWinScreenAfterDelay());
    }

    [Server]
    private IEnumerator TransitionToWinScreenAfterDelay()
    {
        yield return new WaitForSeconds(winScreenTransitionDelay);

        CustomNetworkManager manager = NetworkManager.singleton as CustomNetworkManager;
        if (manager == null || string.IsNullOrEmpty(winScreenScene))
        {
            Debug.LogError("[GameManager] Kein CustomNetworkManager oder winScreenScene nicht gesetzt – " +
                           "kann nicht zur Win-Screen-Scene wechseln.");
            yield break;
        }

        manager.ServerChangeScene(winScreenScene);
    }

    [Server]
    private void SetState(GameState _newState)
    {
        currentState = _newState;
    }

    // ── Win Condition: Timer ────────────────────────────────────────────────────

    [Server]
    private void UpdateInternalTimer()
    {
        if (NetworkTime.time >= matchEndTime)
        {
            EndGame(WinningSide.Hunter);
        }
    }

    // ── Win Condition: Soundboxes ────────────────────────────────────────────

    [Server]
    private void HandleSoundBoxDestroyed(SoundBox box)
    {
        if (!isServer) return;
        if (currentState != GameState.Playing) return;

        if (trackedSoundBoxes.Remove(box))
        {
            remainingSoundBoxCount = trackedSoundBoxes.Count;

            if (remainingSoundBoxCount == 0)
            {
                EndGame(WinningSide.Vandalist);
            }
        }
    }

    // ── Win Condition: Alle Vandalisten gefangen ────────────────────────────────

    [Server]
    private void HandlePlayerCaughtStateChanged(PlayerRoleSetup setup, bool caught)
    {
        if (!isServer) return;
        if (currentState != GameState.Playing) return;

        CheckAllVandalistsCaughtWinCondition();
    }

    /// <summary>
    /// Hunter gewinnen, wenn kein Vandalist mehr aktiv (nicht gefangen) ist.
    /// Zählt bewusst mit, ob es überhaupt Vandalisten gibt – sonst würde ein Match
    /// ohne Vandalisten (z.B. Testfall) sofort fälschlich als Hunter-Sieg werten.
    /// HINWEIS: Solange Vandalisten automatisch respawnen (PlayerRoleSetup.allowRespawn),
    /// ist "gefangen" nur ein temporärer Zustand – dieser Check wird erst dann zuverlässig
    /// final, wenn ihr den Auto-Respawn wie geplant deaktiviert.
    /// </summary>
    [Server]
    private void CheckAllVandalistsCaughtWinCondition()
    {
        CustomNetworkManager manager = NetworkManager.singleton as CustomNetworkManager;
        if (manager == null) return;

        int vandalistCount = 0;
        bool anyActiveVandalist = false;

        foreach (PlayerObjectController player in manager.gamePlayers)
        {
            if (player == null || player.playerRole != PlayerRole.Vandalist) continue;
            vandalistCount++;

            PlayerRoleSetup setup = player.GetComponent<PlayerRoleSetup>();
            if (setup == null || !setup.IsCaught)
            {
                anyActiveVandalist = true;
                break;
            }
        }

        if (vandalistCount == 0) return; // keine Vandalisten im Match, Check ignorieren
        if (!anyActiveVandalist) EndGame(WinningSide.Hunter);
    }

    // ── RPCs — broadcast to all clients ───────────────────────────────────────

    [ClientRpc]
    private void RpcGameOver(WinningSide winner)
    {
        bool vandalistsWon = winner == WinningSide.Vandalist;

        //RuntimeManager.PlayOneShot(vandalistsWon ? "event:/SFX/GameWon" : "event:/SFX/GameOver");


        if (UI_GameOver.Instance != null)
            UI_GameOver.Instance.SetGameOverScreen(vandalistsWon);
        else
            Debug.LogWarning("[GameManager] UI_GameOver.Instance ist null – Game-Over-Screen wird nicht angezeigt.");

        Debug.Log($"[GameManager] Game Over — {winner} gewinnt.");
    }

    [ClientRpc]
    private void RpcSetTimeScale(float _timeScale)
    {
        Time.timeScale = _timeScale;
    }

    // ── SyncVar hook — fires on all clients when state changes ─────────────────

    private void OnGameStateChanged(GameState _oldState, GameState _newState)
    {
        Debug.Log($"[GameManager] State: {_oldState} → {_newState}");
    }

    public void RestartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}