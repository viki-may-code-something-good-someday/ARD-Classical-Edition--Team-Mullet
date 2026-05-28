using UnityEngine;

public class SoundBoxSpawnPoint : MonoBehaviour
{

    public void SpawnSoundBox(GameObject soundBoxPrefab, Transform parent)
    {
        Instantiate(soundBoxPrefab, transform.position, Quaternion.identity, parent);
    }

}
