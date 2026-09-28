using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Reorder.Core;
using Reorder.Web.Endpoints;
using Reorder.Web.Infrastructure;
using Reorder.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Services -------------------------------------------------------------
builder.Services.AddRazorPages();
builder.Services.AddHealthChecks();   // Day 6 adds a blob-reachability check

builder.Services
    .AddOptions<AdvisorOptions>()
    .Bind(builder.Configuration.GetSection(AdvisorOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();                // bad config fails fast at startup, not mid-run

// Ingestion (Day 2): blob storage + CSV validation.
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));
builder.Services.AddSingleton<IBlobStore, BlobStore>();   // BlobServiceClient is thread-safe
builder.Services.AddScoped<IngestionService>();
builder.Services.AddScoped<AdvisorJob>();   // Day 4: runs the advisor and writes latest.json

// Cap the multipart upload of the three CSVs at 10 MB.
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 10 * 1024 * 1024);

// Emit reject reasons as their string codes (e.g. "DUPLICATE_KEY") in JSON responses,
// matching the quarantine blob.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ---- Pipeline -------------------------------------------------------------
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapRazorPages();
app.MapHealthChecks("/health");

app.MapUploadEndpoints(app.Configuration["Security:AdminKey"] ?? string.Empty);
app.MapJobEndpoints(app.Configuration["Security:JobKey"] ?? string.Empty);

// Endpoints added on later days:
//   Day 6  GET  /api/summary, /api/recommendations, /api/recommendations/export.csv

app.Run();

// Makes Program visible to WebApplicationFactory in Reorder.Web.Tests.
public partial class Program { }
