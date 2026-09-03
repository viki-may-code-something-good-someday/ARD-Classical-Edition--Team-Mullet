using DG.Tweening;
using Mirror;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Vandalist-Aktion: Schlagen + Interagieren mit der Welt.
/// Wird von PlayerRoleSetup über IRoleAction aktiviert/deaktiviert.
///
/// Der Client sagt den Treffer nur für den Sound voraus – die eigentliche Wirkung
/// (Interact / Damage / Billboard-Punch) entscheidet ausschließlich der Server.
/// </summary>
public class PlayerInteract : NetworkBehaviour, IRoleAction
{
    [Header("References")]
    [SerializeField] private List<GameObject> armsVisuals = new List<GameObject>();
    [SerializeField] private Camera playerCamera;

    [Header("Interaction Settings")]
    [SerializeField] private float hitRange = 2f;
    [SerializeField] private float hitDamage = 1f;
    [Tooltip("Radius in dem Billboards auch ohne Raycast-Treffer geschlagen werden können.")]
    [SerializeField] private float pointBlankRadius = 1.5f;
    [Tooltip("Blickrichtungs-Toleranz: 0 = 180°-Kegel, 1 = exakt geradeaus. 0.3 ≈ 72°.")]
    [SerializeField] private float pointBlankMinDot = 0.3f;
    [Tooltip("Innerhalb dieser Distanz zählt der Treffer unabhängig von der Blickrichtung.")]
    [SerializeField] private float pointBlankContactRadius = 0.8f;

    [Header("Vertical Look Rotation")]
    [SerializeField] private Transform armRotationContainer;
    [Tooltip("Wie stark die Arme der Kamera-Neigung folgen. 1 = 1:1, kleiner = gedämpft.")]
    [SerializeField] private float pitchRotationMultiplier = 1f;
    [Tooltip("Maximale Auf-/Ab-Rotation der Arme in Grad, unabhängig von der Kamera-Neigung.")]
    [SerializeField] private float maxArmPitchAngle = 60f;

    [Header("Anti-Cheat")]
    [Tooltip("Mindestzeit zwischen zwei Schlägen. Wird Server-seitig durchgesetzt.")]
    [SerializeField] private float punchCooldown = 0.15f;
    [Tooltip("Wie weit der vom Client gemeldete Raycast-Ursprung maximal vom Spieler entfernt " +
             "sein darf. Muss die Kamerahöhe abdecken, sonst werden legitime Schläge verworfen.")]
    [SerializeField] private float maxOriginDistance = 2f;

    [Header("Sound")]
    [Tooltip("LuaSoundEmitter am Player, Script-Mode. Enable Multiplayer = false.")]
    [SerializeField] private LuaSoundEmitter punchSoundEmitter;
    [SerializeField] private LuaSoundEmitter punchAirSoundEmitter;


    // Ursprüngliche lokale Rotation jedes Arms (Index synchron zu armsVisuals), als Basis für den Pitch-Offset.
    private readonly List<Quaternion> armBaseLocalRotations = new List<Quaternion>();
    private bool rightArmPunching;
    private float serverNextAllowedPunchTime;

    private int interactableMask;
    private int destructableMask;
    private int billboardMask;

    private void Awake()
    {
        interactableMask = LayerMask.GetMask("Interactable");
        destructableMask = LayerMask.GetMask("Destructable");
        billboardMask = LayerMask.GetMask("Billboard");

        // Ausgangs-Rotation jedes Arms merken, damit wir den Pitch nur als Offset drauflegen
        // und nicht die Basis-Pose (z.B. leichte Schräghaltung) überschreiben.
        foreach (GameObject arm in armsVisuals)
            armBaseLocalRotations.Add(arm != null ? arm.transform.localRotation : Quaternion.identity);
    }

    private void Start()
    {
        CacheArmContainerBaseRotation();
    }

    // ── IRoleAction ───────────────────────────────────────────────────────────

    public void OnRoleActivated() => enabled = true;
    public void OnRoleDeactivated() => enabled = false;

    // ── Update ────────────────────────────────────────────────────────────────

    void Update()
    {
        if (!isOwned || InputManager.Instance == null) return;

        ApplyVerticalArmRotation();

        if (InputManager.Instance.CurrentInput.ActionPressed)
            LocalPunch();
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        int armsLayer = LayerMask.NameToLayer("Arms");
        if (armsLayer < 0)
        {
            Debug.LogError("[PlayerInteract] Layer 'Arms' existiert nicht – Arme bleiben auf ihrem Layer.");
            return;
        }

        foreach (GameObject arm in armsVisuals)
            SetLayerRecursively(arm, armsLayer);
    }

    private static void SetLayerRecursively(GameObject obj, int layer)
    {
        if (obj == null) return;

        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    // ── Arms ─────────────────────────────────────────────────────────────────

    private Quaternion armContainerBaseLocalRotation;

    // Call this once, e.g. in Start() or Awake(), after armRotationContainer is assigned
    private void CacheArmContainerBaseRotation()
    {
        if (armRotationContainer == null) return;
        armContainerBaseLocalRotation = armRotationContainer.localRotation;
    }

    private void ApplyVerticalArmRotation()
    {
        if (playerCamera == null || armRotationContainer == null) return;

        float pitch = GetCameraPitch();

        pitch = Mathf.Clamp(pitch, -maxArmPitchAngle, maxArmPitchAngle) * pitchRotationMultiplier;

        armRotationContainer.localRotation = armContainerBaseLocalRotation * Quaternion.Euler(-pitch, 0f, 0f);
    }

    private float GetCameraPitch()
    {
        Vector3 forward = playerCamera.transform.forward;
        float pitch = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        return pitch; // positiv = nach oben schauen, negativ = nach unten
    }

    // ── Punch ─────────────────────────────────────────────────────────────────

    private void LocalPunch()
    {
        if (playerCamera == null)
        {
            Debug.LogError("[PlayerInteract] playerCamera ist nicht zugewiesen.");
            return;
        }

        PunchAnimation();

        Vector3 origin = playerCamera.transform.position;
        Vector3 direction = playerCamera.transform.forward;

        CmdTryInteract(origin, direction, PredictHit(origin, direction));
    }

    /// <summary>
    /// Reine Sound-Vorhersage für den Owner. Bildet die Abfragekette von CmdTryInteract exakt
    /// nach (gleiche Masken, gleiche Trigger-Behandlung, gleiche Reihenfolge), damit der lokal
    /// gespielte Sound zu dem passt was der Server tatsächlich als Treffer wertet.
    /// </summary>
    private bool PredictHit(Vector3 origin, Vector3 direction)
    {
        if (Physics.Raycast(origin, direction, out RaycastHit hit, hitRange, interactableMask, QueryTriggerInteraction.Ignore))
            return hit.collider.GetComponent<Interactable>() != null;

        if (Physics.Raycast(origin, direction, out hit, hitRange, destructableMask, QueryTriggerInteraction.Ignore))
            return hit.collider.GetComponentInParent<IDestructable>() != null;

        if (Physics.Raycast(origin, direction, out hit, hitRange, billboardMask, QueryTriggerInteraction.Collide))
            return hit.collider.GetComponent<BillboardObject>() != null;

        return FindPointBlankBillboard(direction) != null;
    }

    [Command]
    private void CmdTryInteract(Vector3 origin, Vector3 direction, bool predictedHit)
    {
        // ── Server-seitige Validierung (Anti-Cheat) ──
        // Origin und Direction kommen vom Client und sind damit grundsätzlich manipulierbar.

        if (Time.time < serverNextAllowedPunchTime) return;
        serverNextAllowedPunchTime = Time.time + punchCooldown;

        float originDistance = Vector3.Distance(origin, transform.position);
        if (originDistance > maxOriginDistance)
        {
            Debug.LogWarning($"[PlayerInteract] Raycast-Ursprung {originDistance:F1}m vom Spieler entfernt " +
                             $"(Max: {maxOriginDistance}m) – Schlag verworfen.");
            return;
        }

        if (direction.sqrMagnitude < 0.0001f) return;
        direction.Normalize();

        bool hitSomething = predictedHit;

        if (Physics.Raycast(origin, direction, out RaycastHit interactableHit, hitRange, interactableMask, QueryTriggerInteraction.Ignore))
        {
            if (interactableHit.collider.TryGetComponent(out Interactable interactableObj))
            {
                interactableObj.Interact();
                hitSomething = true;
            }
        }
        else if (Physics.Raycast(origin, direction, out RaycastHit destructableHit, hitRange, destructableMask, QueryTriggerInteraction.Ignore))
        {
            IDestructable destructable = destructableHit.collider.GetComponentInParent<IDestructable>();
            if (destructable != null)
            {
                destructable.TakeDamage(hitDamage, destructableHit.point, destructableHit.normal);
                hitSomething = true;
            }
        }
        else if (Physics.Raycast(origin, direction, out RaycastHit billboardHit, hitRange, billboardMask, QueryTriggerInteraction.Collide))
        {
            if (billboardHit.collider.TryGetComponent(out BillboardObject billboardObject))
            {
                billboardObject.ServerTakePunch(transform.position);
                hitSomething = true;
            }
        }

        // Fallback wenn man direkt im Trigger des Billboards steht.
        if (!hitSomething)
        {
            BillboardObject pointBlank = FindPointBlankBillboard(direction);
            if (pointBlank != null)
            {
                pointBlank.ServerTakePunch(transform.position);
                hitSomething = true;
            }
        }

        RpcPlayPunchEffects(hitSomething);
    }

    /// <summary>
    /// Bestes Billboard auf Tuchfühlung. Läuft identisch auf Client (Vorhersage) und
    /// Server (autoritativ), damit beide zum selben Ergebnis kommen.
    /// </summary>
    private BillboardObject FindPointBlankBillboard(Vector3 forward)
    {
        Collider[] nearby = Physics.OverlapSphere(
            transform.position, pointBlankRadius, billboardMask, QueryTriggerInteraction.Collide);

        Vector3 flatForward = new Vector3(forward.x, 0f, forward.z);
        bool haveForward = flatForward.sqrMagnitude > 0.0001f;
        if (haveForward) flatForward.Normalize();

        BillboardObject bestAligned = null;
        float bestFacing = pointBlankMinDot;

        foreach (Collider col in nearby)
        {
            BillboardObject billboard = col.GetComponentInParent<BillboardObject>();
            if (billboard == null) continue;

            Vector3 to = billboard.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;

            if (dist <= pointBlankContactRadius) return billboard; // direkt dran → immer treffbar
            if (!haveForward) continue;

            float facing = Vector3.Dot(to / dist, flatForward);
            if (facing > bestFacing)
            {
                bestFacing = facing;
                bestAligned = billboard;
            }
        }

        return bestAligned;
    }

    [ClientRpc(includeOwner = true)]
    private void RpcPlayPunchEffects(bool hitSomething)
    {
        // Mirror führt ClientRpcs auch auf deaktivierten Komponenten aus –
        // ohne diesen Check gäbe es Sounds/Animationen in der Lobby.
        if (!enabled) return;

        if (!isOwned) PunchAnimation();

        LuaSoundEmitter emitter = hitSomething ? punchSoundEmitter : punchAirSoundEmitter;
        if (emitter != null) emitter.PlayOneShot();
    }

    // ── Animation ─────────────────────────────────────────────────────────────

    private void PunchAnimation()
    {
        if (armsVisuals.Count != 2)
        {
            Debug.LogWarning("[PlayerInteract] PunchAnimation: 2 Arme erwartet, gefunden: " + armsVisuals.Count);
            return;
        }

        GameObject arm = armsVisuals[rightArmPunching ? 0 : 1];
        rightArmPunching = !rightArmPunching;

        DOTweenAnimation[] anims = arm.GetComponents<DOTweenAnimation>();
        if (anims.Length == 0)
        {
            Debug.LogError("[PlayerInteract] DOTweenAnimation fehlt auf: " + arm.name);
            return;
        }

        foreach (DOTweenAnimation anim in anims)
            anim.DORestart();
    }
}
