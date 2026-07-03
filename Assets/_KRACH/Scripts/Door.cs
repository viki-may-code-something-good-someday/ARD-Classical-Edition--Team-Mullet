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

        // Nur der lokale Spieler (Besitzer des Objekts) darf den Teleport ausführen
        if (!networkIdentity.isLocalPlayer) return;

        CharacterController_FirstPerson character = other.GetComponent<CharacterController_FirstPerson>();
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

    public void MovePlayerToOtherDoor(CharacterController_FirstPerson character)
    {
        if (closestDoor != null)
        {
            Vector3 newPosition = closestDoor.transform.position + closestDoor.transform.forward * 2f;

            // 3. WICHTIG: Character Controller vor dem Teleportieren deaktivieren
            CharacterController cc = character.GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false;
            }

            // Position aktualisieren
            character.transform.position = newPosition;

            // Character Controller wieder aktivieren
            if (cc != null)
            {
                cc.enabled = true;
            }

            Debug.Log("Player moved to the closest door: " + closestDoor.name);
        }
        else
        {
            Debug.LogWarning("No closest door found.");
        }
    }

    public Door FindClosestDoor()
    {
        Door[] allDors = GameObject.FindObjectsByType<Door>(FindObjectsSortMode.InstanceID);
        Door d = null;
        foreach (Door door in allDors)
        {
            float dist = Vector3.Distance(door.transform.position, transform.position);
            if (closestDoor == null || dist < Vector3.Distance(closestDoor.transform.position, transform.position))
            {
                d = door;
            }
        }
        return d;
    }

    public Wall FindClosestWall()
    {
        Wall[] allWalls = GameObject.FindObjectsByType<Wall>(FindObjectsSortMode.InstanceID);
        Wall closestWall = null;
        foreach (Wall wall in allWalls)
        {
            float dist = Vector3.Distance(wall.transform.position, transform.position);
            if (closestWall == null || dist < Vector3.Distance(closestWall.transform.position, transform.position))
            {
                closestWall = wall;
            }
        }
        return closestWall;
    }
}
