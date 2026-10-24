namespace SSMP.Game.Server;

/// <summary>
/// Decides when scene ownership should change. A scene keeps its current host until that player actually leaves
/// or disconnects; dying and re-entering must not make two clients swap ownership mid-fight.
/// </summary>
internal static class SceneHostPolicy {
    /// <summary>
    /// Whether a player entering a scene should receive ownership.
    /// </summary>
    /// <param name="hasOtherPlayersInScene">Whether another player is already in the scene.</param>
    /// <param name="hasCurrentHost">Whether the server currently has an owner for the scene.</param>
    /// <returns>Whether the entering player becomes the scene host.</returns>
    public static bool ShouldAssignEnteringPlayerAsHost(
        bool hasOtherPlayersInScene,
        bool hasCurrentHost
    ) {
        return !hasOtherPlayersInScene || !hasCurrentHost;
    }
}
