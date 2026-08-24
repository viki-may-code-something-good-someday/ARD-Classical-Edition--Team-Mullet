using Mirror;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// Hunter-Aktion: Anklagen mit Charge-Mechanik.
///
/// Taste halten → Arm zieht zurück, chargeTimer läuft (auch ohne Ziel im Fadenkreuz).
/// Nach chargeTime ist der Charge voll, ausgelöst wird aber erst beim Loslassen –
/// vorher losgelassen bricht ab. Der Server prüft Rolle, Zustand, Distanz und Cooldown
/// erneut, der Client-Cooldown dient nur der Responsiveness.
///
/// Fangereignis abonnieren: HunterAccuse.OnVandalistCaught += HandleCaught;
/// </summary>
public class HunterAccuse : NetworkBehaviour, IRoleAction
{
    [Header("Accuse Settings")]
    [SerializeField] private float accuseRange = 6f;
    [Tooltip("Layer auf dem sich Spieler-Collider befinden (z.B. 'Player').")]
    [SerializeField] private LayerMask playerLayer;

    [Header("Cooldown")]
    [Tooltip("Mindestzeit zwischen zwei Anklagen. Wird zusätzlich Server-seitig durchgesetzt.")]
    [SerializeField] private float accuseCooldown = 1.5f;

    [Header("Charge")]
    [Tooltip("Wie lange die Taste gehalten werden muss um die Anklage auszulösen.")]
    [SerializeField] private float chargeTime = 1.0f;

    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("Arm-GameObject mit DOTweenAnimation-Komponenten für die Schlag-Animation.")]
    [SerializeField] private GameObject armVisual;

    [Header("Vertical Look Rotation")]
    [Tooltip("Wie stark der Arm der Kamera-Neigung folgt. 1 = 1:1, kleiner = gedämpft.")]
    [SerializeField] private float pitchRotationMultiplier = 1f;
    [Tooltip("Maximale Auf-/Ab-Rotation des Arms in Grad, unabhängig von der Kamera-Neigung.")]
    [SerializeField] private float maxArmPitchAngle = 60f;

    [Header("UI Feedback")]
    [Tooltip("Eingeblendet wenn ein Vandalist im Fadenkreuz und in Reichweite ist.")]
    [SerializeField] private GameObject accuseReadyIndicator;

    [Header("Accuse Animation")]
    [Tooltip("Lokale Z-Verschiebung des Arms während des Aufladens (negativ = zurückziehen)." +
             " Die Dauer entspricht immer chargeTime, damit der Arm genau dann hinten ist" +
             " wenn der Charge voll ist.")]
    [SerializeField] private float pullbackDistance = -0.15f;
    [Tooltip("Wie schnell der Arm nach Abbruch zurückkehrt.")]
    [SerializeField] private float returnDuration = 0.2f;
    [SerializeField] private Ease pullbackEase = Ease.InQuad;
    [SerializeField] private Ease returnEase = Ease.OutQuad;

    public static event System.Action<NetworkIdentity> OnVandalistCaught;

    // ── State ─────────────────────────────────────────────────────────────────

    private NetworkIdentity currentTarget;
    private float localNextAllowedAccuseTime;   // nur Owner-Client
    private float serverNextAllowedAccuseTime;  // nur Server (Anti-Cheat)

    private Quaternion armBaseLocalRotation;

    private bool isCharging;
    private bool isFullyCharged;
    private float chargeTimer;
    private Tween pullbackTween;
    private Vector3 armRestPosition;

    // ── IRoleAction ───────────────────────────────────────────────────────────

    public void OnRoleActivated()
    {
        enabled = true;
        if (armVisual != null)
        {
            armRestPosition = armVisual.transform.localPosition;
            armBaseLocalRotation = armVisual.transform.localRotation;
        }

        if (isOwned && accuseReadyIndicator == null)
            Debug.LogWarning("[HunterAccuse] accuseReadyIndicator ist nicht zugewiesen – " +
                             "der Hunter bekommt kein Feedback ob ein Ziel im Fadenkreuz ist.");
    }

    public void OnRoleDeactivated()
    {
        enabled = false;
        CancelCharge();
        ClearTarget();
    }

    // ── Update ────────────────────────────────────────────────────────────────

    void Update()
    {
        if (!isOwned) return;

        ApplyVerticalArmRotation();
        UpdateTarget();

        if (InputManager.Instance == null) return;

        if (InputManager.Instance.CurrentInput.ActionHeld)
        {
            if (isFullyCharged) return;

            if (!isCharging)
            {
                if (Time.time >= localNextAllowedAccuseTime) StartCharge();
                return;
            }

            chargeTimer += Time.deltaTime;
            if (chargeTimer >= chargeTime) isFullyCharged = true;
        }
        else if (isFullyCharged) ExecuteAccuse();
        else if (isCharging) CancelCharge();
    }

    // ── Arm ────────────────────────────────────────────────────────────────

    private void ApplyVerticalArmRotation()
    {
        if (playerCamera == null || armVisual == null) return;

        float pitch = GetCameraPitch();

        pitch = Mathf.Clamp(pitch, -maxArmPitchAngle, maxArmPitchAngle) * pitchRotationMultiplier;

        armVisual.transform.localRotation = armBaseLocalRotation * Quaternion.Euler(-pitch, 0f, 0f);
    }

    private float GetCameraPitch()
    {
        Vector3 forward = playerCamera.transform.forward;
        float pitch = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        return pitch; // positiv = nach oben schauen, negativ = nach unten
    }

    // ── Charge ────────────────────────────────────────────────────────────────

    private void StartCharge()
    {
        isCharging = true;
        chargeTimer = 0f;
        TweenArmTo(armRestPosition + Vector3.forward * pullbackDistance, chargeTime, pullbackEase);
    }

    private void CancelCharge()
    {
        if (!isCharging) return;

        ResetChargeState();
        TweenArmTo(armRestPosition, returnDuration, returnEase);
    }

    private void ExecuteAccuse()
    {
        ResetChargeState();

        if (armVisual != null)
        {
            pullbackTween?.Kill();
            armVisual.transform.localPosition = armRestPosition; // vor der Execute-Anim zurücksetzen
        }

        localNextAllowedAccuseTime = Time.time + accuseCooldown;

        AccuseAnimation(); // sofortiges lokales Feedback ohne Latenz
        CmdTryAccuse(currentTarget);
    }

    private void ResetChargeState()
    {
        isCharging = false;
        isFullyCharged = false;
        chargeTimer = 0f;
    }

    private void TweenArmTo(Vector3 localTarget, float duration, Ease ease)
    {
        if (armVisual == null) return;

        pullbackTween?.Kill();
        pullbackTween = armVisual.transform.DOLocalMove(localTarget, duration).SetEase(ease);
    }

    // ── Ziel-Erkennung ────────────────────────────────────────────────────────

    private void UpdateTarget()
    {
        if (playerCamera == null) return;

        if (Physics.Raycast(
                playerCamera.transform.position,
                playerCamera.transform.forward,
                out RaycastHit hit,
                accuseRange,
                playerLayer,
                QueryTriggerInteraction.Ignore)
            && IsValidTarget(hit.collider.GetComponentInParent<PlayerObjectController>(),
                             hit.collider.GetComponentInParent<PlayerRoleSetup>()))
        {
            currentTarget = hit.collider.GetComponentInParent<NetworkIdentity>();
            SetIndicator(true);
            return;
        }

        ClearTarget();
    }

    private static bool IsValidTarget(PlayerObjectController poc, PlayerRoleSetup setup)
    {
        if (poc == null || poc.playerRole != PlayerRole.Vandalist) return false;
        return setup == null || (!setup.IsCaught && !setup.IsInvulnerable);
    }

    private void ClearTarget()
    {
        currentTarget = null;
        SetIndicator(false);
    }

    private void SetIndicator(bool active)
    {
        if (accuseReadyIndicator != null && accuseReadyIndicator.activeSelf != active)
            accuseReadyIndicator.SetActive(active);
    }

    // ── Mirror ────────────────────────────────────────────────────────────────

    [Command]
    private void CmdTryAccuse(NetworkIdentity target)
    {
        if (Time.time < serverNextAllowedAccuseTime)
        {
            Debug.LogWarning("[HunterAccuse] Anklage im Cooldown ignoriert (evtl. Client-Manipulation).");
            return;
        }
        serverNextAllowedAccuseTime = Time.time + accuseCooldown;

        RpcPlayAccuseAnimationOnOthers();

        if (target == null) return;

        PlayerObjectController targetPoc = target.GetComponent<PlayerObjectController>();
        if (targetPoc == null || targetPoc.playerRole != PlayerRole.Vandalist)
        {
            Debug.LogWarning("[HunterAccuse] Ziel ist kein Vandalist.");
            return;
        }

        PlayerRoleSetup targetSetup = target.GetComponent<PlayerRoleSetup>();
        if (targetSetup != null && (targetSetup.IsCaught || targetSetup.IsInvulnerable))
        {
            Debug.LogWarning("[HunterAccuse] Ziel ist bereits gefangen oder unverwundbar.");
            return;
        }

        float dist = Vector3.Distance(transform.position, target.transform.position);
        if (dist > accuseRange)
        {
            Debug.LogWarning($"[HunterAccuse] Ziel zu weit entfernt: {dist:F1}m (Max: {accuseRange}m).");
            return;
        }

        RpcOnVandalistCaught(target);
    }

    [ClientRpc]
    private void RpcOnVandalistCaught(NetworkIdentity caughtPlayer) => OnVandalistCaught?.Invoke(caughtPlayer);

    [ClientRpc(includeOwner = false)]
    private void RpcPlayAccuseAnimationOnOthers() => AccuseAnimation();

    // ── Animation ─────────────────────────────────────────────────────────────

    private void AccuseAnimation()
    {
        if (armVisual == null) return;

        DOTweenAnimation[] anims = armVisual.GetComponents<DOTweenAnimation>();
        if (anims.Length == 0)
        {
            Debug.LogError("[HunterAccuse] DOTweenAnimation fehlt auf: " + armVisual.name);
            return;
        }

        foreach (DOTweenAnimation anim in anims)
            anim.DORestart();
    }
}
