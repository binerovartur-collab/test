using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using LazerNi.Models;

namespace LazerNi.Services
{
    /// <summary>
    /// Service for managing local SQLite database
    /// </summary>
    public class DatabaseService : IDisposable
    {
        private readonly string _connectionString;
        private readonly LoggingService _logger;
        private readonly object _lock = new object();

        public DatabaseService(string dbPath, LoggingService logger)
        {
            _logger = logger;
            _connectionString = $"Data Source={dbPath}";

            // Create database and tables if not exists
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var createTablesCommand = connection.CreateCommand();
                createTablesCommand.CommandText = @"
                    -- Products table (multi-row, replaces current_product singleton)
                    CREATE TABLE IF NOT EXISTS products (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        name TEXT NOT NULL,
                        gtin TEXT NOT NULL,
                        package_gtin TEXT NOT NULL,
                        items_per_package INTEGER DEFAULT 12,
                        packages_per_pallet INTEGER DEFAULT 6,
                        created_at TEXT DEFAULT CURRENT_TIMESTAMP
                    );

                    -- App configuration table (key-value store)
                    CREATE TABLE IF NOT EXISTS app_config (
                        key TEXT PRIMARY KEY,
                        value TEXT NOT NULL
                    );

                    -- Bottle codes pool
                    CREATE TABLE IF NOT EXISTS bottle_codes (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        code TEXT UNIQUE NOT NULL,
                        status TEXT DEFAULT 'TRANSFERRED',
                        graving_at TEXT,
                        engraved_at TEXT,
                        created_at TEXT DEFAULT CURRENT_TIMESTAMP
                    );

                    CREATE INDEX IF NOT EXISTS idx_bottle_codes_status
                    ON bottle_codes(status);

                    -- Package codes pool (for future)
                    CREATE TABLE IF NOT EXISTS package_codes (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        code TEXT UNIQUE NOT NULL,
                        status TEXT DEFAULT 'TRANSFERRED',
                        used_at TEXT,
                        created_at TEXT DEFAULT CURRENT_TIMESTAMP
                    );

                    -- Marking history
                    CREATE TABLE IF NOT EXISTS marking_history (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        code TEXT NOT NULL,
                        camera_x REAL,
                        camera_y REAL,
                        camera_angle REAL,
                        marked_at TEXT DEFAULT CURRENT_TIMESTAMP
                    );
                ";
                createTablesCommand.ExecuteNonQuery();

                // Migration: Add graving_at column if it doesn't exist
                var checkColumnCommand = connection.CreateCommand();
                checkColumnCommand.CommandText = @"
                    SELECT COUNT(*) FROM pragma_table_info('bottle_codes')
                    WHERE name='graving_at'
                ";
                var hasGravingAt = Convert.ToInt32(checkColumnCommand.ExecuteScalar()) > 0;

                if (!hasGravingAt)
                {
                    var alterCommand = connection.CreateCommand();
                    alterCommand.CommandText = @"ALTER TABLE bottle_codes ADD COLUMN graving_at TEXT";
                    alterCommand.ExecuteNonQuery();
                    _logger.Info("Added 'graving_at' column to bottle_codes table");
                }

                // Migration: Migrate from old current_product table to new products table
                MigrateFromSingletonToMultiProduct(connection);

                // Migration: Add new fields to products table (15 fields total)
                MigrateProductsTableTo15Fields(connection);

                _logger.Info("Database initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to initialize database: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get next available code with TRANSFERRED status
        /// Thread-safe operation
        /// </summary>
        public BottleCode? GetNextAvailableCode()
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        SELECT id, code, status, engraved_at, created_at
                        FROM bottle_codes
                        WHERE status = 'TRANSFERRED'
                        ORDER BY id
                        LIMIT 1
                    ";

                    using var reader = command.ExecuteReader();
                    if (reader.Read())
                    {
                        return new BottleCode
                        {
                            Id = reader.GetInt64(0),
                            Code = reader.GetString(1),
                            Status = reader.GetString(2),
                            EngravedAt = reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)),
                            CreatedAt = DateTime.Parse(reader.GetString(4))
                        };
                    }

                    return null;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to get next available code: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Mark code as engraved (changes status to ENGRAVED and sets timestamp)
        /// </summary>
        public bool MarkCodeAsEngraved(string code)
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        UPDATE bottle_codes
                        SET status = 'ENGRAVED', engraved_at = @engraved_at
                        WHERE code = @code AND (status = 'TRANSFERRED' OR status = 'GRAVING')
                    ";
                    command.Parameters.AddWithValue("@code", code);
                    command.Parameters.AddWithValue("@engraved_at", DateTime.Now.ToString("o"));

                    int rowsAffected = command.ExecuteNonQuery();

                    if (rowsAffected > 0)
                    {
                        _logger.Info($"Code marked as engraved: {code}");
                        return true;
                    }
                    else
                    {
                        _logger.Warning($"Code not found or already used: {code}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to mark code as engraved: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Mark multiple codes as GRAVING (transferred to cache, can never be reused)
        /// </summary>
        /// <param name="codes">List of code strings to mark</param>
        /// <returns>Number of codes marked</returns>
        public int MarkCodesAsGraving(List<string> codes)
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                int markedCount = 0;

                try
                {
                    foreach (var code in codes)
                    {
                        var command = connection.CreateCommand();
                        command.CommandText = @"
                            UPDATE bottle_codes
                            SET status = 'GRAVING', graving_at = @graving_at
                            WHERE code = @code AND status = 'TRANSFERRED'
                        ";
                        command.Parameters.AddWithValue("@code", code);
                        command.Parameters.AddWithValue("@graving_at", DateTime.Now.ToString("o"));

                        int rowsAffected = command.ExecuteNonQuery();
                        if (rowsAffected > 0)
                        {
                            markedCount++;
                        }
                    }

                    transaction.Commit();
                    _logger.Info($"Marked {markedCount} codes as GRAVING (transferred to cache)");
                    return markedCount;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    _logger.Error($"Failed to mark codes as GRAVING: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Delete engraved codes (cleanup after use)
        /// </summary>
        public int DeleteEngravedCodes()
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText = "DELETE FROM bottle_codes WHERE status = 'ENGRAVED'";

                    int deleted = command.ExecuteNonQuery();
                    _logger.Info($"Deleted {deleted} engraved codes");

                    return deleted;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to delete engraved codes: {ex.Message}");
                    return 0;
                }
            }
        }

        /// <summary>
        /// Get count of codes by status
        /// </summary>
        public (int Available, int Engraved, int Total) GetCodesCount()
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        SELECT
                            SUM(CASE WHEN status = 'TRANSFERRED' THEN 1 ELSE 0 END) as available,
                            SUM(CASE WHEN status = 'ENGRAVED' THEN 1 ELSE 0 END) as engraved,
                            COUNT(*) as total
                        FROM bottle_codes
                    ";

                    using var reader = command.ExecuteReader();
                    if (reader.Read())
                    {
                        return (
                            Available: reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                            Engraved: reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                            Total: reader.GetInt32(2)
                        );
                    }

                    return (0, 0, 0);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to get codes count: {ex.Message}");
                    return (0, 0, 0);
                }
            }
        }

        /// <summary>
        /// Get detailed count of codes by each status
        /// </summary>
        /// <returns>(Transferred, Graving, Engraved, Total)</returns>
        public (int Transferred, int Graving, int Engraved, int Total) GetCodesCountByStatus()
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        SELECT
                            COUNT(CASE WHEN status = 'TRANSFERRED' THEN 1 END) as transferred,
                            COUNT(CASE WHEN status = 'GRAVING' THEN 1 END) as graving,
                            COUNT(CASE WHEN status = 'ENGRAVED' THEN 1 END) as engraved,
                            COUNT(*) as total
                        FROM bottle_codes
                    ";

                    using var reader = command.ExecuteReader();
                    if (reader.Read())
                    {
                        return (
                            reader.GetInt32(0), // Transferred
                            reader.GetInt32(1), // Graving
                            reader.GetInt32(2), // Engraved
                            reader.GetInt32(3)  // Total
                        );
                    }

                    return (0, 0, 0, 0);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to get codes count by status: {ex.Message}");
                    return (0, 0, 0, 0);
                }
            }
        }

        /// <summary>
        /// Load codes from file (CSV or TXT with one code per line)
        /// Returns number of codes loaded
        /// </summary>
        public int LoadCodesFromFile(string filePath)
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(filePath))
                    {
                        _logger.Error($"File not found: {filePath}");
                        return 0;
                    }

                    var lines = File.ReadAllLines(filePath);
                    int loaded = 0;
                    int skipped = 0;

                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    using var transaction = connection.BeginTransaction();

                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT OR IGNORE INTO bottle_codes (code, status, created_at)
                        VALUES (@code, 'TRANSFERRED', @created_at)
                    ";

                    foreach (var line in lines)
                    {
                        var code = line.Trim();
                        if (string.IsNullOrEmpty(code) || code.StartsWith("#"))
                        {
                            continue; // Skip empty lines and comments
                        }

                        command.Parameters.Clear();
                        command.Parameters.AddWithValue("@code", code);
                        command.Parameters.AddWithValue("@created_at", DateTime.Now.ToString("o"));

                        int result = command.ExecuteNonQuery();
                        if (result > 0)
                        {
                            loaded++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }

                    transaction.Commit();

                    _logger.Info($"Loaded {loaded} codes from file (skipped {skipped} duplicates)");
                    return loaded;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to load codes from file: {ex.Message}");
                    return 0;
                }
            }
        }

        /// <summary>
        /// Clear all codes from database
        /// </summary>
        public bool ClearAllCodes()
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText = "DELETE FROM bottle_codes";

                    int deleted = command.ExecuteNonQuery();
                    _logger.Info($"Cleared {deleted} codes from database");

                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to clear codes: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Add marking to history
        /// </summary>
        public void AddMarkingHistory(string code, double? cameraX, double? cameraY, double? cameraAngle)
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT INTO marking_history (code, camera_x, camera_y, camera_angle, marked_at)
                        VALUES (@code, @camera_x, @camera_y, @camera_angle, @marked_at)
                    ";
                    command.Parameters.AddWithValue("@code", code);
                    command.Parameters.AddWithValue("@camera_x", cameraX.HasValue ? (object)cameraX.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@camera_y", cameraY.HasValue ? (object)cameraY.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@camera_angle", cameraAngle.HasValue ? (object)cameraAngle.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@marked_at", DateTime.Now.ToString("o"));

                    command.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to add marking history: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Migrate from old singleton current_product table to new multi-product schema
        /// </summary>
        private void MigrateFromSingletonToMultiProduct(SqliteConnection connection)
        {
            try
            {
                // Check if old table exists
                var checkCmd = connection.CreateCommand();
                checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='current_product'";
                var oldTableExists = checkCmd.ExecuteScalar() != null;

                if (oldTableExists)
                {
                    // Migrate data from old table to new products table
                    var migrationCmd = connection.CreateCommand();
                    migrationCmd.CommandText = @"
                        INSERT INTO products (name, gtin, package_gtin, items_per_package, created_at)
                        SELECT product_name, COALESCE(gtin, ''), '', bottles_per_package, created_at
                        FROM current_product;

                        DROP TABLE current_product;
                    ";
                    migrationCmd.ExecuteNonQuery();

                    _logger.Info("Migrated from singleton current_product to multi-product schema");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Migration from current_product failed (table may not exist): {ex.Message}");
            }
        }

        /// <summary>
        /// Migrate products table to include all 15 required fields
        /// </summary>
        private void MigrateProductsTableTo15Fields(SqliteConnection connection)
        {
            try
            {
                // Get current column list
                var checkColumnsCmd = connection.CreateCommand();
                checkColumnsCmd.CommandText = "SELECT name FROM pragma_table_info('products')";
                var existingColumns = new List<string>();

                using (var reader = checkColumnsCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        existingColumns.Add(reader.GetString(0));
                    }
                }

                // List of new columns to add (10 new fields)
                var newColumns = new Dictionary<string, string>
                {
                    { "document_type", "TEXT" },
                    { "document_number", "TEXT" },
                    { "document_date", "TEXT" },
                    { "well_number", "TEXT" },
                    { "nomenclature_type", "TEXT" },
                    { "shelf_life_days", "INTEGER" },
                    { "alcohol_percentage", "REAL DEFAULT 0" },
                    { "tnved_code", "TEXT" },
                    { "subsoil_license", "TEXT" },
                    { "license_date", "TEXT" }
                };

                var migratedCount = 0;
                foreach (var newColumn in newColumns)
                {
                    if (!existingColumns.Contains(newColumn.Key))
                    {
                        var alterCmd = connection.CreateCommand();
                        alterCmd.CommandText = $"ALTER TABLE products ADD COLUMN {newColumn.Key} {newColumn.Value}";
                        alterCmd.ExecuteNonQuery();
                        migratedCount++;
                    }
                }

                if (migratedCount > 0)
                {
                    _logger.Info($"Migrated products table: added {migratedCount} new columns");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Migration of products table to 15 fields failed: {ex.Message}");
            }
        }

        #region Product CRUD Operations

        /// <summary>
        /// Add a new product to the database
        /// </summary>
        public bool AddProduct(Product product)
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT INTO products (
                            name, gtin, package_gtin, items_per_package, packages_per_pallet,
                            document_type, document_number, document_date, well_number, nomenclature_type,
                            shelf_life_days, alcohol_percentage, tnved_code, subsoil_license, license_date,
                            created_at
                        ) VALUES (
                            @name, @gtin, @package_gtin, @items_per_package, @packages_per_pallet,
                            @document_type, @document_number, @document_date, @well_number, @nomenclature_type,
                            @shelf_life_days, @alcohol_percentage, @tnved_code, @subsoil_license, @license_date,
                            @created_at
                        )
                    ";
                    command.Parameters.AddWithValue("@name", product.Name);
                    command.Parameters.AddWithValue("@gtin", product.Gtin);
                    command.Parameters.AddWithValue("@package_gtin", product.PackageGtin);
                    command.Parameters.AddWithValue("@items_per_package", product.ItemsPerPackage);
                    command.Parameters.AddWithValue("@packages_per_pallet", product.PackagesPerPallet);
                    command.Parameters.AddWithValue("@document_type", product.DocumentType ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@document_number", product.DocumentNumber ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@document_date", product.DocumentDate?.ToString("o") ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@well_number", product.WellNumber ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@nomenclature_type", product.NomenclatureType ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@shelf_life_days", product.ShelfLifeDays ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@alcohol_percentage", product.AlcoholPercentage);
                    command.Parameters.AddWithValue("@tnved_code", product.TnvedCode ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@subsoil_license", product.SubsoilLicense ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@license_date", product.LicenseDate?.ToString("o") ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@created_at", DateTime.Now.ToString("o"));

                    command.ExecuteNonQuery();
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to add product: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Get all products from database
        /// </summary>
        public List<Product> GetAllProducts()
        {
            lock (_lock)
            {
                var products = new List<Product>();

                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        SELECT id, name, gtin, package_gtin, items_per_package, packages_per_pallet,
                               document_type, document_number, document_date, well_number, nomenclature_type,
                               shelf_life_days, alcohol_percentage, tnved_code, subsoil_license, license_date,
                               created_at
                        FROM products
                        ORDER BY created_at DESC
                    ";

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        products.Add(new Product
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            Gtin = reader.GetString(2),
                            PackageGtin = reader.GetString(3),
                            ItemsPerPackage = reader.GetInt32(4),
                            PackagesPerPallet = reader.GetInt32(5),
                            DocumentType = reader.IsDBNull(6) ? null : reader.GetString(6),
                            DocumentNumber = reader.IsDBNull(7) ? null : reader.GetString(7),
                            DocumentDate = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8)),
                            WellNumber = reader.IsDBNull(9) ? null : reader.GetString(9),
                            NomenclatureType = reader.IsDBNull(10) ? null : reader.GetString(10),
                            ShelfLifeDays = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                            AlcoholPercentage = reader.IsDBNull(12) ? 0 : (decimal)reader.GetDouble(12),
                            TnvedCode = reader.IsDBNull(13) ? null : reader.GetString(13),
                            SubsoilLicense = reader.IsDBNull(14) ? null : reader.GetString(14),
                            LicenseDate = reader.IsDBNull(15) ? null : DateTime.Parse(reader.GetString(15)),
                            CreatedAt = DateTime.Parse(reader.GetString(16))
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to get all products: {ex.Message}");
                }

                return products;
            }
        }

        /// <summary>
        /// Get a specific product by ID
        /// </summary>
        public Product? GetProductById(int id)
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        SELECT id, name, gtin, package_gtin, items_per_package, packages_per_pallet,
                               document_type, document_number, document_date, well_number, nomenclature_type,
                               shelf_life_days, alcohol_percentage, tnved_code, subsoil_license, license_date,
                               created_at
                        FROM products
                        WHERE id = @id
                    ";
                    command.Parameters.AddWithValue("@id", id);

                    using var reader = command.ExecuteReader();
                    if (reader.Read())
                    {
                        return new Product
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            Gtin = reader.GetString(2),
                            PackageGtin = reader.GetString(3),
                            ItemsPerPackage = reader.GetInt32(4),
                            PackagesPerPallet = reader.GetInt32(5),
                            DocumentType = reader.IsDBNull(6) ? null : reader.GetString(6),
                            DocumentNumber = reader.IsDBNull(7) ? null : reader.GetString(7),
                            DocumentDate = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8)),
                            WellNumber = reader.IsDBNull(9) ? null : reader.GetString(9),
                            NomenclatureType = reader.IsDBNull(10) ? null : reader.GetString(10),
                            ShelfLifeDays = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                            AlcoholPercentage = reader.IsDBNull(12) ? 0 : (decimal)reader.GetDouble(12),
                            TnvedCode = reader.IsDBNull(13) ? null : reader.GetString(13),
                            SubsoilLicense = reader.IsDBNull(14) ? null : reader.GetString(14),
                            LicenseDate = reader.IsDBNull(15) ? null : DateTime.Parse(reader.GetString(15)),
                            CreatedAt = DateTime.Parse(reader.GetString(16))
                        };
                    }

                    return null;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to get product by ID: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Update an existing product
        /// </summary>
        public bool UpdateProduct(Product product)
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        UPDATE products
                        SET name = @name,
                            gtin = @gtin,
                            package_gtin = @package_gtin,
                            items_per_package = @items_per_package,
                            packages_per_pallet = @packages_per_pallet,
                            document_type = @document_type,
                            document_number = @document_number,
                            document_date = @document_date,
                            well_number = @well_number,
                            nomenclature_type = @nomenclature_type,
                            shelf_life_days = @shelf_life_days,
                            alcohol_percentage = @alcohol_percentage,
                            tnved_code = @tnved_code,
                            subsoil_license = @subsoil_license,
                            license_date = @license_date
                        WHERE id = @id
                    ";
                    command.Parameters.AddWithValue("@id", product.Id);
                    command.Parameters.AddWithValue("@name", product.Name);
                    command.Parameters.AddWithValue("@gtin", product.Gtin);
                    command.Parameters.AddWithValue("@package_gtin", product.PackageGtin);
                    command.Parameters.AddWithValue("@items_per_package", product.ItemsPerPackage);
                    command.Parameters.AddWithValue("@packages_per_pallet", product.PackagesPerPallet);
                    command.Parameters.AddWithValue("@document_type", product.DocumentType ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@document_number", product.DocumentNumber ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@document_date", product.DocumentDate?.ToString("o") ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@well_number", product.WellNumber ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@nomenclature_type", product.NomenclatureType ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@shelf_life_days", product.ShelfLifeDays ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@alcohol_percentage", product.AlcoholPercentage);
                    command.Parameters.AddWithValue("@tnved_code", product.TnvedCode ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@subsoil_license", product.SubsoilLicense ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@license_date", product.LicenseDate?.ToString("o") ?? (object)DBNull.Value);

                    int rowsAffected = command.ExecuteNonQuery();
                    return rowsAffected > 0;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to update product: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Delete a product by ID
        /// </summary>
        public bool DeleteProduct(int id)
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText = "DELETE FROM products WHERE id = @id";
                    command.Parameters.AddWithValue("@id", id);

                    int rowsAffected = command.ExecuteNonQuery();
                    return rowsAffected > 0;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to delete product: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Set the selected product for marking operations
        /// </summary>
        public bool SetSelectedProduct(int productId)
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT OR REPLACE INTO app_config (key, value)
                        VALUES ('selected_product_id', @product_id)
                    ";
                    command.Parameters.AddWithValue("@product_id", productId.ToString());

                    command.ExecuteNonQuery();
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to set selected product: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Get the ID of the currently selected product
        /// </summary>
        public int? GetSelectedProductId()
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        SELECT value FROM app_config
                        WHERE key = 'selected_product_id'
                    ";

                    var result = command.ExecuteScalar();
                    if (result != null && int.TryParse(result.ToString(), out int productId))
                    {
                        return productId;
                    }

                    return null;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to get selected product ID: {ex.Message}");
                    return null;
                }
            }
        }

        #endregion

        #region App Configuration

        /// <summary>
        /// Get configuration value by key
        /// </summary>
        public string? GetConfigValue(string key)
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT value FROM app_config WHERE key = @key";
                    command.Parameters.AddWithValue("@key", key);

                    var result = command.ExecuteScalar();
                    return result?.ToString();
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to get config value for key '{key}': {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Set configuration value (insert or update)
        /// </summary>
        public bool SetConfigValue(string key, string value)
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT INTO app_config (key, value) VALUES (@key, @value)
                        ON CONFLICT(key) DO UPDATE SET value = @value
                    ";
                    command.Parameters.AddWithValue("@key", key);
                    command.Parameters.AddWithValue("@value", value);

                    command.ExecuteNonQuery();
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to set config value for key '{key}': {ex.Message}");
                    return false;
                }
            }
        }

        #endregion

        public void Dispose()
        {
            // SQLite connection is disposed automatically
        }
    }
}
