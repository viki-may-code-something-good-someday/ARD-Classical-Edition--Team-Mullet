using Mirror;
using TMPro;
using UnityEngine;

/// <summary>
/// Lebt in der Win-Screen-Scene. Liest beim Start, wer gewonnen hat
/// (von GameManager vor dem Szenenwechsel in CustomNetworkManager hinterlegt)
/// und zeigt es an.
/// </summary>
public class WinScreenController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI winnerText;
    [SerializeField] private GameObject hunterWinVisuals;
    [SerializeField] private GameObject vandalistWinVisuals;

    private void Start()
    {
        CustomNetworkManager manager = NetworkManager.singleton as CustomNetworkManager;
        if (manager == null)
        {
            Debug.LogError("[WinScreenController] Kein CustomNetworkManager gefunden – kann Gewinner nicht anzeigen.");
            return;
        }

        DisplayWinner(manager.LastWinner);
    }

    private void DisplayWinner(WinningSide winner)
    {
        if (winnerText != null)
        {
            winnerText.text = winner switch
            {
                WinningSide.Hunter => "Die Hunter haben gewonnen!",
                WinningSide.Vandalist => "Die Vandalisten haben gewonnen!",
                _ => "Unentschieden."
            };
        }

        if (hunterWinVisuals != null) hunterWinVisuals.SetActive(winner == WinningSide.Hunter);
        if (vandalistWinVisuals != null) vandalistWinVisuals.SetActive(winner == WinningSide.Vandalist);
    }
}