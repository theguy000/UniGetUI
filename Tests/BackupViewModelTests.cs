using Moq;
using OnlineBackupSystem.Models;
using OnlineBackupSystem.Services;
using OnlineBackupSystem.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace OnlineBackupSystem.Tests
{
    public class BackupViewModelTests
    {
        private readonly Mock<BackupManager> _mockBackupManager;
        private BackupViewModel _viewModel;
        private const stringValidPantryId = "valid-pantry-id";

        public BackupViewModelTests()
        {
            // BackupManager constructor needs a PantryApiClient and slots.
            // We mock BackupManager directly, so its internal dependencies don't need full setup unless methods are not virtual/interface based.
            // For simplicity, if BackupManager's constructor is complex, consider an interface IBackupManager.
            // Assuming BackupManager's constructor takes a (possibly null for mock) PantryApiClient and slots.
            var mockPantryClient = new Mock<PantryApiClient>("dummy-id"); // Dummy for BackupManager constructor
            _mockBackupManager = new Mock<BackupManager>(mockPantryClient.Object, 5);
        }

        private void SetupViewModel(string pantryId)
        {
            _viewModel = new BackupViewModel(_mockBackupManager.Object, pantryId);
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

        [Fact]
        public void Constructor_WithValidPantryId_SetsPantryIdMissingToFalse()
        {
            SetupViewModel(constValidPantryId);
            Assert.False(_viewModel.PantryIdMissing);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Constructor_WithInvalidPantryId_SetsPantryIdMissingToTrue(string pantryId)
        {
            SetupViewModel(pantryId);
            Assert.True(_viewModel.PantryIdMissing);
        }

        [Fact]
        public async Task LoadBackupsAsync_PantryIdMissing_SetsErrorAndClearsBackups()
        {
            SetupViewModel(null); // Pantry ID is missing
            _viewModel.Backups.Add(CreateSampleBackup("b1", "b1", DateTime.UtcNow)); // Pre-add a backup

            await _viewModel.LoadBackupsAsync();

            Assert.True(_viewModel.IsLoading); // Should be set to true initially
            Assert.False(string.IsNullOrEmpty(_viewModel.ErrorMessage));
            Assert.Empty(_viewModel.Backups);
            Assert.True(_viewModel.HasNoBackups);
            // IsLoading is set to false in finally block, test might race. Consider explicit check after await.
        }

        [Fact]
        public async Task LoadBackupsAsync_Successful_PopulatesBackupsCollection()
        {
            SetupViewModel(constValidPantryId);
            var sampleBackups = new List<Backup>
            {
                CreateSampleBackup("b1", "Backup 1", DateTime.UtcNow),
                CreateSampleBackup("b2", "Backup 2", DateTime.UtcNow.AddDays(-1))
            };
            _mockBackupManager.Setup(m => m.GetBackupsAsync()).ReturnsAsync(sampleBackups);

            await _viewModel.LoadBackupsAsync();

            Assert.False(_viewModel.IsLoading);
            Assert.Null(_viewModel.ErrorMessage);
            Assert.Equal(2, _viewModel.Backups.Count);
            Assert.False(_viewModel.HasNoBackups);
            Assert.Equal("Backup 1", _viewModel.Backups[0].Metadata.Name);
        }

        [Fact]
        public async Task LoadBackupsAsync_ManagerThrowsException_SetsErrorMessage()
        {
            SetupViewModel(constValidPantryId);
            _mockBackupManager.Setup(m => m.GetBackupsAsync()).ThrowsAsync(new Exception("Test API error"));

            await _viewModel.LoadBackupsAsync();

            Assert.False(_viewModel.IsLoading);
            Assert.Contains("Test API error", _viewModel.ErrorMessage);
            Assert.Empty(_viewModel.Backups);
            Assert.True(_viewModel.HasNoBackups);
        }

        [Fact]
        public async Task CreateBackupAsync_CallsManagerAndRefreshes()
        {
            SetupViewModel(constValidPantryId);
            _mockBackupManager.Setup(m => m.CreateBackupAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
            // Setup GetBackupsAsync to be called after create for refresh
            _mockBackupManager.Setup(m => m.GetBackupsAsync()).ReturnsAsync(new List<Backup>());

            await _viewModel.CreateBackupAsync("New Backup");

            _mockBackupManager.Verify(m => m.CreateBackupAsync("New Backup"), Times.Once);
            _mockBackupManager.Verify(m => m.GetBackupsAsync(), Times.Once); // Verifies refresh
            Assert.Null(_viewModel.ErrorMessage);
        }

        [Fact]
        public async Task DeleteBackupAsync_CallsManagerAndRemovesFromCollection()
        {
            SetupViewModel(constValidPantryId);
            var backupToDelete = CreateSampleBackup("b1", "Backup 1", DateTime.UtcNow);
            _viewModel.Backups.Add(backupToDelete); // Add to collection first

            _mockBackupManager.Setup(m => m.DeleteBackupAsync(backupToDelete)).Returns(Task.CompletedTask);

            await _viewModel.DeleteBackupAsync(backupToDelete);

            _mockBackupManager.Verify(m => m.DeleteBackupAsync(backupToDelete), Times.Once);
            Assert.DoesNotContain(backupToDelete, _viewModel.Backups);
            Assert.Null(_viewModel.ErrorMessage);
        }

        [Fact]
        public async Task DeleteBackupAsync_ManagerThrows_SetsErrorAndReloadsList()
        {
            SetupViewModel(constValidPantryId);
            var backupToDelete = CreateSampleBackup("b1", "Backup 1", DateTime.UtcNow);
             _viewModel.Backups.Add(backupToDelete);

            _mockBackupManager.Setup(m => m.DeleteBackupAsync(backupToDelete)).ThrowsAsync(new Exception("Delete failed"));
            _mockBackupManager.Setup(m => m.GetBackupsAsync()).ReturnsAsync(new List<Backup>{ backupToDelete }); // Simulate it still exists

            await _viewModel.DeleteBackupAsync(backupToDelete);

            Assert.False(_viewModel.IsLoading);
            Assert.Contains("Delete failed", _viewModel.ErrorMessage);
            _mockBackupManager.Verify(m => m.GetBackupsAsync(), Times.Once); // Ensure list was reloaded
            Assert.Contains(backupToDelete, _viewModel.Backups); // Should be re-added after failed delete and reload
        }


        [Fact]
        public async Task RestoreBackupAsync_CallsManager()
        {
            SetupViewModel(constValidPantryId);
            var backupToRestore = CreateSampleBackup("b1", "Restore Me", DateTime.UtcNow);
            _mockBackupManager.Setup(m => m.RestoreBackupAsync(backupToRestore)).Returns(Task.CompletedTask);

            await _viewModel.RestoreBackupAsync(backupToRestore);

            _mockBackupManager.Verify(m => m.RestoreBackupAsync(backupToRestore), Times.Once);
            Assert.Null(_viewModel.ErrorMessage);
        }

        [Fact]
        public async Task DownloadBackupAsync_CallsManagerAndReturnsContent()
        {
            SetupViewModel(constValidPantryId);
            var backupToDownload = CreateSampleBackup("b1", "Download Me", DateTime.UtcNow);
            var expectedContent = "{\"data\":\"content\"}";
            _mockBackupManager.Setup(m => m.DownloadBackupAsync(backupToDownload)).ReturnsAsync(expectedContent);

            var resultContent = await _viewModel.DownloadBackupAsync(backupToDownload);

            _mockBackupManager.Verify(m => m.DownloadBackupAsync(backupToDownload), Times.Once);
            Assert.Equal(expectedContent, resultContent);
            Assert.Null(_viewModel.ErrorMessage);
        }

        [Fact]
        public void UpdatePantryId_ToValid_SetsPantryIdMissingToFalse()
        {
            SetupViewModel(null); // Start with missing ID
            _viewModel.UpdatePantryId(constValidPantryId);
            Assert.False(_viewModel.PantryIdMissing);
        }

        [Fact]
        public void UpdatePantryId_ToInvalid_SetsPantryIdMissingToTrue()
        {
            SetupViewModel(constValidPantryId); // Start with valid ID
            _viewModel.UpdatePantryId("");
            Assert.True(_viewModel.PantryIdMissing);
        }

        // TODO: Test error conditions for Create, Restore, Download (manager throws exception)
        // TODO: Test passing null to Create (name), Restore (backup), Delete (backup), Download (backup)
        // TODO: Test property changed notifications for IsLoading, ErrorMessage, Backups, HasNoBackups, PantryIdMissing
    }
}
