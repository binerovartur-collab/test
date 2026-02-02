using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LazerNi.Services
{
    /// <summary>
    /// TCP Server для EzCad2 TCP/IP communication режима
    /// EzCad подключается как клиент, запрашивает код, сервер отвечает
    /// </summary>
    public class EzCadTcpServer : IDisposable
    {
        private readonly LoggingService _logger;
        private TcpListener? _listener;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _serverTask;

        public int Port { get; private set; }
        public bool IsRunning { get; private set; }

        // Callback для получения кода когда EzCad запрашивает
        public Func<string>? OnCodeRequested { get; set; }

        // Команда которую ожидаем от EzCad (по умолчанию из скриншота)
        public string ExpectedCommand { get; set; } = "TCP:Give me string";

        public EzCadTcpServer(LoggingService logger, int port = 1000)
        {
            _logger = logger;
            Port = port;
        }

        /// <summary>
        /// Запустить TCP сервер для EzCad
        /// </summary>
        public void Start()
        {
            if (IsRunning)
            {
                _logger.Warning("EzCad TCP Server уже запущен");
                return;
            }

            try
            {
                _listener = new TcpListener(IPAddress.Any, Port);
                _listener.Start();

                _cancellationTokenSource = new CancellationTokenSource();
                _serverTask = Task.Run(() => ServerLoopAsync(_cancellationTokenSource.Token));

                IsRunning = true;
                _logger.Info($"✓ EzCad TCP Server запущен на порту {Port}");
                _logger.Info($"   EzCad должен подключиться как клиент и отправить: '{ExpectedCommand}'");
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка запуска EzCad TCP Server: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Остановить TCP сервер
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;

            _logger.Info("Остановка EzCad TCP Server...");

            _cancellationTokenSource?.Cancel();
            _listener?.Stop();

            _serverTask?.Wait(TimeSpan.FromSeconds(5));

            IsRunning = false;
            _logger.Info("✓ EzCad TCP Server остановлен");
        }

        /// <summary>
        /// Основной цикл сервера - принимает подключения от EzCad
        /// </summary>
        private async Task ServerLoopAsync(CancellationToken cancellationToken)
        {
            _logger.Info("EzCad TCP Server: ожидание подключений...");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Ждем подключения от EzCad (таймаут 1 секунда для проверки cancellation)
                    if (_listener!.Pending())
                    {
                        var client = await _listener.AcceptTcpClientAsync();
                        _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
                    }
                    else
                    {
                        await Task.Delay(100, cancellationToken); // Небольшая пауза
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error($"EzCad TCP Server loop error: {ex.Message}");
                    await Task.Delay(1000, cancellationToken); // Пауза перед повтором
                }
            }

            _logger.Info("EzCad TCP Server loop завершен");
        }

        /// <summary>
        /// Обработка одного подключения от EzCad
        /// </summary>
        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            var clientEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
            _logger.Info($"📞 EzCad подключился: {clientEndpoint}");

            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    // Читаем команду от EzCad
                    byte[] buffer = new byte[1024];
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                    if (bytesRead == 0)
                    {
                        _logger.Warning("EzCad отключился без отправки команды");
                        return;
                    }

                    // Декодируем команду (ASCII, т.к. Unicode checkbox снят)
                    string command = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
                    _logger.Info($"📥 Получена команда от EzCad: '{command}'");

                    // Проверяем что это ожидаемая команда
                    if (command != ExpectedCommand)
                    {
                        _logger.Warning($"⚠️ Неожиданная команда! Ожидалось: '{ExpectedCommand}', получено: '{command}'");
                        // Всё равно отвечаем кодом
                    }

                    // Получаем код через callback
                    string code = OnCodeRequested?.Invoke() ?? "ERROR:NO_CODE";

                    if (code.StartsWith("ERROR:"))
                    {
                        _logger.Error($"Не удалось получить код: {code}");
                        // Отправляем пустой ответ или сообщение об ошибке
                        code = "";
                    }
                    else
                    {
                        _logger.Info($"📤 Отправляем код в EzCad: {code}");
                    }

                    // Отправляем код в EzCad (ASCII формат)
                    byte[] response = Encoding.ASCII.GetBytes(code);
                    await stream.WriteAsync(response, 0, response.Length, cancellationToken);
                    await stream.FlushAsync(cancellationToken);

                    _logger.Info($"✓ Код успешно отправлен в EzCad ({response.Length} байт)");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка обработки EzCad клиента: {ex.Message}");
            }
            finally
            {
                _logger.Debug($"EzCad клиент отключен: {clientEndpoint}");
            }
        }

        public void Dispose()
        {
            Stop();
            _cancellationTokenSource?.Dispose();
            _listener?.Stop();
        }
    }
}
