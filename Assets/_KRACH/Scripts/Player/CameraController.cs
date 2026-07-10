using Mirror;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class CameraController : NetworkBehaviour
{
    [Header("References")]
    public Transform playerBody;
    public Transform cameraPivot;
    [SerializeField] private Camera playerCamera;

    [Header("Settings")]
    public float mouseSensitivity = 1.5f;

    // Gameplay-Scene-Pfad kommt direkt vom NetworkManager – kein doppeltes Feld nötig.
    private string GameplayScene => (NetworkManager.singleton as CustomNetworkManager)?.GameplayScene ?? string.Empty;

    private float xRotation = 0f;
    private bool wasInGameplay = false;

    // ── Start ─────────────────────────────────────────────────────────────────

    void Start()
    {
        // Cursor in der Lobby immer freigeben.
        // Locken passiert erst beim Betreten der Gameplay-Scene.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // ── OnStartClient: Fremdspieler-Cleanup ───────────────────────────────────

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (isLocalPlayer) return;

        Camera cam = GetComponentInChildren<Camera>();
        if (cam != null) cam.enabled = false;

        AudioListener audioListener = GetComponentInChildren<AudioListener>();
        if (audioListener != null) audioListener.enabled = false;

        FMODUnity.StudioListener fmodListener = GetComponentInChildren<FMODUnity.StudioListener>();
        if (fmodListener != null) fmodListener.enabled = false;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    void Update()
    {
        if (!isLocalPlayer) return;

        bool inGameplay = SceneManager.GetActiveScene().path == GameplayScene;

        // Lobby → Gameplay: Cursor locken
        if (inGameplay && !wasInGameplay)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        // Gameplay → Lobby: Cursor freigeben, Rotation zurücksetzen
        else if (!inGameplay && wasInGameplay)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            xRotation = 0f;
        }

        wasInGameplay = inGameplay;

        if (!inGameplay) return;

        HandleCursorToggle();
        HandleCameraRotation();
    }

    // ── Cursor ────────────────────────────────────────────────────────────────

    private void HandleCursorToggle()
    {
        if (Input.GetKeyDown(KeyCode.T))
            SwitchCursorMode();
    }

    public void SwitchCursorMode()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // ── Kamera-Rotation ───────────────────────────────────────────────────────

    private void HandleCameraRotation()
    {
        if (Mouse.current == null) return;
        if (Cursor.lockState == CursorLockMode.None) return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * mouseSensitivity;

        xRotation -= mouseDelta.y;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        cameraPivot.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        playerBody.Rotate(Vector3.up * mouseDelta.x);
    }

    // ── Kamera-Shake ─────────────────────────────────────────────────────────

    public void StartCameraShake(float duration, float magnitude)
    {
        StartCoroutine(CameraShake(duration, magnitude));
    }

    private IEnumerator CameraShake(float duration, float magnitude)
    {
        if (playerCamera == null) yield break;

        Vector3 originalPos = playerCamera.transform.localPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            playerCamera.transform.localPosition = originalPos + new Vector3(
                Random.Range(-1f, 1f) * magnitude,
                Random.Range(-1f, 1f) * magnitude,
                0f
            );
            elapsed += Time.deltaTime;
            yield return null;
        }

        playerCamera.transform.localPosition = originalPos;
    }
}