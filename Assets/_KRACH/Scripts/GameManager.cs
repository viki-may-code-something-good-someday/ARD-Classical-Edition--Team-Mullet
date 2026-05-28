using FMOD.Studio;
using FMODUnity;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.SocialPlatforms.Impl;
public enum GameState
{
    EndMenu,
    Playing,
    Paused,
    Sequence,
    GameOver
}

public class GameManager : MonoBehaviour
{
    private float currentPlaytime;
    public int maxScore = 1000;
    private GameState currentState;

    public Camera_FirstPerson playerCameraController;
    public Camera playerCamera;

    public CharacterController_FirstPerson playerController;

    [SerializeField] public RoomObj[] rooms;

    public int gameLostScorePenalty = 500;

    public float maxPlaytimeInSeconds;

    public GameObject arms;

    public static GameManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        StartGame();
    }

    public void Update()
    {
        if (currentState == GameState.Playing) UpdateInternalTimer();
        if (currentState == GameState.GameOver && Input.GetKeyDown(KeyCode.Space))
        {
            RestartGame();
        }
    }

    public void RestartGame()
    {

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void StartGame()
    {
        currentPlaytime = 0f;

        ChooseRandomRoomForEnemy();

        ResumeGame();
    }

    public void StartCameraShake(float duration, float magnitude)
    {
        StartCoroutine(CameraShake(duration, magnitude));
    }

    public void ChooseRandomRoomForEnemy()
    {
        for (int i = 0; i < rooms.Length; i++)
        {
            int randomRoomIndex = Random.Range(0, rooms.Length);
            if (rooms[randomRoomIndex].canEnemySpawnInThisRoom)
            {
                rooms[randomRoomIndex].isEnemyInThisRoom = true;
                SetOtherEnemiesDisabled(randomRoomIndex);
                Debug.Log("Enemy is in room: " + rooms[randomRoomIndex].name);
                return;
            }
            else
            {
                continue;
            }

        }

    }

    private void SetOtherEnemiesDisabled(int roomIndexForRoomWithEnemy)
    {
        for (int i = 0; i < rooms.Length; i++)
        {
            if (i != roomIndexForRoomWithEnemy)
            {
                rooms[i].SetEnemeyIsInThisRoom(false);
            }
            else
            {
                rooms[i].SetEnemeyIsInThisRoom(true);
            }
        }
    }

    public void GameOver(bool won)
    {
        RuntimeManager.PlayOneShot("event:/SFX/GameOver");    // sound

        currentState = GameState.GameOver;
        int finalScore = CalculateScore(won);
        Debug.Log($"Game Over! Final Score: {finalScore} - You {(won ? "won" : "lost")}!");

        UI_GameOver.Instance.SetGameOverScreenWithScore(finalScore, 0);
    }

    public void GameOverBecauseWallDestroyedWithLowRMF()
    {
        currentState = GameState.GameOver;
        int finalScore = CalculateScore(false);
        Debug.Log($"Game Over! Final Score: {finalScore} - You lost because you destroyed a wall when RMF was low!");

        UI_GameOver.Instance.SetGameOverScreenWithScore(finalScore, 2);
    }

    public void WinGame()
    {
        RuntimeManager.PlayOneShot("event:/SFX/GameWon");    // sound

        // End game with screen
    }

    public void PauseGame()
    {
        currentState = GameState.Paused;

        Time.timeScale = 0f;
    }

    public void ResumeGame()
    {
        currentState = GameState.Playing;

        Time.timeScale = 1f;
    }

    public void SetSequence()
    {
        currentState = GameState.Sequence;
    }

    public void SetMenu()
    {
        currentState = GameState.EndMenu;
    }

    public void EndSequence()
    {
        ResumeGame();
    }

    private void UpdateInternalTimer()
    {
        currentPlaytime += Time.deltaTime;

        if (currentPlaytime >= maxPlaytimeInSeconds)
        {
            currentState = GameState.GameOver;
            int finalScore = CalculateScore(false);

            UI_GameOver.Instance.SetGameOverScreenWithScore(finalScore, 3);
        }
    }

    private int CalculateScore(bool gameWon)
    {
        int score = Mathf.FloorToInt(maxScore - currentPlaytime);

        if (gameWon)
        {
            score += 100;
        }
        else
        {
            score -= gameLostScorePenalty; // Penalty für Niederlage
        }

        return Mathf.FloorToInt(score);
    }

    public void WallWasDestroyed(Wall wall)
    {
        for (int i = 0; i < rooms.Length; i++)
        {
            for (int j = 0; j < rooms[i].wallsInThisRoom.Length; j++)
            {
                if (rooms[i].wallsInThisRoom[j] == wall)
                {
                    rooms[i].RemoveWallFromRoomArray(wall);
                    if (rooms[i].isEnemyInThisRoom)
                    {
                        Debug.Log("Enemy was in this room!");
                        StartCoroutine(EndSequence(rooms[i]));
                        return;
                    }
                    else
                    {
                        rooms[i].wallsInThisRoom[j] = null;
                    }
                    Debug.Log("Enemy wasnt in this room");
                }

            }

        }
    }


    IEnumerator CameraShake(float duration, float magnitude)
    {
        Vector3 originalPos = playerCamera.transform.localPosition;
        float elapsed = 0.0f;
        while (elapsed < duration)
        {
            float x = Random.Range(-1f, 1f) * magnitude;
            float y = Random.Range(-1f, 1f) * magnitude;
            playerCamera.transform.localPosition = new Vector3(x, y, originalPos.z);
            elapsed += Time.deltaTime;
            yield return null;
        }
        playerCamera.transform.localPosition = originalPos;
    }

    IEnumerator EndSequence(RoomObj room)
    {
        arms.SetActive(false);
        Debug.Log("Starting end sequence for room: " + room.name);
        currentState = GameState.Sequence;

        playerController.walkSpeed = 1f;
        playerController.SetCursor(true);

        yield return new WaitForSeconds(1f);

        StartCoroutine(RotateCameraToTarget(room.transform, 4f));

        yield return new WaitForSeconds(4f);

        // Danach Spielende auslösen
        GameOver(false);
    }

    private IEnumerator RotateCameraToTarget(Transform target, float duration)
    {
        Debug.Log("Starting Camera Rotation to Target: " + target.name);
        playerCameraController.enabled = false; // Deaktiviere die Spielersteuerung während der Kamerafahrt

        if (playerCamera == null || target == null)
            yield break;

        Transform camT = playerCamera.transform;
        Quaternion startRot = camT.rotation;
        Vector3 direction = target.position - camT.position;
        if (direction.sqrMagnitude <= 0f)
            yield break;

        Quaternion targetRot = Quaternion.LookRotation(direction.normalized);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            camT.rotation = Quaternion.Slerp(startRot, targetRot, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        camT.rotation = targetRot;
    }
}
