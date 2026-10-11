using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using Island.Core.Progress;
using Island.Windows.Progress;

namespace Island.Windows.Tests.Progress;

public class NamedPipeProgressFeedTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private readonly string _pipeName = "DynamicIsland.Test." + Guid.NewGuid().ToString("N");
    private readonly NamedPipeProgressFeed _feed;

    public NamedPipeProgressFeedTests()
    {
        _feed = new NamedPipeProgressFeed(_pipeName);
    }

    public void Dispose() => _feed.Dispose();

    [Fact]
    public async Task Valid_lines_are_delivered_garbage_is_skipped_and_open_tasks_are_dropped_on_close()
    {
        var updates = new ConcurrentQueue<ProgressUpdate>();
        var twoUpdates = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dropped = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _feed.Updated += update =>
        {
            updates.Enqueue(update);
            if (updates.Count == 2) twoUpdates.TrySetResult();
        };
        _feed.Dropped += ids => dropped.TrySetResult(ids.OrderBy(id => id, StringComparer.Ordinal).ToArray());
        _feed.Start();

        using (NamedPipeClientStream client = await ConnectAsync())
        {
            await WriteLineAsync(client, Line("a"));
            await WriteLineAsync(client, "{not json");
            await WriteLineAsync(client, Line("b", percent: 20));
            await twoUpdates.Task.WaitAsync(Timeout);
        }

        string[] ids = await dropped.Task.WaitAsync(Timeout);
        Assert.Equal(new[] { "a", "b" }, ids);
        Assert.Equal(2, updates.Count);
        Assert.Equal(new[] { "a", "b" }, updates.Select(u => u.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Finished_tasks_are_not_reported_as_dropped()
    {
        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dropped = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _feed.Updated += update =>
        {
            if (update.Id == "done") updated.TrySetResult();
        };
        _feed.Dropped += ids => dropped.TrySetResult(ids.ToArray());
        _feed.Start();

        using (NamedPipeClientStream client = await ConnectAsync())
        {
            await WriteLineAsync(client, Line("open"));
            await WriteLineAsync(client, Line("done", state: "done", percent: 100));
            await updated.Task.WaitAsync(Timeout);
        }

        Assert.Equal(new[] { "open" }, await dropped.Task.WaitAsync(Timeout));
    }

    [Fact]
    public async Task Line_longer_than_the_limit_is_discarded_and_the_next_line_still_arrives()
    {
        var updated = new TaskCompletionSource<ProgressUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        _feed.Updated += update => updated.TrySetResult(update);
        _feed.Start();

        using (NamedPipeClientStream client = await ConnectAsync())
        {
            await WriteLineAsync(client, new string('x', ProgressMessageParser.MaxLineChars + 5000));
            await WriteLineAsync(client, Line("after"));

            ProgressUpdate update = await updated.Task.WaitAsync(Timeout);
            Assert.Equal("after", update.Id);
        }
    }

    private static string Line(string id, string state = "running", int percent = 10) =>
        "{\"v\":1,\"id\":\"" + id + "\",\"title\":\"Cortar\",\"state\":\"" + state + "\",\"percent\":" + percent + "}";

    private static async Task WriteLineAsync(NamedPipeClientStream client, string line)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
        await client.WriteAsync(bytes);
        await client.FlushAsync();
    }

    /// <summary>Connects once the feed's listener exists; the feed starts listening on its own task.</summary>
    private async Task<NamedPipeClientStream> ConnectAsync()
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            try
            {
                await client.ConnectAsync(200);
                return client;
            }
            catch (Exception ex) when ((ex is IOException or TimeoutException) && DateTime.UtcNow < deadline)
            {
                client.Dispose();
                await Task.Delay(20);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
    }
}
