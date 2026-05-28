using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "SoundBoxWave", menuName = "Scriptable Objects/SoundBoxWave")]
public class SoundBoxWave : ScriptableObject
{
    [Tooltip("SoundBox prefab references to spawn for this wave.")]
    public List<SoundBox> Boxes = new List<SoundBox>();
    public List<int> SpawnPosNumbers = new List<int>();
}
