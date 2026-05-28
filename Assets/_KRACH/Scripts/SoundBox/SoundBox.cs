using UnityEngine;
using FMODUnity;
using DG.Tweening;
using Mirror;

public class SoundBox : NetworkBehaviour, IDestructable
{
    [SerializeField] private float health;
    [SerializeField] private StudioEventEmitter musicEmitter;

    [SerializeField] private GameObject hitAnimsContainer;

    public ParticleSystem HitParticles => throw new System.NotImplementedException();

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

        if (HitParticles == null)
        {
            Debug.LogWarning($"{HitParticles.name} is unassigned and has to be assigned in the inspector");
        }

        Instantiate(HitParticles, _hitPoint, Quaternion.LookRotation(_hitNormal));
    }

    private void GetDestroyed()
    {
        RuntimeManager.PlayOneShot("event:/SFX/SpeakerDestroy", transform.position);    // sound
        musicEmitter.Stop();

        SoundBoxSpawner.Instance.DestroyingSoundBox(this);
    }
}
