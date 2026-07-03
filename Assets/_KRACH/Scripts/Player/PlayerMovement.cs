using Mirror;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Verwaltet die gesamte Spielerbewegung: Laufen, Sprinten, Springen, Hocken, Footsteps, FOV.
/// Enthält keine Rollenlogik – wird von PlayerRoleSetup mit dem passenden
/// RoleMovementConfig konfiguriert.
///
/// Prefab-Rig Voraussetzung:
///   Player (CharacterController, NetworkTransform, PlayerMovement, ...)
///   ├── GroundCheck          ← groundCheck-Referenz
///   └── CameraHolder         ← cameraHolder-Referenz (leeres Transform)
///       └── Camera           ← playerCamera-Referenz (wird vom MouseLook-Script rotiert)
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : NetworkBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("References")]
    [SerializeField] private CharacterController controller;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private Camera playerCamera;
    [Tooltip("Leeres Transform-Objekt das die Camera trägt. Wird für Crouch-Offset bewegt.")]
    [SerializeField] private Transform cameraHolder;

    [Header("Ground Check")]
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private float groundDistance = 0.4f;

    [Header("Audio")]
    [SerializeField] private float baseStepInterval = 0.3f;
    [Tooltip("LuaSoundEmitter am Fußpunkt, Script-Mode. Enable Multiplayer = false.")]
    [SerializeField] private LuaSoundEmitter footstepSoundEmitter;

    // ── State ────────────────────────────────────────────────────────────────

    private RoleMovementConfig config;

    private Vector3 velocity;
    private bool isGrounded;
    private float currentFallGravity;
    private float airTime;

    private bool sprinting;
    private float currentSprintSpeed;
    private float sprintTimer;

    private bool isJumping;
    private bool isHoldingJump;
    private float jumpHoldTimer;

    private bool isCrouching;
    private float footstepTimer;

    // ── Init ─────────────────────────────────────────────────────────────────

    void Start()
    {
        controller.enabled = false; // gesperrt bis ApplyConfig aufgerufen wird
    }

    /// <summary>
    /// Wird von PlayerRoleSetup aufgerufen sobald die Rolle feststeht.
    /// Nur auf dem Owner-Client ausführen.
    /// </summary>
    public void ApplyConfig(RoleMovementConfig roleConfig)
    {
        config = roleConfig;
        currentSprintSpeed = config.baseSprintSpeed;
        currentFallGravity = config.baseFallGravity;

        controller.height = config.standHeight;
        controller.center = CalculateCenter(config.standHeight);

        if (cameraHolder != null)
        {
            Vector3 pos = cameraHolder.localPosition;
            pos.y = config.standCameraY;
            cameraHolder.localPosition = pos;
        }

        if (playerCamera != null)
            playerCamera.fieldOfView = config.normalFOV;

        controller.enabled = true;
    }

    /// <summary>
    /// Berechnet den Kapsel-Mittelpunkt so, dass die Kapsel-Unterkante
    /// immer exakt auf Höhe von groundCheck liegt (= echte Füße).
    /// </summary>
    private Vector3 CalculateCenter(float height)
    {
        float feetY = groundCheck.localPosition.y;
        return new Vector3(0f, feetY + height / 2f, 0f);
    }

    /// <summary>
    /// Teleportiert den Spieler zur angegebenen Position.
    /// Wird von PlayerRoleSetup beim Spawn aufgerufen.
    /// </summary>
    public void TeleportTo(Vector3 position)
    {
        controller.enabled = false;
        transform.position = position;
        velocity = Vector3.zero;
        controller.enabled = true;
    }

    // ── Update ───────────────────────────────────────────────────────────────

    void Update()
    {
        if (config == null || !isOwned) return;

        HandleMovement();
        HandleJump();     // setzt velocity.y – muss vor dem vertikalen Move laufen
        HandleCrouch();
        HandleFOV();

        // Vertikaler Move ganz am Ende: HandleJump() hat velocity.y bereits gesetzt
        controller.Move(velocity * Time.deltaTime);
    }

    // ── Bewegung ─────────────────────────────────────────────────────────────

    private void HandleMovement()
    {
        isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);

        if (isGrounded)
        {
            if (velocity.y < 0f) velocity.y = -2f;
            currentFallGravity = config.baseFallGravity;
            airTime = 0f;
        }
        else
        {
            airTime += Time.deltaTime;
            currentFallGravity += config.fallGravityScaling * Time.deltaTime * currentFallGravity;
            currentFallGravity = Mathf.Clamp(currentFallGravity, config.baseFallGravity, config.maxFallGravity);
            velocity.y -= currentFallGravity * Time.deltaTime;
        }

        Vector2 moveInput = InputManager.Instance != null
            ? InputManager.Instance.CurrentInput.MoveInput
            : Vector2.zero;

        float x = moveInput.x;
        float z = moveInput.y;
        bool isMoving = x != 0f || z != 0f;

        bool sprintInput = InputManager.Instance != null
            && InputManager.Instance.CurrentInput.SprintHeld;
        sprinting = sprintInput && config.canSprint && !isCrouching;

        // Sprint-Beschleunigung mit Burst
        if (sprinting && isMoving)
        {
            sprintTimer += Time.deltaTime;
            float accel = sprintTimer < config.sprintBurstThreshold
                ? config.sprintAcceleration
                : config.sprintBurstAcceleration;
            currentSprintSpeed = Mathf.Min(currentSprintSpeed + accel * Time.deltaTime, config.maxSprintSpeed);
        }
        else
        {
            sprintTimer = 0f;
            currentSprintSpeed = Mathf.Max(
                currentSprintSpeed - config.sprintDecaySpeed * Time.deltaTime,
                config.baseSprintSpeed);
        }

        // Effektive Geschwindigkeit
        float speed = sprinting && isMoving ? currentSprintSpeed : config.walkSpeed;
        if (isCrouching) speed = config.walkSpeed * config.crouchSpeedMultiplier;

        HandleFootsteps(isMoving, speed);

        Vector3 move = transform.right * x + transform.forward * z;
        controller.Move(move * speed * Time.deltaTime);
        // velocity wird am Ende von Update() per controller.Move(velocity * Time.deltaTime) angewendet
    }

    // ── Springen ─────────────────────────────────────────────────────────────

    private void HandleJump()
    {
        if (InputManager.Instance == null) return;

        InputData input = InputManager.Instance.CurrentInput;

        bool jumpStartedThisFrame = false;

        if (input.JumpPressed && isGrounded && !isCrouching)
        {
            velocity.y = Mathf.Sqrt(config.jumpHeight * 2f * config.baseFallGravity);
            isJumping = true;
            isHoldingJump = true;
            jumpHoldTimer = 0f;
            currentFallGravity = config.baseFallGravity;
            jumpStartedThisFrame = true;
        }

        // Variable Sprunghöhe: Taste halten = höher springen.
        // jumpStartedThisFrame verhindert dass der Schnitt (velocity.y *= 0.3f) im selben Frame
        // greift in dem der Sprung gestartet wurde – sonst wird er sofort abgewürgt wenn
        // JumpHeld im ersten Frame noch nicht true ist.
        if (isHoldingJump && isJumping && velocity.y > 0f && !jumpStartedThisFrame)
        {
            if (!input.JumpHeld)
            {
                isHoldingJump = false;
                velocity.y *= 0.3f;
            }
            else if (jumpHoldTimer < config.jumpHoldTime)
            {
                velocity.y += config.baseFallGravity * config.jumpHoldGravityMultiplier * Time.deltaTime;
                jumpHoldTimer += Time.deltaTime;
            }
        }

        if (velocity.y <= 0f)
            isJumping = false;
    }

    // ── Hocken ───────────────────────────────────────────────────────────────

    private void HandleCrouch()
    {
        if (InputManager.Instance == null) return;

        bool crouchInput = InputManager.Instance.CurrentInput.CrouchHeld;

        if (crouchInput && !isCrouching)
        {
            SetCrouching(true);
        }
        else if (!crouchInput && isCrouching && CanStandUp())
        {
            SetCrouching(false);
        }

        // Kamera smooth überblenden
        if (cameraHolder != null)
        {
            float targetY = isCrouching ? config.crouchCameraY : config.standCameraY;
            Vector3 pos = cameraHolder.localPosition;
            pos.y = Mathf.Lerp(pos.y, targetY, config.crouchTransitionSpeed * Time.deltaTime);
            cameraHolder.localPosition = pos;
        }
    }

    private void SetCrouching(bool crouch)
    {
        isCrouching = crouch;
        float height = crouch ? config.crouchHeight : config.standHeight;
        controller.height = height;
        controller.center = CalculateCenter(height);
    }

    private bool CanStandUp()
    {
        Vector3 top = transform.position + Vector3.up * (config.standHeight - controller.radius);
        return !Physics.CheckSphere(top, controller.radius, groundMask);
    }

    // ── FOV ──────────────────────────────────────────────────────────────────

    private void HandleFOV()
    {
        if (playerCamera == null) return;

        float targetFOV = config.normalFOV;
        if (sprinting && sprintTimer >= config.sprintBurstThreshold)
            targetFOV = config.sprintFOV;

        playerCamera.fieldOfView = Mathf.Lerp(
            playerCamera.fieldOfView, targetFOV,
            config.fovChangeSpeed * Time.deltaTime);
    }

    // ── Footstep-Audio ────────────────────────────────────────────────────────

    private void HandleFootsteps(bool isMoving, float currentSpeed)
    {
        if (!isGrounded || !isMoving)
        {
            footstepTimer = 0f;
            return;
        }

        float speedMultiplier = sprinting ? 4f * config.walkSpeed / config.maxSprintSpeed : 1f;
        float interval = baseStepInterval
            * (config.walkSpeed / Mathf.Max(0.0001f, currentSpeed * speedMultiplier));

        footstepTimer += Time.deltaTime;
        if (footstepTimer >= interval)
        {
            if (footstepSoundEmitter != null) footstepSoundEmitter.PlayOneShot();
            CmdBroadcastFootstep();
            footstepTimer = 0f;
        }
    }

    // ── Öffentliche API ──────────────────────────────────────────────────────

#if UNITY_EDITOR
    [Button("Debug: Jump State")]
    private void DebugJumpState()
    {
        Debug.Log($"[PlayerMovement] config={config != null}, isOwned={isOwned}, isGrounded={isGrounded}");
        Debug.Log($"[PlayerMovement] groundMask={groundMask.value} (0 = Nothing → Sprung unmöglich!)");
        Debug.Log($"[PlayerMovement] groundCheck pos={groundCheck?.position}, groundDistance={groundDistance}");
        Debug.Log($"[PlayerMovement] velocity.y={velocity.y}, isJumping={isJumping}, isHoldingJump={isHoldingJump}");

        if (groundMask.value == 0)
            Debug.LogError("[PlayerMovement] groundMask ist 'Nothing' – isGrounded wird nie true! Layer im Inspector setzen.");
        if (groundCheck == null)
            Debug.LogError("[PlayerMovement] groundCheck ist nicht zugewiesen!");
        if (config == null)
            Debug.LogError("[PlayerMovement] config ist null – ApplyConfig() wurde noch nicht aufgerufen.");
    }
#endif

    public void FreezeMovement()
    {
        controller.enabled = false;
        velocity = Vector3.zero;
        currentSprintSpeed = config != null ? config.baseSprintSpeed : 0f;
        currentFallGravity = config != null ? config.baseFallGravity : 0f;
        airTime = 0f;
        if (playerCamera != null && config != null)
            playerCamera.fieldOfView = config.normalFOV;
    }

    public void UnfreezeMovement() => controller.enabled = true;

    public bool IsCrouching => isCrouching;
    public bool IsSprinting => sprinting;
    public bool IsGrounded => isGrounded;

    // ── Mirror – Footstep Sync ────────────────────────────────────────────────

    [Command]
    private void CmdBroadcastFootstep() => RpcPlayFootstepOnOthers();

    [ClientRpc(includeOwner = false)]
    private void RpcPlayFootstepOnOthers()
    {
        if (footstepSoundEmitter != null) footstepSoundEmitter.PlayOneShot();
    }
}