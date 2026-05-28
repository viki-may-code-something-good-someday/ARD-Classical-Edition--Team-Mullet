using UnityEngine;

public class BillboardObject : MonoBehaviour
{
    [SerializeField] private bool flippedSprite;
    [SerializeField] private bool knockbackEnabled = true;
    [SerializeField] private float knockbackForce = 5f;
    [SerializeField] private float punchKnockbackMultiplier = 3f;
    [SerializeField] private float knockbackDuration = 0.3f;
    [SerializeField] private ParticleSystem punchParticles;

    private Camera mainCamera;
    private Rigidbody rb;
    private bool isKnockedBack;

    private void Start()
    {
        mainCamera = Camera.main;
        rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        if (isKnockedBack || mainCamera == null) return;


        Vector3 directionToCamera = mainCamera.transform.position - transform.position;
        directionToCamera.y = 0f;

        if (flippedSprite)
        {
            transform.rotation = Quaternion.LookRotation(directionToCamera);
        }
        else
        {
            transform.rotation = Quaternion.LookRotation(directionToCamera) * Quaternion.Euler(0, 180, 0);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (knockbackEnabled && collision.gameObject.CompareTag("Player"))
        {
            ApplyKnockback(collision.transform.position, knockbackForce);
        }
    }

    // (von Player_Interact via ClientRpc auf allen Clients aufgerufen)
    public void TakePunch(Vector3 _puncherPosition)
    {
        if (knockbackEnabled)
        {
            ApplyKnockback(_puncherPosition, knockbackForce * punchKnockbackMultiplier);
        }
    }

    private void ApplyKnockback(Vector3 _sourcePosition, float _force)
    {
        if (punchParticles != null)
        {
            ParticleSystem particles = Instantiate(punchParticles, transform.position + new Vector3(0f, 0.6f, 0f), Quaternion.identity);
            particles.Play();
        }

        Vector3 knockbackDirection = (transform.position - _sourcePosition).normalized;
        float randomMultiplier = Mathf.Max(Random.value, 0.5f) * 3f;

        rb.linearVelocity = new Vector3(
            knockbackDirection.x,
            0.8f,
            knockbackDirection.z
        ) * _force * randomMultiplier;

        isKnockedBack = true;
        CancelInvoke(nameof(ResetKnockback));
        Invoke(nameof(ResetKnockback), knockbackDuration);
    }

    private void ResetKnockback()
    {
        isKnockedBack = false;
    }
}
