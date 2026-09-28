using Reorder.Core;

var builder = WebApplication.CreateBuilder(args);

// ---- Services -------------------------------------------------------------
builder.Services.AddRazorPages();
builder.Services.AddHealthChecks();   // Day 6 adds a blob-reachability check

builder.Services
    .AddOptions<AdvisorOptions>()
    .Bind(builder.Configuration.GetSection(AdvisorOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();                // bad config fails fast at startup, not mid-run

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

// Endpoints added on later days:
//   Day 2  POST /api/uploads
//   Day 4  POST /api/jobs/run-advisor
//   Day 6  GET  /api/summary, /api/recommendations, /api/recommendations/export.csv

app.Run();

// Makes Program visible to WebApplicationFactory in Reorder.Web.Tests.
public partial class Program { }
