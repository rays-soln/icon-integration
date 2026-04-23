# Energy24by7 Proxy — HA Add-on

## What this is
A Home Assistant custom add-on that periodically logs into the Energy24by7 customer portal, fetches solar production data, and exposes it as a local REST API for HA to consume.

This exists because the Energy24by7 iCON inverter device (ESP32-based, MAC: 9C:13:9E:A3:90:D8) is cloud-only with no local API. The portal at https://customer-portal.energy24by7.com is a Laravel app that uses server-side rendering and session-based auth — no public API or API keys available.

## Device details
- **Device name:** swasti
- **Device ID (UUID):** `7b8a4e55-04d3-4add-8c2f-74029af8ccf9`
- **Device serial:** CAA0006
- **Portal:** https://customer-portal.energy24by7.com
- **Auth flow:** Laravel session — GET `/login` for CSRF token → POST `/login` with `_token`, `email`, `password` → session cookie → GET `/solar-energy-data`

## How it works
1. `SolarFetchService` (BackgroundService) runs on a configurable interval (default 15 min)
2. Each cycle creates a fresh `HttpClient` with its own `CookieContainer`
3. Logs in using the portal's Laravel auth flow
4. Fetches today's and monthly data from `/solar-energy-data?filterType=today&deviceId=...`
5. Logs out via `POST /logout` with the CSRF token
6. Stores results in `SolarDataCache` (thread-safe, in-memory)
6. `Program.cs` exposes `/solar` and `/health` endpoints via ASP.NET Core Minimal API

## API endpoints
- `GET /solar` — returns today + monthly solar data as JSON
- `GET /health` — returns status, last updated time, and last error if any

### /solar response shape
```json
{
  "last_updated": "2026-04-23T10:00:00Z",
  "today": {
    "total_energy_kwh": 1.46,
    "total_utility_kwh": 0.06,
    "carbon_offset_kg": 1.27,
    "trees_saved": 0,
    "hourly_labels": ["12 AM", "1 AM", ...],
    "hourly_energy": [0, 0, 0.01, ...],
    "hourly_utility": [0, 0, 30, ...]
  },
  "monthly": {
    "total_energy_kwh": 59.26,
    "total_utility_kwh": 3.09,
    "carbon_offset_kg": 51.56,
    "trees_saved": 2,
    "monthly_labels": ["Mar 2026", "Apr 2026"],
    "monthly_energy": [20.3, 38.96]
  }
}
```

## Project structure
```
ha-addons/
├── repository.json               # HA add-on repository descriptor
└── energy24by7-proxy/
    ├── config.yaml               # Add-on metadata, ports, options schema
    ├── Dockerfile                # Multi-stage: SDK build + HA base image
    ├── run.sh                    # Reads HA options via bashio, sets env vars, starts app
    ├── build.yaml                # HA builder arch targets
    └── src/
        ├── Program.cs            # Minimal API — /solar and /health endpoints
        ├── SolarFetchService.cs  # BackgroundService — login + fetch loop
        ├── SolarDataCache.cs     # Thread-safe in-memory cache
        ├── Models.cs             # AppConfig, SolarApiResponse, CacheSnapshot
        └── energy24by7-proxy.csproj
```

## Add-on configuration (set in HA UI)
| Option | Type | Default | Description |
|---|---|---|---|
| `email` | string | — | Energy24by7 portal login email |
| `password` | string | — | Energy24by7 portal login password |
| `device_id` | string | `7b8a4e55-...` | Device UUID from portal |
| `fetch_interval` | int | `900` | Fetch interval in seconds |

## Port
The add-on exposes port `5200`. Within HA, the endpoint is:
```
http://localhost:5200/solar
```

## Home Assistant rest sensor
```yaml
rest:
  - resource: http://localhost:5200/solar
    scan_interval: 900
    sensor:
      - name: "Solar Energy Today"
        value_template: "{{ value_json.today.total_energy_kwh }}"
        unit_of_measurement: kWh
        device_class: energy
        state_class: total_increasing
      - name: "Solar Utility Savings Today"
        value_template: "{{ value_json.today.total_utility_kwh }}"
        unit_of_measurement: kWh
      - name: "Solar Carbon Offset Today"
        value_template: "{{ value_json.today.carbon_offset_kg }}"
        unit_of_measurement: kg
      - name: "Solar Trees Saved Today"
        value_template: "{{ value_json.today.trees_saved }}"
```

## Installing the add-on
1. Push this repo to GitHub
2. In HA: Settings → Add-ons → Add-on Store → ⋮ → Repositories
3. Add: `https://github.com/rays-soln/icon-integration`
4. Find **Energy24by7 Proxy** in the store → Install
5. Go to Configuration tab → enter credentials → Start

## Key technical notes
- Cloudflare protects the portal — curl fails due to TLS fingerprinting; .NET `HttpClient` with a proper `CookieContainer` works correctly
- The portal uses CSRF tokens (Laravel `_token`) as a hidden input field in the login form, not a meta tag
- Session cookies expire in 2 hours — the fetch loop re-authenticates on every cycle to avoid stale sessions
- `totalUtility` on the monthly endpoint returns `0` currently — may be a portal bug; check with Energy24by7 support (Mr. Evin Alfred)
- `HtmlAgilityPack` is used to parse the CSRF token from the login page HTML
