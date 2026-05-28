using FMODUnity;
using Mirror;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Wall : NetworkBehaviour, IDestructable
{
    [Header("References")]
    [SerializeField] private GameObject wallNormal;
    [SerializeField] private GameObject wallBroken;
    [SerializeField] private List<GameObject> wallDecorations = new List<GameObject>();
    [SerializeField] private ParticleSystem hitParticle;

    [Header("Settings")]
    [SerializeField] private bool indestructable;
    // Leben muss nur der Server kennen, das m�ssen wir nicht zwingend synchronisieren
    [SerializeField] private float health;
    [SerializeField] private float explosionForce;
    [SerializeField] private float explosionRadius;

    [Header("Wall Decorations")]
    [SerializeField] private float wallDecorationsFadeOutSpeedMultiplier = 0.2f;
    [SerializeField] private float speedUpWallDecorationsFadeOutSpeedMultiplier = 0.5f;

    public float Health { get { return health; } }

    public ParticleSystem HitParticles => hitParticle;

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
        if (indestructable) return;

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
        RuntimeManager.PlayOneShot("event:/SFX/WallHit", point);
    }

    // Dieser Hook wird auf ALLEN Rechnern ausgefuehrt, sobald isDestroyed = true wird.
    // Auch wenn ein Spieler 5 Minuten spaeter ins Spiel joint, sieht er dadurch die Wand im kaputten Zustand.
    private void OnWallDestroyed(bool oldState, bool newState)
    {
        if (newState == true && oldState == false)
        {
            wallNormal.SetActive(false);
            wallBroken.SetActive(true);

            WallDecorationsSetup();
        }
    }

    // ClientRpc: Die eigentliche physikalische Explosion.
    // Laeuft lokal auf allen Rechnern, spart massiv Netzwerk-Bandbreite.
    [ClientRpc]
    private void RpcTriggerExplosion(Vector3 _hitPoint)
    {
        RuntimeManager.PlayOneShot("event:/SFX/WallBreakdown", _hitPoint);

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
}