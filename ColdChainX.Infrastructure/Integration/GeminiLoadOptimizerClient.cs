using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ColdChainX.Core.Entities;

namespace ColdChainX.Infrastructure.Integration;

public class GeminiLoadOptimizerClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public GeminiLoadOptimizerClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
    }

    public async Task<string> OptimizeLoadPlanAsync(Vehicle vehicle, List<TransportOrder> orders, List<Guid> stopSequenceLocationIds)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            throw new InvalidOperationException("Gemini API Key is not configured.");
        }

        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-pro-latest:generateContent?key={_apiKey}";

        // Construct a prompt based on vehicle dimensions (simulated from MaxCbm) and orders
        // Note: For actual 3D layout, we mock dimensions L x W x H if they are not present.
        var vehicleLength = 3.0m;
        var vehicleWidth = 1.8m;
        var vehicleHeight = vehicle.MaxCbm / (vehicleLength * vehicleWidth);

        var prompt = $@"
You are a highly advanced Cargo Load Optimizer AI.
Please calculate the 3D packing plan and provide a LIFO loading sequence.
Container Dimensions: Length: {vehicleLength}m, Width: {vehicleWidth}m, Height: {vehicleHeight}m, Total Volume: {vehicle.MaxCbm}m3, Max Weight: {vehicle.MaxWeight}kg.
Orders to pack:
";
        foreach (var order in orders)
        {
            // Calculate a mock dimension based on expected Cbm
            var dim = Math.Pow((double)order.ExpectedCbm, 1.0 / 3.0);
            prompt += $"- OrderId: {order.OrderId}, Item: {order.ItemName}, Quantity: {order.Quantity}, CBM: {order.ExpectedCbm}, Weight: {order.ExpectedWeightKg}kg, DestLocationId: {order.DestLocation}\n";
        }

        prompt += $"\nLoading priority based on LIFO route sequence (Load the last stop FIRST, deep in the truck):\n";
        for (int i = 0; i < stopSequenceLocationIds.Count; i++)
        {
            prompt += $"{i + 1}. LocationId: {stopSequenceLocationIds[i]}\n";
        }

        prompt += @"
Please provide the loading instructions in JSON format indicating if they all fit and the sequence to load them. Format:
{
  ""success"": true/false,
  ""reason"": ""if failed, why"",
  ""loadSequence"": [
    { ""orderId"": ""uuid"", ""position"": ""back/middle/front"" }
  ]
}";

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            }
        };

        var response = await _httpClient.PostAsJsonAsync(endpoint, requestBody);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        
        // Extract the JSON text from the response
        try
        {
            using var document = JsonDocument.Parse(responseContent);
            var text = document.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
            
            // Clean markdown block if present
            if (text != null && text.StartsWith("```json"))
            {
                text = text.Replace("```json", "").Replace("```", "").Trim();
            }
            
            return text ?? "{}";
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to parse Gemini response. Response: {responseContent}", ex);
        }
    }
}
