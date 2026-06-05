using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

// Input snapshot for a single frame, consumed by gameplay systems.
public struct InputData
{
    public Vector2 MoveInput;       // WASD / left stick
    public Vector2 LookInput;       // Mouse delta / right stick
    public bool SprintHeld;
    public bool ActionPressed;      // Schlagen / Zeigen (one-shot)
    public bool InteractPressed;    // one-shot
    public bool JumpPressed;        // one-shot
    public bool CrouchHeld;
    public PlayerRole Role;
}

// Reads local input each frame and fires OnInputChanged when anything changes.
// Subscribe to OnInputChanged or poll CurrentInput directly.
// Requires a PlayerInput component with Action Map "Player" and actions:
// Move, Look, Sprint, Action, Interact, Jump, Crouch
[RequireComponent(typeof(PlayerInput))]
public class InputManager : NetworkBehaviour
{
    [Header("Role")]
    [SyncVar(hook = nameof(OnRoleChanged))]
    public PlayerRole Role = PlayerRole.Default;

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


    public static InputManager Instance { get; private set; }


    private void Awake()
    {
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
        if (!isLocalPlayer) { return; }
        ReadAndBroadcastInput();
    }


    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        Instance = this;
        playerInput.enabled = true;
        Debug.Log($"[InputManager] Local player started — Role: {Role}");
    }

    public override void OnStopLocalPlayer()
    {
        base.OnStopLocalPlayer();
        if (Instance == this) { Instance = null; }
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
            Role = Role
        };

        if (HasInputChanged(CurrentInput, newInput))
        {
            CurrentInput = newInput;
            OnInputChanged?.Invoke(CurrentInput);
        }
    }

    // Mirror SyncVar hook — fires when Role changes on any client
    private void OnRoleChanged(PlayerRole _oldRole, PlayerRole _newRole)
    {
        Debug.Log($"[InputManager] Role changed: {_oldRole} → {_newRole}");
        OnInputChanged?.Invoke(CurrentInput);
    }

    [Server]
    public void ServerSetRole(PlayerRole _newRole)
    {
        Role = _newRole;
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