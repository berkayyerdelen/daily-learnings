// See https://aka.ms/new-console-template for more information

using System.Text;
using System.Text.Json;


using var client = new HttpClient { BaseAddress = new Uri("http://localhost:8474") };

// Create a new proxy
var proxyName = "test_proxy";
var proxyConfig = new
{
    name = proxyName,
    listen = "localhost:5098", // Proxy listening port
    upstream = "localhost:5044" // Target service
};

var createProxyResponse = await client.PostAsync(
    "/proxies",
    new StringContent(JsonSerializer.Serialize(proxyConfig), Encoding.UTF8, "application/json")
);


if (!createProxyResponse.IsSuccessStatusCode)
{
    Console.WriteLine($"Failed to create proxy: {createProxyResponse.StatusCode}");
    return;
}


// Add a latency toxic
var latencyToxicConfig = new
{
    type = "latency",
    attributes = new { latency = 5000 }, // Latency in milliseconds
    stream = "downstream"
};

var addToxicResponse = await client.PostAsync(
    $"/proxies/{proxyName}/toxics",
    new StringContent(JsonSerializer.Serialize(latencyToxicConfig), Encoding.UTF8, "application/json")
);

if (addToxicResponse.IsSuccessStatusCode)
{
    Console.WriteLine($"Latency toxic added to proxy '{proxyName}'.");
}
else
{
    Console.WriteLine($"Failed to add toxic: {addToxicResponse.StatusCode}");
}

// Remove the toxic
var removeToxicResponse = await client.DeleteAsync($"/proxies/{proxyName}/toxics/latency");

if (removeToxicResponse.IsSuccessStatusCode)
{
    Console.WriteLine("Latency toxic removed.");
}
else
{
    Console.WriteLine($"Failed to remove toxic: {removeToxicResponse.StatusCode}");
}