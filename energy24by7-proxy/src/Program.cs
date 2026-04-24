using System.Text.Json;
using System.Text.Json.Serialization;
using Energy24by7Proxy;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

// Config from environment variables
var config = new AppConfig
{
    Email    = Environment.GetEnvironmentVariable("E24_EMAIL")    ?? "",
    Password = Environment.GetEnvironmentVariable("E24_PASSWORD") ?? "",
    DeviceId = Environment.GetEnvironmentVariable("E24_DEVICE_ID") ?? "7b8a4e55-04d3-4add-8c2f-74029af8ccf9",
    FetchIntervalSeconds = int.TryParse(Environment.GetEnvironmentVariable("FETCH_INTERVAL"), out var fi) ? fi : 900
};

builder.Services.AddSingleton(config);
builder.Services.AddSingleton<SolarDataCache>();
builder.Services.AddHostedService<SolarFetchService>();

var app = builder.Build();

var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

// GET /solar — today's data
app.MapGet("/solar", (SolarDataCache cache) =>
{
    var snapshot = cache.GetSnapshot();
    if (snapshot.Today is null)
        return Results.Json(new { error = snapshot.Error ?? "Data not yet available" }, statusCode: 503);

    return Results.Json(new
    {
        last_updated = snapshot.LastUpdated,
        device = snapshot.Device is null ? null : new
        {
            battery_percent = snapshot.Device.BatteryPercent,
            current_state   = snapshot.Device.CurrentState,
            active_since    = snapshot.Device.ActiveSince,
            device_id       = snapshot.Device.DeviceId
        },
        today = new
        {
            total_energy_kwh  = snapshot.Today.TotalEnergy,
            total_utility_kwh = snapshot.Today.TotalUtility,
            carbon_offset_kg  = snapshot.Today.CarbonOffset,
            trees_saved       = snapshot.Today.TreesSaved,
            hourly_labels     = snapshot.Today.Labels,
            hourly_energy     = snapshot.Today.Data,
            hourly_utility    = snapshot.Today.Utility,
        },
        monthly = snapshot.Monthly is null ? null : new
        {
            total_energy_kwh  = snapshot.Monthly.TotalEnergy,
            total_utility_kwh = snapshot.Monthly.TotalUtility,
            carbon_offset_kg  = snapshot.Monthly.CarbonOffset,
            trees_saved       = snapshot.Monthly.TreesSaved,
            monthly_labels    = snapshot.Monthly.Labels,
            monthly_energy    = snapshot.Monthly.Data,
        }
    }, jsonOptions);
});

// GET /health
app.MapGet("/health", (SolarDataCache cache) =>
{
    var snapshot = cache.GetSnapshot();
    return Results.Json(new
    {
        status       = snapshot.Today is not null ? "ok" : "pending",
        last_updated = snapshot.LastUpdated,
        error        = snapshot.Error
    });
});

app.Run();
