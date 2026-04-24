using System.Text.Json.Serialization;

namespace Energy24by7Proxy;

public class AppConfig
{
    public string Email    { get; set; } = "";
    public string Password { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public int FetchIntervalSeconds { get; set; } = 900;
}

public class SolarApiResponse
{
    [JsonPropertyName("labels")]       public List<string>  Labels       { get; set; } = [];
    [JsonPropertyName("data")]         public List<double>  Data         { get; set; } = [];
    [JsonPropertyName("utility")]      public List<double>  Utility      { get; set; } = [];
    [JsonPropertyName("unit")]         public string        Unit         { get; set; } = "";
    [JsonPropertyName("unitUtility")]  public string        UnitUtility  { get; set; } = "";
    [JsonPropertyName("totalEnergy")]  public double        TotalEnergy  { get; set; }
    [JsonPropertyName("totalUtility")] public double        TotalUtility { get; set; }
    [JsonPropertyName("carbonOffset")] public double        CarbonOffset { get; set; }
    [JsonPropertyName("treesSaved")]   public int           TreesSaved   { get; set; }
    [JsonPropertyName("description")]  public string        Description  { get; set; } = "";
}

public class DeviceStatus
{
    public string BatteryPercent { get; set; } = "";
    public string CurrentState   { get; set; } = "";
    public string ActiveSince    { get; set; } = "";
    public string DeviceId       { get; set; } = "";
}

public class CacheSnapshot
{
    public SolarApiResponse? Today         { get; set; }
    public SolarApiResponse? Monthly       { get; set; }
    public DeviceStatus?     Device        { get; set; }
    public DateTime?         LastUpdated   { get; set; }
    public string?           Error         { get; set; }
}
