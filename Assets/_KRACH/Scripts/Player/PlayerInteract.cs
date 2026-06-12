using DG.Tweening;
using FMODUnity;
using Mirror;
using System.Collections.Generic;
using UnityEngine;

public class PlayerInteract : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private List<GameObject> armsVisuals = new List<GameObject>();
    [SerializeField] private Camera playerCamera;
    [Header("Interaction Settings")]
    [SerializeField] private float hitRange;
    [SerializeField] private float hitDamage;

    private bool rightArmPunching;

    void Update()
    {
        if (!isLocalPlayer) return;

        HandleActionInput();
    }

    private void HandleActionInput()
    {
        if (InputManager.Instance == null) return;

        if (InputManager.Instance.CurrentInput.ActionPressed)
        {
            LocalPunch();
        }
    }

    private void LocalPunch()
    {
        PunchAnimation();

        bool hitSomething = Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward, hitRange, LayerMask.GetMask("Interactable", "Destructable", "Default"), QueryTriggerInteraction.Ignore);

        if (hitSomething)
            RuntimeManager.PlayOneShot("event:/SFX/Punch");
        else
            RuntimeManager.PlayOneShot("event:/SFX/PunchAir");

        CmdTryInteract(playerCamera.transform.position, playerCamera.transform.forward);
    }

    [Command]
    private void CmdTryInteract(Vector3 origin, Vector3 direction)
    {
        Debug.Log("Sending interaction");
        bool hitSomething = false;

        if (Physics.Raycast(origin, direction, out RaycastHit hitinfo, hitRange, LayerMask.GetMask("Interactable"), QueryTriggerInteraction.Ignore))
        {
            if (hitinfo.collider.TryGetComponent<Interactable>(out Interactable interactableObj))
            {
                interactableObj.Interact();
                hitSomething = true;
            }
        }
        else if (Physics.Raycast(origin, direction, out RaycastHit hitinfoDestructable, hitRange, LayerMask.GetMask("Destructable"), QueryTriggerInteraction.Ignore))
        {
            /*
             if (hitinfoDestructable.collider.TryGetComponent<IDestructable>(out IDestructable destructableObject))
            {
                destructableObject.TakeDamage(hitDamage, hitinfoDestructable.point, hitinfoDestructable.normal);
                hitSomething = true;
            }
            */

            IDestructable destructableObject = hitinfoDestructable.collider.GetComponentInParent<IDestructable>();
            if (destructableObject != null)
            {
                destructableObject.TakeDamage(hitDamage, hitinfoDestructable.point, hitinfoDestructable.normal);
                hitSomething = true;
            }
        }
        else if (Physics.Raycast(origin, direction, out RaycastHit hitinfoBillboard, hitRange, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore))
        {
            if (hitinfoBillboard.collider.TryGetComponent<BillboardObject>(out BillboardObject billboardObject))
            {
                // Punch-Position an alle Clients senden -> die führen TakePunch lokal aus
                RpcPunchBillboard(hitinfoBillboard.collider.gameObject, origin);
                hitSomething = true;
            }
        }

        RpcPlayPunchEffects(hitSomething);
    }

    [ClientRpc]
    private void RpcPunchBillboard(GameObject _billboardGameObject, Vector3 _origin)
    {
        if (_billboardGameObject.TryGetComponent<BillboardObject>(out BillboardObject billboardObject))
        {
            billboardObject.TakePunch(_origin);
        }
    }

    // [ClientRpc] wird vom Server aufgerufen, aber auf ALLEN CLIENTS ausgefuehrt.
    // includeOwner = false verhindert, dass der lokale Spieler den Sound/Animation doppelt abspielt.
    [ClientRpc(includeOwner = false)]
    private void RpcPlayPunchEffects(bool hitSomething)
    {
        PunchAnimation();

        if (hitSomething)
            RuntimeManager.PlayOneShot("event:/SFX/Punch");
        else
            RuntimeManager.PlayOneShot("event:/SFX/PunchAir");
    }

    private void PunchAnimation()
    {
        if (armsVisuals.Count != 2)
        {
            Debug.LogWarning("PunchAnimation: Expected 2 arms, got " + armsVisuals.Count);
            return;
        }

        int selectedArm = rightArmPunching ? 0 : 1;
        rightArmPunching = !rightArmPunching;

        List<DOTweenAnimation> dotweenAnims = new List<DOTweenAnimation>(armsVisuals[selectedArm].GetComponents<DOTweenAnimation>());
        if (dotweenAnims.Count > 0)
        {
            foreach (DOTweenAnimation anim in dotweenAnims)
            {
                anim.DORestart();
            }
        }
        else
        {
            Debug.LogError("PunchAnimation: DOTweenAnimation component missing on " + armsVisuals[selectedArm].name);
        }
    }

}
