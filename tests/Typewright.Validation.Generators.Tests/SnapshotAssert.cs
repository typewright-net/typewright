using System.Runtime.CompilerServices;
using Xunit;

namespace Typewright.Validation.Generators.Tests;

/// <summary>
/// Minimal snapshot assertions: expected content lives in committed
/// <c>Snapshots/*.verified.cs</c> files next to the tests. A missing snapshot is written
/// and the test skips; set <c>TYPEWRIGHT_UPDATE_SNAPSHOTS=1</c> to rewrite all snapshots
/// after an intentional generator change.
/// </summary>
internal static class SnapshotAssert
{
    public static void MatchAll(
        IEnumerable<(string Name, string Content)> snapshots,
        [CallerFilePath] string testFilePath = "")
    {
        var directory = Path.Combine(Path.GetDirectoryName(testFilePath)!, "Snapshots");
        Directory.CreateDirectory(directory);
        var update = Environment.GetEnvironmentVariable("TYPEWRIGHT_UPDATE_SNAPSHOTS") == "1";

        var written = new List<string>();
        var failures = new List<string>();
        foreach (var (name, content) in snapshots)
        {
            var path = Path.Combine(directory, name.Replace(".g.cs", "") + ".verified.cs");
            var normalized = content.Replace("\r\n", "\n");
            if (update || !File.Exists(path))
            {
                File.WriteAllText(path, normalized);
                written.Add(path);
                continue;
            }

            var expected = File.ReadAllText(path).Replace("\r\n", "\n");
            if (expected != normalized)
            {
                File.WriteAllText(path + ".received", normalized);
                failures.Add(name);
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail(
                $"Generated output differs from snapshots: {string.Join(", ", failures)}. " +
                "Inspect the *.received files, or set TYPEWRIGHT_UPDATE_SNAPSHOTS=1 to accept.");
        }

        if (written.Count > 0)
        {
            Assert.Skip($"Wrote {written.Count} snapshot(s); re-run to verify against them.");
        }
    }
}
