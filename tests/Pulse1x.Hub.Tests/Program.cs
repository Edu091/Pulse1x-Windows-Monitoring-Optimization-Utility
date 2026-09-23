using System.Windows.Threading;

namespace Pulse1x.Hub.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var suite = new TestSuite();
        suite.Run("Harness runs on a WPF STA dispatcher", () =>
        {
            TestSuite.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
            var dispatcher = Dispatcher.CurrentDispatcher;
            bool invoked = false;
            dispatcher.Invoke(() => invoked = dispatcher.CheckAccess());
            TestSuite.Equal(true, invoked);
        });

        GameActivationGestureTests.Register(suite);
        HubInputRouterTests.Register(suite);
        ControllerTests.Register(suite);
        InputMetricsTrackerTests.Register(suite);
        PersistenceTests.Register(suite);
        if (args.Contains("--ui")) HubVisualTests.Run(suite);
        return suite.Finish();
    }
}

internal sealed class TestSuite
{
    private int _passed;
    private int _failed;

    public void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception exception)
        {
            _failed++;
            Console.Error.WriteLine($"FAIL {name}\n{exception}");
        }
    }

    public int Finish()
    {
        Console.WriteLine($"{_passed} passed; {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected <{expected}>; got <{actual}>.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        var expectedItems = expected.ToArray();
        var actualItems = actual.ToArray();
        if (!expectedItems.SequenceEqual(actualItems))
            throw new InvalidOperationException(
                $"Expected [{string.Join(", ", expectedItems)}]; got [{string.Join(", ", actualItems)}].");
    }
}
