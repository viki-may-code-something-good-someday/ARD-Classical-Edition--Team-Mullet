using DG.Tweening;
using Mirror;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Vandalist-Aktion: Schlagen + Interagieren mit der Welt.
/// Wird von PlayerRoleSetup aktiviert/deaktiviert über IRoleAction.
/// </summary>
public class PlayerInteract : NetworkBehaviour, IRoleAction
{
    [Header("References")]
    [SerializeField] private List<GameObject> armsVisuals = new List<GameObject>();
    [SerializeField] private Camera playerCamera;

    [Header("Interaction Settings")]
    [SerializeField] private float hitRange = 2f;
    [SerializeField] private float hitDamage = 10f;
    [SerializeField] private float pointBlankRadius = 1.5f;
    [SerializeField] private float pointBlankMinDot = 0.3f; // must be roughly in front (~72°) (in welche Richtung getroffen wird von der Blick-Richtung ausgehend 0=> 180° range, 1 => 0° exakt geradeaus)
    [SerializeField] private float pointBlankContactRadius = 0.8f; // Within this distance the billboard is punchable regardless of where you look


    [Header("Sound")]
    [Tooltip("LuaSoundEmitter am Player, Script-Mode. Enable Multiplayer = false.")]
    [SerializeField] private LuaSoundEmitter punchSoundEmitter;
    [SerializeField] private LuaSoundEmitter punchAirSoundEmitter;

    private bool rightArmPunching;


    // ── IRoleAction ───────────────────────────────────────────────────────────

    public void OnRoleActivated()
    {
        enabled = true;
    }

    public void OnRoleDeactivated()
    {
        enabled = false;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    void Update()
    {
        if (!isOwned) return;
        if (InputManager.Instance == null) return;

        if (InputManager.Instance.CurrentInput.ActionPressed)
        {
            LocalPunch();
        }
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        SetLayerRecursively(armsVisuals, LayerMask.NameToLayer("Arms"));
    }

    private void SetLayerRecursively(List<GameObject> objects, int newLayer)
    {
        if (objects == null) return;

        // Gehe durch jedes Objekt in der Liste (z.B. linker Arm, rechter Arm)
        foreach (GameObject obj in objects)
        {
            SetLayerRecursively(obj, newLayer);
        }
    }

    // 2. Das ist die "Arbeiter"-Methode, die die eigentliche Rekursion macht
    private void SetLayerRecursively(GameObject obj, int newLayer)
    {
        if (obj == null) return;

        obj.layer = newLayer; // Ändert den Layer des aktuellen Objekts

        // Geht durch alle Kinder (Finger, Knochen, Waffen-Attachments)
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }


    // ── Punch logic ────────────────────────────────────────────────────────────

    private void LocalPunch()
    {
        PunchAnimation();

        bool hitSomething = Physics.Raycast(
            playerCamera.transform.position,
            playerCamera.transform.forward,
            hitRange,
            LayerMask.GetMask("Interactable", "Destructable", "Billboard"),
            QueryTriggerInteraction.Collide
        );

        // Point-blank prediction: if the ray missed but we're standing in/next to a billboard,
        // still predict a hit so the hit-sound plays instead of the whiff.
        if (!hitSomething && FindPointBlankBillboard(playerCamera.transform.forward) != null)
            hitSomething = true;

        CmdTryInteract(playerCamera.transform.position, playerCamera.transform.forward, hitSomething);
    }

    [Command]
    private void CmdTryInteract(Vector3 origin, Vector3 direction, bool predictedHit)
    {
        bool hitSomething = predictedHit;

        if (Physics.Raycast(origin, direction, out RaycastHit hitInteractable, hitRange, LayerMask.GetMask("Interactable"), QueryTriggerInteraction.Ignore))
        {
            if (hitInteractable.collider.TryGetComponent(out Interactable interactableObj))
            {
                interactableObj.Interact();
                hitSomething = true;
            }
        }
        else if (Physics.Raycast(origin, direction, out RaycastHit hitDestructable, hitRange, LayerMask.GetMask("Destructable"), QueryTriggerInteraction.Ignore))
        {
            IDestructable destructableObject = hitDestructable.collider.GetComponentInParent<IDestructable>();
            if (destructableObject != null)
            {
                destructableObject.TakeDamage(hitDamage, hitDestructable.point, hitDestructable.normal);
                hitSomething = true;
            }
        }
        else if (Physics.Raycast(origin, direction, out RaycastHit hitBillboard, hitRange, LayerMask.GetMask("Billboard"), QueryTriggerInteraction.Collide))
        {
            if (hitBillboard.collider.TryGetComponent(out BillboardObject billboardObject))
            {
                billboardObject.ServerTakePunch(transform.position);
                hitSomething = true;
            }
        }

        // Point-blank fallback: when you stand inside the billboard's trigger
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

    // Finds the best billboard at point-blank range. Runs on both client (prediction) and
    // server (authoritative), so both agree. Returns null if nothing suitable is around.
    private BillboardObject FindPointBlankBillboard(Vector3 forward)
    {
        Collider[] nearby = Physics.OverlapSphere(
            transform.position, pointBlankRadius,
            LayerMask.GetMask("Billboard"), QueryTriggerInteraction.Collide);

        BillboardObject closest = null;
        float bestFacing = pointBlankMinDot;

        Vector3 flatForward = new Vector3(forward.x, 0f, forward.z);
        bool haveForward = flatForward.sqrMagnitude > 0.0001f;
        if (haveForward) flatForward.Normalize();

        foreach (Collider col in nearby)
        {
            BillboardObject billboard = col.GetComponentInParent<BillboardObject>();
            if (billboard == null) continue;

            Vector3 to = billboard.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;

            // Standing inside / touching it -> punchable no matter where you look.
            if (dist <= pointBlankContactRadius) return billboard;

            if (!haveForward) continue;

            float facing = Vector3.Dot(to / dist, flatForward);
            if (facing > bestFacing) { bestFacing = facing; closest = billboard; }
        }

        return closest;
    }

    [ClientRpc(includeOwner = true)]
    private void RpcPlayPunchEffects(bool hitSomething)
    {
        // Mirror runs ClientRpcs even on disabled components.
        // Explicit check prevents sounds and animations in the lobby.
        if (!enabled) return;

        if (!isOwned)
            PunchAnimation();

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

        int selectedArm = rightArmPunching ? 0 : 1;
        rightArmPunching = !rightArmPunching;

        DOTweenAnimation[] anims = armsVisuals[selectedArm].GetComponents<DOTweenAnimation>();
        if (anims.Length > 0)
        {
            foreach (DOTweenAnimation anim in anims)
                anim.DORestart();
        }
        else
        {
            Debug.LogError("[PlayerInteract] DOTweenAnimation fehlt auf: " + armsVisuals[selectedArm].name);
        }
    }
}