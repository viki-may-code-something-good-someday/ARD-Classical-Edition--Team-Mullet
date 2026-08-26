using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TimerUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI timerText;

    private void Start()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogWarning("[TimerUI] GameManager.Instance ist null – Timer wird nicht aktualisiert.");
            return;
        }

        if (timerText == null)
        {
            Debug.LogWarning("[TimerUI] timerText ist nicht zugewiesen – Timer wird nicht angezeigt.");
            return;
        }
    }

    private void Update()
    {
        if (GameManager.Instance != null)
        {
            UpdateTimer(GameManager.Instance.maxPlaytimeInSeconds - GameManager.Instance.CurrentPlaytime);
        }
    }

    public void UpdateTimer(float timeRemaining)
    {
        if (timeRemaining < 0f) timeRemaining = 0f;

        int minutes = Mathf.FloorToInt(timeRemaining / 60f);
        int seconds = Mathf.FloorToInt(timeRemaining % 60f);
        timerText.text = $"{minutes:00}:{seconds:00}";
    }
}
