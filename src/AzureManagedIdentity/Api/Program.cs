using Azure.Data.Tables;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Azure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddConsole();
builder.Services.AddHttpLogging(options => { options.LoggingFields = HttpLoggingFields.All; });

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

    var serviceBusNamespace = builder.Configuration["Azure:ServiceBusNamespace"];
    azureBuilder.AddServiceBusAdministrationClientWithNamespace($"{serviceBusNamespace}.servicebus.windows.net")
        .WithName("bus-admin")
        .WithCredential(new Azure.Identity.DefaultAzureCredential());
    azureBuilder.AddServiceBusClientWithNamespace($"{serviceBusNamespace}.servicebus.windows.net")
        .WithName("bus")
        .WithCredential(new Azure.Identity.DefaultAzureCredential());

    var eventHubNamespace = builder.Configuration["Azure:EventHubNamespace"];
    var eventHubName = builder.Configuration["Azure:EventHubName"];
    azureBuilder.AddEventHubProducerClientWithNamespace($"{eventHubNamespace}.servicebus.windows.net", eventHubName)
        .WithName("hub")
        .WithCredential(new Azure.Identity.DefaultAzureCredential());
});

var sqlServerName = builder.Configuration["Azure:SqlServerName"];
var sqlDatabaseName = builder.Configuration["Azure:SqlDatabaseName"];
var sqlConnectionString =
    $"Server=tcp:{sqlServerName}.database.windows.net,1433;Database={sqlDatabaseName};Authentication=Active Directory Default;Encrypt=True;";

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();

app.MapGet("/blobs", async (IAzureClientFactory<BlobServiceClient> factory) =>
{
    var client = factory.CreateClient("blob");
    var containers = await client.GetBlobContainersAsync().ToListAsync();
    if (containers.All(c => c.Name != "test"))
        await client.CreateBlobContainerAsync("test");
    var containerClient = client.GetBlobContainerClient("test");
    var blobClient = containerClient.GetBlobClient("hello.txt");
    var result = await blobClient.UploadAsync(BinaryData.FromString("Ciao!"), overwrite: true);
    return $"Blob versione {result.Value.VersionId} aggiunto con successo in data: {result.Value.LastModified}.";
});

app.MapGet("/queues", async (IAzureClientFactory<QueueServiceClient> factory) =>
{
    var client = factory.CreateClient("queue");
    var queues = await client.GetQueuesAsync().ToListAsync();
    if (queues.All(c => c.Name != "test"))
        await client.CreateQueueAsync("test");
    var queueClient = client.GetQueueClient("test");
    var result = await queueClient.SendMessageAsync("Ciao!");
    return $"Messaggio {result.Value.MessageId} accodato.";
});

app.MapGet("/tables", async (IAzureClientFactory<TableServiceClient> factory) =>
{
    var client = factory.CreateClient("table");
    await client.CreateTableIfNotExistsAsync("test");
    var tableClient = client.GetTableClient("test");
    var result = await tableClient.AddEntityAsync(new TableEntity("partition", Guid.NewGuid().ToString())
        { { "Message", "Ciao!" } });
    return result.IsError
        ? $"Errore durante la creazione del record: {result.ReasonPhrase}"
        : "Record creato con successo.";
});

app.MapGet("/secrets", async (IAzureClientFactory<SecretClient> factory) =>
{
    var client = factory.CreateClient("secrets");
    var result = await client.SetSecretAsync("test", "Ciao!");
    return $"Secret {result.Value.Name}:{result.Value.Value} creato." ; 
});

app.MapGet("/keys", async (IAzureClientFactory<KeyClient> factory) =>
{
    var client = factory.CreateClient("keys");
    var result = await client.CreateRsaKeyAsync(new CreateRsaKeyOptions("test") { KeySize = 2048 });
    return $"Key \"{result.Value.Name}\" creata." ; 
});

app.MapGet("/certificates", async (IAzureClientFactory<CertificateClient> factory) =>
{
    var client = factory.CreateClient("certificates");
    var cert = await client.GetCertificateAsync("test1");
    if (cert.HasValue)
        return $"Certificato {cert.Value.Name} già esistente con ID {cert.Value.Id}.";
    
    var certOperation = await client.StartCreateCertificateAsync("test1", 
        new CertificatePolicy(WellKnownIssuerNames.Self, "CN=test1") { Exportable = true });
    return $"Operazione di creazione del certificato {certOperation.Id} in sospeso.";
});

app.MapGet("/bus", async (IAzureClientFactory<ServiceBusAdministrationClient> adminFactory,
    IAzureClientFactory<ServiceBusClient> factory) =>
{
    const string queueName = "gab26";
    var adminClient = adminFactory.CreateClient("bus-admin");
    if (!await adminClient.QueueExistsAsync(queueName))
        await adminClient.CreateQueueAsync(queueName);
    var client = factory.CreateClient("bus");
    var sender = client.CreateSender(queueName);
    await sender.SendMessageAsync(new ServiceBusMessage("Ciao!"));
});

app.MapGet("/hub", async (IAzureClientFactory<EventHubProducerClient> factory) =>
{
    var client = factory.CreateClient("hub");
    using var batch = await client.CreateBatchAsync();
    batch.TryAdd(new EventData("Ciao!"));
    await client.SendAsync(batch);
});

app.MapGet("/sql", async () =>
{
    await using var connection = new SqlConnection(sqlConnectionString);
    await connection.OpenAsync();
    await using var command = new SqlCommand("SELECT DB_NAME(), SUSER_SNAME()", connection);
    await using var reader = await command.ExecuteReaderAsync();
    await reader.ReadAsync();
    return new { Database = reader.GetString(0), User = reader.GetString(1) };
});

app.Run();