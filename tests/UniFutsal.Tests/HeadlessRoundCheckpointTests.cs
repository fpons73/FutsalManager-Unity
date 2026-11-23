using System.Diagnostics;
using System.Security.Cryptography;

namespace UniFutsal.Tests;

public sealed class HeadlessRoundCheckpointTests
{
    [Fact]
    public async Task SavesAndResumesASeasonAcrossSeparateProcesses()
    {
        using var scratch = new ScratchDirectory();
        var checkpoint = Path.Combine(scratch.Root, "round checkpoint.db");

        var played = await RunAsync(scratch.Root, ["--demo-rounds", "42"],
            ["", "", $"guardar {checkpoint}", "salir"]);
        Assert.Equal(0, played.ExitCode);
        Assert.Empty(played.StandardError);
        Assert.Contains("Progreso guardado | Jornadas completadas: 2", played.Lines);
        Assert.Equal("Jornadas completadas: 2 de 14", played.Lines[^1]);
        var before = Checksum(checkpoint);

        var resumed = await RunAsync(scratch.Root, ["--demo-rounds-load", checkpoint], ["", "salir"]);
        Assert.Equal(0, resumed.ExitCode);
        Assert.Empty(resumed.StandardError);
        Assert.Contains("Jornadas retomadas: 2 de 14", resumed.Lines);
        Assert.Equal(ReadTable(played.Lines, last: true), ReadTable(resumed.Lines, last: false));
        Assert.Equal(new[] { "Jornada 3 de 14" },
            resumed.Lines.Where(line => line.StartsWith("Jornada ", StringComparison.Ordinal)));
        Assert.Equal("Jornadas completadas: 3 de 14", resumed.Lines[^1]);
        Assert.Equal(before, Checksum(checkpoint));
        Assert.Single(Directory.GetFiles(scratch.Root));
    }

    [Fact]
    public async Task RejectsInvalidCheckpointsAndKeepsPlayingAfterAnOccupiedSaveTarget()
    {
        using var scratch = new ScratchDirectory();
        var occupied = Path.Combine(scratch.Root, "occupied.db");
        File.WriteAllText(occupied, "not a checkpoint");
        var before = Checksum(occupied);

        var rejected = await RunAsync(scratch.Root, ["--demo-rounds-load", occupied], []);
        Assert.Equal(1, rejected.ExitCode);
        Assert.Empty(rejected.Lines);
        Assert.Contains("No se pudo cargar el progreso por jornadas.", rejected.StandardError);

        var continued = await RunAsync(scratch.Root, ["--demo-rounds", "42"],
            [$"guardar {occupied}", "", "salir"]);
        Assert.Equal(0, continued.ExitCode);
        Assert.Contains("No se pudo guardar el progreso.", continued.StandardError);
        Assert.DoesNotContain(continued.Lines, line => line.StartsWith("Progreso guardado", StringComparison.Ordinal));
        Assert.Contains("Jornada 1 de 14", continued.Lines);
        Assert.Equal("Jornadas completadas: 1 de 14", continued.Lines[^1]);
        Assert.Equal(before, Checksum(occupied));
        Assert.Single(Directory.GetFiles(scratch.Root));
    }

    private static async Task<CliResult> RunAsync(string workingDirectory, string[] arguments, string[] input)
    {
        var app = Path.Combine(AppContext.BaseDirectory, "UniFutsal.Headless.dll");
        Assert.True(File.Exists(app), "The Headless executable must be available in the test build output.");
        var start = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(app);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Headless.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            foreach (var line in input)
                await process.StandardInput.WriteLineAsync(line.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            return new CliResult(process.ExitCode, (await output).Split(
                new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries), await errors);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private static string[] ReadTable(string[] lines, bool last)
    {
        static bool IsHeader(string line) => line.TrimStart().StartsWith("#  Club", StringComparison.Ordinal);
        var header = last ? Array.FindLastIndex(lines, IsHeader) : Array.FindIndex(lines, IsHeader);
        Assert.True(header >= 0, "The CLI output must contain standings.");
        Assert.True(lines.Length > header + 8, "The table must list all eight clubs.");
        return lines[(header + 1)..(header + 9)];
    }

    private static string Checksum(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed record CliResult(int ExitCode, string[] Lines, string StandardError);

    private sealed class ScratchDirectory : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("unifutsal-cli-rounds-").FullName;

        public void Dispose()
        {
            foreach (var file in Directory.GetFiles(Root))
                File.Delete(file);
            Directory.Delete(Root);
        }
    }
}
