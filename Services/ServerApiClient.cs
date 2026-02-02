using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LazerNi.Models;

namespace LazerNi.Services
{
    /// <summary>
    /// HTTP клиент для взаимодействия с LaserMarkingServer API
    /// </summary>
    public class ServerApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _clientId;
        private readonly LoggingService _logger;
        private bool _disposed;

        public bool IsConnected { get; private set; }
        public string ServerUrl { get; }

        public ServerApiClient(string serverUrl, string apiKey, string clientId, LoggingService logger)
        {
            // Убеждаемся, что URL заканчивается на '/' для корректной работы BaseAddress
            ServerUrl = serverUrl.TrimEnd('/');
            _clientId = clientId;
            _logger = logger;

            // Используем SocketsHttpHandler для лучшей совместимости
            var handler = new SocketsHttpHandler
            {
                // Отключаем автоматическое сжатие
                AutomaticDecompression = System.Net.DecompressionMethods.None,
                // Разрешаем автоматические редиректы
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,
                // Увеличиваем таймауты
                ConnectTimeout = TimeSpan.FromSeconds(15),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
                // Отключаем повторное использование соединений (может помочь)
                MaxConnectionsPerServer = 10,
                // ⭐ КРИТИЧЕСКИ ВАЖНО: отключаем системный прокси (NekoBox/Xray)
                UseProxy = false,
                Proxy = null
            };

            _httpClient = new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = new Uri(ServerUrl + "/"),  // Обязательно добавляем '/' в конце
                Timeout = TimeSpan.FromSeconds(30),  // Увеличиваем общий таймаут
                DefaultRequestVersion = new Version(1, 1)  // Принудительно HTTP/1.1
            };

            // НЕ добавляем никакие заголовки - минимальный запрос
            // (временно для диагностики)

            _logger.Info($"ServerApiClient initialized for {serverUrl}");
        }

        /// <summary>
        /// Проверить подключение к серверу
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var fullUrl = $"{_httpClient.BaseAddress}api/nomenclature";
                _logger.Info($"Testing connection to: {fullUrl}");

                // Создаём запрос вручную для лучшего контроля
                var request = new HttpRequestMessage(HttpMethod.Get, "api/nomenclature");
                request.Version = new Version(1, 1);  // Явно HTTP/1.1
                // НЕ добавляем никакие заголовки

                _logger.Info($"Request version: HTTP/{request.Version}");
                _logger.Info($"Request headers count: {request.Headers.Count()}");

                var response = await _httpClient.SendAsync(request);
                IsConnected = response.IsSuccessStatusCode;

                _logger.Info($"Response version: HTTP/{response.Version}");

                if (IsConnected)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.Info($"✓ Connected to server: {ServerUrl}");
                    _logger.Info($"   Response length: {content.Length} bytes");
                }
                else
                {
                    _logger.Error($"✗ Server returned error: {response.StatusCode}");
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.Error($"   Response: {content.Substring(0, Math.Min(200, content.Length))}");
                }

                return IsConnected;
            }
            catch (TaskCanceledException ex)
            {
                IsConnected = false;
                _logger.Error($"✗ Connection timeout: Request took longer than {_httpClient.Timeout.TotalSeconds}s");
                return false;
            }
            catch (HttpRequestException ex)
            {
                IsConnected = false;
                var errorMessage = ex.Message;
                if (ex.InnerException != null)
                {
                    errorMessage += $" | Inner: {ex.InnerException.Message}";
                    if (ex.InnerException.InnerException != null)
                    {
                        errorMessage += $" | InnerInner: {ex.InnerException.InnerException.Message}";
                    }
                }
                _logger.Error($"✗ HTTP request failed: {errorMessage}");
                _logger.Error($"   Server URL: {ServerUrl}");
                return false;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                var errorMessage = ex.Message;
                if (ex.InnerException != null)
                {
                    errorMessage += $" | Inner: {ex.InnerException.Message}";
                }
                _logger.Error($"✗ Connection failed: {errorMessage}");
                _logger.Error($"   Exception type: {ex.GetType().Name}");
                return false;
            }
        }

        /// <summary>
        /// Получить список номенклатуры с сервера
        /// </summary>
        public async Task<List<Product>> GetNomenclatureAsync()
        {
            try
            {
                _logger.Info("Запрос номенклатуры с сервера...");
                var response = await _httpClient.GetAsync("api/nomenclature");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                _logger.Info($"Получен JSON ответ: {json.Substring(0, Math.Min(500, json.Length))}...");

                var apiResponse = JsonSerializer.Deserialize<ApiResponse<List<Product>>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (apiResponse?.Success == true && apiResponse.Data != null)
                {
                    _logger.Info($"✓ Десериализовано {apiResponse.Data.Count} продуктов");

                    // Логируем первый продукт для отладки
                    if (apiResponse.Data.Count > 0)
                    {
                        var first = apiResponse.Data[0];
                        _logger.Info($"  Первый продукт: ID={first.Id}, Name='{first.Name}', GTIN='{first.Gtin}', PackageGTIN='{first.PackageGtin}'");
                    }

                    return apiResponse.Data;
                }

                _logger.Error($"API returned error: {apiResponse?.Message}");
                return new List<Product>();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get nomenclature: {ex.Message}");
                _logger.Error($"Stack trace: {ex.StackTrace}");
                return new List<Product>();
            }
        }

        /// <summary>
        /// Получить следующий код для указанного GTIN
        /// </summary>
        public async Task<string?> GetNextCodeAsync(string gtin)
        {
            try
            {
                var url = $"api/codes/next?gtin={gtin}&clientId={_clientId}";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.Error($"Failed to get next code: {response.StatusCode}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var apiResponse = JsonSerializer.Deserialize<ApiResponse<string>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (apiResponse?.Success == true && !string.IsNullOrEmpty(apiResponse.Data))
                {
                    _logger.Info($"✓ Code received from server: {apiResponse.Data}");
                    return apiResponse.Data;
                }

                _logger.Warning($"No available codes for GTIN {gtin}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error getting next code: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Отправить отчёт об использовании кода
        /// </summary>
        public async Task ReportCodeUsedAsync(string code, string gtin, CameraData? cameraData)
        {
            try
            {
                var request = new
                {
                    code,
                    gtin,
                    clientId = _clientId,
                    status = "USED",
                    cameraData = cameraData != null ? new
                    {
                        x = cameraData.X,
                        y = cameraData.Y,
                        angle = cameraData.AngleDegrees
                    } : null,
                    markedAt = DateTime.UtcNow
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(request),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.PostAsync("api/codes/status", content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.Info($"✓ Code status reported: {code}");
                }
                else
                {
                    _logger.Error($"Failed to report code status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error reporting code status: {ex.Message} (will retry later)");
            }
        }

        /// <summary>
        /// Отправить heartbeat на сервер
        /// </summary>
        public async Task SendHeartbeatAsync(string? currentGtin, int codesUsedToday, int cacheSize, double avgSpeedPerMinute)
        {
            try
            {
                var request = new
                {
                    clientId = _clientId,
                    currentGtin,
                    codesUsedToday,
                    cacheSize,
                    avgSpeedPerMinute
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(request),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.PostAsync("api/client/heartbeat", content);

                if (response.IsSuccessStatusCode)
                {
                    // Heartbeat успешен (не логируем каждый раз для снижения шума)
                }
                else
                {
                    _logger.Warning($"Heartbeat failed: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                // Тихий fail для heartbeat (не критично)
            }
        }

        /// <summary>
        /// Получить статистику кодов
        /// </summary>
        public async Task<Dictionary<string, long>?> GetStatisticsAsync(string gtin)
        {
            try
            {
                var url = $"api/codes/statistics?gtin={gtin}";
                // Не логируем успешные запросы (вызывается каждые 1-2 секунды)

                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.Error($"❌ Сервер вернул ошибку: {errorContent.Substring(0, Math.Min(200, errorContent.Length))}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();

                var apiResponse = JsonSerializer.Deserialize<ApiResponse<Dictionary<string, long>>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (apiResponse?.Success == true && apiResponse.Data != null)
                {
                    // Успешная десериализация - не логируем (периодический запрос)
                    return apiResponse.Data;
                }
                else
                {
                    _logger.Error($"❌ Десериализация вернула Success={apiResponse?.Success}, Data={apiResponse?.Data != null}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"❌ Ошибка получения статистики: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Зарезервировать коды для кэша (массовый запрос)
        /// Статус кодов на сервере: TRANSFERRED → RESERVED
        /// Зарезервированные коды НИКОГДА не возвращаются обратно в пул
        /// </summary>
        /// <param name="gtin">GTIN продукта</param>
        /// <param name="count">Количество кодов для резервации (10-20)</param>
        /// <returns>Список зарезервированных кодов</returns>
        public async Task<List<string>?> ReserveCodesAsync(string gtin, int count = 20)
        {
            try
            {
                var url = $"api/codes/reserve?gtin={gtin}&count={count}&clientId={_clientId}";
                _logger.Info($"→ Резервация кодов: POST {url}");

                var response = await _httpClient.PostAsync(url, null);  // POST без body
                _logger.Info($"← Ответ сервера: HTTP {(int)response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.Error($"❌ Сервер вернул ошибку: {errorContent}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                _logger.Info($"← JSON ответ: {json.Substring(0, Math.Min(300, json.Length))}...");

                var apiResponse = JsonSerializer.Deserialize<ApiResponse<List<string>>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (apiResponse?.Success == true && apiResponse.Data != null)
                {
                    _logger.Info($"✓ Зарезервировано {apiResponse.Data.Count} кодов с сервера");
                    _logger.Info($"   ⚠️ Эти коды теперь RESERVED на сервере (не возвращаются!)");
                    return apiResponse.Data;
                }
                else
                {
                    _logger.Warning($"⚠️ Сервер не вернул коды: {apiResponse?.Message}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"❌ Ошибка резервации кодов: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient?.Dispose();
                _disposed = true;
            }
        }

        // Вспомогательные классы для десериализации
        private class ApiResponse<T>
        {
            public bool Success { get; set; }
            public string? Message { get; set; }
            public T? Data { get; set; }
        }
    }
}
