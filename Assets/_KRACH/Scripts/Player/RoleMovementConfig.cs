using UnityEngine;

/// <summary>
/// Alle bewegungsrelevanten Werte einer Rolle als ScriptableObject.
/// Erstelle pro Rolle eine eigene Asset-Instanz (Rechtsklick → Game → Role Movement Config).
/// So können Werte im Editor angepasst werden ohne Code zu ändern.
/// </summary>
[CreateAssetMenu(fileName = "New RoleMovementConfig", menuName = "Game/Role Movement Config")]
public class RoleMovementConfig : ScriptableObject
{
    [Header("Walk")]
    public float walkSpeed = 6f;

    [Header("Sprint")]
    public bool canSprint = true;
    public float baseSprintSpeed = 9f;
    public float maxSprintSpeed = 15f;
    public float sprintBurstThreshold = 2.5f;
    public float sprintAcceleration = 1.7f;
    public float sprintBurstAcceleration = 14f;
    public float sprintDecaySpeed = 3f;

    [Header("Jump")]
    public float jumpHeight = 2f;
    public float jumpHoldTime = 0.2f;
    public float jumpHoldGravityMultiplier = 0.5f;

    [Header("Crouch")]
    public float crouchSpeedMultiplier = 0.5f;
    [Tooltip("CharacterController-Höhe im Hockzustand.")]
    public float crouchHeight = 1.0f;
    [Tooltip("CharacterController-Höhe im Stehzustand.")]
    public float standHeight = 2.0f;
    [Tooltip("Lokale Y-Position des CameraHolders im Hockzustand.")]
    public float crouchCameraY = 0.3f;
    [Tooltip("Lokale Y-Position des CameraHolders im Stehzustand.")]
    public float standCameraY = 0.75f;
    public float crouchTransitionSpeed = 12f;

    [Header("Fall")]
    public float baseFallGravity = 10f;
    public float maxFallGravity = 30f;
    public float fallGravityScaling = 2f;

    [Header("Camera FOV")]
    public float normalFOV = 60f;
    public float sprintFOV = 70f;
    public float fovChangeSpeed = 8f;
}