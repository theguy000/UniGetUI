using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OnlineBackupSystem.Models; // Assuming PantryBasket model will be here or a similar location

namespace OnlineBackupSystem.Services
{
    // Basic structure for what a Pantry API response might look like for listing baskets.
    // This might need adjustment based on the actual Pantry API response format.
    public class PantryBasketItem
    {
        public string name { get; set; }
        public DateTime date_created { get; set; } // Example, adjust as per actual API
        // Add other relevant fields from the Pantry API's list baskets response
    }

    public class PantryApiClient
    {
        private readonly HttpClient _httpClient;
        private string _pantryId;
        private const string BaseUrl = "https://getpantry.cloud/apiv1/pantry/";

        public string PantryId
        {
            get => _pantryId;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException("Pantry ID cannot be null or whitespace.", nameof(value));
                }
                _pantryId = value;
            }
        }

        public PantryApiClient(string pantryId)
        {
            if (string.IsNullOrWhiteSpace(pantryId))
            {
                throw new ArgumentException("Pantry ID must be provided.", nameof(pantryId));
            }
            _pantryId = pantryId;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private Uri GetPantryUri(string path = "")
        {
            if (string.IsNullOrEmpty(PantryId))
            {
                throw new InvalidOperationException("Pantry ID is not set.");
            }
            return new Uri($"{BaseUrl}{PantryId}{path}");
        }

        private Uri GetBasketUri(string basketName, string path = "")
        {
            if (string.IsNullOrEmpty(PantryId))
            {
                throw new InvalidOperationException("Pantry ID is not set.");
            }
            if (string.IsNullOrEmpty(basketName))
            {
                throw new ArgumentException("Basket name cannot be null or empty.", nameof(basketName));
            }
            return new Uri($"{BaseUrl}{PantryId}/basket/{basketName}{path}");
        }

        public async Task<List<PantryBasketItem>> GetBasketsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync(GetPantryUri());
                response.EnsureSuccessStatusCode(); // Throws if HTTP response status is not a success code.

                var content = await response.Content.ReadAsStringAsync();
                // Assuming the API returns an object that has a "baskets" property which is a list
                // This will need to be adjusted based on the actual Pantry API response structure
                var pantryDetails = JsonSerializer.Deserialize<PantryDetailsResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return pantryDetails?.baskets ?? new List<PantryBasketItem>();
            }
            catch (HttpRequestException e)
            {
                // Log error or handle more gracefully
                Console.WriteLine($"Request error: {e.Message}");
                throw; // Re-throw for now, or handle as per application error strategy
            }
            catch (JsonException e)
            {
                Console.WriteLine($"JSON parsing error: {e.Message}");
                throw;
            }
        }

        public async Task<string> GetBasketContentAsync(string basketName)
        {
            try
            {
                var response = await _httpClient.GetAsync(GetBasketUri(basketName));
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine($"Request error getting basket content: {e.Message}");
                throw;
            }
        }

        public async Task CreateBasketAsync(string basketName, object data)
        {
            try
            {
                var jsonData = JsonSerializer.Serialize(data);
                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(GetBasketUri(basketName), content);
                response.EnsureSuccessStatusCode();
                // Optionally, return the response content if the API sends back useful info (e.g., "Basket 'X' created.")
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine($"Request error creating basket: {e.Message}");
                throw;
            }
            catch (JsonException e)
            {
                Console.WriteLine($"JSON serialization error: {e.Message}");
                throw;
            }
        }

        public async Task DeleteBasketAsync(string basketName)
        {
            try
            {
                var response = await _httpClient.DeleteAsync(GetBasketUri(basketName));
                response.EnsureSuccessStatusCode();
                // Optionally, return the response content or status
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine($"Request error deleting basket: {e.Message}");
                throw;
            }
        }
    }

    // Helper class to deserialize the Pantry details response, specifically the list of baskets.
    // Adjust this based on the actual structure of the Pantry API's response when listing pantry details.
    public class PantryDetailsResponse
    {
        public string name { get; set; }
        public string description { get; set; }
        public List<PantryBasketItem> baskets { get; set; }
        // Add any other fields returned by the GET /pantry/{pantry_id} endpoint
    }
}
