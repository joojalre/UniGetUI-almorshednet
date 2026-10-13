using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Consolonia;
using Consolonia.PlatformSupport.Clipboard;

namespace UniGetUI.Tui.Tests;

public sealed class ClipboardPlatformContractTests
{
    [Theory]
    [InlineData("")]
    [InlineData("quotes \" ' ; $([Environment]::Exit(77)) `n ${HOME}")]
    [InlineData("الكويت 😀 café\r\nsecond line\nfinal\tline\r\n\n")]
    public async Task WslTextTravelsOnlyAsDataAndRoundTripsThroughPowerShell(string text)
    {
        int calls = 0;
        var clipboard = new WslClipboard("fixture", (command, arguments, input) =>
        {
            calls++;
            Assert.Equal("fixture", command);
            Assert.Equal(WslClipboard.SetTextArguments, arguments);
            Assert.Equal(text, Encoding.UTF8.GetString(Convert.FromBase64String(input)));

            // Shadow the clipboard cmdlet in this child only. No real clipboard is accessed.
            const string fakeCommand = "function Set-Clipboard { param([AllowEmptyString()][string]$Value) [Console]::Out.Write([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Value))) }; ";
            string fakeArguments = arguments.Replace("-Command \"", "-Command \"" + fakeCommand, StringComparison.Ordinal);
            var result = ClipboardProcessRunner.ProcessText("pwsh", fakeArguments, input, timeout: 10000);
            Assert.Equal(0, result.exitCode);
            Assert.Equal(input, result.result);
            return result;
        });

        await clipboard.SetTextAsync(text);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task WslFailureDoesNotIncludeClipboardData()
    {
        const string data = "private clipboard fixture";
        var clipboard = new WslClipboard("fixture", (_, _, _) => (1, data));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => clipboard.SetTextAsync(data));
        Assert.DoesNotContain(data, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessRunnerDrainsBothPipesWhileSendingLargeInput()
    {
        string input = new('x', 200000);
        const string arguments = "-NoProfile -NonInteractive -Command \"[Console]::Error.Write('e' * 200000); [Console]::Out.Write([Console]::In.ReadToEnd())\"";
        var result = ClipboardProcessRunner.ProcessText("pwsh", arguments, input, timeout: 10000);
        Assert.Equal(0, result.exitCode);
        Assert.Equal(input, result.result);
    }

    [Fact]
    public void ProcessRunnerBoundsFailureDiagnostics()
    {
        const string arguments = "-NoProfile -NonInteractive -Command \"[Console]::Error.Write('e' * 200000); exit 3\"";
        var result = ClipboardProcessRunner.ProcessText("pwsh", arguments, timeout: 10000);
        Assert.Equal(3, result.exitCode);
        Assert.InRange(result.result.Length, 64 * 1024, 65 * 1024);
    }

    [Fact]
    public async Task InputDeadlineClosesPipeEvenWhenInheritedWriterCannotCancel()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var pipe = new StalledPipeFixture();
        Task write = ClipboardProcessRunner.WriteInputAsync(pipe, new string('x', 200000), deadline.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await write.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(pipe.Closed);
        Assert.False(pipe.WriteFinished);
        pipe.Complete();
    }

    [Fact]
    public void PublicRegistrationRejectsNullPlatform()
        => Assert.Throws<ArgumentNullException>(() => PlatformSupportExtensions.UseClipboard(null!, null!));

    [Fact]
    public async Task FactoryUsesAvaloniaWrapperAndPreservesOwnedDataLifecycle()
    {
        var platform = new OwnedClipboardFixture();
        IClipboard clipboard = PlatformSupportExtensions.CreateClipboard(platform);
        Assert.Equal("Avalonia.Input.Platform.Clipboard", clipboard.GetType().FullName);
        var data = new DataFixture();
        await clipboard.SetDataAsync(data);
        Assert.Same(data, platform.Data);
        Assert.Same(data, await clipboard.TryGetDataAsync());
        Assert.Same(data, await clipboard.TryGetInProcessDataAsync());
        await clipboard.FlushAsync();
        Assert.Equal(1, platform.Flushes);
        await clipboard.SetDataAsync(null);
        Assert.Equal(1, data.Disposals);
        Assert.Equal(1, platform.Clears);
        Assert.Null(await clipboard.TryGetInProcessDataAsync());

        var externalData = new DataFixture();
        await clipboard.SetDataAsync(externalData);
        platform.IsOwner = false;
        Assert.Null(await clipboard.TryGetInProcessDataAsync());
        Assert.Equal(0, externalData.Disposals);
        externalData.Dispose();
    }

    [Fact]
    public async Task FactoryDoesNotPretendUnownedClipboardRetainsData()
    {
        var platform = new ClipboardFixture();
        IClipboard clipboard = PlatformSupportExtensions.CreateClipboard(platform);
        using var data = new DataFixture();
        await clipboard.SetDataAsync(data);
        Assert.Same(data, await clipboard.TryGetDataAsync());
        Assert.Null(await clipboard.TryGetInProcessDataAsync());
        await clipboard.FlushAsync();
        await clipboard.ClearAsync();
        Assert.Equal(1, platform.Clears);
    }

    [Fact]
    public void ObjectiveCStringImportsExplicitlyUseUtf8()
    {
        var stringImports = typeof(MacClipboard).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.GetCustomAttribute<DllImportAttribute>() is not null)
            .SelectMany(method => method.GetParameters())
            .Where(parameter => parameter.ParameterType == typeof(string)).ToArray();
        Assert.Equal(3, stringImports.Length);
        Assert.All(stringImports, parameter =>
            Assert.Equal(UnmanagedType.LPUTF8Str, parameter.GetCustomAttribute<MarshalAsAttribute>()?.Value));
    }

    private sealed class DataFixture : IAsyncDataTransfer
    {
        public IReadOnlyList<DataFormat> Formats => [];
        public IReadOnlyList<IAsyncDataTransferItem> Items => [];
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }

    // Models a descendant retaining stdin without reading; cancellation cannot finish its OS write.
    private sealed class StalledPipeFixture : Stream
    {
        private readonly TaskCompletionSource _write = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Closed { get; private set; }
        public bool WriteFinished => _write.Task.IsCompleted;
        public void Complete() => _write.TrySetResult();
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => new(_write.Task);
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new InvalidOperationException("Cleanup must not flush a blocked pipe.");
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            Closed = true;
            base.Dispose(disposing);
        }
    }

    private class ClipboardFixture : IClipboardImpl
    {
        public IAsyncDataTransfer? Data { get; private set; }
        public int Clears { get; private set; }
        public Task SetDataAsync(IAsyncDataTransfer dataTransfer)
        {
            Data = dataTransfer;
            return Task.CompletedTask;
        }
        public Task<IAsyncDataTransfer?> TryGetDataAsync() => Task.FromResult(Data);
        public Task ClearAsync()
        {
            Clears++;
            Data = null;
            return Task.CompletedTask;
        }
    }

    private sealed class OwnedClipboardFixture : ClipboardFixture, IOwnedClipboardImpl, IFlushableClipboardImpl
    {
        public bool IsOwner { get; set; } = true;
        public int Flushes { get; private set; }
        public Task<bool> IsCurrentOwnerAsync() => Task.FromResult(IsOwner);
        public Task FlushAsync()
        {
            Flushes++;
            return Task.CompletedTask;
        }
    }
}
