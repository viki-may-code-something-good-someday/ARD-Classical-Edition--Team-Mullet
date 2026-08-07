using UnityEngine;

/// <summary>
/// Setzt Objekte ohne Physik auf dem Boden ab.
///
/// Die Spawnpunkte im Level sind für echte Spieler gesetzt: die fallen nach dem Spawn
/// herunter und werden vom CharacterController auf dem Boden abgelegt. Ein Test-Dummy hat
/// weder CharacterController noch Rigidbody und bliebe exakt auf Höhe des Spawnpunkts in
/// der Luft stehen – außerhalb der Reichweite eines Hunters, der auf dem Boden steht.
/// </summary>
public static class SpawnPlacement
{
    /// <summary>
    /// Verschiebt <paramref name="position"/> vertikal so, dass die Unterkante von
    /// <paramref name="body"/> auf dem darunterliegenden Boden aufsetzt. Findet der Raycast
    /// keinen Boden, bleibt die Position unverändert.
    /// </summary>
    public static Vector3 DropToGround(
        Vector3 position, CapsuleCollider body, LayerMask groundMask, float searchHeight = 30f)
    {
        if (body == null) return position;

        // Leicht oberhalb starten, damit auch ein knapp im Boden steckender Spawnpunkt trifft.
        Vector3 rayStart = position + Vector3.up * 0.5f;

        if (!Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit,
                             searchHeight, groundMask, QueryTriggerInteraction.Ignore))
        {
            Debug.LogWarning($"[SpawnPlacement] Kein Boden unter {position} gefunden " +
                             $"(groundMask={groundMask.value}) – Position unverändert übernommen.");
            return position;
        }

        float bottomOffset = body.center.y - body.height * 0.5f;
        return new Vector3(position.x, hit.point.y - bottomOffset, position.z);
    }
}
