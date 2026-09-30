using Pulse1x.App.Models.GameHub;
using Pulse1x.App.Services;
using Pulse1x.App.Services.GameHub;

namespace Pulse1x.Hub.Tests;

/// <summary>
/// Fim de sessão do GameHub: só as alterações de rede criadas pela sessão são desfeitas (as do
/// usuário ficam), e a procura por um processo sucessor só acontece para intermediários de vida curta.
/// </summary>
internal static class SessionRestoreTests
{
    public static void Register(TestSuite suite)
    {
        suite.Run("Session records only the network changes it created", () =>
        {
            var user = Change("user", DateTime.Now.AddHours(-1));
            var before = new HashSet<string> { user.Id };
            var mine = Change("mine", DateTime.Now);

            TestSuite.SequenceEqual(new[] { "mine" }, GameProfileEngine.NewChangeIds(before, new[] { user, mine }));
        });

        suite.Run("Session revert skips user and already reverted changes, newest first", () =>
        {
            var now = DateTime.Now;
            var user = Change("user", now.AddMinutes(-30));
            var older = Change("older", now.AddSeconds(-10));
            var newer = Change("newer", now);
            var reverted = Change("reverted", now.AddSeconds(-5));
            reverted.Reverted = true;

            var list = GameProfileEngine.SessionChangesToRevert(
                new[] { user, older, newer, reverted }, new[] { "older", "newer", "reverted" }, null);
            TestSuite.SequenceEqual(new[] { "newer", "older" }, list.Select(c => c.Id));
        });

        suite.Run("Crash mid network step recovers changes inside the step window only", () =>
        {
            var start = DateTime.Now.AddMinutes(-10);
            var user = Change("user", start.AddSeconds(-1));
            var during = Change("during", start.AddSeconds(3));
            var later = Change("later", start + GameProfileEngine.NetworkStepWindow + TimeSpan.FromSeconds(1));

            var list = GameProfileEngine.SessionChangesToRevert(new[] { user, during, later }, Array.Empty<string>(), start);
            TestSuite.SequenceEqual(new[] { "during" }, list.Select(c => c.Id));
        });

        suite.Run("Snapshot with only network changes still counts as pending", () =>
        {
            TestSuite.Equal(false, new SystemSnapshot().HasAnything);
            TestSuite.Equal(true, new SystemSnapshot { NetworkChangeIds = { "x" } }.HasAnything);
            TestSuite.Equal(true, new SystemSnapshot { NetworkTrackingStartedAt = DateTime.Now }.HasAnything);
        });

        suite.Run("Successor search only for short-lived processes", () =>
        {
            TestSuite.Equal(true, GameSessionManager.ShouldLookForSuccessor(TimeSpan.FromSeconds(20)));
            TestSuite.Equal(true, GameSessionManager.ShouldLookForSuccessor(TimeSpan.FromMinutes(4.9)));
            TestSuite.Equal(false, GameSessionManager.ShouldLookForSuccessor(TimeSpan.FromMinutes(5)));
            TestSuite.Equal(false, GameSessionManager.ShouldLookForSuccessor(TimeSpan.FromHours(2)));
        });
    }

    private static OptimizationChange Change(string id, DateTime at) =>
        new() { Id = id, Timestamp = at, OptimizationId = "network", ValueKind = "powerindex" };
}
