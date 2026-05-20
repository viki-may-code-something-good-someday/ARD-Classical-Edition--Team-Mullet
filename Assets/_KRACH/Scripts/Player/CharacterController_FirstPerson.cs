using FMODUnity;
using Mirror;
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
    public bool ToggleToSprint = false;           // false = hold to sprint, true = press to sprint
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
    public LevelManager levelManager;

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
        if (SceneManager.GetActiveScene().name == "Multiplayer_Test")
        {
            Debug.Log("Gets update");
            if (playerModel.activeSelf == false)
            {
                playerModel.SetActive(true);
                SpawnPlayerAtPosition(PlayerRole.Vandalist);
            }

            if (isOwned)
            {
                Debug.Log("Gets owned");
                HandleSprintInput();
                HandleMovement();
                HandleJump();
                HandleFOV();
            }
        }
    }

    public bool IsInLevel()
    {
        if (levelManager == null)
        {
            if (GameObject.Find("GameManager").GetComponent<LevelManager>() != null)
            {
                levelManager = GameObject.Find("GameManager").GetComponent<LevelManager>();
                return true;
            }
            else
            {
                return false;
            }
        }
        else
        {
            return true;
        }

    }

    public void SpawnPlayerAtPosition(PlayerRole role)
    {
        Debug.Log("Spawning player");
        LevelManager levelManager = GameObject.Find("GameManager").GetComponent<LevelManager>();

        Vector3 position = levelManager.vandalistSpawnPositions[Random.Range(0, 3)].position;

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
        if (ToggleToSprint)
        {
            if (Input.GetKeyDown(KeyCode.LeftShift))
            {
                sprinting = !sprinting;
            }
        }
        else
        {
            sprinting = Input.GetKey(KeyCode.LeftShift);
        }
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
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

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
        // Start jump
        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * 2f * baseFallGravity);
            isJumping = true;
            jumpHoldTimer = 0f;
            currentFallGravity = baseFallGravity;
            airTime = 0f;
        }

        // Variable height logic (hold to go higher)
        if (Input.GetButton("Jump") && isJumping)
        {
            if (jumpHoldTimer < jumpHoldTime)
            {
                // Reduce gravity to prolong upward motion
                velocity.y += baseFallGravity * jumpHoldGravityMultiplier * Time.deltaTime;
                jumpHoldTimer += Time.deltaTime;
            }
        }

        // If jump is released or timer runs out, stop extending jump
        if (Input.GetButtonUp("Jump"))
        {
            isJumping = false;

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