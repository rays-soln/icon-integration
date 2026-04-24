using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;
using HtmlAgilityPack;

namespace Energy24by7Proxy;

public class SolarFetchService(
    AppConfig config,
    SolarDataCache cache,
    ILogger<SolarFetchService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await FetchAndCacheAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(config.FetchIntervalSeconds), stoppingToken);
        }
    }

    private async Task FetchAndCacheAsync(CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Starting fetch cycle...");

            // Each cycle gets a fresh handler with its own cookie jar
            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
                AllowAutoRedirect = true
            };

            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://customer-portal.energy24by7.com"),
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            // Step 1: GET login page — extract CSRF token
            var loginPage = await client.GetStringAsync("/login", ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(loginPage);
            var tokenNode = doc.DocumentNode.SelectSingleNode("//input[@name='_token']");
            var csrfToken = tokenNode?.GetAttributeValue("content", null)
                         ?? tokenNode?.GetAttributeValue("value", null);

            if (string.IsNullOrEmpty(csrfToken))
                throw new Exception("CSRF token not found in login page");

            logger.LogInformation("CSRF token obtained");

            // Step 2: POST login
            var formData = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["_token"]   = csrfToken,
                ["email"]    = config.Email,
                ["password"] = config.Password
            });

            var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/login") { Content = formData };
            loginRequest.Headers.Add("Referer", "https://customer-portal.energy24by7.com/login");
            var loginResp = await client.SendAsync(loginRequest, ct);

            var finalUrl = loginResp.RequestMessage?.RequestUri?.AbsolutePath ?? "";
            if (finalUrl.TrimEnd('/') == "/login")
            {
                var loginHtml = await loginResp.Content.ReadAsStringAsync(ct);
                var errDoc = new HtmlDocument();
                errDoc.LoadHtml(loginHtml);
                var errorNode = errDoc.DocumentNode.SelectSingleNode("//div[contains(@class,'alert')]")
                             ?? errDoc.DocumentNode.SelectSingleNode("//*[contains(@class,'error')]");
                var errorMsg = errorNode?.InnerText.Trim() ?? "(no error message found in page)";
                throw new Exception($"Login failed — {errorMsg}");
            }

            logger.LogInformation("Login successful (landed on {Path}), fetching solar data...", finalUrl);

            // Step 3: Fetch solar + device data
            var today   = await FetchSolarData(client, "today",   config.DeviceId, ct);
            var monthly = await FetchSolarData(client, "monthly", config.DeviceId, ct);
            var device  = await FetchDeviceStatus(client, config.DeviceId, ct);

            cache.Update(today, monthly, device);
            logger.LogInformation("Cache updated — today: {Energy} kWh, battery: {Battery}",
                today.TotalEnergy, device.BatteryPercent);

            // Step 4: Logout
            await LogoutAsync(client, csrfToken, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fetch cycle failed");
            cache.SetError(ex.Message);
        }
    }

    private async Task<DeviceStatus> FetchDeviceStatus(HttpClient client, string deviceId, CancellationToken ct)
    {
        var html = await client.GetStringAsync($"/device-overview/{deviceId}", ct);
        var doc  = new HtmlDocument();
        doc.LoadHtml(html);

        string Label(string label) =>
            doc.DocumentNode
               .SelectSingleNode($"//div[@class='info-label'][normalize-space(text())='{label}']/following-sibling::div[1]")
               ?.InnerText.Trim() ?? "";

        return new DeviceStatus
        {
            BatteryPercent = Label("Battery Status"),
            CurrentState   = Label("Current State"),
            ActiveSince    = Label("Active Since"),
            DeviceId       = Label("Device ID")
        };
    }

    private async Task LogoutAsync(HttpClient client, string csrfToken, CancellationToken ct)
    {
        try
        {
            var formData = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["_token"] = csrfToken
            });
            var req = new HttpRequestMessage(HttpMethod.Post, "/logout") { Content = formData };
            req.Headers.Add("Referer", "https://customer-portal.energy24by7.com/energy-overview");
            await client.SendAsync(req, ct);
            logger.LogInformation("Logout successful");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Logout failed (non-fatal)");
        }
    }

    private async Task<SolarApiResponse> FetchSolarData(
        HttpClient client, string filterType, string deviceId, CancellationToken ct)
    {
        var url = $"/solar-energy-data?filterType={filterType}&deviceId={deviceId}";
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Referer", "https://customer-portal.energy24by7.com/energy-overview");
        req.Headers.Add("X-Requested-With", "XMLHttpRequest");

        var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!body.TrimStart().StartsWith('{') && !body.TrimStart().StartsWith('['))
        {
            logger.LogError("Expected JSON but got unexpected response for {Filter}: {Preview}",
                filterType, body[..Math.Min(500, body.Length)]);
            throw new Exception($"Solar data endpoint returned non-JSON for filterType={filterType}");
        }

        return JsonSerializer.Deserialize<SolarApiResponse>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new Exception("Failed to deserialize solar data");
    }
}
