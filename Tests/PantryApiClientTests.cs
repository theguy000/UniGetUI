using Moq;
using Moq.Protected;
using OnlineBackupSystem.Services;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit; // Assuming xUnit for test attributes

namespace OnlineBackupSystem.Tests
{
    public class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handlerFunc;

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc)
        {
            _handlerFunc = handlerFunc;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handlerFunc(request, cancellationToken);
        }
    }

    public class PantryApiClientTests
    {
        private const string TestPantryId = "test-pantry-id";
        private const string PantryBaseUrl = "https://getpantry.cloud/apiv1/pantry/";

        private Mock<HttpMessageHandler> CreateMockHandler(HttpStatusCode statusCode, HttpContent content = null)
        {
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = statusCode,
                    Content = content ?? new StringContent(""),
                });
            return mockHandler;
        }

        private HttpClient CreateHttpClient(HttpMessageHandler handler)
        {
            return new HttpClient(handler) { BaseAddress = new Uri(PantryBaseUrl) };
        }

        [Fact]
        public void Constructor_WithNullOrEmptyPantryId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new PantryApiClient(null));
            Assert.Throws<ArgumentException>(() => new PantryApiClient(""));
            Assert.Throws<ArgumentException>(() => new PantryApiClient("   "));
        }

        [Fact]
        public void PantryId_SetToNullOrEmpty_ThrowsArgumentException()
        {
            var client = new PantryApiClient(TestPantryId);
            Assert.Throws<ArgumentException>(() => client.PantryId = null);
            Assert.Throws<ArgumentException>(() => client.PantryId = "");
        }


        [Fact]
        public async Task GetBasketsAsync_SuccessfulResponse_ReturnsListOfBaskets()
        {
            // Arrange
            var expectedBaskets = new List<PantryBasketItem>
            {
                new PantryBasketItem { name = "UniGet_basket1", date_created = DateTime.UtcNow },
                new PantryBasketItem { name = "other_basket", date_created = DateTime.UtcNow.AddDays(-1) }
            };
            var pantryDetailsResponse = new PantryDetailsResponse { name = "MyPantry", baskets = expectedBaskets };
            var jsonResponse = JsonSerializer.Serialize(pantryDetailsResponse);

            var mockHandler = new MockHttpMessageHandler(async (request, token) =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal($"{PantryBaseUrl}{TestPantryId}", request.RequestUri.ToString());
                return new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent(jsonResponse) };
            });
            var httpClient = CreateHttpClient(mockHandler); // Pass the handler directly

            // Use reflection to set the private _httpClient field or make it internal for testing
            var client = new PantryApiClient(TestPantryId);
            var httpClientField = typeof(PantryApiClient).GetField("_httpClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            httpClientField.SetValue(client, httpClient);

            // Act
            var result = await client.GetBasketsAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal("UniGet_basket1", result[0].name);
        }

        [Fact]
        public async Task GetBasketsAsync_ApiReturnsError_ThrowsHttpRequestException()
        {
            // Arrange
             var mockHandler = new MockHttpMessageHandler(async (request, token) =>
            {
                return new HttpResponseMessage { StatusCode = HttpStatusCode.InternalServerError };
            });
            var httpClient = CreateHttpClient(mockHandler);
            var client = new PantryApiClient(TestPantryId);
            var httpClientField = typeof(PantryApiClient).GetField("_httpClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            httpClientField.SetValue(client, httpClient);

            // Act & Assert
            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetBasketsAsync());
        }

        [Fact]
        public async Task GetBasketContentAsync_SuccessfulResponse_ReturnsJsonString()
        {
            // Arrange
            var basketName = "testBasket";
            var expectedContent = "{\"key\":\"value\"}";
            var mockHandler = new MockHttpMessageHandler(async (request, token) =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal($"{PantryBaseUrl}{TestPantryId}/basket/{basketName}", request.RequestUri.ToString());
                return new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent(expectedContent) };
            });
            var httpClient = CreateHttpClient(mockHandler);
            var client = new PantryApiClient(TestPantryId);
            var httpClientField = typeof(PantryApiClient).GetField("_httpClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            httpClientField.SetValue(client, httpClient);

            // Act
            var result = await client.GetBasketContentAsync(basketName);

            // Assert
            Assert.Equal(expectedContent, result);
        }

        [Fact]
        public async Task CreateBasketAsync_Successful_DoesNotThrow()
        {
            // Arrange
            var basketName = "newBasket";
            var data = new { message = "hello" };
             var mockHandler = new MockHttpMessageHandler(async (request, token) =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal($"{PantryBaseUrl}{TestPantryId}/basket/{basketName}", request.RequestUri.ToString());
                var requestContent = await request.Content.ReadAsStringAsync();
                Assert.Equal(JsonSerializer.Serialize(data), requestContent);
                return new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent("Basket created") }; // Pantry returns a string message
            });
            var httpClient = CreateHttpClient(mockHandler);
            var client = new PantryApiClient(TestPantryId);
            var httpClientField = typeof(PantryApiClient).GetField("_httpClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            httpClientField.SetValue(client, httpClient);

            // Act & Assert
            await client.CreateBasketAsync(basketName, data); // Should not throw
        }

        [Fact]
        public async Task DeleteBasketAsync_Successful_DoesNotThrow()
        {
            // Arrange
            var basketName = "oldBasket";
            var mockHandler = new MockHttpMessageHandler(async (request, token) =>
            {
                Assert.Equal(HttpMethod.Delete, request.Method);
                Assert.Equal($"{PantryBaseUrl}{TestPantryId}/basket/{basketName}", request.RequestUri.ToString());
                return new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent("Basket deleted") }; // Pantry returns a string message
            });
            var httpClient = CreateHttpClient(mockHandler);
            var client = new PantryApiClient(TestPantryId);
            var httpClientField = typeof(PantryApiClient).GetField("_httpClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            httpClientField.SetValue(client, httpClient);

            // Act & Assert
            await client.DeleteBasketAsync(basketName); // Should not throw
        }

        // Add more tests for:
        // - Invalid JSON response from GetBasketsAsync
        // - API errors (400, 401, 404, 500) for each method
        // - Empty basket name for GetBasketContentAsync, CreateBasketAsync, DeleteBasketAsync (should throw ArgumentException before HTTP call)
    }
}
