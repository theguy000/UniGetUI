using OnlineBackupSystem.Models;
using OnlineBackupSystem.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace OnlineBackupSystem.ViewModels
{
    public class BackupViewModel : INotifyPropertyChanged
    {
        private readonly BackupManager _backupManager;
        private string _pantryId; // To be provided by application settings

        private ObservableCollection<Backup> _backups;
        public ObservableCollection<Backup> Backups
        {
            get => _backups;
            set { _backups = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNoBackups)); }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        private string _errorMessage;
        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        private bool _pantryIdMissing;
        public bool PantryIdMissing
        {
            get => _pantryIdMissing;
            set { _pantryIdMissing = value; OnPropertyChanged(); }
        }

        public bool HasNoBackups => Backups == null || !Backups.Any();

        // Commands for UI actions will be added later e.g. LoadBackupsCommand, CreateBackupCommand etc.

        public BackupViewModel(BackupManager backupManager, string currentPantryId)
        {
            _backupManager = backupManager ?? throw new ArgumentNullException(nameof(backupManager));
            _pantryId = currentPantryId;
            Backups = new ObservableCollection<Backup>();

            PantryIdMissing = string.IsNullOrWhiteSpace(_pantryId);
            // Initial load can be triggered here or by a command
        }

        // Call this method if the Pantry ID is updated in the application settings
        public void UpdatePantryId(string newPantryId)
        {
            _pantryId = newPantryId;
            PantryIdMissing = string.IsNullOrWhiteSpace(_pantryId);
            // Potentially trigger a reload of backups if ID was missing and now is set
            if (!PantryIdMissing && (Backups == null || !Backups.Any()))
            {
                // Consider how to best trigger LoadBackupsAsync, perhaps via a command or direct call
                // For now, let's assume UI will trigger Load if PantryIdMissing becomes false
            }
        }


        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public async Task LoadBackupsAsync()
        {
            if (PantryIdMissing)
            {
                ErrorMessage = "Pantry ID is not configured.";
                Backups.Clear(); // Clear any existing backups if ID becomes missing
                OnPropertyChanged(nameof(HasNoBackups));
                return;
            }

            IsLoading = true;
            ErrorMessage = null;
            try
            {
                var backups = await _backupManager.GetBackupsAsync();
                Backups.Clear();
                foreach (var backup in backups)
                {
                    Backups.Add(backup);
                }
                OnPropertyChanged(nameof(HasNoBackups)); // Explicitly notify for HasNoBackups
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to load backups: {ex.Message}";
                // Log the full exception somewhere for debugging
                Console.WriteLine(ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task CreateBackupAsync(string backupName)
        {
            if (PantryIdMissing)
            {
                ErrorMessage = "Cannot create backup: Pantry ID is not configured.";
                return;
            }
            if (string.IsNullOrWhiteSpace(backupName))
            {
                ErrorMessage = "Backup name cannot be empty.";
                // Or throw new ArgumentException("Backup name cannot be empty.");
                return;
            }

            IsLoading = true;
            ErrorMessage = null;
            try
            {
                await _backupManager.CreateBackupAsync(backupName);
                await LoadBackupsAsync(); // Refresh the list
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to create backup: {ex.Message}";
                Console.WriteLine(ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task RestoreBackupAsync(Backup backupToRestore)
        {
            if (PantryIdMissing)
            {
                ErrorMessage = "Cannot restore backup: Pantry ID is not configured.";
                return;
            }
            if (backupToRestore == null)
            {
                ErrorMessage = "No backup selected to restore.";
                return;
            }

            IsLoading = true;
            ErrorMessage = null;
            try
            {
                await _backupManager.RestoreBackupAsync(backupToRestore);
                // After restore, the application state changes.
                // The UI might need a general refresh or specific updates.
                // No direct change to Backups list unless specified.
                // For now, just indicate success (implicitly, by no error message)
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to restore backup '{backupToRestore.Metadata?.Name}': {ex.Message}";
                Console.WriteLine(ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task DeleteBackupAsync(Backup backupToDelete)
        {
            if (PantryIdMissing)
            {
                ErrorMessage = "Cannot delete backup: Pantry ID is not configured.";
                return;
            }
            if (backupToDelete == null)
            {
                ErrorMessage = "No backup selected to delete.";
                return;
            }

            IsLoading = true;
            ErrorMessage = null;
            try
            {
                await _backupManager.DeleteBackupAsync(backupToDelete);
                Backups.Remove(backupToDelete); // Optimistically remove from collection
                OnPropertyChanged(nameof(HasNoBackups));
                // Or call LoadBackupsAsync() for consistency, though less efficient.
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to delete backup '{backupToDelete.Metadata?.Name}': {ex.Message}";
                Console.WriteLine(ex);
                // If deletion failed, might need to add it back or reload the list to ensure consistency
                await LoadBackupsAsync();
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task<string> DownloadBackupAsync(Backup backupToDownload)
        {
            if (PantryIdMissing)
            {
                ErrorMessage = "Cannot download backup: Pantry ID is not configured.";
                return null;
            }
            if (backupToDownload == null)
            {
                ErrorMessage = "No backup selected to download.";
                return null;
            }

            IsLoading = true;
            ErrorMessage = null;
            try
            {
                return await _backupManager.DownloadBackupAsync(backupToDownload);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to download backup '{backupToDownload.Metadata?.Name}': {ex.Message}";
                Console.WriteLine(ex);
                return null;
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
