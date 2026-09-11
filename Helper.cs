using SamedisCare.Helper.Logging;

namespace SamedisCareLogMonitor
{
  /// <summary>
  /// What is left of this tool's own helpers.
  /// <para>
  /// Message() used to be here: the fifth copy of a logger writing
  /// <c>yyyy-MM-dd HH:mm:ss LEVEL message</c> by hand. It is now
  /// <see cref="FileSyncLog"/>, which matters more here than in the other tools -- this one
  /// reads that very format back out of the other tools' logs, and having written its own
  /// with a second implementation was an invitation for the two to drift.
  /// </para>
  /// </summary>
  internal static class Abort
  {
    /// <summary>
    /// Reports and ends the run. Terminating is the host's decision, not the library's,
    /// which is why this stays in the tool.
    /// </summary>
    internal static void With(ISyncLog log, string message)
    {
      log.Error(message);
      Environment.Exit(1);
    }
  }
}
