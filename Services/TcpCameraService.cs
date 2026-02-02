using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LazerNi.Models;

namespace LazerNi.Services
{
    public class CameraDataReceivedEventArgs : EventArgs
    {
        public CameraData Data { get; }

        public CameraDataReceivedEventArgs(CameraData data)
        {
            Data = data;
        }
    }

    public class TcpCameraService
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly LoggingService _logger;
        private bool _isRunning;
        private CodeManagerService? _codeManager;

        public event EventHandler<CameraDataReceivedEventArgs>? DataReceived;

        public bool IsRunning => _isRunning;

        public TcpCameraService(LoggingService logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Set code manager for handling code requests
        /// </summary>
        public void SetCodeManager(CodeManagerService codeManager)
        {
            _codeManager = codeManager;
        }

        public Task StartAsync(string ipAddress, int port)
        {
            if (_isRunning)
            {
                _logger.Warning("TCP server is already running");
                return Task.CompletedTask;
            }

            try
            {
                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Parse(ipAddress), port);
                _listener.Start();
                _isRunning = true;

                _logger.Info($"TCP server started on {ipAddress}:{port}");

                // Accept clients in background
                _ = Task.Run(() => AcceptClientsAsync(_cts.Token), _cts.Token);

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to start TCP server: {ex.Message}");
                throw;
            }
        }

        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            try
            {
                _cts?.Cancel();
                _listener?.Stop();
                _isRunning = false;

                _logger.Info("TCP server stopped");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error stopping TCP server: {ex.Message}");
            }
        }

        private async Task AcceptClientsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                    _logger.Debug($"Client connected from {client.Client.RemoteEndPoint}");

                    // Handle client in separate task
                    _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error accepting client: {ex.Message}");
                    await Task.Delay(1000, cancellationToken); // Avoid tight loop on errors
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var buffer = new byte[1024];
                    var dataBuilder = new StringBuilder();

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                        if (bytesRead == 0)
                        {
                            // Connection closed
                            break;
                        }

                        var receivedData = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                        dataBuilder.Append(receivedData);

                        // Process complete messages (ending with #CR#LF or \r\n)
                        await ProcessReceivedDataAsync(dataBuilder, stream, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error handling client: {ex.Message}");
                }
                finally
                {
                    _logger.Debug("Client disconnected");
                }
            }
        }

        private async Task ProcessReceivedDataAsync(StringBuilder dataBuilder, NetworkStream stream, CancellationToken cancellationToken)
        {
            var data = dataBuilder.ToString();

            // Split by line endings (handles both #CR#LF and actual \r\n)
            var lines = data.Split(new[] { "\r\n", "#CR#LF" }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length == 0)
            {
                return;
            }

            // Process all complete lines except the last one (might be incomplete)
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                // Check if this is the last line and if original data doesn't end with delimiter
                bool isLastLine = (i == lines.Length - 1);
                bool endsWithDelimiter = data.EndsWith("\r\n") || data.EndsWith("#CR#LF");

                if (isLastLine && !endsWithDelimiter)
                {
                    // This line is incomplete, keep it for next batch
                    dataBuilder.Clear();
                    dataBuilder.Append(line);
                    break;
                }

                // Parse complete line and send response if needed
                await ProcessCommandAsync(line, stream, cancellationToken);
            }

            // Clear the builder if all lines were processed
            if (data.EndsWith("\r\n") || data.EndsWith("#CR#LF"))
            {
                dataBuilder.Clear();
            }
        }

        private async Task ProcessCommandAsync(string dataLine, NetworkStream stream, CancellationToken cancellationToken)
        {
            try
            {
                _logger.Debug($"Raw data received: [{dataLine}]");

                // Check if this is a command
                if (dataLine.Equals("NEXT_CODE", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleNextCodeCommandAsync(stream, cancellationToken);
                    return;
                }

                if (dataLine.Equals("STATUS", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleStatusCommandAsync(stream, cancellationToken);
                    return;
                }

                // Try to parse as camera data (format: X;Y;Angle;)
                var parts = dataLine.Split(';', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 3)
                {
                    var cameraData = new CameraData
                    {
                        X = double.Parse(parts[0], CultureInfo.InvariantCulture),
                        Y = double.Parse(parts[1], CultureInfo.InvariantCulture),
                        AngleDegrees = double.Parse(parts[2], CultureInfo.InvariantCulture),
                        RawData = dataLine,
                        Timestamp = DateTime.Now
                    };

                    _logger.Info($"Camera data received: {cameraData}");

                    // Raise event
                    DataReceived?.Invoke(this, new CameraDataReceivedEventArgs(cameraData));
                }
                else
                {
                    _logger.Warning($"Unknown command or invalid format: {dataLine}");
                }
            }
            catch (FormatException ex)
            {
                _logger.Error($"Failed to parse data: {dataLine} - {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error processing data: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle NEXT_CODE command - returns next available code
        /// </summary>
        private async Task HandleNextCodeCommandAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            try
            {
                if (_codeManager == null)
                {
                    _logger.Warning("NEXT_CODE command received but CodeManager is not set");
                    await SendResponseAsync(stream, "ERROR:NO_CODE_MANAGER\r\n", cancellationToken);
                    return;
                }

                var code = _codeManager.GetNextCode();
                if (code != null)
                {
                    var response = $"CODE:{code}\r\n";
                    await SendResponseAsync(stream, response, cancellationToken);
                    _logger.Info($"NEXT_CODE: {code}");
                }
                else
                {
                    await SendResponseAsync(stream, "ERROR:NO_CODES_AVAILABLE\r\n", cancellationToken);
                    _logger.Warning("NEXT_CODE: No codes available");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error handling NEXT_CODE command: {ex.Message}");
                await SendResponseAsync(stream, "ERROR:INTERNAL\r\n", cancellationToken);
            }
        }

        /// <summary>
        /// Handle STATUS command - returns code statistics
        /// </summary>
        private async Task HandleStatusCommandAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            try
            {
                if (_codeManager == null)
                {
                    _logger.Warning("STATUS command received but CodeManager is not set");
                    await SendResponseAsync(stream, "ERROR:NO_CODE_MANAGER\r\n", cancellationToken);
                    return;
                }

                var stats = _codeManager.GetStatistics();
                var response = $"AVAILABLE:{stats.Available}|ENGRAVED:{stats.Engraved}|CACHE:{stats.CacheSize}|DISPENSED:{stats.Dispensed}\r\n";
                await SendResponseAsync(stream, response, cancellationToken);
                _logger.Debug($"STATUS: {response.Trim()}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error handling STATUS command: {ex.Message}");
                await SendResponseAsync(stream, "ERROR:INTERNAL\r\n", cancellationToken);
            }
        }

        /// <summary>
        /// Send response to TCP client
        /// </summary>
        private async Task SendResponseAsync(NetworkStream stream, string response, CancellationToken cancellationToken)
        {
            try
            {
                var responseBytes = Encoding.ASCII.GetBytes(response);
                await stream.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error sending response: {ex.Message}");
            }
        }
    }
}
