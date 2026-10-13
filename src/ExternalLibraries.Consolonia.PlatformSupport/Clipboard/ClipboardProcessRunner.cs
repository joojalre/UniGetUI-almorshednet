using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Consolonia.PlatformSupport.Clipboard
{
    /// <summary>
    ///     Helper class for console drivers to invoke shell commands to interact with the clipboard.
    /// </summary>
    internal static class ClipboardProcessRunner
    {
        public static (int exitCode, string result) Bash(
            string commandLine,
            string inputText = "",
            bool waitForOutput = false
        )
        {
            string arguments = $"-c \"{commandLine}\"";
            (int exitCode, string result) = Process("bash", arguments, inputText, waitForOutput);

            return (exitCode, result.TrimEnd());
        }

        public static bool FileExists(this string value)
        {
            return !string.IsNullOrEmpty(value) && !value.Contains("not found", StringComparison.Ordinal);
        }

        public static (int exitCode, string result) ProcessText(
            string cmd,
            string arguments,
            string input = null,
            bool waitForOutput = true,
            int timeout = 10000
        )
        {
            string output = string.Empty;

            using var process = new Process();

            process.StartInfo = new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // TaskCompletionSource<bool> eventHandled = new();
            process.Start();

            // Drain both pipes while writing/waiting so large clipboard data cannot fill a pipe.
            using var drainTimeout = new CancellationTokenSource(timeout);
            var stdout = ReadOutputAsync(process.StandardOutput, waitForOutput ? 16 * 1024 * 1024 : 0, drainTimeout.Token);
            var stderr = ReadOutputAsync(process.StandardError, 64 * 1024, drainTimeout.Token);
            var stdin = WriteInputAsync(process.StandardInput.BaseStream, input, drainTimeout.Token);

            if (!process.WaitForExit(timeout))
            {
                drainTimeout.Cancel();
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1000);
                string timeoutError =
                    $@"Process timed out. Command line: {process.StartInfo.FileName} {process.StartInfo.Arguments}.";

                throw new TimeoutException(timeoutError);
            }
            // A descendant retaining an inherited pipe must not keep the UI waiting forever.
            drainTimeout.CancelAfter(1000);

            try
            {
                stdin.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (drainTimeout.IsCancellationRequested)
            {
                throw new TimeoutException("Clipboard command input did not complete within its time limit.");
            }
            catch (IOException) when (process.ExitCode != 0)
            {
                // Preserve the child's failure when it exits before consuming its input.
            }
            var standardOutput = stdout.GetAwaiter().GetResult();
            var standardError = stderr.GetAwaiter().GetResult();
            if (waitForOutput && standardOutput.truncated)
                throw new InvalidOperationException("Clipboard command output exceeded its size or drain-time limit.");
            if (waitForOutput) output = standardOutput.text;

            if (process.ExitCode > 0)
                output = $@"Process failed to run. Command line: {cmd} {arguments}.
										Output: {output}
										Error: {standardError.text}";

            return (process.ExitCode, output);
        }

        public static (int exitCode, string result) Process(
            string cmd,
            string arguments,
            string input = null,
            bool waitForOutput = true,
            int timeout = Timeout.Infinite
        )
        {
            string output = string.Empty;

            using var process = new Process();

            process.StartInfo = new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // TaskCompletionSource<bool> eventHandled = new();
            process.Start();

            if (!string.IsNullOrEmpty(input))
            {
                process.StandardInput.Write(input);
                process.StandardInput.Close();
            }

            if (!process.WaitForExit(timeout))
            {
                string timeoutError =
                    $@"Process timed out. Command line: {process.StartInfo.FileName} {process.StartInfo.Arguments}.";

                throw new TimeoutException(timeoutError);
            }

            if (waitForOutput && process.StandardOutput.Peek() != -1) output = process.StandardOutput.ReadToEnd();

            if (process.ExitCode > 0)
                output = $@"Process failed to run. Command line: {cmd} {arguments}.
										Output: {output}
										Error: {process.StandardError.ReadToEnd()}";

            return (process.ExitCode, output);
        }

        internal static async Task WriteInputAsync(Stream stream, string input, CancellationToken cancellationToken)
        {
            try
            {
                if (!string.IsNullOrEmpty(input))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(input);
                    // WaitAsync also bounds a stream that cannot cancel an in-flight OS write.
                    await stream.WriteAsync(bytes.AsMemory(), cancellationToken).AsTask()
                        .WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                await stream.FlushAsync(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // EOF is required even when an empty string is being copied.
                // Close the unbuffered pipe, never synchronously flush a StreamWriter here.
                stream.Close();
            }
        }

        private static async Task<(string text, bool truncated)> ReadOutputAsync(StreamReader reader, int limit, CancellationToken cancellationToken)
        {
            var output = new StringBuilder();
            char[] buffer = new char[4096];
            bool truncated = false;
            int read;
            try
            {
                while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).AsTask()
                    .WaitAsync(cancellationToken).ConfigureAwait(false)) != 0)
                {
                    int retained = Math.Min(read, limit - output.Length);
                    output.Append(buffer, 0, retained);
                    truncated |= retained != read;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                truncated = true;
            }
            return (output.ToString(), truncated);
        }
    }
}
