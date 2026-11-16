namespace EnclosureHub.App.Api;

/// <summary>
/// Minimal status page for quick checks in a browser. A real display UI is planned
/// separately (see docs/ROADMAP.md) and should use /api/entities + /api/events.
/// </summary>
internal static class IndexPage
{
    public const string Html = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Enclosure Hub</title>
          <style>
            body { margin: 0; background: #111; color: #ddd; font-family: system-ui, sans-serif; }
            img { width: 100%; display: block; }
            ul { list-style: none; padding: 12px; margin: 0; }
            li { padding: 4px 0; }
          </style>
        </head>
        <body>
          <img src="/api/cameras/camera/stream.mjpeg" alt="camera">
          <ul id="entities"></ul>
          <script>
            const list = document.getElementById('entities');
            const items = {};
            const events = new EventSource('/api/events');
            events.addEventListener('state', e => {
              const entity = JSON.parse(e.data);
              if (entity.kind === 'camera') return;
              let li = items[entity.id];
              if (!li) { li = items[entity.id] = document.createElement('li'); list.appendChild(li); }
              li.textContent = entity.name + ': ' + JSON.stringify(entity.state);
            });
          </script>
        </body>
        </html>
        """;
}
