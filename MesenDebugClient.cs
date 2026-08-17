using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;

namespace McpServers.MesenCE;

public sealed class MesenDebugClient : IAsyncDisposable
{
    private const string PipeName = "mesen-debug-api";
    private const int ConnectTimeoutMilliseconds = 3000;

    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly object _stateLock = new();

    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private long _nextRequestId;
    private bool _disposed;

    public async Task<JsonElement> InvokeAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            StreamWriter writer = await GetWriterAsync(cancellationToken).ConfigureAwait(false);
            long id = Interlocked.Increment(ref _nextRequestId);
            var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = completion;

            try
            {
                string request = JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id,
                    method,
                    @params = parameters ?? new { }
                });

                await writer.WriteLineAsync(request.AsMemory(), cancellationToken).ConfigureAwait(false);
                return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (McpException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                ResetConnection();
                throw new McpException(
                    "Communication with MesenCE failed. Ensure MesenCE is running with --debugApi.",
                    exception);
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task<StreamWriter> GetWriterAsync(CancellationToken cancellationToken)
    {
        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            lock (_stateLock)
            {
                if (_pipe?.IsConnected == true && _writer is not null)
                {
                    return _writer;
                }
            }

            ResetConnection();

            var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            try
            {
                await pipe.ConnectAsync(ConnectTimeoutMilliseconds, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is TimeoutException or IOException)
            {
                pipe.Dispose();
                throw new McpException(
                    "Cannot connect to MesenCE. Start MesenCE with the --debugApi option and load a SNES ROM.",
                    exception);
            }

            var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };

            lock (_stateLock)
            {
                _pipe = pipe;
                _reader = reader;
                _writer = writer;
            }

            _ = ReceiveLoopAsync(pipe, reader);
            return writer;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private async Task ReceiveLoopAsync(NamedPipeClientStream pipe, StreamReader reader)
    {
        Exception? failure = null;

        try
        {
            while (true)
            {
                string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    throw new IOException("MesenCE closed the debug API connection.");
                }

                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;

                if (!root.TryGetProperty("id", out JsonElement idElement)
                    || idElement.ValueKind != JsonValueKind.Number
                    || !idElement.TryGetInt64(out long id)
                    || !_pending.TryRemove(id, out TaskCompletionSource<JsonElement>? completion))
                {
                    continue;
                }

                if (root.TryGetProperty("error", out JsonElement error))
                {
                    int code = error.TryGetProperty("code", out JsonElement codeElement)
                        ? codeElement.GetInt32()
                        : -32603;
                    string message = error.TryGetProperty("message", out JsonElement messageElement)
                        ? messageElement.GetString() ?? "Unknown MesenCE API error"
                        : "Unknown MesenCE API error";
                    completion.TrySetException(new McpException($"MesenCE API error {code}: {message}"));
                    continue;
                }

                if (root.TryGetProperty("result", out JsonElement result))
                {
                    completion.TrySetResult(result.Clone());
                }
                else
                {
                    completion.TrySetException(new McpException("MesenCE returned an invalid JSON-RPC response."));
                }
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            bool ownsConnection;
            lock (_stateLock)
            {
                ownsConnection = ReferenceEquals(_pipe, pipe);
            }

            if (ownsConnection)
            {
                ResetConnection();
                var transportError = new McpException(
                    "The MesenCE debug API connection was closed. The next tool call will reconnect.",
                    failure);

                foreach (TaskCompletionSource<JsonElement> completion in _pending.Values)
                {
                    completion.TrySetException(transportError);
                }
            }
        }
    }

    private void ResetConnection()
    {
        NamedPipeClientStream? pipe;
        StreamReader? reader;
        StreamWriter? writer;

        lock (_stateLock)
        {
            pipe = _pipe;
            reader = _reader;
            writer = _writer;
            _pipe = null;
            _reader = null;
            _writer = null;
        }

        writer?.Dispose();
        reader?.Dispose();
        pipe?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        ResetConnection();
        _connectionGate.Dispose();
        _requestGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
