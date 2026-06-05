using FMODUnity;
using Mirror;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(CharacterController))]
public class CharacterController_FirstPerson : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float walkSpeed = 6f;

    [Header("Audio")]
    private string footstepEvent = "event:/SFX/Footstep";
    private float baseStepInterval = 0.4f;
    private float footstepTimer = 0f;

    [Header("Sprint Settings")]
    public bool canSprint;
    public float baseSprintSpeed = 9f;            // initial sprint speed
    public float maxSprintSpeed = 15f;            // maximum sprint speed
    public float sprintAcceleration = 1.7f;       // acceleration before sprintBurstThreshold
    public float sprintBurstAcceleration = 14f;   // acceleration after sprintBurstThreshold
    public float sprintDecaySpeed = 3f;           // decay speed when not sprinting
    public float sprintBurstThreshold = 2.5f;     // time in seconds before burst kicks in

    [Header("Camera FOV Settings")]
    public Camera playerCamera;                   // assign your main camera here
    public float normalFOV = 60f;
    public float maxSprintFOV = 70f;
    public float fovChangeSpeed = 8f;

    [Header("Jump Settings")]
    public float jumpHeight = 2f;
    public float jumpHoldTime = 0.2f;        // How long you can hold the button for higher jump
    public float jumpHoldGravityMultiplier = 0.5f;

    private bool isJumping = false;
    private float jumpHoldTimer = 0f;
    private bool jumpHeldLastFrame = false;  // tracks held state across frames for variable jump


    [Header("Fall Settings")]
    public float baseFallGravity = 10f;
    public float maxFallGravity = 30f;
    public float fallGravityScaling = 2f;

    [Header("References")]
    public Transform groundCheck;
    public LayerMask groundMask;
    public float groundDistance = 0.4f; //make sure this works hukvgjhtfnhjftgfjz

    private CharacterController controller;
    public Vector3 velocity;
    private bool isGrounded = true;

    // Internal sprint state
    private bool sprinting = false;
    private float currentSprintSpeed;
    private float sprintTimer = 0f;

    // Fall tracking
    private float currentFallGravity;
    private float airTime;

    //Multiplayer
    public GameObject playerModel;

    void Start()
    {
        controller = GetComponent<CharacterController>();
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
        if (SceneManager.GetActiveScene().name == "Viktor_TestScene")
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

        // FOOTSTEP SOUND: trigger FMOD event in intervals which scale with movement speed.
        if (isGrounded && isMoving)
        {
            // choose effective movement speed (sprint or walk)
            float effectiveSpeed = (sprinting && isMoving && canSprint) ? currentSprintSpeed : walkSpeed;

            // Interval scales inversely with speed: faster movement => smaller interval
            float interval = baseStepInterval * (walkSpeed / Mathf.Max(0.0001f, effectiveSpeed));

            footstepTimer += Time.deltaTime;
            if (footstepTimer >= interval)
            {
                // Play one-shot footstep attached to the player's GameObject so it follows the player
                RuntimeManager.PlayOneShotAttached(footstepEvent, gameObject);
                footstepTimer = 0f;
            }
        }
        else
        {
            // reset timer when not moving or not grounded so footsteps start immediately when moving again
            footstepTimer = 0f;
        }

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
}