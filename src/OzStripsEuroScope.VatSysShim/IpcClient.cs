using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace OzStripsEuroScope.VatSysShim
{
    public sealed class IpcClient : IDisposable
    {
        private readonly string _pipeName;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _writerLock = new object();
        private StreamWriter? _writer;
        private Task? _worker;

        public IpcClient(string pipeName)
        {
            _pipeName = pipeName;
        }

        public event EventHandler<string>? LineReceived;

        public event EventHandler<string>? StatusChanged;

        public bool IsConnected { get; private set; }

        public void Start()
        {
            if (string.IsNullOrWhiteSpace(_pipeName))
            {
                StatusChanged?.Invoke(this, "Standalone mode");
                return;
            }

            _worker = Task.Run(RunAsync);
        }

        public void SendCommand(string command, string callsign, string value)
        {
            var json = JsonConvert.SerializeObject(new
            {
                command,
                callsign,
                value,
            });

            lock (_writerLock)
            {
                _writer?.WriteLine(json);
            }
        }

        private async Task RunAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    StatusChanged?.Invoke(this, "Connecting to EuroScope");

                    using (var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                    {
                        await Task.Run(() => pipe.Connect(3000), _cts.Token).ConfigureAwait(false);

                        IsConnected = true;
                        StatusChanged?.Invoke(this, "Connected to EuroScope");

                        using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true))
                        using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
                        {
                            lock (_writerLock)
                            {
                                _writer = writer;
                            }

                            while (!_cts.IsCancellationRequested && pipe.IsConnected)
                            {
                                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                                if (line == null)
                                {
                                    break;
                                }

                                LineReceived?.Invoke(this, line);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, "Disconnected: " + ex.Message);
                }
                finally
                {
                    lock (_writerLock)
                    {
                        _writer = null;
                    }

                    IsConnected = false;
                }

                try
                {
                    await Task.Delay(1000, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();

            try
            {
                _worker?.Wait(1000);
            }
            catch
            {
            }

            _cts.Dispose();
        }
    }
}
