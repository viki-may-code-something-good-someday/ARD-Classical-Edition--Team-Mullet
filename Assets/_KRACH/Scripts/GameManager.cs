using FMODUnity;
using Mirror;
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

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [SyncVar(hook = nameof(OnGameStateChanged))]
    private GameState currentState;

    public GameState CurrentState => currentState;

    public float maxPlaytimeInSeconds;

    private float currentPlaytime;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
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
        currentPlaytime = 0f;
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

    [Server]
    public void GameOver(bool _won)
    {
        SetState(GameState.GameOver);
        RpcGameOver(_won);
    }

    [Server]
    public void WinGame()
    {
        SetState(GameState.GameOver);
        RpcWinGame();
    }

    [Server]
    private void SetState(GameState _newState)
    {
        currentState = _newState;
    }

    // ── RPCs — broadcast to all clients ───────────────────────────────────────

    [ClientRpc]
    private void RpcGameOver(bool _won)
    {
        RuntimeManager.PlayOneShot(_won ? "event:/SFX/GameWon" : "event:/SFX/GameOver");
        UI_GameOver.Instance.SetGameOverScreen(_won);
        Debug.Log($"[GameManager] Game Over — {(_won ? "Won" : "Lost")}");
    }

    [ClientRpc]
    private void RpcWinGame()
    {
        RuntimeManager.PlayOneShot("event:/SFX/GameWon");
        Debug.Log("[GameManager] Win Game");
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

    // ── Timer (server only) ────────────────────────────────────────────────────

    [Server]
    private void UpdateInternalTimer()
    {
        currentPlaytime += Time.deltaTime;
        if (currentPlaytime >= maxPlaytimeInSeconds)
        {
            GameOver(false);
        }
    }

    public void RestartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}