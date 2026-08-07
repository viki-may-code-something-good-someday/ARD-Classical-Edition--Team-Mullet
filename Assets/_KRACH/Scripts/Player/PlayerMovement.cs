using Mirror;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Spielerbewegung: Laufen, Sprinten, Springen, Hocken, Footsteps, FOV.
/// Enthält keine Rollenlogik – PlayerRoleSetup übergibt das passende RoleMovementConfig.
///
/// Jump: asymmetrische Gravity (niedrig beim Steigen, hoch beim Fallen). Frühes Loslassen
/// kappt die Restgeschwindigkeit und schaltet auf die stärkste Gravity-Stufe → kurze Hops.
///
/// Prefab-Rig:
///   Player (CharacterController, NetworkTransform, PlayerMovement, ...)
///   ├── GroundCheck   ← muss exakt auf Höhe der Füße sitzen (gleicht Pivot-Offsets aus)
///   └── CameraHolder
///       └── Camera
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : NetworkBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("References")]
    [SerializeField] private CharacterController controller;
    [Tooltip("Direktes Kind des Player-Roots, exakt auf Höhe der Füße. Gleicht Pivot-Offsets des Modells aus.")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private Camera playerCamera;
    [Tooltip("Leeres Transform das die Camera trägt. Wird für den Crouch-Offset bewegt.")]
    [SerializeField] private Transform cameraHolder;
    [Tooltip("Kapsel auf dem Player-Layer die als Trefferzone dient (Ziel des Hunter-Raycasts)." +
             " Wird auf allen Clients an die Controller-Kapsel angeglichen. Leer = CapsuleCollider am Root.")]
    [SerializeField] private CapsuleCollider hitCollider;

    [Header("Ground Check")]
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private float groundDistance = 0.4f;

    [Header("Audio")]
    [SerializeField] private float baseStepInterval = 0.3f;
    [Tooltip("LuaSoundEmitter am Fußpunkt, Script-Mode. Enable Multiplayer = false.")]
    [SerializeField] private LuaSoundEmitter footstepSoundEmitter;

    // ── State ────────────────────────────────────────────────────────────────

    private RoleMovementConfig config;

    private Vector3 velocity;          // vertikal
    private Vector3 horizontalVelocity; // in HandleMovement gesetzt, in Update angewendet
    private bool isGrounded;

    private bool sprinting;
    private float currentSprintSpeed;
    private float sprintTimer;

    private bool jumpButtonHeld;
    private bool isCrouching;
    private float footstepTimer;

    public bool IsCrouching => isCrouching;
    public bool IsSprinting => sprinting;
    public bool IsGrounded => isGrounded;

    /// <summary>Lokale Y-Position der Füße relativ zum Root-Pivot.</summary>
    private float FeetOffsetY => groundCheck != null ? groundCheck.localPosition.y : 0f;

    // Höhe der Körperkapsel. Der Owner meldet jede Änderung (Stehen/Hocken) per Command,
    // damit die Trefferzone auf allen Clients zum tatsächlichen Zustand passt.
    [SyncVar(hook = nameof(OnBodyHeightChanged))]
    private float syncedBodyHeight = 2f;

    // ── Init ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (hitCollider == null) hitCollider = GetComponent<CapsuleCollider>();
    }

    void Start()
    {
        controller.enabled = false; // gesperrt bis ApplyConfig aufgerufen wird

        // Remote-Spieler bekommen ihre Kapselmaße ausschließlich über die SyncVar.
        if (!isOwned) ApplyHitColliderHeight(syncedBodyHeight);
    }

    /// <summary>
    /// Wird von PlayerRoleSetup aufgerufen sobald die Rolle feststeht. Nur auf dem Owner-Client.
    /// </summary>
    public void ApplyConfig(RoleMovementConfig roleConfig)
    {
        config = roleConfig;
        currentSprintSpeed = config.baseSprintSpeed;

        SetBodyHeight(config.standHeight);

        if (cameraHolder != null)
        {
            Vector3 pos = cameraHolder.localPosition;
            pos.y = config.standCameraY;
            cameraHolder.localPosition = pos;
        }

        if (playerCamera != null)
            playerCamera.fieldOfView = config.normalFOV;

        controller.enabled = true;

        WarnIfCameraLeavesCapsule();
    }

    /// <summary>
    /// Die Kamerahöhen sind relativ zum Root-Pivot angegeben, die Kapselhöhen relativ zu den
    /// Füßen. Liegt eine Kamera über ihrer Kapsel, schaut man durch niedrige Decken hindurch –
    /// beim Hocken passiert das schnell unbemerkt.
    /// </summary>
    private void WarnIfCameraLeavesCapsule()
    {
        CheckEyeHeight("Crouch", config.crouchCameraY, config.crouchHeight);
        CheckEyeHeight("Stand", config.standCameraY, config.standHeight);
    }

    private void CheckEyeHeight(string label, float cameraY, float capsuleHeight)
    {
        float eyeAboveFeet = cameraY - FeetOffsetY;
        if (eyeAboveFeet <= capsuleHeight) return;

        Debug.LogWarning($"[PlayerMovement] {label}: Kamera liegt {eyeAboveFeet:F2}m über den Füßen, " +
                         $"die Kapsel ist aber nur {capsuleHeight:F2}m hoch – man sieht durch Decken. " +
                         $"{label}CameraY muss kleiner als {capsuleHeight + FeetOffsetY:F2} sein.");
    }

    /// <summary>
    /// Setzt Controller- und Trefferkapsel so, dass die Unterkante immer auf Höhe von groundCheck
    /// liegt – unabhängig davon wo der Pivot des Modells sitzt. Nur der Owner ruft das auf.
    /// </summary>
    private void SetBodyHeight(float height)
    {
        controller.height = height;
        controller.center = CapsuleCenter(height);

        ApplyHitColliderHeight(height);

        if (isOwned && NetworkClient.ready) CmdSetBodyHeight(height);
    }

    /// <summary>Trefferzone an die Körperhöhe angleichen. Läuft auf allen Clients.</summary>
    private void ApplyHitColliderHeight(float height)
    {
        if (hitCollider == null || height <= 0f) return;

        hitCollider.height = height;
        hitCollider.center = CapsuleCenter(height);
    }

    private Vector3 CapsuleCenter(float height) => new Vector3(0f, FeetOffsetY + height / 2f, 0f);

    [Command]
    private void CmdSetBodyHeight(float height) => syncedBodyHeight = height;

    private void OnBodyHeightChanged(float oldHeight, float newHeight)
    {
        // Der Owner hat seine eigene Kapsel bereits lokal gesetzt.
        if (!isOwned) ApplyHitColliderHeight(newHeight);
    }

    /// <summary>
    /// Teleportiert den Spieler. Wird von PlayerRoleSetup beim Spawn/Respawn aufgerufen
    /// (nur für den Owner sinnvoll, da das Movement client-authoritative ist).
    /// Der Aktivierungszustand des Controllers bleibt erhalten – ein Teleport darf einen
    /// eingefrorenen Spieler (Caught-Zustand) nicht vorzeitig wieder beweglich machen.
    /// </summary>
    public void TeleportTo(Vector3 position)
    {
        bool wasEnabled = controller.enabled;

        controller.enabled = false;
        transform.position = position;
        velocity = Vector3.zero;
        horizontalVelocity = Vector3.zero;
        controller.enabled = wasEnabled;
    }

    // ── Update ───────────────────────────────────────────────────────────────

    void Update()
    {
        // Deaktivierter Controller = eingefroren (FreezeMovement). Ohne diese Prüfung liefe
        // die komplette Bewegungslogik weiter: Hocken, Gravity-Aufbau und Move-Aufrufe auf
        // einem deaktivierten CharacterController.
        if (config == null || !isOwned || !controller.enabled) return;

        HandleMovement();
        HandleJump();
        HandleCrouch();
        HandleFOV();

        // Ein einziger Move pro Frame: horizontal + vertikal kombiniert. Zwei getrennte
        // Aufrufe kosten einen zusätzlichen Kapsel-Sweep und reagieren an Kanten anders.
        controller.Move((horizontalVelocity + velocity) * Time.deltaTime);
    }

    // ── Bewegung ─────────────────────────────────────────────────────────────

    private void HandleMovement()
    {
        isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);

        if (isGrounded)
        {
            if (velocity.y < 0f) velocity.y = -2f;
        }
        else
        {
            ApplyAirGravity();
        }

        Vector2 moveInput = InputManager.Instance != null
            ? InputManager.Instance.CurrentInput.MoveInput
            : Vector2.zero;
        bool isMoving = moveInput != Vector2.zero;

        bool sprintInput = InputManager.Instance != null
            && InputManager.Instance.CurrentInput.SprintHeld;
        sprinting = sprintInput && config.canSprint && !isCrouching;

        UpdateSprintSpeed(isMoving);

        float speed = isCrouching
            ? config.walkSpeed * config.crouchSpeedMultiplier
            : (sprinting && isMoving ? currentSprintSpeed : config.walkSpeed);

        HandleFootsteps(isMoving, speed);

        Vector3 direction = transform.right * moveInput.x + transform.forward * moveInput.y;
        horizontalVelocity = direction * speed;
    }

    /// <summary>Sprint beschleunigt langsam an und bekommt nach sprintBurstThreshold einen Burst.</summary>
    private void UpdateSprintSpeed(bool isMoving)
    {
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
    }

    /// <summary>
    /// Asymmetrische Gravity: niedrig beim Aufstieg (passend zur sqrt(2gh)-Formel in HandleJump),
    /// stärker beim Fallen, am stärksten nach frühem Loslassen der Jump-Taste.
    /// </summary>
    private void ApplyAirGravity()
    {
        float gravityMultiplier;

        if (velocity.y > 0f)
        {
            gravityMultiplier = jumpButtonHeld
                ? config.riseGravityMultiplier
                : config.jumpCutGravityMultiplier;
        }
        else
        {
            gravityMultiplier = config.fallGravityMultiplier;
        }

        velocity.y -= config.baseFallGravity * gravityMultiplier * Time.deltaTime;
        velocity.y = Mathf.Max(velocity.y, -config.maxFallSpeed);
    }

    // ── Springen ─────────────────────────────────────────────────────────────

    private void HandleJump()
    {
        if (InputManager.Instance == null) return;

        InputData input = InputManager.Instance.CurrentInput;

        if (input.JumpPressed && isGrounded && !isCrouching)
        {
            velocity.y = Mathf.Sqrt(config.jumpHeight * 2f * config.baseFallGravity * config.riseGravityMultiplier);
            jumpButtonHeld = true;
            return; // Cut darf im Startframe nicht greifen (JumpHeld ist evtl. noch false)
        }

        // Frühes Loslassen während des Aufstiegs: Velocity kappen und auf die stärkste
        // Gravity-Stufe wechseln (siehe ApplyAirGravity).
        if (jumpButtonHeld && !input.JumpHeld && velocity.y > 0f)
        {
            jumpButtonHeld = false;
            velocity.y *= config.jumpCutVelocityMultiplier;
        }
        else if (velocity.y <= 0f)
        {
            jumpButtonHeld = false;
        }
    }

    // ── Hocken ───────────────────────────────────────────────────────────────

    private void HandleCrouch()
    {
        if (InputManager.Instance == null) return;

        bool crouchInput = InputManager.Instance.CurrentInput.CrouchHeld;

        if (crouchInput && !isCrouching) SetCrouching(true);
        else if (!crouchInput && isCrouching && CanStandUp()) SetCrouching(false);

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
        SetBodyHeight(crouch ? config.crouchHeight : config.standHeight);
    }

    private bool CanStandUp()
    {
        // Prüfsphäre am oberen Ende der Stehkapsel – relativ zu den Füßen, nicht zum Pivot.
        Vector3 top = transform.position
            + Vector3.up * (FeetOffsetY + config.standHeight - controller.radius);
        return !Physics.CheckSphere(top, controller.radius, groundMask);
    }

    // ── FOV ──────────────────────────────────────────────────────────────────

    private void HandleFOV()
    {
        if (playerCamera == null) return;

        float targetFOV = sprinting && sprintTimer >= config.sprintBurstThreshold
            ? config.sprintFOV
            : config.normalFOV;

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

        // Referenzgeschwindigkeit, bei der baseStepInterval gilt. Beim Sprint deutlich
        // niedriger angesetzt → merklich schnellere Schrittfolge.
        float referenceSpeed = sprinting ? config.maxSprintSpeed * 0.25f : config.walkSpeed;
        float interval = baseStepInterval * referenceSpeed / Mathf.Max(0.0001f, currentSpeed);

        footstepTimer += Time.deltaTime;
        if (footstepTimer < interval) return;

        footstepTimer = 0f;
        if (footstepSoundEmitter != null) footstepSoundEmitter.PlayOneShot();
        CmdBroadcastFootstep();
    }

    // ── Öffentliche API ──────────────────────────────────────────────────────

    public void FreezeMovement()
    {
        controller.enabled = false;
        velocity = Vector3.zero;
        horizontalVelocity = Vector3.zero;
        currentSprintSpeed = config != null ? config.baseSprintSpeed : 0f;
        jumpButtonHeld = false;
        if (playerCamera != null && config != null)
            playerCamera.fieldOfView = config.normalFOV;
    }

    public void UnfreezeMovement() => controller.enabled = true;

    // ── Mirror – Footstep Sync ────────────────────────────────────────────────

    [Command]
    private void CmdBroadcastFootstep() => RpcPlayFootstepOnOthers();

    [ClientRpc(includeOwner = false)]
    private void RpcPlayFootstepOnOthers()
    {
        if (footstepSoundEmitter != null) footstepSoundEmitter.PlayOneShot();
    }

    // ── Debug ─────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    [Button("Debug: Jump State")]
    private void DebugJumpState()
    {
        Debug.Log($"[PlayerMovement] config={config != null}, isOwned={isOwned}, isGrounded={isGrounded}");
        Debug.Log($"[PlayerMovement] groundMask={groundMask.value} (0 = Nothing → Sprung unmöglich!)");
        Debug.Log($"[PlayerMovement] groundCheck pos={groundCheck?.position}, groundDistance={groundDistance}");
        Debug.Log($"[PlayerMovement] velocity.y={velocity.y}, jumpButtonHeld={jumpButtonHeld}");

        if (groundMask.value == 0)
            Debug.LogError("[PlayerMovement] groundMask ist 'Nothing' – isGrounded wird nie true! Layer im Inspector setzen.");
        if (groundCheck == null)
            Debug.LogError("[PlayerMovement] groundCheck ist nicht zugewiesen!");
        if (config == null)
            Debug.LogError("[PlayerMovement] config ist null – ApplyConfig() wurde noch nicht aufgerufen.");
    }
#endif
}
