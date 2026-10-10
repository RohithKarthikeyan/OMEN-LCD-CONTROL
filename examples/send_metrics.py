"""Import publish() into your telemetry script; Python standard library only."""
import json
import os
from pathlib import Path
from urllib.request import Request, urlopen


def publish(metrics, token=None, timeout=2):
    """Publish numeric/text records. Values must come from your telemetry source."""
    if token is None:
        token = (Path(os.environ['LOCALAPPDATA']) / 'OmenScreen' / 'api-token.txt').read_text().strip()
    request = Request(
        'http://127.0.0.1:47845/v1/metrics',
        data=json.dumps({'metrics': metrics}, allow_nan=False).encode('utf-8'),
        headers={'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json'},
        method='POST',
    )
    with urlopen(request, timeout=timeout) as response:
        return json.load(response)


# Example call from your existing telemetry loop:
# publish([{'id': 'custom.fps', 'value': measured_fps, 'unit': 'fps', 'ttlSeconds': 3},
#          {'id': 'custom.status', 'text': current_status, 'ttlSeconds': 5}])
