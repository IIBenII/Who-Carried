using RunRecap.Core;

namespace RunRecap.Tests;

public static class EventLogTests
{
    [Test]
    public static void ResetStartsFreshAndWriteAppends()
    {
        string path = Path.Combine(Path.GetTempPath(), "runrecap-tests", Guid.NewGuid().ToString("N"), "events.log");
        var log = new EventLog(path);
        log.Reset("header 1");
        log.Write("line a");
        log.Reset("header 2");
        log.Write("line b");
        string[] lines = File.ReadAllLines(path);
        Check.Equal(2, lines.Length, "line count");
        Check.Equal("header 2", lines[0], "header");
        Check.Equal("line b", lines[1], "line");
    }
}
