using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class TestDummySpawner : NetworkBehaviour
{
    [System.Serializable]
    public struct DummySpawnGroup
    {
        public PlayerRole role;
        public int count;
    }

    [Header("Dummy Settings")]
    [Tooltip("Prefab mit NetworkIdentity + PlayerObjectController + PlayerRoleSetup + CapsuleCollider." +
             " PlayerRoleSetup wird für Modell-Sichtbarkeit UND Catch/Respawn-Verhalten benötigt.")]
    [SerializeField] private GameObject dummyPrefab;

    [Tooltip("Welche Rollen mit wie vielen Dummies gespawnt werden. So lassen sich z.B. gleichzeitig " +
             "Vandalist- und Hunter-Dummies für unterschiedliche Testszenarien erzeugen.")]
    [SerializeField]
    private DummySpawnGroup[] spawnGroups = new DummySpawnGroup[]
    {
        new DummySpawnGroup { role = PlayerRole.Vandalist, count = 2 }
    };

    [Tooltip("Offset damit Dummies nicht exakt auf dem Spawnpunkt stehen.")]
    [SerializeField] private float spawnSpread = 1.5f;

    [Tooltip("Dummies per Raycast auf dem Boden absetzen. Spawnpunkte sind für Spieler gesetzt," +
             " die nach dem Spawn herunterfallen – ein Dummy ohne Physik bliebe sonst in der Luft" +
             " stehen und wäre für einen Hunter am Boden nicht erreichbar.")]
    [SerializeField] private bool dropToGround = true;
    [Tooltip("Layer die als Boden gelten. Sollte der groundMask von PlayerMovement entsprechen.")]
    [SerializeField] private LayerMask groundMask = 8;

    private readonly List<GameObject> spawnedDummies = new List<GameObject>();

    // ── Server-Start ──────────────────────────────────────────────────────────

    public override void OnStartServer()
    {
        CustomNetworkManager mgr = NetworkManager.singleton as CustomNetworkManager;

        if (mgr == null || !mgr.IsTestMode)
        {
            Debug.Log("[TestDummySpawner] Test-Modus nicht aktiv – Dummy-Spawn übersprungen.");
            return;
        }

        if (dummyPrefab == null)
        {
            Debug.LogError("[TestDummySpawner] dummyPrefab ist nicht zugewiesen!");
            return;
        }

        // Kurz warten bis LevelManager sicher geladen ist
        StartCoroutine(SpawnAfterDelay());
    }

    public override void OnStopServer()
    {
        // Verhindert Ghost-Dummies bei mehrfachem Play-Test im Editor (v.a. bei deaktiviertem Domain Reload).
        foreach (GameObject dummy in spawnedDummies)
        {
            if (dummy != null)
                NetworkServer.Destroy(dummy);
        }

        spawnedDummies.Clear();
    }

    private System.Collections.IEnumerator SpawnAfterDelay()
    {
        // Einen Frame warten damit Awake/Start im LevelManager abgeschlossen sind
        yield return null;

        if (LevelManager.Instance == null)
        {
            Debug.LogError("[TestDummySpawner] LevelManager nicht gefunden – Dummies konnten nicht gespawnt werden.");
            yield break;
        }

        foreach (DummySpawnGroup group in spawnGroups)
        {
            SpawnDummies(group.role, group.count);
        }
    }

    // ── Spawn-Logik ───────────────────────────────────────────────────────────

    private void SpawnDummies(PlayerRole role, int count)
    {
        Transform[] spawnPoints = role == PlayerRole.Hunter
            ? LevelManager.Instance.hunterSpawnPositions
            : LevelManager.Instance.vandalistSpawnPositions;

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning($"[TestDummySpawner] Keine Spawn-Positionen für Rolle {role} im LevelManager konfiguriert!");
            return;
        }

        for (int i = 0; i < count; i++)
        {
            // Spawnpunkt zyklisch nutzen, leichten Offset damit Dummies nicht übereinander stehen
            Transform spawnPoint = spawnPoints[i % spawnPoints.Length];
            Vector3 offset = new Vector3(
                Random.Range(-spawnSpread, spawnSpread),
                0f,
                Random.Range(-spawnSpread, spawnSpread)
            );
            Vector3 spawnPos = spawnPoint.position + offset;

            if (dropToGround && dummyPrefab.TryGetComponent(out CapsuleCollider body))
                spawnPos = SpawnPlacement.DropToGround(spawnPos, body, groundMask);

            GameObject dummy = Instantiate(dummyPrefab, spawnPos, spawnPoint.rotation);

            // Rolle direkt auf dem Server setzen bevor Spawn → wird mit Spawn-Nachricht übertragen
            PlayerObjectController poc = dummy.GetComponent<PlayerObjectController>();
            if (poc != null)
            {
                poc.playerRole = role;
            }
            else
            {
                Debug.LogWarning("[TestDummySpawner] Dummy-Prefab hat keinen PlayerObjectController!");
            }

            if (dummy.GetComponent<PlayerRoleSetup>() == null)
            {
                Debug.LogWarning("[TestDummySpawner] Dummy-Prefab hat kein PlayerRoleSetup – " +
                                  "Modell bleibt unsichtbar und Catch/Respawn funktioniert nicht für diesen Dummy!");
            }

            NetworkServer.Spawn(dummy);
            spawnedDummies.Add(dummy);

            Debug.Log($"[TestDummySpawner] {role}-Dummy #{i + 1} gespawnt bei {spawnPos}");
        }
    }
}