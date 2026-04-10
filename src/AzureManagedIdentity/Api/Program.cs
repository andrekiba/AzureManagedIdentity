using Azure.Data.Tables;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Azure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddConsole();
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.All;
});

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddAzureClients(azureBuilder =>
{
    var storageAccountName = builder.Configuration["Azure:StorageAccountName"];

    azureBuilder.AddBlobServiceClient(new Uri($"https://{storageAccountName}.blob.core.windows.net"))
        .WithName("blob")
        .WithCredential(new Azure.Identity.DefaultAzureCredential());

    azureBuilder.AddQueueServiceClient(new Uri($"https://{storageAccountName}.queue.core.windows.net"))
        .WithName("queue")
        .WithCredential(new Azure.Identity.DefaultAzureCredential());

    azureBuilder.AddTableServiceClient(new Uri($"https://{storageAccountName}.table.core.windows.net"))
        .WithName("table")
        .WithCredential(new Azure.Identity.DefaultAzureCredential());

    var keyVaultName = builder.Configuration["Azure:KeyVaultName"];
    var keyVaultUri = new Uri($"https://{keyVaultName}.vault.azure.net");

    azureBuilder.AddSecretClient(keyVaultUri)
        .WithName("secrets")
        .WithCredential(new Azure.Identity.DefaultAzureCredential());

    azureBuilder.AddKeyClient(keyVaultUri)
        .WithName("keys")
        .WithCredential(new Azure.Identity.DefaultAzureCredential());

    azureBuilder.AddCertificateClient(keyVaultUri)
        .WithName("certificates")
        .WithCredential(new Azure.Identity.DefaultAzureCredential());
});

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();

app.MapGet("/blobs", (IAzureClientFactory<BlobServiceClient> factory) => {
    var client = factory.CreateClient("blob");
    // ...
});

app.MapGet("/queues", (IAzureClientFactory<QueueServiceClient> factory) => {
    var client = factory.CreateClient("queue");
    // ...
});

app.MapGet("/tables", (IAzureClientFactory<TableServiceClient> factory) => {
    var client = factory.CreateClient("table");
    // ...
});

app.MapGet("/secrets", (IAzureClientFactory<SecretClient> factory) => {
    var client = factory.CreateClient("secrets");
    // ...
});

app.MapGet("/keys", (IAzureClientFactory<KeyClient> factory) => {
    var client = factory.CreateClient("keys");
    // ...
});

app.MapGet("/certificates", (IAzureClientFactory<CertificateClient> factory) => {
    var client = factory.CreateClient("certificates");
    // ...
});

app.Run();

