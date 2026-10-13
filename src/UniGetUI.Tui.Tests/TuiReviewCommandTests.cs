using System.Diagnostics;
using System.Text;
using UniGetUI.Tui.Infrastructure;

namespace UniGetUI.Tui.Tests;

public class TuiReviewCommandTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void InteractiveStartupRejectsEitherRedirectedStreamOnEveryPlatform(bool input, bool output)
        => Assert.Contains("requires console input and output",
            Assert.Throws<IOException>(() => TuiAppHost.EnsureInteractiveStreams(input, output)).Message);

    [Fact]
    public void InteractiveStartupAcceptsTerminalStreams()
        => TuiAppHost.EnsureInteractiveStreams(false, false);

    [Theory]
    [InlineData(true, "cmd.exe", "/C")]
    [InlineData(false, "sh", "-c")]
    public async Task ExportedScriptPassesCommandsIntactAndCountsFailedExitCodes(bool windows, string shell, string flag)
    {
        string directory = Path.Join(Path.GetTempPath(), "tui-script-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Join(directory, "test.ps1");
        string[] commands = ["printf 'مرحبا الكويت'", "echo second"];
        // Inert shell function receives the actual exported argument list; no package manager runs.
        string stub = $$"""
            function Clear-Host { }
            function {{shell}} {
                if ($args[0] -ne '{{flag}}') { throw 'Wrong shell argument' }
                if ($args.Count -ne 2) { throw 'Command was split into multiple arguments' }
                Write-Output ('REC:' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($args[1])))
                $global:LASTEXITCODE = if ($args[1] -eq 'echo second') { 7 } else { 0 }
            }

            """;
        try
        {
            await File.WriteAllTextAsync(path, stub + TuiBundleService.GenerateScript(["a 'quoted' package"], commands, windows));
            var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (string arg in new[] { "-NoProfile", "-File", path, "/DisablePausePrompts" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            string text = await output;
            Assert.Equal(1, process.ExitCode);
            Assert.Equal("", await error);
            foreach (string command in commands)
                Assert.Contains("REC:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(command)), text);
            Assert.Contains("Total commands run: 2  Successful: 1  Failed: 1", text);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(false, false, "pkill -x 'editor'")]
    [InlineData(false, true, "pkill -9 -x 'editor'")]
    [InlineData(true, false, "taskkill /im \"editor\"")]
    [InlineData(true, true, "taskkill /im \"editor\" /f")]
    public void ExportedProcessStopMatchesTargetPlatform(bool windows, bool force, string expected)
        => Assert.Equal(expected, TuiBundleService.CreateProcessKillCommand("editor", force, windows));
}
