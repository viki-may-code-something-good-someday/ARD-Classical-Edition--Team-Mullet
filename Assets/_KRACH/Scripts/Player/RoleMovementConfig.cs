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
    [Tooltip("Zielhöhe des Sprungs in Metern (bei vollem Aufstieg, riseGravityMultiplier berücksichtigt).")]
    public float jumpHeight = 1.6f;
    [Tooltip("Gravity-Multiplikator während des Aufstiegs. 1 = normale/realistische Gravity." +
             " Niedriger = etwas mehr Hangtime, höher = noch direkterer, kürzerer Aufstieg.")]
    public float riseGravityMultiplier = 1f;
    [Tooltip("Gravity-Multiplikator während des Falls. Deutlich höher als riseGravityMultiplier" +
             " sorgt für ein knackiges, direktes Lande-Gefühl statt einem floatigen Fall.")]
    public float fallGravityMultiplier = 2.2f;
    [Tooltip("Gravity-Multiplikator direkt nach frühem Loslassen der Jump-Taste während des Aufstiegs." +
             " Sollte >= fallGravityMultiplier sein, damit kurze Hops besonders knackig abgebrochen werden.")]
    public float jumpCutGravityMultiplier = 3.2f;
    [Tooltip("Wie viel der verbleibenden Aufwärtsgeschwindigkeit beim frühen Loslassen sofort" +
             " gekappt wird. 1 = kein Cut, 0.4 = 60% der Restgeschwindigkeit sofort abgeschnitten.")]
    [Range(0f, 1f)]
    public float jumpCutVelocityMultiplier = 0.45f;

    [Header("Crouch")]
    public float crouchSpeedMultiplier = 0.5f;
    [Tooltip("CharacterController-Höhe im Hockzustand.")]
    public float crouchHeight = 1.0f;
    [Tooltip("CharacterController-Höhe im Stehzustand.")]
    public float standHeight = 2.0f;
    [Tooltip("Lokale Y-Position des CameraHolders im Hockzustand – relativ zum Root-Pivot, der" +
             " nicht an den Füßen sitzt. Muss klein genug sein, dass die Kamera in der Hockkapsel" +
             " bleibt, sonst schaut man unter niedrigen Decken hindurch. Grenze:" +
             " crouchCameraY - groundCheck.localPosition.y < crouchHeight." +
             " PlayerMovement.ApplyConfig warnt wenn der Wert nicht passt.")]
    public float crouchCameraY = -0.04f;
    [Tooltip("Lokale Y-Position des CameraHolders im Stehzustand.")]
    public float standCameraY = 0.75f;
    public float crouchTransitionSpeed = 12f;

    [Header("Fall")]
    [Tooltip("Basis-Gravity in m/s². Wird mit riseGravityMultiplier / fallGravityMultiplier /" +
             " jumpCutGravityMultiplier kombiniert – siehe Header 'Jump'.")]
    public float baseFallGravity = 20f;
    [Tooltip("Maximale Fallgeschwindigkeit (Terminal Velocity) in m/s. Verhindert unrealistisch" +
             " schnelles Fallen bei langen Stürzen. ACHTUNG: Ersetzt das alte Feld 'maxFallGravity' –" +
             " bei bereits existierenden Assets bitte den Wert einmal im Inspector neu prüfen/setzen.")]
    public float maxFallSpeed = 25f;

    [Header("Camera FOV")]
    public float normalFOV = 60f;
    public float sprintFOV = 70f;
    public float fovChangeSpeed = 8f;
}