# Energy24by7 Proxy — HA Add-on

## What this is
A Home Assistant custom add-on that periodically logs into the Energy24by7 customer portal, fetches solar production data, and exposes it as a local REST API for HA to consume.

This exists because the Energy24by7 iCON inverter device (ESP32-based, MAC: 9C:13:9E:A3:90:D8) is cloud-only with no local API. The portal at https://customer-portal.energy24by7.com is a Laravel app that uses server-side rendering and session-based auth — no public API or API keys available.

## Device details
- **Device name:** swasti
- **Device ID (UUID):** `7b8a4e55-04d3-4add-8c2f-74029af8ccf9`
- **Device serial:** CAA0006
- **Portal:** https://customer-portal.energy24by7.com
- **Auth flow:** Laravel session — GET `/login` for CSRF token (hidden input `name="_token"`) → POST `/login` with `_token`, `email`, `password` → session cookie → GET `/solar-energy-data`

## How it works
1. `SolarFetchService` (BackgroundService) runs on a configurable interval (default 5 min)
2. Each cycle creates a fresh `HttpClient` with its own `CookieContainer`
3. Logs in using the portal's Laravel auth flow
4. Fetches today's and monthly data from `/solar-energy-data?filterType=today&deviceId=...`
5. Stores results in `SolarDataCache` (thread-safe, in-memory)
6. `Program.cs` exposes `/solar` and `/health` endpoints via ASP.NET Core Minimal API

## API endpoints
- `GET /solar` — returns today + monthly solar data as JSON
- `GET /health` — returns status, last updated time, and last error if any

### /solar response shape
```json
{
  "last_updated": "2026-04-23T10:00:00Z",
  "today": {
    "total_energy_kwh": 1.66,
    "total_utility_kwh": 0.06,
    "carbon_offset_kg": 1.44,
    "trees_saved": 0,
    "hourly_labels": ["12 AM", "1 AM", "2 AM", ...],
    "hourly_energy": [0, 0, 0, 0, 0, 0, 0.01, 0.14, ...],
    "hourly_utility": [0, 0, 0, 0, 0, 0, 0, 0, 30, ...]
  },
  "monthly": {
    "total_energy_kwh": 59.46,
    "total_utility_kwh": 3.09,
    "carbon_offset_kg": 51.73,
    "trees_saved": 2,
    "monthly_labels": ["Mar 2026", "Apr 2026"],
    "monthly_energy": [20.3, 38.96]
  }
}
```

## Project structure
```
icon-integration/
├── CLAUDE.md                         # This file
├── repository.json                   # HA add-on repository descriptor
└── energy24by7-proxy/
    ├── config.yaml                   # Add-on metadata, ports, options schema
    ├── Dockerfile                    # Multi-stage: SDK build + HA base image
    ├── run.sh                        # Reads HA options via bashio, sets env vars, starts app
    ├── build.yaml                    # HA builder arch targets
    └── src/
        ├── Program.cs                # Minimal API — /solar and /health endpoints
        ├── SolarFetchService.cs      # BackgroundService — login + fetch loop
        ├── SolarDataCache.cs         # Thread-safe in-memory cache
        ├── Models.cs                 # AppConfig, SolarApiResponse, CacheSnapshot
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
The add-on exposes port `5200`. Within HA the endpoint is:
```
http://localhost:5200/solar
```

---

## Home Assistant configuration

### configuration.yaml — sensor + template blocks

```yaml
sensor:
  - platform: rest
    name: "energy24by7_icon_data"
    resource: http://localhost:5200/solar
    scan_interval: 900
    value_template: "{{ value_json.last_updated }}"
    json_attributes:
      - today
      - monthly
      - device
      - last_updated

template:
  - sensor:
      - name: "Solar Energy Today"
        unique_id: solar_energy_today
        unit_of_measurement: "kWh"
        device_class: energy
        state_class: total_increasing
        icon: mdi:solar-power
        state: >
          {{ state_attr('sensor.energy24by7_icon_data', 'today')['total_energy_kwh'] | float(0) }}

      - name: "Solar Utility Savings Today"
        unique_id: solar_utility_savings_today
        unit_of_measurement: "kWh"
        device_class: energy
        state_class: total_increasing
        icon: mdi:transmission-tower
        state: >
          {{ state_attr('sensor.energy24by7_icon_data', 'today')['total_utility_kwh'] | float(0) }}

      - name: "Solar Carbon Offset Today"
        unique_id: solar_carbon_offset_today
        unit_of_measurement: "kg"
        icon: mdi:leaf
        state: >
          {{ state_attr('sensor.energy24by7_icon_data', 'today')['carbon_offset_kg'] | float(0) }}

      - name: "Solar Trees Saved Today"
        unique_id: solar_trees_saved_today
        icon: mdi:tree
        state: >
          {{ state_attr('sensor.energy24by7_icon_data', 'today')['trees_saved'] | int(0) }}

      - name: "Solar Energy Monthly"
        unique_id: solar_energy_monthly
        unit_of_measurement: "kWh"
        device_class: energy
        icon: mdi:calendar-month
        state: >
          {{ state_attr('sensor.energy24by7_icon_data', 'monthly')['total_energy_kwh'] | float(0) if state_attr('sensor.energy24by7_icon_data', 'monthly') else 0 }}

      - name: "Solar Carbon Offset Monthly"
        unique_id: solar_carbon_offset_monthly
        unit_of_measurement: "kg"
        icon: mdi:leaf
        state: >
          {{ state_attr('sensor.energy24by7_icon_data', 'monthly')['carbon_offset_kg'] | float(0) if state_attr('sensor.energy24by7_icon_data', 'monthly') else 0 }}

      - name: "Solar Trees Saved Monthly"
        unique_id: solar_trees_saved_monthly
        icon: mdi:tree
        state: >
          {{ state_attr('sensor.energy24by7_icon_data', 'monthly')['trees_saved'] | int(0) if state_attr('sensor.energy24by7_icon_data', 'monthly') else 0 }}

      - name: "Battery Status"
        unique_id: solar_battery_status
        unit_of_measurement: "%"
        icon: mdi:battery
        state: >
          {{ state_attr('sensor.energy24by7_icon_data', 'device')['battery_percent'] | replace('%', '') | int(0) }}

      - name: "Device State"
        unique_id: solar_device_state
        icon: mdi:power-plug
        state: >
          {{ state_attr('sensor.energy24by7_icon_data', 'device')['current_state'] }}

      - name: "Device ID"
        unique_id: solar_device_id
        icon: mdi:identifier
        state: >
          {{ state_attr('sensor.energy24by7_icon_data', 'device')['device_id'] }}

      - name: "Device Active Since"
        unique_id: solar_device_active_since
        icon: mdi:calendar-clock
        state: >
          {{ state_attr('sensor.energy24by7_icon_data', 'device')['active_since'] }}

      - name: "Solar Hourly Labels"
        unique_id: solar_hourly_labels
        unit_of_measurement: "kWh"
        state: >
          {{ state_attr('sensor.energy24by7_icon_data', 'today')['total_energy_kwh'] | float(0) }}
        attributes:
          energy: >
            {{ state_attr('sensor.energy24by7_icon_data', 'today')['hourly_energy'] }}
```

### Dashboard card YAML

```yaml
type: vertical-stack
cards:
  - type: custom:mushroom-template-card
    primary: iCON Solar — today
    secondary: >
      swasti · CAA0006 · Kollam · updated {{ states('sensor.energy24by7_icon_data') | as_timestamp | timestamp_custom('%I:%M %p') }}
    icon: mdi:solar-power
    icon_color: amber
    badge_icon: mdi:check-circle
    badge_color: green
    fill_container: true

  - type: glance
    title: Device
    show_name: true
    show_icon: true
    show_state: true
    entities:
      - entity: sensor.battery_status
        name: Battery
        icon: mdi:battery
      - entity: sensor.device_state
        name: State
        icon: mdi:power-plug
      - entity: sensor.device_id
        name: Device ID
        icon: mdi:identifier
      - entity: sensor.device_active_since
        name: Active Since
        icon: mdi:calendar-clock

  - type: glance
    title: Today
    show_name: true
    show_icon: true
    show_state: true
    entities:
      - entity: sensor.solar_energy_today
        name: Produced
        icon: mdi:solar-power
      - entity: sensor.solar_utility_savings_today
        name: Savings
        icon: mdi:transmission-tower
      - entity: sensor.solar_carbon_offset_today
        name: CO₂ offset
        icon: mdi:leaf
      - entity: sensor.solar_trees_saved_today
        name: Trees
        icon: mdi:tree

  - type: glance
    title: This month
    show_name: true
    show_icon: true
    show_state: true
    entities:
      - entity: sensor.solar_energy_monthly
        name: Produced
        icon: mdi:solar-power
      - entity: sensor.solar_carbon_offset_monthly
        name: CO₂ offset
        icon: mdi:leaf
      - entity: sensor.solar_trees_saved_monthly
        name: Trees
        icon: mdi:tree

  - type: markdown
    content: |
      ## Hourly production
      {% set e = state_attr('sensor.energy24by7_icon_data', 'today')['hourly_energy'] %}
      {% set l = state_attr('sensor.energy24by7_icon_data', 'today')['hourly_labels'] %}
      {% set m = e | max %}
      {% for i in range(e | length) %}{% if e[i] > 0 %}
      **{{ l[i] }}** {{ '▓' * ((e[i] / m * 15) | int) }} *{{ e[i] }} kWh*
      {% endif %}{% endfor %}
```

The monthly chart is a separate standalone card (not inside the vertical stack):

```yaml
type: markdown
content: |
  ## Monthly production
  {% set e = state_attr('sensor.energy24by7_icon_data', 'monthly')['monthly_energy'] %}
  {% set l = state_attr('sensor.energy24by7_icon_data', 'monthly')['monthly_labels'] %}
  {% set m = e | max %}
  {% for i in range(e | length) %}{% if e[i] > 0 %}
  **{{ l[i] }}** {{ '▓' * ((e[i] / m * 15) | int) }} *{{ e[i] }} kWh*
  {% endif %}{% endfor %}
```

---

## Installing the add-on
1. Push this repo to GitHub (must be public)
2. In HA: Settings → Apps → App Store → ⋮ → Repositories
3. Add: `https://github.com/rays-soln/icon-integration`
4. Find **Energy24by7 Proxy** in the store → Install
5. Go to Configuration tab → enter credentials → Start

---

## Key technical notes
- Cloudflare protects the portal — curl fails due to TLS fingerprinting; .NET `HttpClient` with a proper `CookieContainer` works correctly
- The portal CSRF token is in a **hidden input field** (`name="_token"`), not a meta tag — grep must match `name="_token" value="..."` not `name="csrf-token" content="..."`
- Login returns HTTP 302 (redirect to dashboard) on success — treat both 200 and 302 as success
- Session cookies expire in 2 hours — the fetch loop re-authenticates on every cycle to avoid stale sessions
- `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true` is required in the Dockerfile — HA base image does not have libicu installed
- `HtmlAgilityPack` is used to parse the CSRF token from the login page HTML
- `totalUtility` on the monthly endpoint returns `0` — possible portal bug; check with Energy24by7 support (Mr. Evin Alfred)
- The device has no local API — MAC `9C:13:9E:A3:90:D8` shows as Espressif in router, all local ports are filtered/closed
- ApexCharts card `data_generator` does not work reliably with non-time-series entity attributes — use Markdown card for hourly display instead