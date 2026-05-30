# LabcomSync

Watches your [LabCom Cloud](https://labcom.cloud) account for manual photometer measurements (e.g. DPD1 free chlorine) and back-fills corresponding Prometheus metrics (e.g. ORP/Redox, Temperature from the Bayrol AS5) at the exact same timestamp. Runs as a Docker container.

## How it works

1. On startup, runs an immediate sync cycle so any existing gaps are filled right away.
2. Then sleeps until the next aligned clock boundary (e.g. with a 15-minute interval: :00, :15, :30, :45) and repeats.
3. Each cycle fetches the N most recent *trigger* measurements from LabCom (matched by scenario code substring).
4. For each trigger, checks whether a *target* measurement with the same timestamp already exists.
5. If not, queries Prometheus for the configured metric at that exact timestamp (Prometheus returns the most recent sample at or before that time, within its 5-minute staleness window).
6. Writes the value to LabCom via GraphQL `createMeasurement`, tagged with a configurable comment so you can identify it in the LabCom UI.

A single trigger scenario can fan out to multiple targets — e.g. every Chlorine measurement gets both ORP and Temperature filled in.

## Prerequisites

- A LabCom Cloud account with at least one pool
- Your LabCom GraphQL API token (find it in the LabCom web UI under your account settings)
- Prometheus running with the metrics you want to sync (e.g. `bayrol_redox_value`, `bayrol_temperature_value`)

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
| `intervalSeconds` | `900` | Interval between syncs (aligned to clock, not startup time) |
| `lookbackDays` | `30` | How far back to look for trigger measurements |
| `topN` | `10` | Maximum number of recent triggers to process per mapping |

### Mappings

Each mapping has a `triggerScenarioContains` string matched case-insensitively against the LabCom `scenario` field. Check your actual scenario codes in the LabCom app or by querying the API — they are short codes like `"8–CL"`, not descriptive names.

Targets also match by `scenarioContains`. Measurements written by this service get scenario `"manually added"` from LabCom, so use that for target matching. Pin `parameterId` explicitly (query `{ Scenarios { ... } }` once to find the right value):

```json
{
  "name": "Chlorine measurements",
  "triggerScenarioContains": "8–CL",
  "targets": [
    {
      "scenarioContains": "manually added",
      "prometheusMetric": "bayrol_redox_value",
      "parameterId": 161
    },
    {
      "scenarioContains": "manually added",
      "prometheusMetric": "bayrol_temperature_value",
      "parameterId": 124
    }
  ]
}
```

Known `parameterId` values: `161` = Redox (ORP), `124` = Temperature.

## Deployment

Add to `docker-compose.yml` (already done in the bayrolconnect project):

```yaml
  labcom-sync:
    build: ~/git/LabcomSync
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

1. On startup the service syncs immediately — check logs right away:
   ```
   [Chlorine measurements / manually added] Wrote 863 at 2026-05-25 15:48:55Z (trigger id=375)
   ```
2. Subsequent runs are logged with their scheduled time:
   ```
   Next run at 2026-05-30 08:30:00Z
   ```
3. Verify in LabCom that the new entries appear with `comment = "prometheus-sync"`.

If the log shows `No Prometheus data for bayrol_redox_value at <timestamp>`, the measurement is older than Prometheus's retention window or the AS5 was offline at that time.
