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

    // ── Punch-Logik ───────────────────────────────────────────────────────────

    private void LocalPunch()
    {
        PunchAnimation();

        bool hitSomething = Physics.Raycast(
            playerCamera.transform.position,
            playerCamera.transform.forward,
            hitRange,
            LayerMask.GetMask("Interactable", "Destructable", "Default"),
            QueryTriggerInteraction.Ignore
        );

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
        else if (Physics.Raycast(origin, direction, out RaycastHit hitBillboard, hitRange, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore))
        {
            if (hitBillboard.collider.TryGetComponent(out BillboardObject _))
            {
                RpcPunchBillboard(hitBillboard.collider.gameObject, origin);
                hitSomething = true;
            }
        }

        RpcPlayPunchEffects(hitSomething);
    }

    [ClientRpc]
    private void RpcPunchBillboard(GameObject billboardGameObject, Vector3 origin)
    {
        if (billboardGameObject.TryGetComponent(out BillboardObject billboardObject))
            billboardObject.TakePunch(origin);
    }

    [ClientRpc(includeOwner = true)]
    private void RpcPlayPunchEffects(bool hitSomething)
    {
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