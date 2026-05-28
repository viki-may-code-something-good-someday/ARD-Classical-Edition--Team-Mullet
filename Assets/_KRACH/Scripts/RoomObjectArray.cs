using UnityEngine;

public enum RoomTypes
{
    OfficeRoom,
    StudioRoom,
    Floor,
    Other
}

public class RoomObjectArray
{
    public RoomObj roomObject;
    public RoomTypes roomType;
    [SerializeField] public Wall[] destructableWallsNextToRoom;
}
