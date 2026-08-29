using UnityEngine;
using TMPro;

public class SoundBoxCounterUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI counterText;

    private void Update()
    {
        if (GameManager.Instance == null || counterText == null) return;

        counterText.text = $"{GameManager.Instance.DestroyedSoundBoxCount}/{GameManager.Instance.TotalSoundBoxCount}";
    }
}