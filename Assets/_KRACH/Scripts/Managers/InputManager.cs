using UnityEngine;
using UnityEngine.InputSystem;

// Input snapshot for a single frame, consumed by gameplay systems.
public struct InputData
{
    public Vector2 MoveInput;
    public Vector2 LookInput;
    public bool SprintHeld;
    public bool ActionPressed;
    public bool InteractPressed;
    public bool JumpPressed;
    public bool CrouchHeld;
    public bool JumpHeld;
    public PlayerRole Role;
}

// Reads local input each frame and fires OnInputChanged when anything changes.
// Subscribe to OnInputChanged or poll CurrentInput directly.
// Requires a PlayerInput component with Action Map "Player" and actions:
// Move, Look, Sprint, Action, Interact, Jump, Crouch
[RequireComponent(typeof(PlayerInput))]
public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    public event System.Action<InputData> OnInputChanged;
    public InputData CurrentInput { get; private set; }

    private PlayerInput playerInput;
    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction sprintAction;
    private InputAction actionAction;
    private InputAction interactAction;
    private InputAction jumpAction;
    private InputAction crouchAction;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        playerInput = GetComponent<PlayerInput>();

        moveAction = playerInput.actions["Move"];
        lookAction = playerInput.actions["Look"];
        sprintAction = playerInput.actions["Sprint"];
        actionAction = playerInput.actions["Action"];
        interactAction = playerInput.actions["Interact"];
        jumpAction = playerInput.actions["Jump"];
        crouchAction = playerInput.actions["Crouch"];
    }

    private void Update()
    {
        ReadAndBroadcastInput();
    }

    public void SetRole(PlayerRole _newRole)
    {
        InputData updated = CurrentInput;
        updated.Role = _newRole;
        CurrentInput = updated;
        OnInputChanged?.Invoke(CurrentInput);
        Debug.Log($"[InputManager] Role set to: {_newRole}");
    }

    private void ReadAndBroadcastInput()
    {
        InputData newInput = new InputData
        {
            MoveInput = moveAction.ReadValue<Vector2>(),
            LookInput = lookAction.ReadValue<Vector2>(),
            SprintHeld = sprintAction.IsPressed(),
            ActionPressed = actionAction.WasPressedThisFrame(),
            InteractPressed = interactAction.WasPressedThisFrame(),
            JumpPressed = jumpAction.WasPressedThisFrame(),
            CrouchHeld = crouchAction.IsPressed(),
            Role = CurrentInput.Role  // Role wird lokal gehalten, nicht überschrieben
        };

        if (HasInputChanged(CurrentInput, newInput))
        {
            CurrentInput = newInput;
            OnInputChanged?.Invoke(CurrentInput);
        }
    }

    private bool HasInputChanged(InputData _previous, InputData _current)
    {
        return _previous.MoveInput != _current.MoveInput
            || _previous.LookInput != _current.LookInput
            || _previous.SprintHeld != _current.SprintHeld
            || _previous.ActionPressed != _current.ActionPressed
            || _previous.InteractPressed != _current.InteractPressed
            || _previous.JumpPressed != _current.JumpPressed
            || _previous.CrouchHeld != _current.CrouchHeld;
    }
}