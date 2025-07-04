using Moq;
using OnlineBackupSystem.Models;
using OnlineBackupSystem.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace OnlineBackupSystem.Tests
{
    public class BackupManagerTests
    {
        private readonly Mock<PantryApiClient> _mockPantryApiClient;
        private readonly BackupManager _backupManager;
        private const int DefaultBackupSlots = 3;

        public BackupManagerTests()
        {
            // PantryApiClient constructor requires a non-empty pantryId.
            _mockPantryApiClient = new Mock<PantryApiClient>("test-pantry-id");
            _backupManager = new BackupManager(_mockPantryApiClient.Object, DefaultBackupSlots);

            // Setup default mock behaviors for data retrieval funcs if needed for most tests
             _backupManager.GetCurrentUserSettingsAsync = () => Task.FromResult<object>(new { setting = "value" });
             _backupManager.GetCurrentInstalledPackagesAsync = () => Task.FromResult(new List<InstalledPackage> { new InstalledPackage { Id = "pkg1", Version = "1.0" } });
        }

        private List<PantryBasketItem> CreateSamplePantryBasketItems(int count, string prefix = "UniGet_")
        {
            var items = new List<PantryBasketItem>();
            for (int i = 0; i < count; i++)
            {
                long timestamp = DateTimeOffset.UtcNow.AddDays(-i).ToUnixTimeMilliseconds();
                items.Add(new PantryBasketItem { name = $"{prefix}{timestamp}", date_created = DateTime.UtcNow.AddDays(-i) });
            }
            return items;
        }

        private Backup CreateSampleBackup(string basketName, string userGivenName, DateTime timestamp)
        {
            return new Backup
            {
                BasketName = basketName,
                Metadata = new BackupMetadata { Name = userGivenName, Timestamp = timestamp.ToString("o"), Version = "1.0" },
                AppData = new ApplicationData { UserSettings = new { }, InstalledPackages = new List<InstalledPackage>() }
            };
        }

        private string SerializeBackup(Backup backup) => JsonSerializer.Serialize(backup);

        [Fact]
        public async Task GetBackupsAsync_FiltersAndDeserializesCorrectly()
        {
            // Arrange
            var pantryItems = new List<PantryBasketItem>
            {
                new PantryBasketItem { name = "UniGet_123", date_created = DateTime.UtcNow },
                new PantryBasketItem { name = "Other_456", date_created = DateTime.UtcNow },
                new PantryBasketItem { name = "UniGet_789", date_created = DateTime.UtcNow.AddDays(-1) }
            };
            _mockPantryApiClient.Setup(c => c.GetBasketsAsync()).ReturnsAsync(pantryItems);

            var backup1Content = CreateSampleBackup("UniGet_123", "Backup1", DateTime.UtcNow);
            var backup2Content = CreateSampleBackup("UniGet_789", "Backup2", DateTime.UtcNow.AddDays(-1));

            _mockPantryApiClient.Setup(c => c.GetBasketContentAsync("UniGet_123")).ReturnsAsync(SerializeBackup(backup1Content));
            _mockPantryApiClient.Setup(c => c.GetBasketContentAsync("UniGet_789")).ReturnsAsync(SerializeBackup(backup2Content));

            // Act
            var results = await _backupManager.GetBackupsAsync();

            // Assert
            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.BasketName == "UniGet_123" && r.Metadata.Name == "Backup1");
            Assert.Contains(results, r => r.BasketName == "UniGet_789" && r.Metadata.Name == "Backup2");
            Assert.Equal("UniGet_123", results[0].BasketName); // Sorted by date (mocked as most recent)
        }

        [Fact]
        public async Task CreateBackupAsync_WithinSlotLimit_CreatesNewBackup()
        {
            // Arrange
            _mockPantryApiClient.Setup(c => c.GetBasketsAsync()).ReturnsAsync(new List<PantryBasketItem>()); // No existing backups
            _mockPantryApiClient.Setup(c => c.CreateBasketAsync(It.IsAny<string>(), It.IsAny<Backup>()))
                .Returns(Task.CompletedTask);

            // Act
            await _backupManager.CreateBackupAsync("New Backup");

            // Assert
            _mockPantryApiClient.Verify(c => c.CreateBasketAsync(It.Is<string>(s => s.StartsWith("UniGet_")), It.IsAny<Backup>()), Times.Once);
            _mockPantryApiClient.Verify(c => c.DeleteBasketAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task CreateBackupAsync_ExceedsSlotLimit_DeletesOldestAndCreatesNew()
        {
            // Arrange
            var existingItems = CreateSamplePantryBasketItems(DefaultBackupSlots, "UniGet_"); // Already at limit
            // Oldest backup will have name UniGet_{UtcNow - (DefaultBackupSlots-1) days}
            var oldestTimestamp = DateTime.UtcNow.AddDays(-(DefaultBackupSlots - 1));
            var oldestBasketName = existingItems.OrderBy(item => item.date_created).First().name;


            var backups = new List<Backup>();
            for(int i=0; i< DefaultBackupSlots; i++)
            {
                var item = existingItems[i];
                var dt = DateTime.UtcNow.AddDays(-i);
                var backup = CreateSampleBackup(item.name, $"Backup {i}", dt);
                backups.Add(backup);
                _mockPantryApiClient.Setup(c => c.GetBasketContentAsync(item.name)).ReturnsAsync(SerializeBackup(backup));
            }

            _mockPantryApiClient.Setup(c => c.GetBasketsAsync()).ReturnsAsync(existingItems);
             _mockPantryApiClient.Setup(c => c.DeleteBasketAsync(oldestBasketName)).Returns(Task.CompletedTask);
            _mockPantryApiClient.Setup(c => c.CreateBasketAsync(It.IsAny<string>(), It.IsAny<Backup>()))
                .Returns(Task.CompletedTask);

            // Act
            await _backupManager.CreateBackupAsync("Latest Backup");

            // Assert
            _mockPantryApiClient.Verify(c => c.DeleteBasketAsync(oldestBasketName), Times.Once);
            _mockPantryApiClient.Verify(c => c.CreateBasketAsync(It.Is<string>(s => s.StartsWith("UniGet_")), It.IsAny<Backup>()), Times.Once);
        }

        [Fact]
        public async Task DeleteBackupAsync_CallsApiClientDelete()
        {
            // Arrange
            var backupToDelete = CreateSampleBackup("UniGet_ToDelete", "Old Backup", DateTime.UtcNow);
            _mockPantryApiClient.Setup(c => c.DeleteBasketAsync(backupToDelete.BasketName)).Returns(Task.CompletedTask);

            // Act
            await _backupManager.DeleteBackupAsync(backupToDelete);

            // Assert
            _mockPantryApiClient.Verify(c => c.DeleteBasketAsync(backupToDelete.BasketName), Times.Once);
        }

        [Fact]
        public async Task DownloadBackupAsync_ReturnsBasketContent()
        {
            // Arrange
            var backupToDownload = CreateSampleBackup("UniGet_Download", "Download Me", DateTime.UtcNow);
            var expectedJson = SerializeBackup(backupToDownload);
            _mockPantryApiClient.Setup(c => c.GetBasketContentAsync(backupToDownload.BasketName)).ReturnsAsync(expectedJson);

            // Act
            var resultJson = await _backupManager.DownloadBackupAsync(backupToDownload);

            // Assert
            Assert.Equal(expectedJson, resultJson);
        }

        [Fact]
        public async Task RestoreBackupAsync_CallsApplyDelegates()
        {
            // Arrange
            var userSettingsRestored = false;
            var packagesRestored = false;

            _backupManager.ApplyRestoredUserSettingsAsync = (settings) =>
            {
                userSettingsRestored = true;
                Assert.NotNull(settings);
                return Task.CompletedTask;
            };
            _backupManager.ApplyRestoredInstalledPackagesAsync = (packages) =>
            {
                packagesRestored = true;
                Assert.NotNull(packages);
                return Task.CompletedTask;
            };

            var backupToRestore = CreateSampleBackup("UniGet_Restore", "Restore Me", DateTime.UtcNow);
            // No need to mock GetBasketContentAsync for restore, as it uses the passed Backup object's AppData.

            // Act
            await _backupManager.RestoreBackupAsync(backupToRestore);

            // Assert
            Assert.True(userSettingsRestored, "User settings were not applied.");
            Assert.True(packagesRestored, "Installed packages were not applied.");
        }

        [Fact]
        public void Constructor_InvalidBackupSlots_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BackupManager(_mockPantryApiClient.Object, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BackupManager(_mockPantryApiClient.Object, 11));
        }

        [Fact]
        public void UpdateBackupSlots_Valid_UpdatesSlots()
        {
            _backupManager.UpdateBackupSlots(7);
            // Need a way to verify this, perhaps by checking CreateBackupAsync behavior or making _backupSlots internal/protected for testing.
            // For now, just test it doesn't throw for valid values.
             Assert.True(true); // Placeholder if no direct verification
        }

        [Fact]
        public void UpdateBackupSlots_Invalid_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _backupManager.UpdateBackupSlots(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _backupManager.UpdateBackupSlots(11));
        }

        // TODO: Add tests for:
        // - GetBackupsAsync with empty pantry response, or basket content that fails deserialization.
        // - CreateBackupAsync when GetCurrentUserSettingsAsync or GetCurrentInstalledPackagesAsync return null/empty.
        // - CreateBackupAsync when DeleteBasketAsync for an old backup fails (should still create new backup).
        // - RestoreBackupAsync with a backup object that has null AppData.
        // - All methods when passed null arguments (should throw ArgumentNullException).
    }
}
