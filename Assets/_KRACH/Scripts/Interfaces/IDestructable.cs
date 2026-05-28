using UnityEngine;

public interface IDestructable
{
    public void TakeDamage(float damage, Vector3 _hitPoint, Vector3 _hitNormal);
    public ParticleSystem HitParticles { get; }
}
