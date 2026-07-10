using Mirror;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// Hunter-Aktion: Anklagen mit Charge-Mechanik.
///
/// FUNKTIONSWEISE:
///   1. Jeder Frame: Raycast vorwärts gegen den "Player"-Layer (accuseRange).
///   2. Trifft ein Vandalist der weder gefangen noch unverwundbar ist: accuseReadyIndicator einblenden.
///   3. ActionHeld → Arm zieht sich zurück (DOTween Pullback), chargeTimer läuft. Charging ist
///      IMMER möglich, unabhängig davon ob gerade ein Ziel im Fadenkreuz ist.
///   4. Nach chargeTime Sekunden ist der Charge voll (isFullyCharged) – der Arm bleibt in der
///      Pullback-Pose, es passiert noch nichts. Es wird NICHT automatisch ausgelöst.
///   5. Erst beim Loslassen der Taste: War der Charge voll → Anklage wird ausgeführt (Arm schlägt
///      vor, Server prüft ob aktuell ein gültiges Ziel getroffen wird). War der Charge noch nicht
///      voll → Charge bricht ab, Arm kehrt zurück, keine Anklage.
///
/// ARM-SETUP:
///   armVisual: enthält DOTweenAnimation-Komponenten für die Schlag-Animation (Execute).
///   Der Pullback-Weg läuft über programmatisches DOTween (localPosition Z).
///   Beide arbeiten unabhängig – der Pullback wird vor Execute gekillt.
///
/// ANTI-CHEAT:
///   Client-Cooldown reduziert Cmd-Traffic und dient der Responsiveness.
///   Server-Cooldown ist die eigentliche Durchsetzung – unabhängig vom Client-Wert.
///
/// Auf OnVandalistCaught abonnieren um das Fangereignis zu verarbeiten (z.B. PlayerRoleSetup, GameManager):
///   HunterAccuse.OnVandalistCaught += HandleCaught;
/// </summary>
public class HunterAccuse : NetworkBehaviour, IRoleAction
{
    [Header("Accuse Settings")]
    [SerializeField] private float accuseRange = 6f;
    [Tooltip("Layer auf dem sich Spieler-Collider befinden (z.B. 'Player').")]
    [SerializeField] private LayerMask playerLayer;

    [Header("Cooldown")]
    [Tooltip("Mindestzeit zwischen zwei Anklagen. Wird zusätzlich Server-seitig durchgesetzt " +
             "(Anti-Cheat) – der Client-Wert dient nur der Responsiveness/Traffic-Reduktion.")]
    [SerializeField] private float accuseCooldown = 1.5f;

    [Header("Charge")]
    [Tooltip("Wie lange die Taste gehalten werden muss um die Anklage auszulösen.")]
    [SerializeField] private float chargeTime = 1.0f;

    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("Arm-GameObject mit DOTweenAnimation-Komponenten für die Execute-Animation.")]
    [SerializeField] private GameObject armVisual;

    [Header("UI Feedback")]
    [Tooltip("Eingeblendet wenn ein Vandalist im Fadenkreuz und in Reichweite ist.")]
    [SerializeField] private GameObject accuseReadyIndicator;

    [Header("Accuse Animation")]
    [Tooltip("Lokale Z-Verschiebung des Arms während des Aufladens (negativ = zurückziehen).")]
    [SerializeField] private float pullbackDistance = -0.15f;
    [Tooltip("Wie lange der Arm braucht um die Pullback-Position zu erreichen. Sollte chargeTime entsprechen.")]
    [SerializeField] private float pullbackDuration = 1.0f;
    [Tooltip("Wie schnell der Arm nach Abbruch zurückkehrt.")]
    [SerializeField] private float returnDuration = 0.2f;
    [SerializeField] private Ease pullbackEase = Ease.InQuad;
    [SerializeField] private Ease returnEase = Ease.OutQuad;

    // ── Statisches Ereignis – GameManager oder andere Systeme können sich einklinken ──
    public static event System.Action<NetworkIdentity> OnVandalistCaught;

    // ── State ─────────────────────────────────────────────────────────────────
    private NetworkIdentity currentTarget;

    // Nur lokal auf dem Owner-Client relevant – reduziert unnötigen Cmd-Traffic.
    private float localNextAllowedAccuseTime = 0f;

    // Nur Server-seitig relevant – eigentliche Anti-Cheat-Durchsetzung.
    private float serverNextAllowedAccuseTime = 0f;

    private bool isCharging = false;
    private bool isFullyCharged = false;
    private float chargeTimer = 0f;
    private Tween pullbackTween;
    private Vector3 armRestPosition;

    // ── IRoleAction ───────────────────────────────────────────────────────────

    public void OnRoleActivated()
    {
        enabled = true;
        if (armVisual != null)
            armRestPosition = armVisual.transform.localPosition;
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

        UpdateTarget();

        if (InputManager.Instance == null) return;

        bool actionHeld = InputManager.Instance.CurrentInput.ActionHeld;
        bool cooldownOk = Time.time >= localNextAllowedAccuseTime;

        if (actionHeld)
        {
            // Charging ist immer möglich, unabhängig von einem aktuellen Ziel.
            if (!isCharging && !isFullyCharged && cooldownOk)
            {
                StartCharge();
            }
            else if (isCharging && !isFullyCharged)
            {
                chargeTimer += Time.deltaTime;
                if (chargeTimer >= chargeTime)
                {
                    // Voll aufgeladen: Arm bleibt in Pullback-Pose, wartet auf Loslassen.
                    isFullyCharged = true;
                }
            }
            // isFullyCharged && weiterhin gehalten → nichts tun, einfach warten.
        }
        else
        {
            // Taste losgelassen: nur bei vollem Charge wird tatsächlich angeklagt (Ziel-Check
            // passiert dabei serverseitig mit dem Ziel, das im Moment des Loslassens anvisiert ist).
            if (isFullyCharged)
                ExecuteAccuse();
            else if (isCharging)
                CancelCharge();
        }
    }

    // ── Charge ────────────────────────────────────────────────────────────────

    private void StartCharge()
    {
        isCharging = true;
        chargeTimer = 0f;

        if (armVisual != null)
        {
            pullbackTween?.Kill();
            Vector3 target = armRestPosition + new Vector3(0f, 0f, pullbackDistance);
            pullbackTween = armVisual.transform
                .DOLocalMove(target, pullbackDuration)
                .SetEase(pullbackEase);
        }
    }

    private void CancelCharge()
    {
        if (!isCharging) return;

        isCharging = false;
        isFullyCharged = false;
        chargeTimer = 0f;

        if (armVisual != null)
        {
            pullbackTween?.Kill();
            pullbackTween = armVisual.transform
                .DOLocalMove(armRestPosition, returnDuration)
                .SetEase(returnEase);
        }
    }

    private void ExecuteAccuse()
    {
        isCharging = false;
        isFullyCharged = false;
        chargeTimer = 0f;

        // Pullback beenden, Execute-Animation (DOTweenAnimation-Komponenten) abspielen
        if (armVisual != null)
        {
            pullbackTween?.Kill();
            armVisual.transform.localPosition = armRestPosition; // Arm zurück vor Execute-Anim
        }

        localNextAllowedAccuseTime = Time.time + accuseCooldown;

        // Sofortiges lokales Feedback, damit der Owner keine Latenz beim Anim-Start spürt.
        AccuseAnimation();
        CmdTryAccuse(currentTarget);
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
                QueryTriggerInteraction.Ignore))
        {
            PlayerObjectController poc = hit.collider.GetComponent<PlayerObjectController>();
            PlayerRoleSetup targetSetup = hit.collider.GetComponent<PlayerRoleSetup>();
            bool unavailable = targetSetup != null && (targetSetup.IsCaught || targetSetup.IsInvulnerable);

            if (poc != null && poc.playerRole == PlayerRole.Vandalist && !unavailable)
            {
                currentTarget = poc.GetComponent<NetworkIdentity>();
                SetIndicator(true);
                return;
            }
        }

        ClearTarget();
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
        // Server-seitiger Cooldown – unabhängig vom Client-Wert, verhindert Spam durch modifizierte Clients.
        if (Time.time < serverNextAllowedAccuseTime)
        {
            Debug.LogWarning("[HunterAccuse] Anklage im Cooldown ignoriert (evtl. Client-Manipulation).");
            return;
        }
        serverNextAllowedAccuseTime = Time.time + accuseCooldown;

        // Andere Clients sehen die Execute-Animation (Owner hat sie bereits lokal abgespielt).
        RpcPlayAccuseAnimationOnOthers();

        if (target == null) return;

        // Server-seitige Validierung: Rolle prüfen
        PlayerObjectController targetPoc = target.GetComponent<PlayerObjectController>();
        if (targetPoc == null || targetPoc.playerRole != PlayerRole.Vandalist)
        {
            Debug.LogWarning("[HunterAccuse] Ziel ist kein Vandalist.");
            return;
        }

        // Server-seitige Validierung: bereits gefangen oder gerade unverwundbar (frischer Respawn)?
        PlayerRoleSetup targetSetup = target.GetComponent<PlayerRoleSetup>();
        if (targetSetup != null && (targetSetup.IsCaught || targetSetup.IsInvulnerable))
        {
            Debug.LogWarning("[HunterAccuse] Ziel ist bereits gefangen oder unverwundbar.");
            return;
        }

        // Server-seitige Validierung: Distanz prüfen
        float dist = Vector3.Distance(transform.position, target.transform.position);
        if (dist > accuseRange)
        {
            Debug.LogWarning($"[HunterAccuse] Ziel zu weit entfernt: {dist:F1}m (Max: {accuseRange}m).");
            return;
        }

        // Validierung bestanden → alle Clients benachrichtigen
        RpcOnVandalistCaught(target);
    }

    [ClientRpc]
    private void RpcOnVandalistCaught(NetworkIdentity caughtPlayer)
    {
        // Ereignis für PlayerRoleSetup, GameManager etc. auslösen
        OnVandalistCaught?.Invoke(caughtPlayer);
    }

    [ClientRpc(includeOwner = false)]
    private void RpcPlayAccuseAnimationOnOthers()
    {
        AccuseAnimation();
    }

    // ── Animation ─────────────────────────────────────────────────────────────

    private void AccuseAnimation()
    {
        if (armVisual == null) return;

        DOTweenAnimation[] anims = armVisual.GetComponents<DOTweenAnimation>();
        if (anims.Length > 0)
        {
            foreach (DOTweenAnimation anim in anims)
                anim.DORestart();
        }
        else
        {
            Debug.LogError("[HunterAccuse] DOTweenAnimation fehlt auf: " + armVisual.name);
        }
    }
}