using DG.Tweening;
using FMODUnity;
using Mirror;
using System.Collections.Generic;
using UnityEngine;

public class Player_Interact : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private List<GameObject> armsVisuals = new List<GameObject>();
    [Header("Interaction Settings")]
    [SerializeField] private float hitRange;
    [SerializeField] private float hitDamage;

    private Camera cameraMain;
    private bool rightArmPunching;


    void Start()
    {
        if (isLocalPlayer)
        {
            cameraMain = Camera.main;
        }
    }

    void Update()
    {
        if (!isLocalPlayer) return;

        if (Input.GetMouseButtonDown(0))
        {
            LocalPunch();
        }
    }

    private void LocalPunch()
    {
        Debug.Log("Local punch!");
        PunchAnimation();

        bool hitSomething = Physics.Raycast(cameraMain.transform.position, cameraMain.transform.forward, hitRange, LayerMask.GetMask("Interactable", "Destructable", "Default"), QueryTriggerInteraction.Ignore);

        if (hitSomething)
            RuntimeManager.PlayOneShot("event:/SFX/Punch");
        else
            RuntimeManager.PlayOneShot("event:/SFX/PunchAir");

        CmdTryInteract(cameraMain.transform.position, cameraMain.transform.forward);
    }

    [Command]
    private void CmdTryInteract(Vector3 origin, Vector3 direction)
    {
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
            if (hitinfoDestructable.collider.TryGetComponent<Destructable>(out Destructable destructableObject))
            {
                destructableObject.Destruct(hitDamage, hitinfoDestructable.point, hitinfoDestructable.normal);
                hitSomething = true;
            }
        }
        else if (Physics.Raycast(origin, direction, out RaycastHit hitinfoBillboard, hitRange, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore))
        {
            if (hitinfoBillboard.collider.TryGetComponent<BillboardObject>(out BillboardObject billboardObject))
            {
                billboardObject.TakePunch(origin);
                hitSomething = true;
            }
        }

        RpcPlayPunchEffects(hitSomething);
    }

    // [ClientRpc] wird vom Server aufgerufen, aber auf ALLEN CLIENTS ausgef�hrt.
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
