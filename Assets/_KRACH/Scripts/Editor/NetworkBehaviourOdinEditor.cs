#if UNITY_EDITOR
using Mirror;
using Sirenix.OdinInspector.Editor;
using UnityEditor;

/// <summary>
/// Mirror registers its own custom Inspector for every NetworkBehaviour, which overrides
/// Odin's automatic editor replacement. This restores Odin's inspector (including [Button]
/// attributes) for all NetworkBehaviour scripts, project-wide, with a single class.
/// </summary>
[CustomEditor(typeof(NetworkBehaviour), true, isFallback = true)]
[CanEditMultipleObjects]
public class NetworkBehaviourOdinEditor : OdinEditor { }
#endif