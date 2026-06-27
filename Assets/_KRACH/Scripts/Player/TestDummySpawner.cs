using Mirror;
using UnityEngine;

/// <summary>
/// Spawnt im Test-Modus stationäre Dummy-Vandalists die sich nicht bewegen,
/// aber geschlagen (PlayerInteract) und angeklagt (HunterAccuse) werden können.
///
/// SETUP:
/// 1. Dieses Script auf ein leeres GameObject in der Gameplay-Scene legen.
/// 2. Ein Dummy-Prefab erstellen (siehe unten) und in dummyPrefab ziehen.
/// 3. TestDummySpawner läuft nur wenn LobbyController.IsTestMode = true.
///
/// DUMMY-PREFAB SETUP:
///   ┌─ DummyPlayer (leeres GameObject)
///   │    ├─ NetworkIdentity           (Mirror – für Netzwerk-Sync)
///   │    ├─ PlayerObjectController    (playerRole wird in Awake auf Vandalist gesetzt)
///   │    ├─ CapsuleCollider           (Layer: "Player" → für HunterAccuse-Raycast)
///   │    └─ MeshRenderer + MeshFilter (Capsule-Mesh zur Sichtbarkeit)
///   │
///   Danach: NetworkManager → Registered Spawnable Prefabs → Dummy-Prefab eintragen.
///
/// LAYER-HINWEIS:
///   HunterAccuse sucht auf dem in playerLayer eingestellten Layer (z.B. "Player").
///   PlayerInteract sucht auf "Interactable", "Destructable", "Default".
///   → Dummy auf "Player" setzen UND im HunterAccuse-Inspector playerLayer = "Player".
///   → Für Punch-Tests: Kind-Objekt mit zweitem Collider auf "Default" hinzufügen.
/// </summary>
public class TestDummySpawner : NetworkBehaviour
{
    [Header("Dummy Settings")]
    [Tooltip("Prefab mit NetworkIdentity + PlayerObjectController + CapsuleCollider.")]
    [SerializeField] private GameObject dummyPrefab;

    [Tooltip("Wie viele Dummies gespawnt werden.")]
    [SerializeField] private int dummyCount = 2;

    [Tooltip("Rolle die den Dummies zugewiesen wird.")]
    [SerializeField] private PlayerRole dummyRole = PlayerRole.Vandalist;

    [Tooltip("Offset damit Dummies nicht exakt auf dem Spawnpunkt stehen.")]
    [SerializeField] private float spawnSpread = 1.5f;

    // ── Server-Start ──────────────────────────────────────────────────────────

    public override void OnStartServer()
    {
        // Nur spawnen wenn Test-Modus aktiv
        if (LobbyController.instance == null) // || !LobbyController.instance.IsTestMode
            return;

        if (dummyPrefab == null)
        {
            Debug.LogError("[TestDummySpawner] dummyPrefab ist nicht zugewiesen!");
            return;
        }

        // Kurz warten bis LevelManager sicher geladen ist
        StartCoroutine(SpawnAfterDelay());
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

        SpawnDummies();
    }

    // ── Spawn-Logik ───────────────────────────────────────────────────────────

    private void SpawnDummies()
    {
        Transform[] spawnPoints = dummyRole == PlayerRole.Hunter
            ? LevelManager.Instance.hunterSpawnPositions
            : LevelManager.Instance.vandalistSpawnPositions;

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning($"[TestDummySpawner] Keine Spawn-Positionen für Rolle {dummyRole} im LevelManager konfiguriert!");
            return;
        }

        for (int i = 0; i < dummyCount; i++)
        {
            // Spawnpunkt zyklisch nutzen, leichten Offset damit Dummies nicht übereinander stehen
            Transform spawnPoint = spawnPoints[i % spawnPoints.Length];
            Vector3 offset = new Vector3(
                Random.Range(-spawnSpread, spawnSpread),
                0f,
                Random.Range(-spawnSpread, spawnSpread)
            );
            Vector3 spawnPos = spawnPoint.position + offset;

            GameObject dummy = Instantiate(dummyPrefab, spawnPos, spawnPoint.rotation);

            // Rolle direkt auf dem Server setzen bevor Spawn → wird mit Spawn-Nachricht übertragen
            PlayerObjectController poc = dummy.GetComponent<PlayerObjectController>();
            if (poc != null)
            {
                poc.playerRole = dummyRole;
            }
            else
            {
                Debug.LogWarning("[TestDummySpawner] Dummy-Prefab hat keinen PlayerObjectController!");
            }

            NetworkServer.Spawn(dummy);
            Debug.Log($"[TestDummySpawner] Dummy #{i + 1} gespawnt bei {spawnPos}");
        }
    }
}