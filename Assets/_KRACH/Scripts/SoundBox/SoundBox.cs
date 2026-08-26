using UnityEngine;
using FMODUnity;
using DG.Tweening;
using Mirror;

public class SoundBox : NetworkBehaviour, IDestructable
{
    [Header("References")]
    //[SerializeField] private StudioEventEmitter musicEmitter;
    [SerializeField] private GameObject hitAnimsContainer;
    [SerializeField] private ParticleSystem hitParticles;
    [SerializeField] private LuaSoundEmitter hitSoundEmitter;
    [SerializeField] private LuaSoundEmitter destroySoundEmitter;

    [Header("Settings")]
    [SerializeField] private float health;
    [SerializeField] private float destroyDelay = 2f;

    public ParticleSystem HitParticles => hitParticles;


    public static event System.Action<SoundBox> OnDestroyedServer;


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

        if (hitSoundEmitter != null)
            hitSoundEmitter.PlayOneShot();
    }

    [Server]
    private void GetDestroyed()
    {
        if (SoundBoxSpawner.Instance != null)
            SoundBoxSpawner.Instance.NotifySoundBoxDestroyed(this);
        else
            Debug.LogWarning($"[SoundBox] SoundBoxSpawner.Instance ist null – '{name}' wird nicht beim Spawner abgemeldet.");

        OnDestroyedServer?.Invoke(this);

        if (destroySoundEmitter != null)
            destroySoundEmitter.PlayOneShot();
        else
            Debug.LogWarning($"[SoundBox] destroySoundEmitter auf '{name}' ist nicht zugewiesen.");

        NetworkServer.Destroy(gameObject);
    }
}