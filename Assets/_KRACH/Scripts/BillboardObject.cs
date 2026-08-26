using System.Collections.Generic;
using UnityEngine;
using Mirror;

[RequireComponent(typeof(NetworkIdentity))]
public class BillboardObject : NetworkBehaviour
{
    [Header("Visuals")]
    [SerializeField] private Transform spriteTransform;
    [SerializeField] private bool flippedSprite;

    [Header("Punch Knockback")]
    [SerializeField] private bool knockbackEnabled = true;
    [SerializeField] private float punchForce = 12f;
    [SerializeField] private float punchUpForce = 3f;
    [SerializeField] private Vector2 punchRandomRange = new Vector2(0.75f, 1.25f);

    [Header("Shove (walking into it)")]
    [SerializeField] private bool shoveEnabled = true;
    [SerializeField] private float shoveRadius = 0.6f;
    [SerializeField] private float shoveForce = 6f;
    [SerializeField] private float shoveUpForce = 4f;
    [SerializeField] private float shoveSideSharpness = 2f;
    [SerializeField] private float minApproachSpeed = 1f;
    [SerializeField] private float shoveCooldown = 0.35f;
    [SerializeField] private LayerMask playerMask;

    [Header("Shove Speed Scaling")]
    [Tooltip("Wie stark die Lauf-Geschwindigkeit des Spielers die Flugweite beeinflusst. " +
             "0 = keine Auswirkung (immer shoveForce/shoveUpForce), 1 = volle Skalierung mit der Geschwindigkeit.")]
    [Range(0f, 1f)]
    [SerializeField] private float shoveSpeedInfluence = 0.5f;
    [Tooltip("Anlaufgeschwindigkeit, bei der der Spieler exakt die konfigurierte shoveForce/shoveUpForce " +
             "auslöst (1x Multiplikator). Schnelleres Anlaufen skaliert proportional darüber hinaus.")]
    [SerializeField] private float shoveReferenceSpeed = 4f;
    [Tooltip("Obergrenze für den geschwindigkeitsbasierten Multiplikator, damit extrem schnelles " +
             "Anlaufen das Objekt nicht ins Unendliche schleudert.")]
    [SerializeField] private float maxShoveSpeedMultiplier = 2f;

    [Header("Physics")]
    [SerializeField] private float gravity = 20f;
    [SerializeField] private float groundDrag = 8f;   // horizontal friction while grounded
    [SerializeField] private float airDrag = 0.5f;    // horizontal friction in the air
    [SerializeField] private float groundCheckDistance = 0.2f;
    [SerializeField] private float groundOffset = 0f; // if the pivot is not exactly at the feet
    // Environment used for BOTH ground snapping and wall collision.
    [SerializeField] private LayerMask environmentMask = ~0;

    [Header("Wall Collision")]
    [SerializeField] private bool blockedByWalls = true;
    [SerializeField] private float collideRadius = 0.25f;

    [Header("Feedback")]
    [SerializeField] private ParticleSystem punchParticles;
    [SerializeField] private Vector3 particleOffset = new Vector3(0f, 0.6f, 0f);

    private Camera mainCamera;
    private LuaSoundEmitter soundEmitter;

    // --- Server-only state ---
    private Vector3 velocity;
    private bool isGrounded;
    private float shoveCooldownTimer;
    private readonly Collider[] overlapBuffer = new Collider[8];
    // Tracks player positions across frames so we can derive their run direction/speed on the server.
    private readonly Dictionary<Transform, Vector3> lastPlayerPositions = new Dictionary<Transform, Vector3>();
    private readonly HashSet<Transform> seenPlayers = new HashSet<Transform>();
    private readonly List<Transform> staleKeys = new List<Transform>();

    private const float Epsilon = 0.0001f;
    private const float GroundRayUp = 0.1f;
    private const float SnapTolerance = 0.05f;
    private const float WallSkin = 0.02f;

    private void Awake()
    {
        if (spriteTransform == null) spriteTransform = transform;
        soundEmitter = GetComponent<LuaSoundEmitter>();
    }

    public override void OnStartClient()
    {
        mainCamera = Camera.main;
    }

    // ---------------- SERVER: simulation ----------------
    [ServerCallback]
    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        if (shoveCooldownTimer > 0f) shoveCooldownTimer -= dt;

        if (shoveEnabled) UpdateShove(dt);

        velocity.y -= gravity * dt;

        MoveHorizontal(new Vector3(velocity.x, 0f, velocity.z) * dt);
        MoveVertical(velocity.y * dt, dt);

        ApplyDrag(dt);
    }

    [Server]
    private void UpdateShove(float dt)
    {
        int count = Physics.OverlapSphereNonAlloc(
            transform.position, shoveRadius, overlapBuffer, playerMask, QueryTriggerInteraction.Ignore);

        seenPlayers.Clear();

        float bestApproach = 0f;
        Vector3 bestAway = Vector3.zero;
        Vector3 bestRunDir = Vector3.zero;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            Transform player = overlapBuffer[i].transform;
            seenPlayers.Add(player);

            // Derive the player's velocity from its position delta (authority-independent).
            Vector3 current = player.position;
            Vector3 playerVel = Vector3.zero;
            if (lastPlayerPositions.TryGetValue(player, out Vector3 previous))
                playerVel = (current - previous) / dt;
            lastPlayerPositions[player] = current;

            Vector3 flatVel = new Vector3(playerVel.x, 0f, playerVel.z);
            Vector3 flatAway = transform.position - current;
            flatAway.y = 0f;

            // Player standing on top of it: fling it along their run direction instead.
            Vector3 awayDir = flatAway.sqrMagnitude > Epsilon
                ? flatAway.normalized
                : (flatVel.sqrMagnitude > Epsilon ? flatVel.normalized : RandomFlatDir());

            // Approach speed = how fast the player moves toward the object.
            float approach = flatAway.sqrMagnitude > Epsilon
                ? Vector3.Dot(flatVel, flatAway.normalized)
                : flatVel.magnitude;

            if (approach > bestApproach)
            {
                bestApproach = approach;
                bestAway = awayDir;
                bestRunDir = flatVel.sqrMagnitude > Epsilon ? flatVel.normalized : Vector3.zero;
                found = true;
            }
        }

        // Remove tracking entries for players that left the radius, so stale
        // positions don't produce a huge fake velocity when they return.
        staleKeys.Clear();
        foreach (Transform t in lastPlayerPositions.Keys)
            if (t == null || !seenPlayers.Contains(t)) staleKeys.Add(t);
        for (int i = 0; i < staleKeys.Count; i++)
            lastPlayerPositions.Remove(staleKeys[i]);

        if (!found || bestApproach < minApproachSpeed || shoveCooldownTimer > 0f) return;

        LaunchShove(bestAway, bestRunDir, bestApproach);
        shoveCooldownTimer = shoveCooldown;
    }

    [Server]
    private void LaunchShove(Vector3 awayDir, Vector3 runDir, float approachSpeed)
    {
        Vector3 forward = runDir.sqrMagnitude > Epsilon ? runDir : awayDir;

        Vector3 right = Vector3.Cross(Vector3.up, forward);
        right.y = 0f;
        if (right.sqrMagnitude < Epsilon) right = Vector3.Cross(Vector3.up, awayDir);
        right.Normalize();

        float lean = Vector3.Dot(right, awayDir);
        float absLean = Mathf.Clamp01(Mathf.Abs(lean));
        float sideAmount = Mathf.Clamp01(absLean * shoveSideSharpness);

        float sideSign = absLean > 0.01f
            ? Mathf.Sign(lean)
            : ((netId & 1u) == 0u ? 1f : -1f);

        Vector3 forwardDir = awayDir;
        Vector3 sideDir = right * sideSign;

        float speedRatio = shoveReferenceSpeed > Epsilon ? approachSpeed / shoveReferenceSpeed : 1f;
        float speedMultiplier = Mathf.Lerp(1f, Mathf.Clamp(speedRatio, 0f, maxShoveSpeedMultiplier), shoveSpeedInfluence);

        // WICHTIG: Nur der Vorwärtsanteil wird mit der Geschwindigkeit skaliert.
        Vector3 forwardComponent = forwardDir * ((1f - sideAmount) * shoveForce * speedMultiplier);
        Vector3 sideComponent = sideDir * (sideAmount * shoveForce);

        Vector3 horizontalForce = forwardComponent + sideComponent;

        velocity = horizontalForce + Vector3.up * (shoveUpForce * speedMultiplier / 2f);
    }

    // Called SERVER-SIDE from Player_Interact (inside the puncher's Command).
    [Server]
    public void ServerTakePunch(Vector3 puncherPosition)
    {
        if (!knockbackEnabled) return;

        Vector3 dir = transform.position - puncherPosition;
        dir.y = 0f;
        if (dir.sqrMagnitude < Epsilon) dir = RandomFlatDir();
        dir.Normalize();

        float rnd = Random.Range(punchRandomRange.x, punchRandomRange.y);
        velocity = dir * punchForce * rnd + Vector3.up * punchUpForce;

        RpcPlayPunchEffects();
    }

    // --- Movement helpers (server) ---
    [Server]
    private void MoveHorizontal(Vector3 delta)
    {
        float dist = delta.magnitude;
        if (dist < 1e-5f) return;

        Vector3 dir = delta / dist;

        if (blockedByWalls)
        {
            Vector3 origin = transform.position + Vector3.up * collideRadius;
            if (Physics.SphereCast(origin, collideRadius, dir, out RaycastHit hit, dist,
                    environmentMask, QueryTriggerInteraction.Ignore))
            {
                float allowed = Mathf.Max(0f, hit.distance - WallSkin);
                transform.position += dir * allowed;

                // Slide: remove the velocity component pointing into the wall.
                Vector3 n = hit.normal; n.y = 0f;
                if (n.sqrMagnitude > Epsilon)
                {
                    n.Normalize();
                    Vector3 v = new Vector3(velocity.x, 0f, velocity.z);
                    float into = Vector3.Dot(v, -n);
                    if (into > 0f) v += n * into;
                    velocity.x = v.x;
                    velocity.z = v.z;
                }
                return;
            }
        }

        transform.position += delta;
    }

    [Server]
    private void MoveVertical(float dy, float dt)
    {
        transform.position += Vector3.up * dy;

        // Ray length grows with fall speed so a fast descent can't tunnel through the floor.
        float fall = Mathf.Max(0f, -velocity.y) * dt;
        float rayLen = GroundRayUp + groundCheckDistance + fall;
        Vector3 origin = transform.position + Vector3.up * GroundRayUp;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayLen,
                environmentMask, QueryTriggerInteraction.Ignore))
        {
            float targetY = hit.point.y + groundOffset;
            if (velocity.y <= 0f && transform.position.y <= targetY + SnapTolerance)
            {
                Vector3 p = transform.position; p.y = targetY; transform.position = p;
                velocity.y = 0f;
                isGrounded = true;
                return;
            }
        }
        isGrounded = false;
    }

    [Server]
    private void ApplyDrag(float dt)
    {
        float drag = isGrounded ? groundDrag : airDrag;
        float factor = 1f / (1f + drag * dt); // framerate-independent exponential damping
        velocity.x *= factor;
        velocity.z *= factor;
    }

    private static Vector3 RandomFlatDir()
    {
        Vector2 r = Random.insideUnitCircle.normalized;
        return new Vector3(r.x, 0f, r.y);
    }

    // ---------------- CLIENTS: effects + billboard ----------------
    [ClientRpc]
    private void RpcPlayPunchEffects()
    {
        if (punchParticles != null)
        {
            ParticleSystem ps = Instantiate(punchParticles, transform.position + particleOffset, Quaternion.identity);
            ps.Play();
            Destroy(ps.gameObject, ps.main.duration + ps.main.startLifetime.constantMax);
        }
        if (soundEmitter != null) soundEmitter.PlayOneShot();
    }

    private void LateUpdate()
    {
        if (isServerOnly) return; // a dedicated server has no camera

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null) return;
        }

        Vector3 toCam = mainCamera.transform.position - spriteTransform.position;
        toCam.y = 0f;
        if (toCam.sqrMagnitude < Epsilon) return;

        Quaternion look = Quaternion.LookRotation(toCam);
        spriteTransform.rotation = flippedSprite ? look : look * Quaternion.Euler(0f, 180f, 0f);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, shoveRadius);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * collideRadius, collideRadius);
    }
}