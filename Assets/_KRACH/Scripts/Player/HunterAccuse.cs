using Mirror;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// Hunter-Aktion: Anklagen.
/// Der Hunter zeigt auf einen Vandalist in Reichweite und fängt ihn damit.
///
/// Funktionsweise:
///   – Jeder Frame: Raycast vorwärts gegen den "Player"-Layer.
///   – Trifft ein Vandalist der weder gefangen noch unverwundbar ist: accuseReadyIndicator einblenden.
///   – ActionPressed (mit Client-seitigem Cooldown): lokale Animation sofort abspielen (Responsiveness)
///     + CmdTryAccuse → Server validiert (Cooldown, Rolle, Caught/Invuln-Status, Distanz) → Ereignis auslösen.
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
    [Tooltip("Mindestzeit zwischen zwei Anklage-Versuchen. Wird zusätzlich Server-seitig " +
             "durchgesetzt (Anti-Cheat) – der Client-Wert dient nur der Responsiveness/Traffic-Reduktion.")]
    [SerializeField] private float accuseCooldown = 1.5f;

    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private GameObject armVisual;

    [Header("UI Feedback")]
    [Tooltip("Wird eingeblendet wenn ein Vandalist im Fadenkreuz und in Reichweite ist.")]
    [SerializeField] private GameObject accuseReadyIndicator;

    // ── Statisches Ereignis – GameManager oder andere Systeme können sich einklinken ──
    // Beispiel: HunterAccuse.OnVandalistCaught += myGameManager.HandleCaught;
    public static event System.Action<NetworkIdentity> OnVandalistCaught;

    private NetworkIdentity currentTarget;

    // Nur lokal auf dem Owner-Client relevant – reduziert unnötigen Cmd-Traffic.
    private float localNextAllowedAccuseTime = 0f;

    // Nur Server-seitig relevant – eigentliche Anti-Cheat-Durchsetzung.
    private float serverNextAllowedAccuseTime = 0f;

    // ── IRoleAction ───────────────────────────────────────────────────────────

    public void OnRoleActivated()
    {
        enabled = true;
    }

    public void OnRoleDeactivated()
    {
        enabled = false;
        ClearTarget();
    }

    // ── Update ────────────────────────────────────────────────────────────────

    void Update()
    {
        if (!isOwned) return;

        UpdateTarget();

        if (InputManager.Instance == null) return;

        if (InputManager.Instance.CurrentInput.ActionPressed
            && currentTarget != null
            && Time.time >= localNextAllowedAccuseTime)
        {
            localNextAllowedAccuseTime = Time.time + accuseCooldown;

            // Sofortiges lokales Feedback, damit der Owner keine Latenz beim Anim-Start spürt.
            AccuseAnimation();
            CmdTryAccuse(currentTarget);
        }
    }

    // ── Ziel-Erkennung ────────────────────────────────────────────────────────

    private void UpdateTarget()
    {
        if (playerCamera == null) return;

        if (Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward,
            out RaycastHit hit, accuseRange, playerLayer, QueryTriggerInteraction.Ignore))
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

        // Andere Clients sehen die Animation ebenfalls (der Owner hat sie bereits lokal abgespielt).
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

    // ── Animation ─────────────────────────────────────────────────────────────

    [ClientRpc(includeOwner = false)]
    private void RpcPlayAccuseAnimationOnOthers()
    {
        AccuseAnimation();
    }

    private void AccuseAnimation()
    {
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