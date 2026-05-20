using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public class Camera_FirstPerson : NetworkBehaviour
{
    public Transform playerBody;     // The character root (rotates horizontally)
    public Transform cameraPivot;    // The vertical pivot (rotates up/down)
    public float mouseSensitivity = 1.5f;

    private float xRotation = 0f;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public override void OnStartClient()
    {
        if (!isLocalPlayer)
        {
            // Kamera für andere Spieler ausschalten
            GetComponentInChildren<Camera>().enabled = false;
            // AudioListener auch ausschalten falls vorhanden
            GetComponentInChildren<AudioListener>().enabled = false;
        }
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

    void Update()
    {
        if (!isLocalPlayer) return;

        if (Input.GetKeyDown(KeyCode.T)) { SwitchCursorMode(); }
        if (Mouse.current == null) return;
        if (Cursor.lockState == CursorLockMode.None) return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * mouseSensitivity;

        xRotation -= mouseDelta.y;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);
        cameraPivot.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        playerBody.Rotate(Vector3.up * mouseDelta.x);

        /*
        if (Input.GetKeyDown(KeyCode.T)) { SwitchCursorMode(); }

        if (Mouse.current == null) return;
        if (Cursor.lockState == CursorLockMode.None) return;

        // Get mouse movement delta
        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * mouseSensitivity;

        // Vertical rotation (pitch)
        xRotation -= mouseDelta.y;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);
        cameraPivot.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // Horizontal rotation (yaw)
        playerBody.Rotate(Vector3.up * mouseDelta.x);
        */
    }
}
