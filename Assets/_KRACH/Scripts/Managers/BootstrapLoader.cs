using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;

/// <summary>
/// Lädt die Menu-Scene direkt nach dem Start der Bootstrap-Scene.
/// </summary>
public class BootstrapLoader : MonoBehaviour
{
    [Scene]
    [SerializeField] private string menuSceneName;

    private void Start()
    {
        SceneManager.LoadScene(menuSceneName);
    }
}