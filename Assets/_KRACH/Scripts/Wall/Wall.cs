using Mirror;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using Sirenix.OdinInspector;
using UnityEditor.SceneManagement;
#endif

public class Wall : NetworkBehaviour, IDestructable
{
    [Header("References")]
    [SerializeField] private GameObject wallNormal;
    [SerializeField] private GameObject wallBroken;
    [SerializeField] private List<GameObject> wallDecorations = new List<GameObject>();
    [SerializeField] private ParticleSystem hitParticle;
    [SerializeField] private LuaSoundEmitter wallHitSoundEmitter;
    [SerializeField] private LuaSoundEmitter wallBreakdownSoundEmitter;
    [Tooltip("Klang für unzerstörbare ('Metall'-)Wände — gespielt statt Schaden, wenn 'indestructable' an ist.")]
    [SerializeField] private LuaSoundEmitter wallMetalSoundEmitter;

    [Header("Settings")]
    [SerializeField] private bool indestructable;
    // Leben muss nur der Server kennen, das m�ssen wir nicht zwingend synchronisieren
    [SerializeField] private float health;
    [SerializeField] private float explosionForce;
    [SerializeField] private float explosionRadius;

    [Header("Wall Decorations")]
    [SerializeField] private float wallDecorationsFadeOutSpeedMultiplier = 0.2f;
    [SerializeField] private float speedUpWallDecorationsFadeOutSpeedMultiplier = 0.5f;

    [Header("Doors")]
    [SerializeField] private Door doorPrefab;
    [SerializeField] private List<Door> spawnedDoors = new List<Door>();



    public float Health { get { return health; } }

    public ParticleSystem HitParticles => hitParticle;

#if UNITY_EDITOR
    private enum Axis { X, Y, Z }
    [SerializeField] private Axis thicknessAxis = Axis.Z;
    [SerializeField] private float doorSurfaceGap = 0.05f;
#endif

    private bool fadeOutSpeedIncreased;
    private bool fadeOutPieces;

    // SyncVar: Wenn der Server das auf true setzt, wissen ALLE Spieler (auch die, die sp�ter ins Spiel joinen), 
    // dass diese Wand kaputt ist. Das l�st automatisch "OnWallDestroyed" auf allen PCs aus.
    [SyncVar(hook = nameof(OnWallDestroyed))]
    private bool isDestroyed = false;

    private void Update()
    {
        // Die visuelle Fade-Out-Logik l�uft einfach lokal auf jedem Rechner
        if (fadeOutPieces)
        {
            FadeOutWallDecorations();
        }
    }

    [Server]
    public void TakeDamage(float _damage, Vector3 _hitPoint, Vector3 _hitNormal)
    {
        if (indestructable)
        {
            // Unzerstörbare ("Metall"-)Wand: nur Feedback-Klang, kein Schaden.
            RpcPlayMetalSound();
            return;
        }

        if (isDestroyed)
        {
            Debug.LogWarning($"trying to damage a wall that is already destroyed: {gameObject.name}");
            return;
        }

        // Sound/Effects auf allen Clients abspielen
        RpcShowEffects(_hitPoint, _hitNormal);
        RpcPlayHitSound(_hitPoint);

        health -= _damage;

        if (health <= 0f)
        {
            // 1. Status auf zerstoert setzen (Triggert den Hook fuer das Mesh-Swapping)
            isDestroyed = true;

            // 2. Den RPC fuer die physikalische Explosion an alle aktiven Spieler senden
            RpcTriggerExplosion(_hitPoint);
        }
    }

    [ClientRpc]
    private void RpcShowEffects(Vector3 _hitPoint, Vector3 _hitNormal)
    {
        if (HitParticles == null)
        {
            Debug.LogWarning($"is unassigned on {name} and has to be assigned in the inspector");
        }

        Instantiate(HitParticles, _hitPoint, Quaternion.LookRotation(_hitNormal));
    }

    [ClientRpc]
    private void RpcPlayHitSound(Vector3 point)
    {
        wallHitSoundEmitter.PlayOneShot();
    }

    [ClientRpc]
    private void RpcPlayMetalSound()
    {
        if (wallMetalSoundEmitter != null)
            wallMetalSoundEmitter.PlayOneShot();
    }

    private void OnWallDestroyed(bool oldState, bool newState)
    {
        if (newState == true && oldState == false)
        {
            wallNormal.SetActive(false);
            wallBroken.SetActive(true);

            WallDecorationsSetup();

            // Doors are children of this Wall, not of wallNormal, so they are NOT
            // deactivated by the line above. A destroyed wall has a hole, not a doorway,
            // so we explicitly disable them here — runs on every client via the SyncVar hook.
            foreach (Door door in spawnedDoors)
            {
                if (door != null) door.DeactivateDoor();
            }
        }
    }

    // ClientRpc: Die eigentliche physikalische Explosion.
    // Laeuft lokal auf allen Rechnern, spart massiv Netzwerk-Bandbreite.
    [ClientRpc]
    private void RpcTriggerExplosion(Vector3 _hitPoint)
    {
        wallBreakdownSoundEmitter.PlayOneShot();

        List<Rigidbody> rigidbodies = wallBroken.transform.GetComponentsInChildren<Rigidbody>().ToList();

        foreach (Rigidbody rb in rigidbodies)
        {
            rb.isKinematic = false;
            rb.useGravity = true;

            // Die Explosion wird auf jedem Rechner lokal berechnet
            rb.AddExplosionForce(explosionForce, _hitPoint, explosionRadius, 1f, ForceMode.Impulse);
        }
    }

    private void WallDecorationsSetup()
    {
        fadeOutPieces = true;

        foreach (GameObject piece in wallDecorations)
        {
            piece.transform.parent = null;
            piece.AddComponent<Rigidbody>();

            SphereCollider sc = piece.AddComponent<SphereCollider>();
            sc.radius = 0.1f;

            piece.AddComponent<BillboardObject>();
        }
    }

    private void FadeOutWallDecorations()
    {
        for (int i = wallDecorations.Count - 1; i >= 0; i--)
        {
            SpriteRenderer sr = wallDecorations[i].GetComponent<SpriteRenderer>();

            if (sr == null)
            {
                Destroy(wallDecorations[i]);
                wallDecorations.RemoveAt(i);
                continue;
            }

            Color currentColor = sr.material.color;
            float newAlpha = currentColor.a - Time.deltaTime * wallDecorationsFadeOutSpeedMultiplier;
            sr.material.color = new Color(currentColor.r, currentColor.g, currentColor.b, newAlpha);

            if (newAlpha <= 0f)
            {
                Destroy(wallDecorations[i]);
                wallDecorations.RemoveAt(i);
            }
            else if (newAlpha <= 80f && !fadeOutSpeedIncreased)
            {
                fadeOutSpeedIncreased = true;
                wallDecorationsFadeOutSpeedMultiplier = speedUpWallDecorationsFadeOutSpeedMultiplier;
            }
        }
    }


#if UNITY_EDITOR
    [Button("Generate Paired Doors")]
    private void GeneratePairedDoors()
    {
        if (doorPrefab == null)
        {
            Debug.LogError("[Wall] doorPrefab is not assigned.");
            return;
        }

        ClearDoors();

        if (!TryGetLocalBounds(out Bounds localBounds))
        {
            Debug.LogError("[Wall] Could not determine bounds (no BoxCollider or Renderer found).");
            return;
        }

        Vector3 axisDir = thicknessAxis switch
        {
            Axis.X => Vector3.right,
            Axis.Y => Vector3.up,
            _ => Vector3.forward
        };

        float halfExtent = Vector3.Scale(localBounds.extents, axisDir).magnitude;

        Door frontDoor = SpawnDoor(localBounds.center + axisDir * (halfExtent + doorSurfaceGap), axisDir);
        Door backDoor = SpawnDoor(localBounds.center - axisDir * (halfExtent + doorSurfaceGap), -axisDir);

        LinkDoors(frontDoor, backDoor);
        LinkDoors(backDoor, frontDoor);

        spawnedDoors.Add(frontDoor);
        spawnedDoors.Add(backDoor);

        // Without this, the change to the spawnedDoors list is never flagged as unsaved —
        // Unity won't write it to the scene/prefab even on explicit save, and it's lost
        // as soon as the scene is reloaded or reopened.
        EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    private void LinkDoors(Door target, Door pair)
    {
        SerializedObject so = new SerializedObject(target);
        so.FindProperty("pairedDoor").objectReferenceValue = pair;
        so.ApplyModifiedProperties();
    }

    private Door SpawnDoor(Vector3 localPosition, Vector3 localOutwardDir)
    {
        Door door = (Door)PrefabUtility.InstantiatePrefab(doorPrefab, transform);
        Undo.RegisterCreatedObjectUndo(door.gameObject, "Generate Door");

        // Apply the door's own configurable Y offset (e.g. pivot correction)
        localPosition.y += door.YOffset;

        door.transform.localPosition = localPosition;
        door.transform.localRotation = Quaternion.LookRotation(localOutwardDir, Vector3.up);

        return door;
    }

    [Button("Clear Doors")]
    private void ClearDoors()
    {
        foreach (Door door in spawnedDoors)
        {
            if (door != null) Undo.DestroyObjectImmediate(door.gameObject);
        }
        spawnedDoors.Clear();

        foreach (Door child in GetComponentsInChildren<Door>(true).ToList())
        {
            Undo.DestroyObjectImmediate(child.gameObject);
        }

        EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    private bool TryGetLocalBounds(out Bounds localBounds)
    {
        BoxCollider box = GetComponentInChildren<BoxCollider>();
        if (box != null)
        {
            Vector3 worldCenter = box.transform.TransformPoint(box.center);
            Vector3 localCenter = transform.InverseTransformPoint(worldCenter);
            Vector3 localSize = Vector3.Scale(box.size, box.transform.lossyScale);
            localBounds = new Bounds(localCenter, localSize);
            return true;
        }

        Renderer rend = wallNormal != null ? wallNormal.GetComponentInChildren<Renderer>() : GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            Vector3 localCenter = transform.InverseTransformPoint(rend.bounds.center);
            localBounds = new Bounds(localCenter, rend.bounds.size);
            return true;
        }

        localBounds = default;
        return false;
    }
#endif
}