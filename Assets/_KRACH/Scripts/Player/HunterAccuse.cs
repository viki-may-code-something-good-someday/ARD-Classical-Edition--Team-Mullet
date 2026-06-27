using Mirror;
using UnityEngine;

/// <summary>
/// Hunter-Aktion: Anklagen.
/// Der Hunter zeigt auf einen Vandalist in Reichweite und fängt ihn damit.
///
/// Funktionsweise:
///   – Jeder Frame: Raycast vorwärts gegen den "Player"-Layer.
///   – Trifft ein Vandalist: accuseReadyIndicator einblenden.
///   – ActionPressed: CmdTryAccuse → Server validiert (Distanz + Rolle) → Ereignis auslösen.
///
/// Auf OnVandalistCaught abonnieren um das Fangereignis zu verarbeiten (z.B. GameManager):
///   HunterAccuse.OnVandalistCaught += HandleCaught;
/// </summary>
public class HunterAccuse : NetworkBehaviour, IRoleAction
{
    [Header("Accuse Settings")]
    [SerializeField] private float accuseRange = 6f;
    [Tooltip("Layer auf dem sich Spieler-Collider befinden (z.B. 'Player').")]
    [SerializeField] private LayerMask playerLayer;

    [Header("References")]
    [SerializeField] private Camera playerCamera;

    [Header("UI Feedback")]
    [Tooltip("Wird eingeblendet wenn ein Vandalist im Fadenkreuz und in Reichweite ist.")]
    [SerializeField] private GameObject accuseReadyIndicator;

    // ── Statisches Ereignis – GameManager oder andere Systeme können sich einklinken ──
    // Beispiel: HunterAccuse.OnVandalistCaught += myGameManager.HandleCaught;
    public static event System.Action<NetworkIdentity> OnVandalistCaught;

    private NetworkIdentity currentTarget;

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

        if (InputManager.Instance.CurrentInput.ActionPressed && currentTarget != null)
        {
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
            if (poc != null && poc.playerRole == PlayerRole.Vandalist)
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
        if (target == null) return;

        // Server-seitige Validierung: Rolle prüfen
        PlayerObjectController targetPoc = target.GetComponent<PlayerObjectController>();
        if (targetPoc == null || targetPoc.playerRole != PlayerRole.Vandalist)
        {
            Debug.LogWarning("[HunterAccuse] Ziel ist kein Vandalist.");
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
        // Ereignis für GameManager etc. auslösen
        OnVandalistCaught?.Invoke(caughtPlayer);
    }
}