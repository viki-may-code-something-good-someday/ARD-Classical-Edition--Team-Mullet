using UnityEngine;

public class Door : MonoBehaviour
{
    private Door closestDoor;
    private Collider doorCollider;
    void Start()
    {

    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("Player entered the door trigger: " + other.name);
    }

    private void Awake()
    {
        closestDoor = FindClosestDoor();
        doorCollider = GetComponentInChildren<Collider>();
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
}
