namespace TowerSentinel
{
    /// <summary>
    /// The way a player is told that the tower hit them.
    /// </summary>
    public enum NotificationType
    {
        /// <summary>No message is shown.</summary>
        None,

        /// <summary>The message is shown as a hint.</summary>
        Hint,

        /// <summary>The message is shown as a broadcast.</summary>
        Broadcast,
    }
}
