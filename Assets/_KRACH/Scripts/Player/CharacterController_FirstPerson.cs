using Mirror;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(CharacterController))]
public class CharacterController_FirstPerson : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float walkSpeed = 6f;

    private Vector3 velocity;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private float groundDistance = 0.4f;
    private bool isGrounded = true;

    [Header("Jump Settings")]
    [SerializeField] private float jumpHeight = 2f;
    // How long you can hold the button for higher jump
    [SerializeField] private float jumpHoldTime = 0.2f;
    [SerializeField] private float jumpHoldGravityMultiplier = 0.5f;

    private bool isJumping = false;
    private float jumpHoldTimer = 0f;
    private bool jumpHeldLastFrame = false;  // tracks held state across frames for variable jump

    [Header("Fall Settings")]
    [SerializeField] private float baseFallGravity = 10f;
    [SerializeField] private float maxFallGravity = 30f;
    [SerializeField] private float fallGravityScaling = 2f;

    // Fall tracking
    private float currentFallGravity;
    private float airTime;

    [Header("Sprint Settings")]
    [SerializeField] private bool canSprint;
    [SerializeField] private float baseSprintSpeed = 9f;
    [SerializeField] private float maxSprintSpeed = 15f;
    [SerializeField] private float sprintBurstThreshold = 2.5f;     // time in seconds before burst kicks in
    [SerializeField] private float sprintAcceleration = 1.7f;       // acceleration before sprintBurstThreshold
    [SerializeField] private float sprintBurstAcceleration = 14f;   // acceleration after sprintBurstThreshold
    [SerializeField] private float sprintDecaySpeed = 3f;           // decay speed when not sprinting per Second

    // Internal sprint state
    private bool sprinting = false;
    private float currentSprintSpeed;
    private float sprintTimer = 0f;

    [Header("Camera FOV Settings")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float normalFOV = 60f;
    [SerializeField] private float maxSprintFOV = 70f;
    [SerializeField] private float fovChangeSpeed = 8f;

    [Header("Audio")]
    [SerializeField] private float baseStepInterval = 0.3f;
    [SerializeField] private float footstepTimer = 0f;
    [Tooltip("LuaSoundEmitter am Fußpunkt, Script-Mode. Enable Multiplayer = false — die Verteilung (lokal + RpcPlayFootstepOnOthers) macht dieses Script.")]
    [SerializeField] private LuaSoundEmitter footstepSoundEmitter;

    [Header("References")]
    [Scene][SerializeField] private string gameplayScene;
    [SerializeField] private CharacterController controller;
    [SerializeField] private GameObject playerModel;

    void Start()
    {
        currentSprintSpeed = baseSprintSpeed;
        currentFallGravity = baseFallGravity;

        if (playerCamera != null)
        {
            playerCamera.fieldOfView = normalFOV;
        }

        playerModel.SetActive(false);
    }

    void Update()
    {
        if (SceneManager.GetActiveScene().path == gameplayScene)
        {
            if (playerModel.activeSelf == false)
            {
                playerModel.SetActive(true);
                SpawnPlayerAtPosition(PlayerRole.Vandalist);
            }

            if (isOwned)
            {
                HandleSprintInput();
                HandleMovement();
                HandleJump();
                HandleFOV();
            }
        }
    }

    [Button]
    public bool LevelManagerIsInLevel()
    {
        if (LevelManager.Instance == null)
        {
            Debug.LogError("LevelManager not found in scene!");
            return false;
        }
        else
        {
            Debug.Log("LevelManager found in scene!");
            return true;
        }
    }

    public void SpawnPlayerAtPosition(PlayerRole role)
    {
        Debug.Log("Spawning player");
        if (LevelManager.Instance == null)
        {
            Debug.LogError("LevelManager not found! Cannot spawn player.");
            return;
        }

        // currently only spawns vandalists
        Transform[] spawnPositions = LevelManager.Instance.vandalistSpawnPositions;
        Vector3 position = spawnPositions[Random.Range(0, spawnPositions.Length)].position;


        controller.enabled = false;

        transform.position = position;

        controller.enabled = true;

        velocity = Vector3.zero;
    }

    public void SetCursor(bool active)
    {
        Cursor.visible = active;
    }

    private void HandleSprintInput()
    {
        if (InputManager.Instance == null) return;
        sprinting = InputManager.Instance.CurrentInput.SprintHeld;
    }

    void HandleMovement()
    {
        // Ground check
        isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);

        if (isGrounded)
        {
            if (velocity.y < 0)
                velocity.y = -2f;

            currentFallGravity = baseFallGravity;
            airTime = 0f;
        }
        else
        {
            // Apply fall acceleration
            airTime += Time.deltaTime;
            currentFallGravity += fallGravityScaling * Time.deltaTime * currentFallGravity;
            currentFallGravity = Mathf.Clamp(currentFallGravity, baseFallGravity, maxFallGravity);
            velocity.y -= currentFallGravity * Time.deltaTime;
        }

        // Get input
        Vector2 moveInput = InputManager.Instance != null ? InputManager.Instance.CurrentInput.MoveInput : Vector2.zero;
        float x = moveInput.x;
        float z = moveInput.y;

        bool isMoving = x != 0 || z != 0;

        if (sprinting && isMoving && canSprint)
        {
            sprintTimer += Time.deltaTime;

            if (sprintTimer < sprintBurstThreshold)
            {
                currentSprintSpeed += sprintAcceleration * Time.deltaTime;
            }
            else
            {
                currentSprintSpeed += sprintBurstAcceleration * Time.deltaTime;
            }

            currentSprintSpeed = Mathf.Min(currentSprintSpeed, maxSprintSpeed);
        }
        else
        {
            sprintTimer = 0f;
            currentSprintSpeed -= sprintDecaySpeed * Time.deltaTime;
            currentSprintSpeed = Mathf.Max(currentSprintSpeed, baseSprintSpeed);
        }

        // FOOTSTEP SOUND: trigger FMOD event in intervals which scale with movement speed.
        if (isGrounded && isMoving)
        {
            // choose effective movement speed (sprint or walk)
            float effectiveSpeed = (sprinting && isMoving && canSprint) ? currentSprintSpeed : walkSpeed;

            // Calculate sprint multiplier dynamically: at max sprint speed, footsteps are 6x faster than walk
            float speedMultiplier = sprinting ? 4f * walkSpeed / maxSprintSpeed : 1f;

            // Interval scales inversely with speed: faster movement => smaller interval
            float interval = baseStepInterval * (walkSpeed / Mathf.Max(0.0001f, effectiveSpeed * speedMultiplier));

            footstepTimer += Time.deltaTime;
            if (footstepTimer >= interval)
            {
                if (footstepSoundEmitter != null) footstepSoundEmitter.PlayOneShot(); // local sound
                CmdBroadcastFootstep(); // to all others
                footstepTimer = 0f;
            }
        }
        else
        {
            // reset timer when not moving or not grounded so footsteps start immediately when moving again
            footstepTimer = 0f;
        }

        float speed = sprinting && isMoving ? currentSprintSpeed : walkSpeed;

        Vector3 move = transform.right * x + transform.forward * z;
        controller.Move(move * speed * Time.deltaTime);

        controller.Move(velocity * Time.deltaTime);
    }


    public void SetCanSprint(bool canSprint)
    {
        // set the backing field so sprint availability is updated
        this.canSprint = canSprint;
        if (!canSprint)
        {
            sprinting = false;
            currentSprintSpeed = baseSprintSpeed;
            sprintTimer = 0f;
        }
    }

    private void HandleJump()
    {
        if (InputManager.Instance == null) return;

        InputData input = InputManager.Instance.CurrentInput;

        // Start jump
        if (input.JumpPressed && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * 2f * baseFallGravity);
            isJumping = true;
            jumpHoldTimer = 0f;
            jumpHeldLastFrame = true;
            currentFallGravity = baseFallGravity;
            airTime = 0f;
        }

        // Variable height logic (hold to go higher)
        // JumpPressed is one-shot, so we track held state via jumpHeldLastFrame
        if (jumpHeldLastFrame && isJumping)
        {
            if (jumpHoldTimer < jumpHoldTime)
            {
                // Reduce gravity to prolong upward motion
                velocity.y += baseFallGravity * jumpHoldGravityMultiplier * Time.deltaTime;
                jumpHoldTimer += Time.deltaTime;
            }
        }

        // Detect jump release: was held last frame, button no longer pressed this frame
        bool jumpHeldThisFrame = !input.JumpPressed && jumpHeldLastFrame;
        if (jumpHeldThisFrame)
        {
            isJumping = false;
            jumpHeldLastFrame = false;

            // CUT the upward velocity for small hop
            if (velocity.y > 0)
            {
                velocity.y *= 0.3f; // You can adjust this — lower = sharper cut
            }
        }

        // Cancel jump if falling
        if (velocity.y <= 0)
            if (velocity.y <= 0)
            {
                isJumping = false;
            }
    }


    void HandleFOV()
    {
        if (playerCamera == null) return;

        float targetFOV = normalFOV;

        if (sprinting && sprintTimer >= sprintBurstThreshold)
        {
            targetFOV = maxSprintFOV;
        }

        playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFOV, fovChangeSpeed * Time.deltaTime);
    }

    public void FreezeMovement()
    {
        controller.enabled = false;
        velocity = Vector3.zero;
        isGrounded = true;
        currentSprintSpeed = baseSprintSpeed;
        currentFallGravity = baseFallGravity;
        airTime = 0f;

        if (playerCamera != null)
        {
            playerCamera.fieldOfView = normalFOV;
        }

    }

    public void UnfreezeMovement()
    {
        controller.enabled = true;
    }

    #region SOUND
    [Command]
    private void CmdBroadcastFootstep()
    {
        RpcPlayFootstepOnOthers();
    }

    [ClientRpc(includeOwner = false)]
    private void RpcPlayFootstepOnOthers()
    {
        if (footstepSoundEmitter != null) footstepSoundEmitter.PlayOneShot();
    }
    #endregion
}