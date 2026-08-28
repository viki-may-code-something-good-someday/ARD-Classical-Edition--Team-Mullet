using Mirror;
using UnityEngine;

public class Door : MonoBehaviour
{
    [Header("Door Pairing")]
    [Tooltip("Assigned automatically by Wall.GeneratePairedDoors(). Only set manually for standalone doors.")]
    [SerializeField] private Door pairedDoor;

    [Header("Teleport Settings")]
    [SerializeField] private float teleportCooldown = 1f;
    [SerializeField] private float exitOffsetDistance = 2f;

    [Header("Placement")]
    [Tooltip("Vertical offset applied when this door is auto-placed by Wall.GeneratePairedDoors(). " +
             "Useful if the door prefab's pivot doesn't sit at floor height.")]
    [SerializeField] private float yOffset = -0.4f;
    public float YOffset => yOffset;

    private Wall parentWall;
    private float lastTeleportTime = -999f;

    private void Awake()
    {
        // Doors are spawned as children of their Wall by Wall.GeneratePairedDoors(),
        // so GetComponentInParent is reliable and needs zero manual setup.
        parentWall = GetComponentInParent<Wall>();

        // Fallback only, for doors that were placed manually and not via the generator.
        if (pairedDoor == null)
        {
            Debug.LogWarning($"[Door] No pairedDoor assigned on {name} — falling back to distance search. " +
                              "This is not reliable if multiple walls are close together.");
            pairedDoor = FindClosestDoor();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        NetworkIdentity networkIdentity = other.GetComponent<NetworkIdentity>();
        if (networkIdentity == null) return;

        // Only the local player (owner of the object) executes the teleport
        if (!networkIdentity.isLocalPlayer) return;

        // Prevent immediate re-trigger between paired doors (player standing in the exit trigger)
        if (Time.time - lastTeleportTime < teleportCooldown) return;

        PlayerMovement character = other.GetComponent<PlayerMovement>();
        if (character == null) return;

        // Only Hunters may use these doors.
        // TODO: confirm PlayerObjectController.playerRole is the correct public accessor.
        PlayerObjectController poc = other.GetComponent<PlayerObjectController>();
        if (poc == null || poc.playerRole != PlayerRole.Hunter)
        {
            Debug.Log($"{other.name} entered the door trigger but is not a Hunter — ignoring.");
            return;
        }

        Debug.Log("Hunter entered the door trigger: " + other.name);
        MovePlayerToOtherDoor(character);
    }

    public void MovePlayerToOtherDoor(PlayerMovement character)
    {
        if (pairedDoor == null)
        {
            Debug.LogWarning("No paired door found for " + name);
            return;
        }

        lastTeleportTime = Time.time;
        pairedDoor.lastTeleportTime = Time.time; // also arm the cooldown on the exit side

        // Use the TARGET door's forward direction, not the player's, so the exit
        // position/orientation is always correct regardless of the approach angle.
        Vector3 exitPosition = pairedDoor.transform.position + pairedDoor.transform.forward * exitOffsetDistance;

        character.TeleportTo(exitPosition);

        Debug.Log("Player moved to paired door: " + pairedDoor.name);
    }

    /// <summary>Called by the parent Wall when it is destroyed — a broken wall has a hole, not a doorway.</summary>
    public void DeactivateDoor()
    {
        gameObject.SetActive(false);
    }

    // Fallback only — do not rely on this once doors are generated via Wall.GeneratePairedDoors().
    private Door FindClosestDoor()
    {
        Door[] allDoors = FindObjectsByType<Door>(FindObjectsSortMode.InstanceID);
        Door closest = null;
        float closestDistance = float.MaxValue;

        foreach (Door door in allDoors)
        {
            if (door == this) continue;

            float dist = Vector3.Distance(door.transform.position, transform.position);
            if (dist < closestDistance)
            {
                closestDistance = dist;
                closest = door;
            }
        }

        return closest;
    }
}