using Mirror;
using UnityEngine.Events;

public class Interactable : NetworkBehaviour
{
    public UnityEvent onInteracted;

    [Server]
    public void Interact()
    {
        RpcTriggerInteraction();
    }

    [ClientRpc]
    private void RpcTriggerInteraction()
    {
        onInteracted?.Invoke();
    }
}
