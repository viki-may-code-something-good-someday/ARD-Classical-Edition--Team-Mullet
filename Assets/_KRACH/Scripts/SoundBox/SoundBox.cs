using UnityEngine;
using FMODUnity;
using DG.Tweening;
using Mirror;

public class SoundBox : NetworkBehaviour, IDestructable
{
    [Header("References")]
    [SerializeField] private StudioEventEmitter musicEmitter;
    [SerializeField] private GameObject hitAnimsContainer;
    [SerializeField] private ParticleSystem hitParticles;

    [Header("Settings")]
    [SerializeField] private float health;

    public ParticleSystem HitParticles => hitParticles;

    [Server]
    public void TakeDamage(float _damage, Vector3 _hitPoint, Vector3 _hitNormal)
    {
        health -= _damage;
        RpcShowEffects(_hitPoint, _hitNormal);

        if (health <= 0f)
        {
            GetDestroyed();
        }
    }

    [ClientRpc]
    private void RpcShowEffects(Vector3 _hitPoint, Vector3 _hitNormal)
    {
        foreach (DOTweenAnimation anim in hitAnimsContainer.GetComponentsInChildren<DOTweenAnimation>())
        {
            if (anim != null)
            {
                anim.DORestart();
            }
        }

        if (hitParticles == null)
        {
            Debug.LogWarning($"[SoundBox] hitParticles on '{name}' is unassigned in the Inspector.");
        }
        else
        {
            Instantiate(hitParticles, _hitPoint, Quaternion.LookRotation(_hitNormal));
        }
    }

    [Server]
    private void GetDestroyed()
    {
        RpcOnDestroyed();
        SoundBoxSpawner.Instance.NotifySoundBoxDestroyed(this);

        NetworkServer.Destroy(gameObject);
    }

    [ClientRpc]
    private void RpcOnDestroyed()
    {
        RuntimeManager.PlayOneShot("event:/SFX/SpeakerDestroy", transform.position);
        if (musicEmitter == null)
        {
            Debug.LogWarning($"[SoundBox] musicEmitter on '{name}' is unassigned in the Inspector.");
        }
        else
        {
            musicEmitter.Stop();
        }
    }
}