using Mirror;
using UnityEngine;

public class Door : MonoBehaviour
{
    private Door closestDoor;
    private Collider doorCollider;
    private Wall linkedWall;
    void Start()
    {

    }

    private void OnTriggerEnter(Collider other)
    {
        NetworkIdentity networkIdentity = other.GetComponent<NetworkIdentity>();
        if (networkIdentity == null) return;

        // Nur der lokale Spieler (Besitzer des Objekts) darf den Teleport ausf�hren
        if (!networkIdentity.isLocalPlayer) return;

        PlayerMovement character = other.GetComponent<PlayerMovement>();
        if (character != null)
        {
            Debug.Log("Local player entered the door trigger: " + other.name);
            MovePlayerToOtherDoor(character);
        }
    }

    private void Awake()
    {
        closestDoor = FindClosestDoor();
        doorCollider = GetComponentInChildren<Collider>();
        linkedWall = FindClosestWall();
    }

    public void MovePlayerToOtherDoor(PlayerMovement character)
    {
        if (closestDoor == null)
        {
            Debug.LogWarning("No closest door found.");
            return;
        }

        // TeleportTo kümmert sich um das Deaktivieren des Controllers und setzt die
        // Restgeschwindigkeit zurück – sonst nimmt man seinen Fall-Impuls mit durch die Tür.
        character.TeleportTo(closestDoor.transform.position + character.transform.forward * 2f);

        Debug.Log("Player moved to the closest door: " + closestDoor.name);
    }

    public Door FindClosestDoor()
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

    public Wall FindClosestWall()
    {
        Wall[] allWalls = FindObjectsByType<Wall>(FindObjectsSortMode.InstanceID);
        Wall closest = null;
        float closestDistance = float.MaxValue;

        foreach (Wall wall in allWalls)
        {
            float dist = Vector3.Distance(wall.transform.position, transform.position);
            if (dist < closestDistance)
            {
                closestDistance = dist;
                closest = wall;
            }
        }

        return closest;
    }
}
