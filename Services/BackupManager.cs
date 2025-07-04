using OnlineBackupSystem.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Globalization; // For ISO 8601 parsing

namespace OnlineBackupSystem.Services
{
    public class BackupManager
    {
        private readonly PantryApiClient _pantryApiClient;
        private const string BackupPrefix = "UniGet_";
        private int _backupSlots = 5; // Default value, can be made configurable

        // Placeholder for where current application data would be fetched
        // In a real app, these would likely be services or repositories
        public Func<Task<object>> GetCurrentUserSettingsAsync { get; set; } = () => Task.FromResult<object>(new { theme = "default", notifications_enabled = false });
        public Func<Task<List<InstalledPackage>>> GetCurrentInstalledPackagesAsync { get; set; } = () => Task.FromResult(new List<InstalledPackage>());


        public BackupManager(PantryApiClient pantryApiClient, int configuredBackupSlots = 5)
        {
            _pantryApiClient = pantryApiClient ?? throw new ArgumentNullException(nameof(pantryApiClient));
            if (configuredBackupSlots <= 0 || configuredBackupSlots > 10)
            {
                throw new ArgumentOutOfRangeException(nameof(configuredBackupSlots), "Backup slots must be between 1 and 10.");
            }
            _backupSlots = configuredBackupSlots;

            // Example of how to set the data retrieval functions if they were passed in or resolved via DI
            // GetCurrentUserSettingsAsync = ...
            // GetCurrentInstalledPackagesAsync = ...
        }

        public void UpdateBackupSlots(int slots)
        {
            if (slots <= 0 || slots > 10)
            {
                throw new ArgumentOutOfRangeException(nameof(slots), "Backup slots must be between 1 and 10.");
            }
            _backupSlots = slots;
        }

        public async Task<List<Backup>> GetBackupsAsync()
        {
            var allPantryBaskets = await _pantryApiClient.GetBasketsAsync();
            if (allPantryBaskets == null)
            {
                return new List<Backup>(); // Or throw an exception if this state is unexpected
            }

            var uniGetBasketItems = allPantryBaskets.Where(b => b.name.StartsWith(BackupPrefix)).ToList();
            var backups = new List<Backup>();

            foreach (var basketItem in uniGetBasketItems)
            {
                try
                {
                    var jsonContent = await _pantryApiClient.GetBasketContentAsync(basketItem.name);
                    if (string.IsNullOrEmpty(jsonContent))
                    {
                        Console.WriteLine($"Warning: Basket '{basketItem.name}' content was null or empty. Skipping.");
                        continue;
                    }

                    var backup = JsonSerializer.Deserialize<Backup>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (backup != null)
                    {
                        backup.BasketName = basketItem.name; // Store the original basket name
                        backups.Add(backup);
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Failed to deserialize content for basket '{basketItem.name}'. Skipping.");
                    }
                }
                catch (JsonException jsonEx)
                {
                    Console.WriteLine($"Error deserializing basket '{basketItem.name}': {jsonEx.Message}. Skipping.");
                    // Potentially log this error more formally
                }
                catch (Exception ex) // Catch other potential errors like network issues during GetBasketContentAsync
                {
                    Console.WriteLine($"Error processing basket '{basketItem.name}': {ex.Message}. Skipping.");
                    // Potentially log this error more formally
                }
            }

            // Sort by timestamp in descending order (most recent first)
            return backups.OrderByDescending(b => b.Metadata?.Timestamp).ToList();
        }

        // Other methods (CreateBackupAsync, RestoreBackupAsync, etc.) will follow.

        public async Task CreateBackupAsync(string userGivenName)
        {
            if (string.IsNullOrWhiteSpace(userGivenName))
            {
                throw new ArgumentException("User-given name for backup cannot be empty.", nameof(userGivenName));
            }

            var currentBackups = await GetBackupsAsync();

            if (currentBackups.Count >= _backupSlots)
            {
                // FIFO: Identify and delete the oldest backup(s) to make space
                var backupsToDelete = currentBackups
                    .OrderBy(b => ParseTimestamp(b.Metadata?.Timestamp)) // Oldest first
                    .Take(currentBackups.Count - _backupSlots + 1) // +1 because we're adding a new one
                    .ToList();

                foreach (var oldBackup in backupsToDelete)
                {
                    if (!string.IsNullOrEmpty(oldBackup.BasketName))
                    {
                        try
                        {
                            Console.WriteLine($"Backup limit reached. Deleting oldest backup: {oldBackup.BasketName} (Timestamp: {oldBackup.Metadata?.Timestamp})");
                            await _pantryApiClient.DeleteBasketAsync(oldBackup.BasketName);
                        }
                        catch (Exception ex)
                        {
                            // Log the error but continue, as creating the new backup is higher priority
                            Console.WriteLine($"Error deleting old backup '{oldBackup.BasketName}': {ex.Message}");
                        }
                    }
                }
            }

            // Generate new basket name
            var timestampEpoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var newBasketName = $"{BackupPrefix}{timestampEpoch}";

            // Prepare backup data
            var backupData = new Backup
            {
                Metadata = new BackupMetadata
                {
                    Name = userGivenName,
                    Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), // ISO 8601 format
                    Version = "1.0"
                },
                AppData = new ApplicationData
                {
                    UserSettings = await (GetCurrentUserSettingsAsync?.Invoke() ?? Task.FromResult<object>(null)),
                    InstalledPackages = await (GetCurrentInstalledPackagesAsync?.Invoke() ?? Task.FromResult(new List<InstalledPackage>()))
                }
                // BasketName will be the newBasketName, but it's not stored in the JSON itself.
            };

            try
            {
                await _pantryApiClient.CreateBasketAsync(newBasketName, backupData);
                Console.WriteLine($"Successfully created backup: {newBasketName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating new backup '{newBasketName}': {ex.Message}");
                throw; // Re-throw to allow ViewModel to catch and display error
            }
        }

        private DateTime ParseTimestamp(string iso8601Timestamp)
        {
            if (string.IsNullOrEmpty(iso8601Timestamp))
            {
                return DateTime.MinValue; // Or handle as an error/default
            }
            if (DateTime.TryParse(iso8601Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate))
            {
                return parsedDate;
            }
            return DateTime.MinValue; // Fallback or error
        }

        // Placeholder for where application data would be restored
        // In a real app, these would interact with application services/state management
        public Func<object, Task> ApplyRestoredUserSettingsAsync { get; set; } = (settings) => {
            Console.WriteLine("Applying user settings (placeholder)...");
            // Example: MyApp.Settings.Load(settings);
            return Task.CompletedTask;
        };
        public Func<List<InstalledPackage>, Task> ApplyRestoredInstalledPackagesAsync { get; set; } = (packages) => {
            Console.WriteLine("Applying installed packages (placeholder)...");
            // Example: MyApp.PackageManager.Import(packages);
            return Task.CompletedTask;
        };

        public async Task RestoreBackupAsync(Backup backupToRestore)
        {
            if (backupToRestore == null || string.IsNullOrEmpty(backupToRestore.BasketName))
            {
                throw new ArgumentNullException(nameof(backupToRestore), "Backup to restore is invalid or has no basket name.");
            }

            try
            {
                Console.WriteLine($"Restoring from backup: {backupToRestore.BasketName} (Name: {backupToRestore.Metadata?.Name})");
                // The Backup object passed in should already contain the AppData
                // If not, or if a fresh copy is needed:
                // var jsonContent = await _pantryApiClient.GetBasketContentAsync(backupToRestore.BasketName);
                // var fullBackupData = JsonSerializer.Deserialize<Backup>(jsonContent);
                // var appDataToRestore = fullBackupData.AppData;

                var appDataToRestore = backupToRestore.AppData;

                if (appDataToRestore == null)
                {
                    throw new InvalidOperationException("Backup data does not contain application data to restore.");
                }

                // Apply restored data (using placeholder functions)
                if (ApplyRestoredUserSettingsAsync != null)
                {
                    await ApplyRestoredUserSettingsAsync(appDataToRestore.UserSettings);
                }
                if (ApplyRestoredInstalledPackagesAsync != null)
                {
                    await ApplyRestoredInstalledPackagesAsync(appDataToRestore.InstalledPackages);
                }

                Console.WriteLine($"Successfully restored data from backup: {backupToRestore.BasketName}");
                // Here, the application might need to trigger a refresh or reload.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error restoring backup '{backupToRestore.BasketName}': {ex.Message}");
                throw; // Re-throw for ViewModel to handle
            }
        }

        public async Task DeleteBackupAsync(Backup backupToDelete)
        {
            if (backupToDelete == null || string.IsNullOrEmpty(backupToDelete.BasketName))
            {
                throw new ArgumentNullException(nameof(backupToDelete), "Backup to delete is invalid or has no basket name.");
            }

            try
            {
                await _pantryApiClient.DeleteBasketAsync(backupToDelete.BasketName);
                Console.WriteLine($"Successfully deleted backup: {backupToDelete.BasketName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting backup '{backupToDelete.BasketName}': {ex.Message}");
                throw; // Re-throw for ViewModel to handle
            }
        }

        public async Task<string> DownloadBackupAsync(Backup backupToDownload)
        {
            if (backupToDownload == null || string.IsNullOrEmpty(backupToDownload.BasketName))
            {
                throw new ArgumentNullException(nameof(backupToDownload), "Backup to download is invalid or has no basket name.");
            }

            try
            {
                var jsonContent = await _pantryApiClient.GetBasketContentAsync(backupToDownload.BasketName);
                // The content is the raw JSON of the basket, which matches the Backup structure.
                // No further processing needed here, just return it.
                return jsonContent;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading backup content for '{backupToDownload.BasketName}': {ex.Message}");
                throw; // Re-throw for ViewModel to handle
            }
        }
    }
}
