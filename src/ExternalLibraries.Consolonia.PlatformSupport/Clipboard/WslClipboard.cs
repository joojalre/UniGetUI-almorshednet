using System;
using System.Text;
using System.Threading.Tasks;
using Consolonia.Core.Infrastructure;

namespace Consolonia.PlatformSupport.Clipboard
{
    /// <summary>
    ///     A clipboard implementation for Linux, when running under WSL. This implementation uses the Windows clipboard
    ///     to store the data, and uses Windows' powershell.exe (launched via WSL interop services) to set/get the Windows
    ///     clipboard.
    /// </summary>
    internal class WslClipboard : ConsoleClipboard
    {
        private static string _powershellPath = string.Empty;

        private readonly bool _isSupported;
        private readonly Func<string, string, string, (int exitCode, string result)> _run =
            (command, arguments, input) => ClipboardProcessRunner.ProcessText(command, arguments, input, timeout: 10000);
        private readonly string _command;

        // Clipboard data travels on stdin as base64, never as PowerShell source.
        internal const string SetTextArguments = "-NoProfile -NonInteractive -Command \"$text = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([Console]::In.ReadToEnd())); Set-Clipboard -Value $text\"";

        public WslClipboard()
        {
            if (string.IsNullOrEmpty(_powershellPath))
            {
                // Specify pwsh.exe (not pwsh) to ensure we get the Windows version (invoked via WSL)
                (int exitCode, string result) = ClipboardProcessRunner.Bash("which pwsh.exe", waitForOutput: true);

                if (exitCode > 0)
                    (exitCode, result) = ClipboardProcessRunner.Bash("which powershell.exe", waitForOutput: true);

                if (exitCode == 0) _powershellPath = result;
            }

            _isSupported = !string.IsNullOrEmpty(_powershellPath);
            _command = _powershellPath;
        }

        internal WslClipboard(string command, Func<string, string, string, (int exitCode, string result)> run)
        {
            _command = command;
            _run = run;
            _isSupported = !string.IsNullOrEmpty(command);
        }

        public override Task<string> GetTextAsync()
        {
            if (!_isSupported) return Task.FromResult(string.Empty);

            (int exitCode, string output) =
                _run(_command, "-noprofile -command \"Get-Clipboard\"", null);

            if (exitCode == 0) return Task.FromResult(output);

            return Task.FromResult(string.Empty);
        }

        public override Task SetTextAsync(string text)
        {
            if (_isSupported)
            {
                (int exitCode, _) = _run(_command, SetTextArguments,
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? string.Empty)));

                if (exitCode != 0)
                    throw new InvalidOperationException("Failed to set clipboard text using powershell");
            }

            return Task.CompletedTask;
        }
    }
}
