using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UniGetUI.Core.Tools;
using UniGetUI.Core.SettingsEngine;
using UniGetUI.Core.Data;
using System.Diagnostics;
using UniGetUI.Pages.DialogPages;
using UniGetUI.Interface.SoftwarePages;
using UniGetUI.PackageEngine.Enums;
using UniGetUI.PackageEngine.Enums;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using UniGetUI.PackageEngine;


// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace UniGetUI.Pages.SettingsPages.GeneralPages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class Backup : Page, ISettingsPage
    {
        private static readonly HttpClient client = new();
        private const string PANTRY_API_URL = "https://getpantry.cloud/apiv1/pantry/";
        private const string BASKET_NAME = "UniGetUISettings";

        public Backup()
        {
            this.InitializeComponent();

            PantryIdPasswordBox.Password = Settings.GetValue(Settings.K.PantryId);


            EnablePackageBackupUI(Settings.Get(Settings.K.EnablePackageBackup));
            ResetBackupDirectory.Content = CoreTools.Translate("Reset");
            OpenBackupDirectory.Content = CoreTools.Translate("Open");
        }

        public bool CanGoBack => true;

        public string ShortTitle => CoreTools.Translate("Package backup");

        public event EventHandler? RestartRequired;
        public event EventHandler<Type>? NavigationRequested;

        public void ShowRestartBanner(object sender, EventArgs e)
            => RestartRequired?.Invoke(this, e);

        private void ChangeBackupDirectory_Click(object sender, EventArgs e)
        {
            ExternalLibraries.Pickers.FolderPicker openPicker = new(MainApp.Instance.MainWindow.GetWindowHandle());
            string folder = openPicker.Show();
            if (folder != string.Empty)
            {
                Settings.SetValue(Settings.K.ChangeBackupOutputDirectory, folder);
                BackupDirectoryLabel.Text = folder;
                ResetBackupDirectory.IsEnabled = true;
            }
        }

        public void EnablePackageBackupUI(bool enabled)
        {
            EnableBackupTimestampingCheckBox.IsEnabled = enabled;
            ChangeBackupFileNameTextBox.IsEnabled = enabled;
            ChangeBackupDirectory.IsEnabled = enabled;
            BackupNowButton.IsEnabled = enabled;

            if (enabled)
            {
                if (!Settings.Get(Settings.K.ChangeBackupOutputDirectory))
                {
                    BackupDirectoryLabel.Text = CoreData.UniGetUI_DefaultBackupDirectory;
                    ResetBackupDirectory.IsEnabled = false;
                }
                else
                {
                    BackupDirectoryLabel.Text = Settings.GetValue(Settings.K.ChangeBackupOutputDirectory);
                    ResetBackupDirectory.IsEnabled = true;
                }
            }
        }

        private void ResetBackupPath_Click(object sender, RoutedEventArgs e)
        {
            BackupDirectoryLabel.Text = CoreData.UniGetUI_DefaultBackupDirectory;
            Settings.Set(Settings.K.ChangeBackupOutputDirectory, false);
            ResetBackupDirectory.IsEnabled = false;
        }

        private void OpenBackupPath_Click(object sender, RoutedEventArgs e)
        {
            string directory = Settings.GetValue(Settings.K.ChangeBackupOutputDirectory);
            if (directory == "")
            {
                directory = CoreData.UniGetUI_DefaultBackupDirectory;
            }

            directory = directory.Replace("/", "\\");

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Process.Start("explorer.exe", directory);
        }

        private void DoBackup_Click(object sender, EventArgs e)
        {
            DialogHelper.ShowLoadingDialog(CoreTools.Translate("Performing backup, please wait..."));
            _ = InstalledPackagesPage.BackupPackages();
            DialogHelper.HideLoadingDialog();
        }

        private async void BackupToPantry_Click(object sender, EventArgs e)
        {
            string? pantryId = await GetAndValidatePantryIdAsync();
            if (pantryId is null)
                return;

            DialogHelper.ShowLoadingDialog(CoreTools.Translate("Backing up installed packages to Pantry..."));
            try
            {
                var packages = PEInterface.InstalledPackagesLoader.Packages.ToList();
                string bundleJson = await PackageBundlesPage.CreateBundle(packages, BundleFormatType.UBUNDLE);

                HttpRequestMessage request = new(HttpMethod.Post, $"{PANTRY_API_URL}{pantryId}/basket/{BASKET_NAME}")
                {
                    Content = new StringContent(bundleJson, Encoding.UTF8, "application/json")
                };

                HttpResponseMessage response = await client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    ShowSuccess(CoreTools.Translate("Installed packages backed up to Pantry successfully."));
                }
                else
                {
                    ShowError(CoreTools.Translate($"Failed to back up to Pantry. Status: {response.StatusCode}"));
                }
            }
            catch (Exception ex)
            {
                ShowError(CoreTools.Translate($"An error occurred: {ex.Message}"));
            }
            finally
            {
                DialogHelper.HideLoadingDialog();
            }
        }

        private async void RestoreBundleFromPantry_Click(object sender, EventArgs e)
        {
            string? pantryId = await GetAndValidatePantryIdAsync();
            if (pantryId is null) return;

            string directory = Settings.GetValue(Settings.K.ChangeBackupOutputDirectory);
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = CoreData.UniGetUI_DefaultBackupDirectory;
            }
            directory = directory.Replace("/", "\\");
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            string bundlePath = Path.Combine(directory, $"PantryBackup_{DateTime.Now:yyyyMMdd_HHmmss}.ubundle");

            DialogHelper.ShowLoadingDialog(CoreTools.Translate("Downloading bundle from Pantry..."));
            try
            {
                HttpRequestMessage request = new(HttpMethod.Get, $"{PANTRY_API_URL}{pantryId}/basket/{BASKET_NAME}");
                HttpResponseMessage response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    string bundleJson = await response.Content.ReadAsStringAsync();
                    if (string.IsNullOrWhiteSpace(bundleJson) || bundleJson.Trim() == "{}")
                    {
                        ShowError(CoreTools.Translate("Downloaded bundle is empty or invalid. Please check your Pantry contents."));
                        return;
                    }
                    await File.WriteAllTextAsync(bundlePath, bundleJson);
                    PackageBundlesPage.OpenBundle(bundlePath);
                    ShowSuccess(CoreTools.Translate("Bundle restored and opened successfully."));
                }
                else
                {
                    string errorMessage = await response.Content.ReadAsStringAsync();
                    ShowError(CoreTools.Translate($"Failed to download bundle: {errorMessage}"));
                }
            }
            catch (Exception ex)
            {
                ShowError(CoreTools.Translate($"An error occurred: {ex.Message}"));
            }
            finally
            {
                DialogHelper.HideLoadingDialog();
            }
        }

        private async Task<string?> GetAndValidatePantryIdAsync()
        {
            string pantryId = PantryIdPasswordBox.Password;
            if (string.IsNullOrWhiteSpace(pantryId))
            {
                ShowError(CoreTools.Translate("Pantry ID cannot be empty."));
                return null;
            }
            Settings.SetValue(Settings.K.PantryId, pantryId);
            return pantryId;
        }

        private void ShowError(string message)
        {
            Debug.WriteLine($"Error: {message}");
        }

        private void ShowSuccess(string message)
        {
            Debug.WriteLine($"Success: {message}");
        }
    }
}
