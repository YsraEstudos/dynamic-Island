using System.Collections.Concurrent;
using Island.Windows.Focus;

namespace Island.Windows.Tests;

public sealed class BrowserTitleReaderTests
{
    [Fact]
    public async Task Queue_ReturnsWhileAccessibilityProviderIsBlocked()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new BrowserTitleReader(_ =>
        {
            started.SetResult();
            release.Wait(TimeSpan.FromSeconds(5));
            return "Home / X";
        }, (_, title) => delivered.SetResult(title));

        try
        {
            await Task.Run(() => reader.Queue((IntPtr)1, null)).WaitAsync(TimeSpan.FromSeconds(3));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(delivered.Task.IsCompleted);
            release.Set();
            Assert.Equal("Home / X", await delivered.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        }
        finally { release.Set(); reader.Cancel(); }
    }

    [Fact]
    public async Task Queue_WhileReading_KeepsOnlyLatestWindowAndDiscardsOldResult()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var latest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = new ConcurrentQueue<IntPtr>();
        var results = new ConcurrentQueue<string?>();
        var reader = new BrowserTitleReader(hwnd =>
        {
            reads.Enqueue(hwnd);
            if (hwnd == (IntPtr)1)
            {
                started.SetResult();
                release.Wait(TimeSpan.FromSeconds(5));
                return "Home / X";
            }
            return "Gmail";
        }, (hwnd, title) =>
        {
            results.Enqueue(title);
            if (hwnd == (IntPtr)3) latest.TrySetResult();
        });

        try
        {
            reader.Queue((IntPtr)1, null);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            reader.Queue((IntPtr)2, null);
            reader.Queue((IntPtr)3, null);
            release.Set();
            await latest.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(new[] { (IntPtr)1, (IntPtr)3 }, reads.ToArray());
            Assert.Equal(new[] { "Gmail" }, results.ToArray());
        }
        finally { release.Set(); reader.Cancel(); }
    }

    [Fact]
    public async Task Cancel_DropsResultAlreadyPostedToOwnerThread()
    {
        var context = new HeldContext();
        var results = new List<string?>();
        var reader = new BrowserTitleReader(_ => "Home / X", (_, title) => results.Add(title));

        reader.Queue((IntPtr)1, context);
        await context.Posted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        reader.Cancel();
        context.Deliver();

        Assert.Empty(results);
    }

    private sealed class HeldContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<Action> _callbacks = new();
        public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            _callbacks.Enqueue(() => callback(state));
            Posted.TrySetResult();
        }

        public void Deliver()
        {
            while (_callbacks.TryDequeue(out var callback)) callback();
        }
    }
}
