/// <summary>
/// Interface für rollenspezifische Aktionen.
/// Implementiert von PlayerInteract (Vandalist) und HunterAccuse (Hunter).
///
/// Wird von PlayerRoleSetup aufgerufen wenn die Rolle aktiviert/deaktiviert wird.
/// Neue rollenspezifische Aktionen einfach dieses Interface implementieren lassen
/// und in PlayerRoleSetup als IRoleAction-Referenz eintragen.
/// </summary>
public interface IRoleAction
{
    void OnRoleActivated();
    void OnRoleDeactivated();
}