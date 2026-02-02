using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LazerNi.Models;

namespace LazerNi.Services
{
    /// <summary>
    /// Service for managing products (CRUD operations and validation)
    /// </summary>
    public class ProductManagerService
    {
        private readonly DatabaseService _database;
        private readonly LoggingService _logger;

        public ProductManagerService(DatabaseService database, LoggingService logger)
        {
            _database = database;
            _logger = logger;
        }

        /// <summary>
        /// Add a new product to the database
        /// </summary>
        public async Task<bool> AddProductAsync(Product product)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!ValidateProduct(product, out string error))
                    {
                        _logger.Error($"Product validation failed: {error}");
                        return false;
                    }

                    bool result = _database.AddProduct(product);
                    if (result)
                    {
                        _logger.Info($"Product added: {product.Name} (GTIN: {product.Gtin})");
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to add product: {ex.Message}");
                    return false;
                }
            });
        }

        /// <summary>
        /// Update an existing product
        /// </summary>
        public async Task<bool> UpdateProductAsync(Product product)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!ValidateProduct(product, out string error))
                    {
                        _logger.Error($"Product validation failed: {error}");
                        return false;
                    }

                    bool result = _database.UpdateProduct(product);
                    if (result)
                    {
                        _logger.Info($"Product updated: {product.Name} (GTIN: {product.Gtin})");
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to update product: {ex.Message}");
                    return false;
                }
            });
        }

        /// <summary>
        /// Delete a product by ID
        /// </summary>
        public async Task<bool> DeleteProductAsync(int productId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    bool result = _database.DeleteProduct(productId);
                    if (result)
                    {
                        _logger.Info($"Product deleted: ID {productId}");
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to delete product: {ex.Message}");
                    return false;
                }
            });
        }

        /// <summary>
        /// Get all products from database
        /// </summary>
        public async Task<List<Product>> GetAllProductsAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    return _database.GetAllProducts();
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to get products: {ex.Message}");
                    return new List<Product>();
                }
            });
        }

        /// <summary>
        /// Get a specific product by ID
        /// </summary>
        public async Task<Product?> GetProductByIdAsync(int id)
        {
            return await Task.Run(() =>
            {
                try
                {
                    return _database.GetProductById(id);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to get product by ID: {ex.Message}");
                    return null;
                }
            });
        }

        /// <summary>
        /// Set the selected product for marking operations
        /// </summary>
        public async Task<bool> SelectProductAsync(int productId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    bool result = _database.SetSelectedProduct(productId);
                    if (result)
                    {
                        var product = _database.GetProductById(productId);
                        if (product != null)
                        {
                            _logger.Info($"Product selected: {product.Name}");
                        }
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to select product: {ex.Message}");
                    return false;
                }
            });
        }

        /// <summary>
        /// Get the currently selected product
        /// </summary>
        public async Task<Product?> GetSelectedProductAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    int? selectedId = _database.GetSelectedProductId();
                    if (selectedId.HasValue)
                    {
                        return _database.GetProductById(selectedId.Value);
                    }
                    return null;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to get selected product: {ex.Message}");
                    return null;
                }
            });
        }

        /// <summary>
        /// Validate product data
        /// </summary>
        public bool ValidateProduct(Product product, out string error)
        {
            if (string.IsNullOrWhiteSpace(product.Name))
            {
                error = "Наименование продукта не может быть пустым";
                return false;
            }

            if (string.IsNullOrWhiteSpace(product.Gtin))
            {
                error = "GTIN продукта не может быть пустым";
                return false;
            }

            if (product.Gtin.Length != 13 || !long.TryParse(product.Gtin, out _))
            {
                error = "GTIN продукта должен содержать ровно 13 цифр";
                return false;
            }

            if (string.IsNullOrWhiteSpace(product.PackageGtin))
            {
                error = "GTIN упаковки не может быть пустым";
                return false;
            }

            if (product.PackageGtin.Length != 13 || !long.TryParse(product.PackageGtin, out _))
            {
                error = "GTIN упаковки должен содержать ровно 13 цифр";
                return false;
            }

            if (product.ItemsPerPackage <= 0)
            {
                error = "Количество бутылок в упаковке должно быть больше 0";
                return false;
            }

            if (product.PackagesPerPallet <= 0)
            {
                error = "Количество упаковок на палете должно быть больше 0";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
