using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OnlineBackupSystem.Models
{
    public class Backup
    {
        [JsonPropertyName("backup_metadata")]
        public BackupMetadata Metadata { get; set; }

        [JsonPropertyName("application_data")]
        public ApplicationData AppData { get; set; }

        // This property is not part of the JSON, but useful for managing the backup object itself.
        // It will be derived from the basket name (e.g., "UniGet_1678886400000").
        [JsonIgnore]
        public string BasketName { get; set; }
    }

    public class BackupMetadata
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } // User-given name/description

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } // ISO 8601 string

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0"; // Default to 1.0 as per spec
    }

    public class ApplicationData
    {
        [JsonPropertyName("user_settings")]
        public object UserSettings { get; set; } // Raw JSON, so object or JsonElement might be appropriate

        [JsonPropertyName("installed_packages")]
        public List<InstalledPackage> InstalledPackages { get; set; }
    }

    public class InstalledPackage
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }
    }
}
