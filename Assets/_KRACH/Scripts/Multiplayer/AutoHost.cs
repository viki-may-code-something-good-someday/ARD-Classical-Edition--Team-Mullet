using Mirror;
using UnityEngine;

public class AutoHost : MonoBehaviour
{
    void Start()
    {
        // Prüft, ob Mirror nicht bereits läuft (verhindert Fehler bei Szenenwechseln)
        if (!NetworkServer.active && !NetworkClient.active)
        {
            Debug.Log("Starte lokalen Host für Testing...");
            NetworkManager.singleton.StartHost();
        }
    }
}
