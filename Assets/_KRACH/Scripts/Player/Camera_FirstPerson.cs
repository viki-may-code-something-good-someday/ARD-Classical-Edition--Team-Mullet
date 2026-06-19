using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class Camera_FirstPerson : NetworkBehaviour
{
    public Transform playerBody;     // The character root (rotates horizontally)
    public Transform cameraPivot;    // The vertical pivot (rotates up/down)

    [SerializeField] private Camera playerCamera;
    public float mouseSensitivity = 1.5f;

    private float xRotation = 0f;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!isLocalPlayer)
        {
            // Kamera für andere Spieler ausschalten
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) { cam.enabled = false; }

            // AudioListener auch ausschalten falls vorhanden
            AudioListener listener = GetComponentInChildren<AudioListener>();
            if (listener != null) { listener.enabled = false; }

            // FMOD-Listener für andere Spieler ausschalten — sonst hat FMOD auf jedem Client
            // mehrere Listener und spatialisiert (inkl. Occlusion) vom nächstgelegenen Spieler
            // statt vom eigenen. So bleibt pro Client genau ein Listener: der lokale Spieler.
            FMODUnity.StudioListener fmodListener = GetComponentInChildren<FMODUnity.StudioListener>();
            if (fmodListener != null) { fmodListener.enabled = false; }
        }
    }

    void Update()
    {
        if (!isLocalPlayer) { return; }

        HandleCursorToggle();
        HandleCameraRotation();
    }

    private void HandleCursorToggle()
    {
        if (Input.GetKeyDown(KeyCode.T)) { SwitchCursorMode(); }
    }

    private void HandleCameraRotation()
    {
        if (Mouse.current == null) { return; }
        if (Cursor.lockState == CursorLockMode.None) { return; }

        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * mouseSensitivity;

        xRotation -= mouseDelta.y;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);
        cameraPivot.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        playerBody.Rotate(Vector3.up * mouseDelta.x);
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

    public void StartCameraShake(float _duration, float _magnitude)
    {
        StartCoroutine(CameraShake(_duration, _magnitude));
    }

    private IEnumerator CameraShake(float _duration, float _magnitude)
    {
        if (playerCamera == null) { yield break; }

        Vector3 originalPos = playerCamera.transform.localPosition;
        float elapsed = 0f;

        while (elapsed < _duration)
        {
            float x = Random.Range(-1f, 1f) * _magnitude;
            float y = Random.Range(-1f, 1f) * _magnitude;
            playerCamera.transform.localPosition = new Vector3(x, y, originalPos.z);
            elapsed += Time.deltaTime;
            yield return null;
        }

        playerCamera.transform.localPosition = originalPos;
    }
}