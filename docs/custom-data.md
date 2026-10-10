# Stream custom data

Open **Stats studio** to start the local API. It stays active while the app is in the tray. The endpoint is `http://127.0.0.1:47845/v1/metrics`. Click **Copy token** in the designer. The same token is stored in `%LOCALAPPDATA%\OmenScreen\api-token.txt`; do not publish it.

Send JSON with an `Authorization: Bearer YOUR_TOKEN` header:

```json
{"metrics":[{"id":"custom.fps","label":"Game FPS","value":144,"unit":"fps","ttlSeconds":3},{"id":"custom.status","label":"Status","text":"IN GAME","ttlSeconds":10}]}
```

`144` is an example, not a measured game FPS. Your game overlay, telemetry tool, or script must supply the actual value. No game injection or automatic FPS capture is included.

Each metric has a lowercase `custom.` ID (letters, digits, dots, underscores and hyphens; maximum 64 characters), and either a finite numeric `value` or a string `text`. Optional fields are `label` (48 characters), `unit` (16 characters), and `ttlSeconds` (1–86400; default 5). Text is limited to 80 characters. Submit numbers as JSON numbers using a decimal point. Send updates more often than the TTL; expired data displays `--` rather than a frozen value. Numeric readings can fill graphs; text readings work best in the two center/footer slots or Spotlight.

POST creates or updates metrics. Click **Refresh metrics** after introducing an ID, then select it in a slot. **Add custom metric** lets you register an ID before its first update. Assignments persist with the dashboard; publishers should send labels and units when reconnecting. An authenticated GET to the same endpoint returns the current readings.

PowerShell example (replace the example values with your own measurements):

```powershell
$token = Get-Content "$env:LOCALAPPDATA\OmenScreen\api-token.txt" -Raw
$data = @{ metrics = @(
    @{id='custom.fps'; label='Game FPS'; value=144; unit='fps'; ttlSeconds=3},
    @{id='custom.status'; label='Status'; text='IN GAME'; ttlSeconds=10}
) } | ConvertTo-Json -Depth 4
Invoke-RestMethod 'http://127.0.0.1:47845/v1/metrics' -Method Post `
    -Headers @{Authorization="Bearer $($token.Trim())"} `
    -ContentType 'application/json' -Body $data
```

See [the Python sender](../examples/send_metrics.py) for a standard-library integration function. Call it from your telemetry loop with current values.

The server binds only to IPv4 loopback, requires its token for reads and writes, and enables no browser CORS access. Requests are limited to 64 KiB, 64 metrics per batch and 128 registered metrics total. Invalid batches are rejected without publishing their other rows. Responses use 200 for success, 400 for invalid data and 401 for missing/incorrect credentials. A port conflict is reported in the designer. Changing/deleting the token file takes effect after restarting the app; existing senders then need the new token. Metric values live in memory and are not logged or sent externally.
