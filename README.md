# LabcomSync

Watches your [LabCom Cloud](https://labcom.cloud) account for manual photometer measurements (e.g. DPD1 free chlorine) and back-fills corresponding Prometheus metrics (e.g. ORP/Redox, Temperature from the Bayrol AS5) at the exact same timestamp. Runs as a Docker container, checks every minute.

## How it works

1. Fetches the N most recent *trigger* measurements from LabCom (configurable by scenario name substring).
2. For each trigger, checks whether a *target* measurement with the same timestamp already exists.
3. If not, queries Prometheus for the configured metric at that exact timestamp (Prometheus returns the most recent sample at or before that time, within its 5-minute staleness window).
4. Writes the value to LabCom via GraphQL `createMeasurement`, tagged with a configurable comment so you can see in the LabCom UI that it came from this script.

A single trigger scenario can fan out to multiple targets — e.g. every Chlorine measurement gets both ORP and Temperature filled in.

## Prerequisites

- A LabCom Cloud account with at least one pool
- Your LabCom GraphQL API token (find it in the LabCom web UI under your account settings)
- Prometheus running with `bayrol_redox_value` (and/or other metrics you want to sync)

## Configuration

### Secret — add to `.env`

```
LABCOM_TOKEN=your_token_here
```

### Non-secret — `labcom-sync/config.json` (mounted into container)

Copy `config.json.example` to `/opt/bayrolconnect/labcom-sync/config.json` and adjust:

| Field | Default | Description |
|---|---|---|
| `labcomGraphqlUrl` | `https://backend.labcom.cloud/graphql` | GraphQL endpoint |
| `prometheusUrl` | `http://prometheus:9090` | Prometheus base URL |
| `syncComment` | `prometheus-sync` | Comment added to every written measurement |
| `intervalSeconds` | `60` | How often to check for gaps |
| `lookbackDays` | `30` | How far back to look for trigger measurements |
| `topN` | `10` | Maximum number of recent triggers to process per mapping |

### Mappings

Each mapping has a `triggerScenarioContains` string (matched case-insensitively against the LabCom `scenario` field) and one or more `targets`:

```json
{
  "name": "Chlorine measurements",
  "triggerScenarioContains": "Chlorine",
  "targets": [
    {
      "scenarioContains": "ORP",
      "prometheusMetric": "bayrol_redox_value"
    },
    {
      "scenarioContains": "Temperature",
      "prometheusMetric": "bayrol_temperature_value"
    }
  ]
}
```

`scenarioId` and `parameterId` per target are optional — when omitted, the service discovers them from an existing measurement with the matching scenario. If no such measurement exists yet, add one manually in LabCom first, then the service will replicate the IDs for all subsequent auto-writes.

Once discovered, the IDs are logged at startup so you can pin them explicitly in the config to avoid the discovery step.

## Deployment

Add to `docker-compose.yml` (already done in the bayrolconnect project):

```yaml
  labcom-sync:
    build: /home/rainer/git/LabcomSync
    restart: unless-stopped
    volumes:
      - ./labcom-sync/config.json:/config/config.json:ro
    environment:
      - LABCOM_TOKEN=${LABCOM_TOKEN}
    depends_on:
      - prometheus
```

Then:

```bash
docker compose build labcom-sync
docker compose up -d labcom-sync
docker compose logs -f labcom-sync
```

## Verifying a sync

1. Open LabCom web UI, find a Chlorine measurement without an ORP entry.
2. Watch the service logs — within one interval you should see:
   ```
   [Chlorine measurements / ORP] Wrote 780 at 2026-05-15 09:30:00Z (trigger id=1234)
   ```
3. Refresh LabCom — the new ORP measurement appears with `comment = "prometheus-sync"`.

If the log shows `No Prometheus data for bayrol_redox_value at <timestamp>`, the measurement is older than Prometheus's retention window or the AS5 was offline at that time.
