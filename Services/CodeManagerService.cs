using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LazerNi.Models;

namespace LazerNi.Services
{
    /// <summary>
    /// Service for managing codes with caching and high-speed access
    /// Thread-safe with concurrent queue for fast code retrieval
    /// </summary>
    public class CodeManagerService : IDisposable
    {
        private readonly DatabaseService _database;
        private readonly LoggingService _logger;
        private readonly ServerApiClient? _serverApiClient; // NEW: Optional server connection

        // Cache of pre-loaded codes for instant access
        private readonly ConcurrentQueue<string> _codeCache = new ConcurrentQueue<string>();

        // Cache size settings
        private const int MIN_CACHE_SIZE = 10; // Minimum codes in cache before refill
        private const int MAX_CACHE_SIZE = 20; // Maximum codes to keep in cache

        // Background refill synchronization
        private readonly SemaphoreSlim _refillSemaphore = new SemaphoreSlim(1, 1);

        // Product change synchronization (CRITICAL: prevents race condition during product switch)
        private readonly SemaphoreSlim _productChangeLock = new SemaphoreSlim(1, 1);

        // Statistics
        private long _totalCodesDispensed = 0;
        private readonly object _statsLock = new object();

        // NEW: Server mode tracking
        private string? _currentGtin; // Current product GTIN for server requests
        public bool IsServerMode => _serverApiClient != null && _serverApiClient.IsConnected;
        public int CacheSize => _codeCache.Count;
        public long TotalDispensed => _totalCodesDispensed;

        public CodeManagerService(DatabaseService database, LoggingService logger, ServerApiClient? serverApiClient = null)
        {
            _database = database;
            _logger = logger;
            _serverApiClient = serverApiClient;

            // ⭐ ИСПРАВЛЕНИЕ: Не заполняем кэш в конструкторе
            // Кэш заполняется после выбора продукта через SetCurrentGtinAsync()
            // Причина: в конструкторе ещё нет _currentGtin
        }

        /// <summary>
        /// Set current product GTIN and pre-fill cache
        /// THREAD-SAFE: Uses lock to prevent concurrent product changes
        /// </summary>
        public async Task SetCurrentGtinAsync(string gtin)
        {
            await _productChangeLock.WaitAsync();
            try
            {
                _currentGtin = gtin;
                _logger.Info($"✓ GTIN установлен: {gtin}");

                // ⭐ НОВОЕ: Очистить старый кэш при смене продукта
                ClearCache();

                // ⭐ НОВОЕ: Предзагрузить коды в кэш
                if (IsServerMode)
                {
                    _logger.Info($"Предзагрузка кэша для GTIN: {gtin}");
                    await RefillCacheFromServerAsync();
                }
                else
                {
                    await RefillCacheAsync();  // Локальный режим (из SQLite)
                }
            }
            finally
            {
                _productChangeLock.Release();
            }
        }

        /// <summary>
        /// Get next code from cache (< 1ms, no DB/HTTP access)
        /// Works in both server and local mode
        /// THREAD-SAFE: Waits if product change is in progress
        /// </summary>
        public string? GetNextCode()
        {
            // Wait if product change is in progress (prevents wrong product code)
            _productChangeLock.Wait();
            try
            {
                // ⭐ НОВАЯ ЛОГИКА: И в серверном, и в локальном режиме используем кэш!

                // Try to get from cache first (fast path)
                if (_codeCache.TryDequeue(out string? code))
                {
                    lock (_statsLock)
                    {
                        _totalCodesDispensed++;
                    }

                    _logger.Debug($"Code dispensed from cache: {code} (cache size: {_codeCache.Count})");

                    // ⭐ Trigger background refill if cache is getting low
                    if (_codeCache.Count < MIN_CACHE_SIZE)
                    {
                        _logger.Info($"⚠️ Кэш < {MIN_CACHE_SIZE}, запускаем автоматическую догрузку...");

                        if (IsServerMode)
                        {
                            _ = Task.Run(() => RefillCacheFromServerAsync());  // Серверный режим
                        }
                        else
                        {
                            _ = Task.Run(() => RefillCacheAsync());  // Локальный режим
                        }
                    }

                    return code;
                }

                // ❌ Cache is empty - critical error!
                _logger.Error("❌ КЭШ ПУСТ! Невозможно выдать код!");
                _logger.Error("   Возможные причины:");
                _logger.Error("   1. Не выбран продукт (GTIN не установлен)");
                _logger.Error("   2. На сервере закончились коды");
                _logger.Error("   3. Ошибка сети (не удалось запросить коды)");

                return null;
            }
            finally
            {
                _productChangeLock.Release();
            }
        }

        /// <summary>
        /// Mark code as used (engraved)
        /// This operation is async and doesn't block
        /// </summary>
        public void MarkCodeAsUsed(string code)
        {
            // Run in background to avoid blocking
            _ = Task.Run(() =>
            {
                try
                {
                    _database.MarkCodeAsEngraved(code);
                    _logger.Debug($"Code marked as engraved: {code}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to mark code as engraved: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Refill cache from database (background operation)
        /// Thread-safe with semaphore to prevent multiple simultaneous refills
        /// </summary>
        private async Task RefillCacheAsync()
        {
            // Only one refill at a time
            if (!await _refillSemaphore.WaitAsync(0))
            {
                _logger.Debug("Cache refill already in progress, skipping");
                return;
            }

            try
            {
                int currentSize = _codeCache.Count;
                int neededCodes = MAX_CACHE_SIZE - currentSize;

                if (neededCodes <= 0)
                {
                    _logger.Debug($"Cache is full ({currentSize} codes), no refill needed");
                    return;
                }

                _logger.Debug($"Refilling cache: current={currentSize}, loading={neededCodes}");

                // Load codes from database
                await Task.Run(() =>
                {
                    // Step 1: Fetch codes from DB (status=TRANSFERRED)
                    var codesToCache = new System.Collections.Generic.List<string>();
                    int loaded = 0;

                    for (int i = 0; i < neededCodes; i++)
                    {
                        var code = _database.GetNextAvailableCode();
                        if (code == null)
                        {
                            _logger.Warning($"Database has no more codes (loaded {loaded}/{neededCodes})");
                            break;
                        }
                        codesToCache.Add(code.Code);
                    }

                    if (codesToCache.Count == 0)
                    {
                        _logger.Debug("No available codes to refill cache");
                        return;
                    }

                    // Step 2: Mark codes as GRAVING in DB (atomic transaction)
                    try
                    {
                        int marked = _database.MarkCodesAsGraving(codesToCache);
                        if (marked != codesToCache.Count)
                        {
                            _logger.Warning($"Marked {marked}/{codesToCache.Count} codes as GRAVING (some may have been already processed)");
                        }
                        else
                        {
                            _logger.Debug($"Marked {marked} codes as GRAVING, adding to cache");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"CRITICAL: Failed to mark codes as GRAVING: {ex.Message}");
                        // Don't add codes to cache if we couldn't mark them!
                        return;
                    }

                    // Step 3: Add to cache (only codes that were successfully marked)
                    foreach (var code in codesToCache)
                    {
                        _codeCache.Enqueue(code);
                        loaded++;
                    }

                    if (loaded > 0)
                    {
                        _logger.Info($"Cache refilled: +{loaded} codes (total: {_codeCache.Count})");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to refill cache: {ex.Message}");
            }
            finally
            {
                _refillSemaphore.Release();
            }
        }

        /// <summary>
        /// Refill cache from server (reserve codes)
        /// Thread-safe with semaphore to prevent multiple simultaneous refills
        /// </summary>
        private async Task RefillCacheFromServerAsync()
        {
            if (!await _refillSemaphore.WaitAsync(0))
            {
                _logger.Debug("Cache refill already in progress, skipping");
                return;
            }

            try
            {
                int currentSize = _codeCache.Count;
                int neededCodes = MAX_CACHE_SIZE - currentSize;

                if (neededCodes <= 0)
                {
                    _logger.Debug($"Cache is full ({currentSize} codes), no refill needed");
                    return;
                }

                if (string.IsNullOrEmpty(_currentGtin))
                {
                    _logger.Error("Cannot refill cache: GTIN not set");
                    return;
                }

                _logger.Debug($"Запрос резервации: {neededCodes} кодов для GTIN {_currentGtin}");

                // ⭐ КЛЮЧЕВОЙ МОМЕНТ: Запрос на сервер РЕЗЕРВИРУЕТ коды (статус → RESERVED)
                var reservedCodes = await _serverApiClient!.ReserveCodesAsync(_currentGtin!, neededCodes);

                if (reservedCodes == null || reservedCodes.Count == 0)
                {
                    _logger.Warning($"❌ Сервер не вернул коды (возможно закончились)");
                    return;
                }

                // ⭐ Добавить в кэш
                foreach (var code in reservedCodes)
                {
                    _codeCache.Enqueue(code);
                }

                _logger.Debug($"Кэш пополнен: +{reservedCodes.Count} кодов (всего: {_codeCache.Count})");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to refill cache from server: {ex.Message}");
            }
            finally
            {
                _refillSemaphore.Release();
            }
        }

        /// <summary>
        /// Load codes from file and refill cache
        /// clearExisting: if true, removes all old codes before loading new ones (default: true)
        /// </summary>
        public async Task<int> LoadCodesFromFileAsync(string filePath, bool clearExisting = true)
        {
            try
            {
                if (clearExisting)
                {
                    _logger.Info("Clearing all existing codes from database...");

                    // Clear database first
                    await Task.Run(() => _database.ClearAllCodes());

                    // Clear cache
                    ClearCache();

                    // Reset statistics
                    lock (_statsLock)
                    {
                        _totalCodesDispensed = 0;
                    }

                    _logger.Info("Database and cache cleared, ready for new codes");
                }

                int loaded = await Task.Run(() => _database.LoadCodesFromFile(filePath));

                if (loaded > 0)
                {
                    // Refill cache with new codes
                    await RefillCacheAsync();

                    _logger.Info($"Loaded {loaded} codes from file, cache refilled");
                }

                return loaded;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load codes from file: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Clear all codes from database and cache
        /// </summary>
        public void ClearAllCodes()
        {
            try
            {
                ClearCache();
                _database.ClearAllCodes();

                lock (_statsLock)
                {
                    _totalCodesDispensed = 0;
                }

                _logger.Info("All codes cleared from database and cache");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to clear codes: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear code cache (on product change, STOP button, app shutdown)
        /// Codes in cache remain RESERVED on server (never returned!)
        /// </summary>
        public void ClearCache()
        {
            int droppedCodes = _codeCache.Count;

            while (_codeCache.TryDequeue(out _)) { }

            if (droppedCodes > 0)
            {
                _logger.Warning($"⚠️ Кэш очищен: {droppedCodes} кодов СБРОШЕНЫ");
                if (IsServerMode)
                {
                    _logger.Warning($"   Эти коды остаются RESERVED на сервере (не возвращаются!)");
                }
            }
            else
            {
                _logger.Debug("Code cache cleared (was empty)");
            }
        }

        /// <summary>
        /// Get statistics
        /// </summary>
        public (int Available, int Engraved, int Total, int CacheSize, long Dispensed) GetStatistics()
        {
            var (available, engraved, total) = _database.GetCodesCount();

            lock (_statsLock)
            {
                return (available, engraved, total, _codeCache.Count, _totalCodesDispensed);
            }
        }

        /// <summary>
        /// Get detailed statistics including GRAVING status (synchronous, local DB only)
        /// </summary>
        public (int Transferred, int Graving, int Engraved, int Total, int CacheSize, long Dispensed) GetDetailedStatistics()
        {
            var (transferred, graving, engraved, total) = _database.GetCodesCountByStatus();

            lock (_statsLock)
            {
                return (transferred, graving, engraved, total, _codeCache.Count, _totalCodesDispensed);
            }
        }

        /// <summary>
        /// Get statistics in async mode (supports server/local mode)
        /// In server mode: fetches statistics from server API
        /// In local mode: reads from local SQLite database
        /// </summary>
        public async Task<CodeStatistics> GetStatisticsAsync()
        {
            if (IsServerMode && !string.IsNullOrEmpty(_currentGtin))
            {
                // SERVER MODE: Request statistics from server
                try
                {
                    // Не логируем успешные запросы (вызывается каждые 1-2 секунды)
                    var serverStats = await _serverApiClient!.GetStatisticsAsync(_currentGtin);

                    if (serverStats != null)
                    {
                        return new CodeStatistics
                        {
                            Transferred = (int)(serverStats.ContainsKey("available") ? serverStats["available"] : 0),
                            Reserved = (int)(serverStats.ContainsKey("reserved") ? serverStats["reserved"] : 0),
                            Used = (int)(serverStats.ContainsKey("used") ? serverStats["used"] : 0),
                            Total = (int)(serverStats.ContainsKey("total") ? serverStats["total"] : 0),
                            Expired = (int)(serverStats.ContainsKey("expired") ? serverStats["expired"] : 0),
                            CacheSize = _codeCache.Count,
                            Dispensed = _totalCodesDispensed
                        };
                    }
                    else
                    {
                        _logger.Warning("⚠️ Сервер вернул NULL статистику, используем локальную БД");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"❌ Ошибка получения статистики с сервера: {ex.Message}");
                }
            }

            // LOCAL MODE or server error: use local database
            var (transferred, graving, engraved, total) = _database.GetCodesCountByStatus();

            return new CodeStatistics
            {
                Transferred = transferred,
                Graving = graving,
                Engraved = engraved,
                Total = total,
                CacheSize = _codeCache.Count,
                Dispensed = _totalCodesDispensed
            };
        }

        /// <summary>
        /// Check if cache needs refill and trigger it
        /// Can be called periodically from UI
        /// </summary>
        public void CheckAndRefillCache()
        {
            if (_codeCache.Count < MIN_CACHE_SIZE)
            {
                _ = Task.Run(() => RefillCacheAsync());
            }
        }

        /// <summary>
        /// Get GTIN of currently selected product
        /// </summary>
        private string? GetCurrentProductGtin()
        {
            try
            {
                var productIdStr = _database.GetConfigValue("selected_product_id");
                if (int.TryParse(productIdStr, out var productId))
                {
                    var product = _database.GetProductById(productId);
                    return product?.Gtin;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error getting current product GTIN: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Report code as used to server (async, non-blocking)
        /// </summary>
        public async Task ReportCodeUsedAsync(string code, CameraData? cameraData)
        {
            if (_serverApiClient != null && _serverApiClient.IsConnected)
            {
                var gtin = GetCurrentProductGtin();
                if (!string.IsNullOrEmpty(gtin))
                {
                    try
                    {
                        await _serverApiClient.ReportCodeUsedAsync(code, gtin, cameraData);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to report code to server: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Проверить доступность кодов на сервере для указанного GTIN
        /// </summary>
        public async Task<int> LoadCodesFromServerAsync(string gtin)
        {
            if (_serverApiClient == null || !_serverApiClient.IsConnected)
            {
                _logger.Warning("Сервер недоступен для загрузки кодов");
                return 0;
            }

            try
            {
                _logger.Info($"Проверка доступности кодов на сервере для GTIN: {gtin}");

                // Запрашиваем статистику чтобы узнать сколько кодов доступно
                var stats = await _serverApiClient.GetStatisticsAsync(gtin);

                // ⭐ НОВОЕ: Проверка на null
                if (stats == null)
                {
                    _logger.Error("❌ Сервер вернул NULL статистику!");
                    _logger.Error($"   Запрошен GTIN: {gtin}");
                    return 0;
                }

                // ⭐ НОВОЕ: Детальное логирование всех ключей
                _logger.Info($"✓ Сервер вернул словарь с ключами: {string.Join(", ", stats.Keys)}");
                foreach (var kvp in stats)
                {
                    _logger.Info($"   - {kvp.Key}: {kvp.Value}");
                }

                long available = stats.ContainsKey("available") ? stats["available"] : 0;
                long total = stats.ContainsKey("total") ? stats["total"] : 0;

                _logger.Info($"Статистика: доступно {available} кодов, всего {total}");

                if (available == 0)
                {
                    _logger.Warning($"❌ На сервере нет доступных кодов для GTIN: {gtin}");
                    _logger.Warning($"   Всего записей: {total}");
                    _logger.Warning($"   Проверьте статус кодов в БД сервера");
                    return 0;
                }

                _logger.Info($"✓ На сервере доступно {available} кодов для GTIN: {gtin}");
                _logger.Info("  Коды будут запрашиваться по мере необходимости при маркировке");

                return (int)available;
            }
            catch (Exception ex)
            {
                _logger.Error($"❌ Ошибка проверки кодов на сервере: {ex.Message}");
                _logger.Error($"   Stack trace: {ex.StackTrace}");
                return 0;
            }
        }

        public void Dispose()
        {
            _refillSemaphore?.Dispose();
            _productChangeLock?.Dispose();
        }
    }

    /// <summary>
    /// Code statistics data model
    /// </summary>
    public class CodeStatistics
    {
        // Local mode fields
        public int Transferred { get; set; }  // Available codes in local DB
        public int Graving { get; set; }      // Codes in cache/being processed
        public int Engraved { get; set; }     // Already marked codes

        // Server mode fields (only populated when connected to server)
        public int Reserved { get; set; }     // Reserved codes on server
        public int Used { get; set; }         // Used codes on server
        public int Expired { get; set; }      // Expired codes on server

        // Common fields
        public int Total { get; set; }        // Total codes
        public int CacheSize { get; set; }    // Current cache size
        public long Dispensed { get; set; }   // Total codes dispensed
    }
}
