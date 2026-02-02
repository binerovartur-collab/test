using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;

namespace LazerNi.Services
{
    /// <summary>
    /// Класс-обертка для работы с MarkEzd.dll (EzCad2 SDK)
    /// Реализует алгоритм коррекции позиции и угла для конвейера с машинным зрением
    ///
    /// Алгоритм обработки данных от камеры:
    /// 1. Сброс: LoadEzdFile - возврат шаблона в исходное состояние
    /// 2. Расчет центра: GetEntSize - вычисление геометрического центра объекта
    /// 3. Вращение: RotateEnt - поворот вокруг своего центра (угол в РАДИАНАХ!)
    /// 4. Смещение: MoveEnt - относительный сдвиг объекта
    /// 5. Взвод: MarkFlyByStartSignal - ожидание сигнала датчика конвейера
    /// </summary>
    public class MarkEzdDll
    {
        private readonly LoggingService _logger;
        private string _templatePath = string.Empty;
        private string _entityName = "Code";
        private bool _isInitialized = false;
        private int _operationCounter = 0; // Счетчик операций для отслеживания
        private bool _useConveyorSensor = false; // Wait for IN8/IN9 signal or mark immediately

        #region P/Invoke declarations (строго по документации PDF)

        /// <summary>
        /// Загрузка .ezd файла шаблона (PDF стр. 7)
        /// Очищает текущую базу данных и загружает новый шаблон
        /// </summary>
        [DllImport("MarkEzd", EntryPoint = "lmc1_LoadEzdFile", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private static extern int lmc1_LoadEzdFile(string strFileName);

        /// <summary>
        /// Получение размеров и границ объекта (PDF стр. 8)
        /// Используется для вычисления геометрического центра объекта
        /// </summary>
        [DllImport("MarkEzd", EntryPoint = "lmc1_GetEntSize", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private static extern int lmc1_GetEntSize(
            string strEntName,
            ref double dMinx,
            ref double dMiny,
            ref double dMaxx,
            ref double dMaxy,
            ref double dZ);

        /// <summary>
        /// Поворот объекта вокруг заданного центра (PDF стр. 9)
        /// ВАЖНО: Угол задается в РАДИАНАХ!
        /// </summary>
        [DllImport("MarkEzd", EntryPoint = "lmc1_RotateEnt", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private static extern int lmc1_RotateEnt(
            string pEntName,
            double dCenx,
            double dCeny,
            double dAngle);

        /// <summary>
        /// Перемещение объекта на заданное смещение (PDF стр. 8)
        /// Относительное смещение от текущей позиции
        /// </summary>
        [DllImport("MarkEzd", EntryPoint = "lmc1_MoveEnt", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private static extern int lmc1_MoveEnt(
            string strEntName,
            double dMovex,
            double dMovey);

        /// <summary>
        /// Маркировка по сигналу датчика конвейера (PDF стр. 4)
        /// Ожидает внешний сигнал (IN8/IN9) перед началом маркировки
        /// </summary>
        [DllImport("MarkEzd", EntryPoint = "lmc1_MarkFlyByStartSignal", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private static extern int lmc1_MarkFlyByStartSignal();

        /// <summary>
        /// Обычная маркировка (без ожидания сигнала)
        /// </summary>
        [DllImport("MarkEzd", EntryPoint = "lmc1_Mark", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private static extern int lmc1_Mark(bool bFlyMark);

        /// <summary>
        /// Превью красным лучом (безопасно для тестирования)
        /// </summary>
        [DllImport("MarkEzd", EntryPoint = "lmc1_RedLightMark", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private static extern int lmc1_RedLightMark();

        /// <summary>
        /// Остановка маркировки
        /// </summary>
        [DllImport("MarkEzd", EntryPoint = "lmc1_StopMark", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private static extern int lmc1_StopMark();

        /// <summary>
        /// Проверка, идет ли сейчас маркировка
        /// </summary>
        [DllImport("MarkEzd", EntryPoint = "lmc1_IsMarking", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private static extern bool lmc1_IsMarking();

        /// <summary>
        /// Alternative rotation method - rotates the entire work area instead of individual entities
        /// This is a void function, doesn't return error codes!
        /// </summary>
        [DllImport("MarkEzd", EntryPoint = "lmc1_SetRotateMoveParam", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private static extern void lmc1_SetRotateMoveParam(
            double moveX,
            double moveY,
            double centerX,
            double centerY,
            double rotateAngle);

        #endregion

        #region Error codes

        private static string GetErrorText(int errorCode)
        {
            return errorCode switch
            {
                0 => "Success",
                1 => "EZCAD is already running",
                2 => "EZCAD.CFG not found",
                3 => "Failed to open LMC board",
                4 => "No LMC board found",
                5 => "LMC version mismatch",
                6 => "MarkCfg not found in Plug folder",
                7 => "Error signal",
                8 => "User stopped",
                9 => "Unknown error",
                10 => "Timeout",
                11 => "Not initialized",
                12 => "Read file error",
                13 => "Full windows",
                14 => "Font not found",
                15 => "Pen error",
                16 => "Object is not text",
                17 => "Save file failed",
                18 => "Object not found",
                19 => "Invalid state for command",
                20 => "Invalid parameter",
                _ => $"Unknown error code: {errorCode}"
            };
        }

        #endregion

        public MarkEzdDll(LoggingService logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Настройка параметров для работы
        /// </summary>
        /// <param name="templatePath">Полный путь к .ezd файлу шаблона</param>
        /// <param name="entityName">Имя объекта в шаблоне (по умолчанию "Code")</param>
        /// <param name="useConveyorSensor">Ожидать сигнал от датчика конвейера (IN8/IN9) или маркировать сразу</param>
        public void Configure(string templatePath, string entityName = "Code", bool useConveyorSensor = false)
        {
            _templatePath = templatePath;
            _entityName = entityName;
            _useConveyorSensor = useConveyorSensor;
            _isInitialized = true;
            _logger.Info($"MarkEzdDll configured: Template={templatePath}, Entity={entityName}, ConveyorSensor={useConveyorSensor}");
        }

        /// <summary>
        /// Загрузка шаблона .ezd файла (PDF стр. 7)
        /// Сбрасывает все трансформации объектов к исходному состоянию
        /// </summary>
        public int LoadEzdFile(string fileName)
        {
            _logger.Debug($"LoadEzdFile: {fileName}");
            int result = lmc1_LoadEzdFile(fileName);

            if (result != 0)
            {
                _logger.Error($"LoadEzdFile failed: {GetErrorText(result)}");
            }
            else
            {
                _logger.Debug("LoadEzdFile: Success");
            }

            return result;
        }

        /// <summary>
        /// Получение размеров объекта (PDF стр. 8)
        /// </summary>
        /// <returns>Tuple (minX, minY, maxX, maxY, z) или null при ошибке</returns>
        public (double minX, double minY, double maxX, double maxY, double z)? GetEntSize(string entityName)
        {
            double minX = 0, minY = 0, maxX = 0, maxY = 0, z = 0;

            int result = lmc1_GetEntSize(entityName, ref minX, ref minY, ref maxX, ref maxY, ref z);

            if (result != 0)
            {
                _logger.Error($"GetEntSize failed for '{entityName}': {GetErrorText(result)}");
                return null;
            }

            _logger.Debug($"GetEntSize '{entityName}': Min({minX:F3}, {minY:F3}), Max({maxX:F3}, {maxY:F3}), Z={z:F3}");
            return (minX, minY, maxX, maxY, z);
        }

        /// <summary>
        /// Вычисление геометрического центра объекта
        /// CenterX = (MinX + MaxX) / 2
        /// CenterY = (MinY + MaxY) / 2
        /// </summary>
        public (double centerX, double centerY)? GetEntityCenter(string entityName)
        {
            var size = GetEntSize(entityName);
            if (size == null)
            {
                return null;
            }

            double centerX = (size.Value.minX + size.Value.maxX) / 2.0;
            double centerY = (size.Value.minY + size.Value.maxY) / 2.0;

            _logger.Debug($"Entity '{entityName}' center: ({centerX:F3}, {centerY:F3})");
            return (centerX, centerY);
        }

        /// <summary>
        /// Поворот объекта (PDF стр. 9)
        /// ВАЖНО: Угол должен быть в РАДИАНАХ!
        /// </summary>
        /// <param name="entityName">Имя объекта</param>
        /// <param name="centerX">X координата центра вращения (мм)</param>
        /// <param name="centerY">Y координата центра вращения (мм)</param>
        /// <param name="angleRadians">Угол поворота в РАДИАНАХ</param>
        public int RotateEnt(string entityName, double centerX, double centerY, double angleRadians)
        {
            _logger.Debug($"RotateEnt '{entityName}': Center({centerX:F3}, {centerY:F3}), Angle={angleRadians:F4} rad ({angleRadians * 180.0 / Math.PI:F2} deg)");

            int result = lmc1_RotateEnt(entityName, centerX, centerY, angleRadians);

            if (result != 0)
            {
                _logger.Error($"RotateEnt failed: {GetErrorText(result)}");
            }
            else
            {
                _logger.Debug("RotateEnt: Success");
            }

            return result;
        }

        /// <summary>
        /// Перемещение объекта (PDF стр. 8)
        /// Относительное смещение от текущей позиции
        /// </summary>
        /// <param name="entityName">Имя объекта</param>
        /// <param name="moveX">Смещение по X (мм)</param>
        /// <param name="moveY">Смещение по Y (мм)</param>
        public int MoveEnt(string entityName, double moveX, double moveY)
        {
            _logger.Debug($"MoveEnt '{entityName}': Shift({moveX:F3}, {moveY:F3}) mm");

            int result = lmc1_MoveEnt(entityName, moveX, moveY);

            if (result != 0)
            {
                _logger.Error($"MoveEnt failed: {GetErrorText(result)}");
            }
            else
            {
                _logger.Debug("MoveEnt: Success");
            }

            return result;
        }

        /// <summary>
        /// Set rotation and movement for the entire work area (alternative to RotateEnt+MoveEnt)
        /// NOTE: This rotates EVERYTHING around a center point, not just one entity
        /// This is a VOID function - doesn't return errors!
        /// </summary>
        /// <param name="moveX">X offset in mm</param>
        /// <param name="moveY">Y offset in mm</param>
        /// <param name="centerX">Rotation center X in mm</param>
        /// <param name="centerY">Rotation center Y in mm</param>
        /// <param name="angleRadians">Angle in RADIANS (not degrees!)</param>
        public void SetRotateMoveParam(double moveX, double moveY, double centerX, double centerY, double angleRadians)
        {
            _logger.Debug($"SetRotateMoveParam: Move({moveX:F3}, {moveY:F3}), Center({centerX:F3}, {centerY:F3}), Angle={angleRadians:F4} rad ({angleRadians * 180.0 / Math.PI:F2} deg)");

            // No error code returned from this function
            lmc1_SetRotateMoveParam(moveX, moveY, centerX, centerY, angleRadians);

            _logger.Debug("SetRotateMoveParam: Called (no error code available)");
        }

        /// <summary>
        /// Запуск маркировки по сигналу датчика конвейера (PDF стр. 4)
        /// БЛОКИРУЮЩИЙ ВЫЗОВ - ожидает внешний сигнал!
        /// Вызывать в отдельном потоке!
        /// </summary>
        public int MarkFlyByStartSignal()
        {
            _logger.Info("MarkFlyByStartSignal: Waiting for conveyor sensor signal...");

            int result = lmc1_MarkFlyByStartSignal();

            if (result != 0)
            {
                _logger.Error($"MarkFlyByStartSignal failed: {GetErrorText(result)}");
            }
            else
            {
                _logger.Info("MarkFlyByStartSignal: Marking completed");
            }

            return result;
        }

        /// <summary>
        /// Обычная маркировка (без ожидания сигнала)
        /// БЛОКИРУЮЩИЙ ВЫЗОВ! Вызывать в отдельном потоке!
        /// </summary>
        public int Mark(bool flyMode = false)
        {
            _logger.Info($"Mark: Starting (flyMode={flyMode})...");

            int result = lmc1_Mark(flyMode);

            if (result != 0)
            {
                _logger.Error($"Mark failed: {GetErrorText(result)}");
            }
            else
            {
                _logger.Info("Mark: Completed");
            }

            return result;
        }

        /// <summary>
        /// Превью красным лучом (безопасно для тестирования позиционирования)
        /// </summary>
        public int RedLightMark()
        {
            _logger.Debug("RedLightMark: Starting preview...");

            int result = lmc1_RedLightMark();

            if (result != 0)
            {
                _logger.Error($"RedLightMark failed: {GetErrorText(result)}");
            }
            else
            {
                _logger.Debug("RedLightMark: Done");
            }

            return result;
        }

        /// <summary>
        /// Остановка маркировки
        /// </summary>
        public int StopMark()
        {
            _logger.Info("StopMark: Stopping...");
            int result = lmc1_StopMark();

            if (result != 0)
            {
                _logger.Warning($"StopMark: {GetErrorText(result)}");
            }

            return result;
        }

        /// <summary>
        /// Проверка, идет ли маркировка
        /// </summary>
        public bool IsMarking()
        {
            return lmc1_IsMarking();
        }

        /// <summary>
        /// ГЛАВНЫЙ МЕТОД: Применение коррекции от камеры и запуск маркировки
        /// СИНХРОННАЯ ВЕРСИЯ - БЛОКИРУЕТ ПОТОК!
        /// Используйте ApplyCorrectionAndMarkAsync() для вызова из UI
        ///
        /// Алгоритм (строго по порядку):
        /// 1. СБРОС: LoadEzdFile - перезагрузка шаблона для сброса трансформаций
        /// 2. РАСЧЕТ ЦЕНТРА: GetEntSize + вычисление геометрического центра
        /// 3. ВРАЩЕНИЕ: RotateEnt вокруг своего центра (угол конвертируется в радианы!)
        /// 4. СМЕЩЕНИЕ: MoveEnt - относительный сдвиг
        /// 5. ВЗВОД: MarkFlyByStartSignal - ожидание сигнала датчика конвейера
        /// </summary>
        /// <param name="angleDegrees">Угол от камеры в ГРАДУСАХ</param>
        /// <param name="shiftX">Смещение X от камеры (мм)</param>
        /// <param name="shiftY">Смещение Y от камеры (мм)</param>
        /// <returns>0 при успехе, код ошибки при неудаче</returns>
        public int ApplyCorrectionAndMark(double angleDegrees, double shiftX, double shiftY)
        {
            int opId = ++_operationCounter;
            var startTime = DateTime.Now;

            _logger.Info($"");
            _logger.Info($"╔══════════════════════════════════════════════════════════════╗");
            _logger.Info($"║  OPERATION #{opId} - ApplyCorrectionAndMark                    ║");
            _logger.Info($"╚══════════════════════════════════════════════════════════════╝");
            _logger.Info($"[Op#{opId}] Timestamp: {startTime:yyyy-MM-dd HH:mm:ss.fff}");
            _logger.Info($"[Op#{opId}] ┌─────────────────────────────────────────────────────────────┐");
            _logger.Info($"[Op#{opId}] │ INPUT DATA FROM CAMERA:                                      │");
            _logger.Info($"[Op#{opId}] │   Angle  = {angleDegrees,10:F4}° (degrees)                          │");
            _logger.Info($"[Op#{opId}] │   ShiftX = {shiftX,10:F4} mm                                      │");
            _logger.Info($"[Op#{opId}] │   ShiftY = {shiftY,10:F4} mm                                      │");
            _logger.Info($"[Op#{opId}] └─────────────────────────────────────────────────────────────┘");

            if (!_isInitialized || string.IsNullOrEmpty(_templatePath))
            {
                _logger.Error($"[Op#{opId}] ❌ FATAL: Not configured! Call Configure() first.");
                _logger.Error($"[Op#{opId}]    _isInitialized={_isInitialized}, _templatePath='{_templatePath}'");
                return -1;
            }

            _logger.Info($"[Op#{opId}] Configuration: Template='{_templatePath}', Entity='{_entityName}'");

            int result;

            // ═══════════════════════════════════════════════════════════════
            // ШАГ 1: СБРОС - Перезагрузка шаблона для возврата в исходное состояние
            // ═══════════════════════════════════════════════════════════════
            _logger.Info($"[Op#{opId}] ───────────────────────────────────────────────────────────────");
            _logger.Info($"[Op#{opId}] STEP 1/5: RESET - Loading template to reset transformations");
            _logger.Info($"[Op#{opId}]   Calling: lmc1_LoadEzdFile(\"{_templatePath}\")");

            var stepStart = DateTime.Now;
            result = LoadEzdFile(_templatePath);
            var stepDuration = (DateTime.Now - stepStart).TotalMilliseconds;

            if (result != 0)
            {
                _logger.Error($"[Op#{opId}] ❌ STEP 1 FAILED: LoadEzdFile returned {result} ({GetErrorText(result)})");
                _logger.Error($"[Op#{opId}]    Duration: {stepDuration:F1}ms");
                LogOperationEnd(opId, startTime, false, result);
                return result;
            }
            _logger.Info($"[Op#{opId}] ✓ STEP 1 OK: Template loaded ({stepDuration:F1}ms)");

            // ═══════════════════════════════════════════════════════════════
            // ШАГ 2: РАСЧЕТ ЦЕНТРА - Получаем размеры и вычисляем геометрический центр
            // ═══════════════════════════════════════════════════════════════
            _logger.Info($"[Op#{opId}] ───────────────────────────────────────────────────────────────");
            _logger.Info($"[Op#{opId}] STEP 2/5: CALCULATE CENTER - Getting entity bounding box");
            _logger.Info($"[Op#{opId}]   Calling: lmc1_GetEntSize(\"{_entityName}\", ...)");

            stepStart = DateTime.Now;
            var size = GetEntSize(_entityName);
            stepDuration = (DateTime.Now - stepStart).TotalMilliseconds;

            if (size == null)
            {
                _logger.Error($"[Op#{opId}] ❌ STEP 2 FAILED: Could not get entity '{_entityName}' size");
                _logger.Error($"[Op#{opId}]    Entity may not exist in template or wrong name");
                LogOperationEnd(opId, startTime, false, -2);
                return -2;
            }

            double centerX = (size.Value.minX + size.Value.maxX) / 2.0;
            double centerY = (size.Value.minY + size.Value.maxY) / 2.0;

            _logger.Info($"[Op#{opId}] ✓ STEP 2 OK: Entity bounds retrieved ({stepDuration:F1}ms)");
            _logger.Info($"[Op#{opId}]   Bounding box: Min({size.Value.minX:F4}, {size.Value.minY:F4}) Max({size.Value.maxX:F4}, {size.Value.maxY:F4})");
            _logger.Info($"[Op#{opId}]   Entity size: {size.Value.maxX - size.Value.minX:F4} x {size.Value.maxY - size.Value.minY:F4} mm");
            _logger.Info($"[Op#{opId}]   ★ Calculated center: ({centerX:F4}, {centerY:F4}) mm");

            // ═══════════════════════════════════════════════════════════════
            // ШАГ 3: ВРАЩЕНИЕ - Поворот объекта вокруг своего центра
            // КРИТИЧНО: Конвертация градусов в радианы!
            // ═══════════════════════════════════════════════════════════════
            _logger.Info($"[Op#{opId}] ───────────────────────────────────────────────────────────────");
            _logger.Info($"[Op#{opId}] STEP 3/5: ROTATION - Rotating entity around its center");

            double angleRadians = angleDegrees * Math.PI / 180.0;

            _logger.Info($"[Op#{opId}]   Input angle: {angleDegrees:F4}° (degrees from camera)");
            _logger.Info($"[Op#{opId}]   Conversion: {angleDegrees:F4} × π / 180 = {angleRadians:F6} rad");
            _logger.Info($"[Op#{opId}]   Rotation center: ({centerX:F4}, {centerY:F4}) mm");
            _logger.Info($"[Op#{opId}]   Calling: lmc1_RotateEnt(\"{_entityName}\", {centerX:F4}, {centerY:F4}, {angleRadians:F6})");

            stepStart = DateTime.Now;
            result = RotateEnt(_entityName, centerX, centerY, angleRadians);
            stepDuration = (DateTime.Now - stepStart).TotalMilliseconds;

            if (result != 0)
            {
                _logger.Error($"[Op#{opId}] ❌ STEP 3 FAILED: RotateEnt returned {result} ({GetErrorText(result)})");
                _logger.Error($"[Op#{opId}]    Duration: {stepDuration:F1}ms");
                LogOperationEnd(opId, startTime, false, result);
                return result;
            }
            _logger.Info($"[Op#{opId}] ✓ STEP 3 OK: Entity rotated by {angleDegrees:F4}° ({stepDuration:F1}ms)");

            // ═══════════════════════════════════════════════════════════════
            // ШАГ 4: СМЕЩЕНИЕ - Сдвиг объекта на позицию от камеры
            // ═══════════════════════════════════════════════════════════════
            _logger.Info($"[Op#{opId}] ───────────────────────────────────────────────────────────────");
            _logger.Info($"[Op#{opId}] STEP 4/5: TRANSLATION - Moving entity to camera position");
            _logger.Info($"[Op#{opId}]   Shift vector: ({shiftX:F4}, {shiftY:F4}) mm");
            _logger.Info($"[Op#{opId}]   Calling: lmc1_MoveEnt(\"{_entityName}\", {shiftX:F4}, {shiftY:F4})");

            stepStart = DateTime.Now;
            result = MoveEnt(_entityName, shiftX, shiftY);
            stepDuration = (DateTime.Now - stepStart).TotalMilliseconds;

            if (result != 0)
            {
                _logger.Error($"[Op#{opId}] ❌ STEP 4 FAILED: MoveEnt returned {result} ({GetErrorText(result)})");
                _logger.Error($"[Op#{opId}]    Duration: {stepDuration:F1}ms");
                LogOperationEnd(opId, startTime, false, result);
                return result;
            }
            _logger.Info($"[Op#{opId}] ✓ STEP 4 OK: Entity moved by ({shiftX:F4}, {shiftY:F4}) mm ({stepDuration:F1}ms)");

            // ═══════════════════════════════════════════════════════════════
            // ШАГ 5: ВЗВОД - Запуск ожидания сигнала датчика конвейера
            // ВНИМАНИЕ: Этот вызов БЛОКИРУЮЩИЙ!
            // ═══════════════════════════════════════════════════════════════
            _logger.Info($"[Op#{opId}] ───────────────────────────────────────────────────────────────");
            _logger.Info($"[Op#{opId}] STEP 5/5: ARM & MARK - Waiting for conveyor sensor signal");
            _logger.Info($"[Op#{opId}]   ⚠️  WARNING: This call BLOCKS until sensor triggers!");
            _logger.Info($"[Op#{opId}]   Calling: lmc1_MarkFlyByStartSignal()");
            _logger.Info($"[Op#{opId}]   Waiting for IN8/IN9 signal from conveyor...");

            stepStart = DateTime.Now;
            result = MarkFlyByStartSignal();
            stepDuration = (DateTime.Now - stepStart).TotalMilliseconds;

            if (result != 0)
            {
                _logger.Error($"[Op#{opId}] ❌ STEP 5 FAILED: MarkFlyByStartSignal returned {result} ({GetErrorText(result)})");
                _logger.Error($"[Op#{opId}]    Wait duration: {stepDuration:F1}ms");
                LogOperationEnd(opId, startTime, false, result);
                return result;
            }
            _logger.Info($"[Op#{opId}] ✓ STEP 5 OK: Marking completed! (waited {stepDuration:F1}ms for signal + marking)");

            LogOperationEnd(opId, startTime, true, 0);
            return 0;
        }

        /// <summary>
        /// Логирование завершения операции
        /// </summary>
        private void LogOperationEnd(int opId, DateTime startTime, bool success, int errorCode)
        {
            var totalDuration = (DateTime.Now - startTime).TotalMilliseconds;
            _logger.Info($"[Op#{opId}] ═══════════════════════════════════════════════════════════════");
            if (success)
            {
                _logger.Info($"[Op#{opId}] ✅ OPERATION #{opId} COMPLETED SUCCESSFULLY");
            }
            else
            {
                _logger.Error($"[Op#{opId}] ❌ OPERATION #{opId} FAILED (Error code: {errorCode})");
            }
            _logger.Info($"[Op#{opId}] Total duration: {totalDuration:F1}ms ({totalDuration / 1000.0:F2}s)");
            _logger.Info($"[Op#{opId}] ═══════════════════════════════════════════════════════════════");
            _logger.Info($"");
        }

        /// <summary>
        /// АСИНХРОННАЯ ВЕРСИЯ главного метода - НЕ БЛОКИРУЕТ UI!
        /// Используйте эту версию при вызове из GUI или TCP обработчиков
        ///
        /// Пример использования:
        /// <code>
        /// // В обработчике TCP данных:
        /// private async void OnCameraDataReceived(object sender, CameraDataEventArgs e)
        /// {
        ///     var result = await _markEzd.ApplyCorrectionAndMarkAsync(e.Angle, e.ShiftX, e.ShiftY);
        ///     if (result != 0)
        ///         _logger.Error($"Marking failed with code {result}");
        /// }
        /// </code>
        /// </summary>
        public Task<int> ApplyCorrectionAndMarkAsync(double angleDegrees, double shiftX, double shiftY)
        {
            _logger.Info($"[Async] Starting ApplyCorrectionAndMarkAsync on background thread...");
            _logger.Info($"[Async] Camera data: Angle={angleDegrees:F4}°, ShiftX={shiftX:F4}mm, ShiftY={shiftY:F4}mm");

            return Task.Run(() => ApplyCorrectionAndMark(angleDegrees, shiftX, shiftY));
        }

        /// <summary>
        /// Версия для тестирования: применяет коррекцию и показывает превью красным лучом
        /// (без реальной маркировки) - БЕЗОПАСНО для тестов!
        /// </summary>
        public int ApplyCorrectionAndPreview(double angleDegrees, double shiftX, double shiftY)
        {
            int opId = ++_operationCounter;
            var startTime = DateTime.Now;

            _logger.Info($"");
            _logger.Info($"╔══════════════════════════════════════════════════════════════╗");
            _logger.Info($"║  OPERATION #{opId} - ApplyCorrectionAndPreview (RED LIGHT)     ║");
            _logger.Info($"╚══════════════════════════════════════════════════════════════╝");
            _logger.Info($"[Op#{opId}] 🔴 RED LIGHT MODE - No actual marking will occur!");
            _logger.Info($"[Op#{opId}] Camera data: Angle={angleDegrees:F4}°, ShiftX={shiftX:F4}mm, ShiftY={shiftY:F4}mm");

            if (!_isInitialized || string.IsNullOrEmpty(_templatePath))
            {
                _logger.Error($"[Op#{opId}] ❌ Not configured! Call Configure() first.");
                return -1;
            }

            int result;

            // ШАГ 1: СБРОС
            _logger.Info($"[Op#{opId}] STEP 1/5: Loading template...");
            result = LoadEzdFile(_templatePath);
            if (result != 0)
            {
                _logger.Error($"[Op#{opId}] ❌ LoadEzdFile failed: {result}");
                return result;
            }

            // ШАГ 2: РАСЧЕТ ЦЕНТРА
            _logger.Info($"[Op#{opId}] STEP 2/5: Getting entity center...");
            var center = GetEntityCenter(_entityName);
            if (center == null)
            {
                _logger.Error($"[Op#{opId}] ❌ Could not get entity '{_entityName}' size");
                return -2;
            }
            _logger.Info($"[Op#{opId}]   Center: ({center.Value.centerX:F4}, {center.Value.centerY:F4}) mm");

            // ШАГ 3: ВРАЩЕНИЕ (градусы -> радианы)
            double angleRadians = angleDegrees * Math.PI / 180.0;
            _logger.Info($"[Op#{opId}] STEP 3/5: Rotating {angleDegrees:F4}° ({angleRadians:F6} rad)...");
            result = RotateEnt(_entityName, center.Value.centerX, center.Value.centerY, angleRadians);
            if (result != 0)
            {
                _logger.Error($"[Op#{opId}] ❌ RotateEnt failed: {result}");
                return result;
            }

            // ШАГ 4: СМЕЩЕНИЕ
            _logger.Info($"[Op#{opId}] STEP 4/5: Moving by ({shiftX:F4}, {shiftY:F4}) mm...");
            result = MoveEnt(_entityName, shiftX, shiftY);
            if (result != 0)
            {
                _logger.Error($"[Op#{opId}] ❌ MoveEnt failed: {result}");
                return result;
            }

            // ШАГ 5: ПРЕВЬЮ (вместо маркировки)
            _logger.Info($"[Op#{opId}] STEP 5/5: 🔴 Red light preview (SAFE - no engraving)...");
            result = RedLightMark();
            if (result != 0)
            {
                _logger.Error($"[Op#{opId}] ❌ RedLightMark failed: {result}");
                return result;
            }

            var duration = (DateTime.Now - startTime).TotalMilliseconds;
            _logger.Info($"[Op#{opId}] ✅ Preview completed in {duration:F1}ms");
            return 0;
        }

        /// <summary>
        /// Асинхронная версия превью
        /// </summary>
        public Task<int> ApplyCorrectionAndPreviewAsync(double angleDegrees, double shiftX, double shiftY)
        {
            _logger.Info($"[Async] Starting ApplyCorrectionAndPreviewAsync...");
            return Task.Run(() => ApplyCorrectionAndPreview(angleDegrees, shiftX, shiftY));
        }

        /// <summary>
        /// Получить изображение предпросмотра текущего шаблона
        /// </summary>
        [DllImport("MarkEzd", EntryPoint = "lmc1_GetPrevBitmap2", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private static extern IntPtr lmc1_GetPrevBitmap2(int bmpWidth, int bmpHeight);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        /// <summary>
        /// Получить bitmap предпросмотра как System.Drawing.Bitmap
        /// </summary>
        public System.Drawing.Bitmap? GetPreviewBitmap(int width, int height)
        {
            try
            {
                IntPtr hBitmap = lmc1_GetPrevBitmap2(width, height);
                if (hBitmap == IntPtr.Zero)
                {
                    _logger.Warning("GetPreviewBitmap: Failed to get bitmap from SDK");
                    return null;
                }

                // Создаем копию bitmap
                var bitmap = System.Drawing.Image.FromHbitmap(hBitmap);

                // Освобождаем GDI handle
                DeleteObject(hBitmap);

                _logger.Debug($"GetPreviewBitmap: Created {width}x{height} preview");
                return bitmap as System.Drawing.Bitmap;
            }
            catch (Exception ex)
            {
                _logger.Error($"GetPreviewBitmap exception: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Открыть диалог настройки устройства
        /// </summary>
        [DllImport("MarkEzd", EntryPoint = "lmc1_SetDevCfg", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private static extern int lmc1_SetDevCfg();

        public int OpenDeviceConfig()
        {
            _logger.Info("Opening device configuration dialog...");
            return lmc1_SetDevCfg();
        }

        /// <summary>
        /// Изменить текст в объекте шаблона
        /// </summary>
        [DllImport("MarkEzd", EntryPoint = "lmc1_ChangeTextByName", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private static extern int lmc1_ChangeTextByName(string strTextName, string strTextNew);

        public int ChangeText(string entityName, string newText)
        {
            _logger.Info($"Changing text in '{entityName}' to: {newText}");
            int result = lmc1_ChangeTextByName(entityName, newText);
            if (result != 0)
            {
                _logger.Error($"ChangeText failed: {GetErrorText(result)}");
            }
            return result;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // УДАЛЕНО: ChangeBarcodeText() и lmc1_ChangeBarcodeStrByName
        // ═══════════════════════════════════════════════════════════════════════
        // Причина: Функция lmc1_ChangeBarcodeStrByName НЕ существует в SDK 2019-09-09
        // Это вызывало exception: "Unable to find an entry point named 'lmc1_ChangeBarcodeStrByName'"
        //
        // РЕШЕНИЕ: Использовать TEXT объект с DataMatrix шрифтом в шаблоне EzCad
        // Тогда ChangeTextByName() будет работать корректно
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Универсальный метод: автоматически определяет тип объекта (текст или штрих-код)
        /// ВАЖНО: SDK версии 2019-09-09 НЕ содержит функцию lmc1_ChangeBarcodeStrByName
        /// Если объект не является TEXT, нужно использовать TEXT объект с DataMatrix шрифтом
        /// </summary>
        public int ChangeTextOrBarcode(string entityName, string newText)
        {
            _logger.Debug($"ChangeTextOrBarcode: Attempting to change '{entityName}' to: {newText}");

            // Пробуем изменить через ChangeTextByName (работает для TEXT объектов)
            int result = lmc1_ChangeTextByName(entityName, newText);

            if (result == 0)
            {
                _logger.Info($"✓ Changed as TEXT object: '{entityName}'");
                return 0;
            }
            else if (result == 16) // LMC1_ERR_NOTTEXT - объект не текстовый
            {
                // КРИТИЧЕСКОЕ ПРЕДУПРЕЖДЕНИЕ: SDK не поддерживает изменение DataMatrix/Barcode объектов
                _logger.Warning($"⚠️ Объект '{entityName}' НЕ является TEXT объектом (error 16)");
                _logger.Warning($"   SDK версии 2019-09-09 НЕ содержит функцию lmc1_ChangeBarcodeStrByName");
                _logger.Warning($"   РЕШЕНИЕ: Замените объект в шаблоне на TEXT с DataMatrix шрифтом");
                _logger.Warning($"   Маркировка будет выполнена с ДЕФОЛТНЫМ содержимым из шаблона");

                // Возвращаем 0 чтобы маркировка продолжилась (но с дефолтным кодом)
                // Альтернатива: return result (прервет маркировку)
                return 0;
            }
            else
            {
                // Другая ошибка (объект не найден, и т.д.)
                _logger.Error($"Не удалось изменить объект '{entityName}': {GetErrorText(result)}");
                return result;
            }
        }

        /// <summary>
        /// Версия с обычной маркировкой (без ожидания сигнала конвейера)
        /// Используйте для тестирования без конвейера или ручного запуска
        /// ВНИМАНИЕ: БЛОКИРУЮЩИЙ ВЫЗОВ! Используйте Async версию для UI
        /// </summary>
        public int ApplyCorrectionAndMarkImmediate(double angleDegrees, double shiftX, double shiftY)
        {
            int opId = ++_operationCounter;
            var startTime = DateTime.Now;

            _logger.Info($"");
            _logger.Info($"╔══════════════════════════════════════════════════════════════╗");
            _logger.Info($"║  OPERATION #{opId} - ApplyCorrectionAndMarkImmediate           ║");
            _logger.Info($"╚══════════════════════════════════════════════════════════════╝");
            _logger.Info($"[Op#{opId}] ⚡ IMMEDIATE MODE - No conveyor signal wait!");
            _logger.Info($"[Op#{opId}] Camera data: Angle={angleDegrees:F4}°, ShiftX={shiftX:F4}mm, ShiftY={shiftY:F4}mm");

            if (!_isInitialized || string.IsNullOrEmpty(_templatePath))
            {
                _logger.Error($"[Op#{opId}] ❌ Not configured! Call Configure() first.");
                return -1;
            }

            int result;

            // ШАГ 1: СБРОС
            _logger.Info($"[Op#{opId}] STEP 1/5: Loading template...");
            result = LoadEzdFile(_templatePath);
            if (result != 0)
            {
                _logger.Error($"[Op#{opId}] ❌ LoadEzdFile failed: {result} ({GetErrorText(result)})");
                return result;
            }

            // ШАГ 2: РАСЧЕТ ЦЕНТРА
            _logger.Info($"[Op#{opId}] STEP 2/5: Getting entity center...");
            var center = GetEntityCenter(_entityName);
            if (center == null)
            {
                _logger.Error($"[Op#{opId}] ❌ Could not get entity '{_entityName}' size");
                return -2;
            }
            _logger.Info($"[Op#{opId}]   Center: ({center.Value.centerX:F4}, {center.Value.centerY:F4}) mm");

            // ШАГ 3: ВРАЩЕНИЕ (градусы -> радианы)
            double angleRadians = angleDegrees * Math.PI / 180.0;
            _logger.Info($"[Op#{opId}] STEP 3/5: Rotating {angleDegrees:F4}° ({angleRadians:F6} rad)...");
            result = RotateEnt(_entityName, center.Value.centerX, center.Value.centerY, angleRadians);
            if (result != 0)
            {
                _logger.Error($"[Op#{opId}] ❌ RotateEnt failed: {result} ({GetErrorText(result)})");
                return result;
            }

            // ШАГ 4: СМЕЩЕНИЕ
            _logger.Info($"[Op#{opId}] STEP 4/5: Moving by ({shiftX:F4}, {shiftY:F4}) mm...");
            result = MoveEnt(_entityName, shiftX, shiftY);
            if (result != 0)
            {
                _logger.Error($"[Op#{opId}] ❌ MoveEnt failed: {result} ({GetErrorText(result)})");
                return result;
            }

            // ШАГ 5: МАРКИРОВКА НЕМЕДЛЕННО
            _logger.Info($"[Op#{opId}] STEP 5/5: ⚡ MARKING IMMEDIATELY (lmc1_Mark)...");
            _logger.Info($"[Op#{opId}]   ⚠️  This call BLOCKS until marking is complete!");

            var markStart = DateTime.Now;
            result = Mark(flyMode: false);
            var markDuration = (DateTime.Now - markStart).TotalMilliseconds;

            if (result != 0)
            {
                _logger.Error($"[Op#{opId}] ❌ Mark failed: {result} ({GetErrorText(result)})");
                return result;
            }

            var totalDuration = (DateTime.Now - startTime).TotalMilliseconds;
            _logger.Info($"[Op#{opId}] ✅ MARKING COMPLETE!");
            _logger.Info($"[Op#{opId}]   Mark time: {markDuration:F1}ms");
            _logger.Info($"[Op#{opId}]   Total time: {totalDuration:F1}ms");
            return 0;
        }

        /// <summary>
        /// Асинхронная версия немедленной маркировки - НЕ БЛОКИРУЕТ UI!
        /// </summary>
        public Task<int> ApplyCorrectionAndMarkImmediateAsync(double angleDegrees, double shiftX, double shiftY)
        {
            _logger.Info($"[Async] Starting ApplyCorrectionAndMarkImmediateAsync...");
            return Task.Run(() => ApplyCorrectionAndMarkImmediate(angleDegrees, shiftX, shiftY));
        }

        /// <summary>
        /// Alternative marking approach using SetRotateMoveParam instead of RotateEnt+MoveEnt
        /// This rotates the ENTIRE WORK AREA instead of individual entities
        /// Use this to test if SetRotateMoveParam works better for your setup
        ///
        /// Algorithm:
        /// 1. RESET: Load template to clear transformations
        /// 2. GET CENTER: Calculate rotation center (can use laser center or entity center)
        /// 3. SET TRANSFORM: Apply rotation + movement in ONE call using SetRotateMoveParam
        /// 4. MARK: Wait for conveyor signal and mark
        /// </summary>
        public int ApplyCorrectionUsingSetRotateMove(double angleDegrees, double shiftX, double shiftY, double centerX, double centerY)
        {
            int opId = ++_operationCounter;
            var startTime = DateTime.Now;

            _logger.Info($"");
            _logger.Info($"╔══════════════════════════════════════════════════════════════╗");
            _logger.Info($"║  OPERATION #{opId} - SetRotateMoveParam Method                 ║");
            _logger.Info($"╚══════════════════════════════════════════════════════════════╝");
            _logger.Info($"[Op#{opId}] Using SetRotateMoveParam (rotates ENTIRE work area)");
            _logger.Info($"[Op#{opId}] ┌─────────────────────────────────────────────────────────────┐");
            _logger.Info($"[Op#{opId}] │ INPUT DATA:                                                  │");
            _logger.Info($"[Op#{opId}] │   Angle   = {angleDegrees,10:F4}° (degrees)                         │");
            _logger.Info($"[Op#{opId}] │   ShiftX  = {shiftX,10:F4} mm                                     │");
            _logger.Info($"[Op#{opId}] │   ShiftY  = {shiftY,10:F4} mm                                     │");
            _logger.Info($"[Op#{opId}] │   CenterX = {centerX,10:F4} mm (rotation center)                  │");
            _logger.Info($"[Op#{opId}] │   CenterY = {centerY,10:F4} mm (rotation center)                  │");
            _logger.Info($"[Op#{opId}] └─────────────────────────────────────────────────────────────┘");

            if (!_isInitialized || string.IsNullOrEmpty(_templatePath))
            {
                _logger.Error($"[Op#{opId}] ❌ Not configured!");
                return -1;
            }

            int result;

            // Step 1: Reset the template
            _logger.Info($"[Op#{opId}] ───────────────────────────────────────────────────────────────");
            _logger.Info($"[Op#{opId}] STEP 1/3: Loading template to reset state");

            var stepStart = DateTime.Now;
            result = LoadEzdFile(_templatePath);
            var stepDuration = (DateTime.Now - stepStart).TotalMilliseconds;

            if (result != 0)
            {
                _logger.Error($"[Op#{opId}] ❌ STEP 1 FAILED: {GetErrorText(result)} ({stepDuration:F1}ms)");
                LogOperationEnd(opId, startTime, false, result);
                return result;
            }
            _logger.Info($"[Op#{opId}] ✓ STEP 1 OK: Template loaded ({stepDuration:F1}ms)");

            // Step 2: Apply transformation using SetRotateMoveParam
            // This does rotation AND movement in a single call
            _logger.Info($"[Op#{opId}] ───────────────────────────────────────────────────────────────");
            _logger.Info($"[Op#{opId}] STEP 2/3: Applying rotation + movement with SetRotateMoveParam");

            double angleRadians = angleDegrees * Math.PI / 180.0;

            _logger.Info($"[Op#{opId}]   Angle conversion: {angleDegrees:F4}° → {angleRadians:F6} rad");
            _logger.Info($"[Op#{opId}]   Calling: SetRotateMoveParam(");
            _logger.Info($"[Op#{opId}]     moveX={shiftX:F4}, moveY={shiftY:F4},");
            _logger.Info($"[Op#{opId}]     centerX={centerX:F4}, centerY={centerY:F4},");
            _logger.Info($"[Op#{opId}]     angle={angleRadians:F6} rad)");

            stepStart = DateTime.Now;
            SetRotateMoveParam(shiftX, shiftY, centerX, centerY, angleRadians);
            stepDuration = (DateTime.Now - stepStart).TotalMilliseconds;

            // Note: SetRotateMoveParam returns void, so we can't check for errors
            _logger.Info($"[Op#{opId}] ✓ STEP 2 OK: Transform applied ({stepDuration:F1}ms)");
            _logger.Info($"[Op#{opId}]   ⚠️  NOTE: This function doesn't return errors!");

            // Step 3: Mark (with or without conveyor signal)
            _logger.Info($"[Op#{opId}] ───────────────────────────────────────────────────────────────");

            stepStart = DateTime.Now;

            if (_useConveyorSensor)
            {
                _logger.Info($"[Op#{opId}] STEP 3/3: Waiting for conveyor signal and marking");
                _logger.Info($"[Op#{opId}]   ⚠️  This will BLOCK until IN8/IN9 triggers!");
                result = MarkFlyByStartSignal();
                stepDuration = (DateTime.Now - stepStart).TotalMilliseconds;

                if (result != 0)
                {
                    _logger.Error($"[Op#{opId}] ❌ STEP 3 FAILED: {GetErrorText(result)} (waited {stepDuration:F1}ms)");
                    LogOperationEnd(opId, startTime, false, result);
                    return result;
                }
                _logger.Info($"[Op#{opId}] ✓ STEP 3 OK: Marking complete (waited {stepDuration:F1}ms)");
            }
            else
            {
                _logger.Info($"[Op#{opId}] STEP 3/3: Marking immediately (no conveyor sensor)");
                result = Mark(false); // false = normal marking, not fly marking
                stepDuration = (DateTime.Now - stepStart).TotalMilliseconds;

                if (result != 0)
                {
                    _logger.Error($"[Op#{opId}] ❌ STEP 3 FAILED: {GetErrorText(result)} ({stepDuration:F1}ms)");
                    LogOperationEnd(opId, startTime, false, result);
                    return result;
                }
                _logger.Info($"[Op#{opId}] ✓ STEP 3 OK: Marking complete ({stepDuration:F1}ms)");
            }

            LogOperationEnd(opId, startTime, true, 0);
            return 0;
        }

        /// <summary>
        /// Async version of SetRotateMoveParam marking - won't block UI
        /// </summary>
        public Task<int> ApplyCorrectionUsingSetRotateMoveAsync(double angleDegrees, double shiftX, double shiftY, double centerX, double centerY)
        {
            _logger.Info($"[Async] Starting SetRotateMoveParam method on background thread...");
            return Task.Run(() => ApplyCorrectionUsingSetRotateMove(angleDegrees, shiftX, shiftY, centerX, centerY));
        }

        /// <summary>
        /// Test version using SetRotateMoveParam with red light preview (no actual marking)
        /// Safe for testing the rotation without engraving
        /// </summary>
        public int TestSetRotateMoveWithPreview(double angleDegrees, double shiftX, double shiftY, double centerX, double centerY)
        {
            int opId = ++_operationCounter;
            var startTime = DateTime.Now;

            _logger.Info($"");
            _logger.Info($"╔══════════════════════════════════════════════════════════════╗");
            _logger.Info($"║  TEST MODE - SetRotateMoveParam with Red Light                ║");
            _logger.Info($"╚══════════════════════════════════════════════════════════════╝");
            _logger.Info($"[Op#{opId}] 🔴 RED LIGHT MODE - No engraving!");
            _logger.Info($"[Op#{opId}] Testing SetRotateMoveParam function");
            _logger.Info($"[Op#{opId}] Angle={angleDegrees:F4}°, Shift=({shiftX:F4}, {shiftY:F4}), Center=({centerX:F4}, {centerY:F4})");

            if (!_isInitialized || string.IsNullOrEmpty(_templatePath))
            {
                _logger.Error($"[Op#{opId}] ❌ Not configured!");
                return -1;
            }

            // Step 1: Reset
            _logger.Info($"[Op#{opId}] STEP 1: Loading template...");
            int result = LoadEzdFile(_templatePath);
            if (result != 0)
            {
                _logger.Error($"[Op#{opId}] ❌ LoadEzdFile failed: {GetErrorText(result)}");
                return result;
            }

            // Step 2: Apply transform
            _logger.Info($"[Op#{opId}] STEP 2: Applying SetRotateMoveParam...");
            double angleRadians = angleDegrees * Math.PI / 180.0;
            _logger.Info($"[Op#{opId}]   {angleDegrees:F4}° = {angleRadians:F6} rad");
            SetRotateMoveParam(shiftX, shiftY, centerX, centerY, angleRadians);
            _logger.Info($"[Op#{opId}] ✓ Transform applied");

            // Step 3: Red light preview
            _logger.Info($"[Op#{opId}] STEP 3: 🔴 Red light preview...");
            result = RedLightMark();
            if (result != 0)
            {
                _logger.Error($"[Op#{opId}] ❌ RedLightMark failed: {GetErrorText(result)}");
                return result;
            }

            var duration = (DateTime.Now - startTime).TotalMilliseconds;
            _logger.Info($"[Op#{opId}] ✅ Preview complete ({duration:F1}ms)");
            return 0;
        }

        /// <summary>
        /// Async test version
        /// </summary>
        public Task<int> TestSetRotateMoveWithPreviewAsync(double angleDegrees, double shiftX, double shiftY, double centerX, double centerY)
        {
            _logger.Info($"[Async] Starting SetRotateMoveParam test with preview...");
            return Task.Run(() => TestSetRotateMoveWithPreview(angleDegrees, shiftX, shiftY, centerX, centerY));
        }
    }
}
