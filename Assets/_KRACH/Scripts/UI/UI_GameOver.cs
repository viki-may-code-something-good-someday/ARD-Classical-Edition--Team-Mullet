using FMOD.Studio;
using FMODUnity;
using TMPro;
using UnityEngine;

public class UI_GameOver : MonoBehaviour
{
    public TextMeshProUGUI reasonText;
    public TextMeshProUGUI resultText;
    public TextMeshProUGUI buttonText;

    // [0] = verloren, [1] = gewonnen
    public string[] reasonForGameOverString;
    public string[] buttonTextRestart;

    private EventInstance gameOverSound;

    public static UI_GameOver Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        gameObject.SetActive(false);
    }

    // reasonForGameOver: 0 = verloren, 1 = gewonnen
    public void SetGameOverScreen(bool _won)
    {
        gameObject.SetActive(true);

        buttonText.text = buttonTextRestart[Random.Range(0, buttonTextRestart.Length)];
        reasonText.text = reasonForGameOverString[_won ? 1 : 0];
        resultText.text = _won ? "Gewonnen!" : "Verloren!";

        if (!_won)
        {
            //gameOverSound = RuntimeManager.CreateInstance("event:/SFX/GameOver");
            if (gameOverSound.isValid())
            {
                gameOverSound.start();
            }
        }
    }

    private void OnDestroy()
    {
        if (gameOverSound.isValid())
        {
            gameOverSound.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            gameOverSound.release();
        }
    }
}