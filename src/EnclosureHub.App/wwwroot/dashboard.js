// Enclosure Hub dashboard – vanilla JS, talks only to the local HTTP API (/api/*).
(() => {
  'use strict';

  const $ = (sel, el = document) => el.querySelector(sel);
  const el = (tag, attrs = {}, children = []) => {
    const node = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs)) {
      if (k === 'class') node.className = v;
      else if (k === 'text') node.textContent = v;
      else if (k.startsWith('on')) node.addEventListener(k.slice(2), v);
      else if (v !== null && v !== undefined) node.setAttribute(k, v);
    }
    for (const child of [].concat(children)) if (child != null) node.append(child);
    return node;
  };

  const api = async (url, options) => {
    const response = await fetch(url, Object.assign({ headers: { 'Content-Type': 'application/json' } }, options));
    const text = await response.text();
    let data = null;
    try { data = text ? JSON.parse(text) : null; } catch { data = { error: text }; }
    if (!response.ok) throw new Error((data && data.error) || `${response.status} ${response.statusText}`);
    return data;
  };

  // ---------------------------------------------------------------- routing

  const pages = ['camera', 'timelapse', 'printer', 'status', 'settings', 'system'];
  let currentPage = null;

  // "#settings/timelapse" = page + optional tab
  function route() {
    const [requested, tab] = (location.hash || '#camera').slice(1).split('/');
    const page = pages.includes(requested) ? requested : 'camera';
    currentPage = page;
    document.querySelectorAll('.page').forEach(s => s.classList.toggle('active', s.id === 'page-' + page));
    document.querySelectorAll('.sidebar a').forEach(a => a.classList.toggle('active', a.dataset.page === page));
    if (page === 'settings') loadSettings().then(() => showSettingsTab(tab));
    if (page === 'system') loadSystem();
    if (page === 'timelapse') loadTimelapse();
    if (page === 'printer') loadPrinter();
    updateCameraStreams();
  }

  window.addEventListener('hashchange', route);

  // --------------------------------------------------------------- entities

  const entities = {};        // id -> entity DTO (from /api/entities and SSE)
  let entityOrder = [];       // ids in registry order

  const formatState = e => {
    const s = e.state || {};
    switch (e.kind) {
      case 'sensor': return s.value == null ? '—' : `${s.value}${s.unit ? ' ' + s.unit : ''}`;
      case 'binarySensor':
      case 'switch': return s.isOn == null ? 'Unknown' : (s.isOn ? 'On' : 'Off');
      case 'camera': return s.hasFrame ? `Live · frame ${s.frameAgeSeconds}s ago` : 'No image';
      case 'light': return s.isOn ? `On · ${Math.round((s.brightness ?? 255) / 2.55)} %` : 'Off';
      case 'fan': return s.isOn ? `On · ${s.percentage} %` : 'Off';
      default: return JSON.stringify(s);
    }
  };

  const dotClass = e => {
    const s = e.state || {};
    switch (e.kind) {
      case 'binarySensor':
      case 'switch': return s.isOn == null ? '' : (s.isOn ? 'on' : 'off');
      case 'camera': return s.hasFrame ? 'on' : 'off';
      case 'light':
      case 'fan': return s.isOn ? 'on' : 'off';
      case 'sensor': return s.value == null ? 'err' : 'on';
      default: return '';
    }
  };

  const kindLabel = { sensor: 'Sensor', binarySensor: 'Binary sensor', switch: 'Switch', camera: 'Camera', light: 'Light', fan: 'Fan' };

  const sendOnOff = (id, on) =>
    api(`/api/entities/${encodeURIComponent(id)}/command`, { method: 'POST', body: JSON.stringify({ on }) }).catch(alertError);

  function renderEntities() {
    const list = $('#entities');
    list.replaceChildren();
    if (entityOrder.length === 0) {
      list.append(el('div', { class: 'empty', text: 'No entities registered.' }));
      return;
    }
    for (const id of entityOrder) {
      const e = entities[id];
      if (!e) continue;
      const control = el('div', { class: 'control' }, [
        el('span', { class: 'dot ' + dotClass(e) }),
        el('span', { class: 'value', text: formatState(e) }),
      ]);
      if (e.kind === 'switch' || e.kind === 'light' || e.kind === 'fan') {
        const on = !!(e.state && e.state.isOn);
        control.append(el('button', {
          class: 'btn', type: 'button', text: on ? 'Turn off' : 'Turn on',
          onclick: () => sendOnOff(e.id, !on),
        }));
      }
      list.append(el('div', { class: 'row' }, [
        el('div', {}, [
          el('div', { class: 'label', text: e.name }),
          el('div', { class: 'sub', text: `${kindLabel[e.kind] || e.kind} · ${e.id}${e.isDiagnostic ? ' · diagnostic' : ''}` }),
        ]),
        control,
      ]));
    }
  }

  const alertError = err => console.error(err);

  // ----------------------------------------------------------------- camera

  const cameraViews = {};  // camera id -> { img, placeholder, dot, text }

  function renderCameras() {
    const container = $('#cameras');
    const cameras = entityOrder.map(id => entities[id]).filter(e => e && e.kind === 'camera');

    if (cameras.length === 0) {
      container.replaceChildren(el('div', { class: 'empty' }, [
        'No camera module is loaded. Turn it on under ',
        el('a', { href: '#settings', style: 'text-decoration: underline', text: 'Settings' }),
        ' and restart the service.',
      ]));
      return;
    }

    for (const camera of cameras) {
      let view = cameraViews[camera.id];
      if (!view) {
        const img = el('img', { alt: camera.name });
        const placeholder = el('div', { class: 'placeholder', text: 'Waiting for the first frame…' });
        const dot = el('span', { class: 'dot' });
        const text = el('span', { text: '' });
        // Power toggle bound to the "{id}_active" switch entity (hidden when the module has none)
        const power = el('button', { class: 'btn toggle', type: 'button', role: 'switch', style: 'display:none' });
        power.addEventListener('click', () => {
          const sw = entities[camera.id + '_active'];
          if (sw) sendOnOff(sw.id, !(sw.state && sw.state.isOn));
        });
        const card = el('div', { class: 'camera' }, [
          el('div', { class: 'camera-view' }, [img, placeholder]),
          el('div', { class: 'camera-bar' }, [
            el('span', { class: 'name' }, [dot, camera.name]),
            text,
            el('span', { class: 'spacer' }),
            power,
            el('a', { href: `/api/cameras/${encodeURIComponent(camera.id)}/snapshot.jpg`, target: '_blank', rel: 'noopener', text: 'Snapshot URL' }),
            el('a', { href: `/api/cameras/${encodeURIComponent(camera.id)}/stream.mjpeg`, target: '_blank', rel: 'noopener', text: 'Stream URL' }),
            el('a', { href: '#settings', text: 'Settings' }),
          ]),
        ]);
        img.addEventListener('load', () => { placeholder.style.display = 'none'; });
        container.append(card);
        view = cameraViews[camera.id] = { img, placeholder, dot, text, power };
      }
      updateCameraView(camera, view);
    }
  }

  // The "{id}_active" switch is ON only while the camera is switched on and producing frames.
  function updateCameraView(camera, view) {
    const active = entities[camera.id + '_active'];
    const isOn = active ? active.state.isOn === true : !!(camera.state && camera.state.hasFrame);

    if (active) {
      view.power.style.display = '';
      view.power.setAttribute('aria-checked', isOn ? 'true' : 'false');
      view.power.textContent = isOn ? 'On' : 'Off';
      view.power.title = isOn ? 'Turn the camera off' : 'Turn the camera on (restarts the capture if it is stuck)';
    }

    view.dot.className = 'dot ' + (isOn ? 'on' : 'off');
    view.text.textContent = isOn ? 'Running' : 'Off';
    if (!isOn) {
      view.placeholder.textContent = 'Camera is off or not producing frames.';
      view.placeholder.style.display = '';
    } else if (!(camera.state && camera.state.hasFrame)) {
      view.placeholder.textContent = 'Waiting for the first frame…';
      view.placeholder.style.display = '';
    }
  }

  // Only stream while the camera page is visible and the camera is on (saves CPU on the Pi).
  function updateCameraStreams() {
    for (const [id, view] of Object.entries(cameraViews)) {
      const active = entities[id + '_active'];
      const shouldStream = currentPage === 'camera' && (!active || active.state.isOn !== false);
      const url = `/api/cameras/${encodeURIComponent(id)}/stream.mjpeg`;
      if (shouldStream && !view.img.getAttribute('src')) {
        view.img.src = url + '?t=' + Date.now();
      } else if (!shouldStream && view.img.getAttribute('src')) {
        view.img.removeAttribute('src');
        view.placeholder.style.display = '';
      }
    }
  }

  // --------------------------------------------------------------- settings

  let settingsLoaded = false;
  let settingsSections = [];  // last GET /api/settings (the printer section lists tagged fields of the others)
  let settingsNotice = null;  // { id, message, restart } shown in a section after the page was re-rendered
  let settingsTab = null;     // group name of the visible tab
  const settingsGroups = {};  // tab slug ("timelapse") -> group name ("Timelapse")
  const slug = text => text.toLowerCase().replace(/[^a-z0-9]+/g, '-');

  async function loadSettings(force = false) {
    if (settingsLoaded && !force) return;
    const container = $('#settings');
    const tabs = $('#settings-tabs');
    try {
      const sections = await api('/api/settings');
      settingsSections = sections;

      // One tab per group, in order of first use; sections keep their order within a tab
      const groups = [];
      for (const s of sections) if (!groups.includes(s.group)) groups.push(s.group);
      for (const g of groups) settingsGroups[slug(g)] = g;

      tabs.replaceChildren(...groups.map(g => el('a', { href: '#settings/' + slug(g), 'data-group': g, text: g })));
      container.replaceChildren(...groups.map(g => el('div', { class: 'tab-panel', 'data-group': g },
        sections.filter(s => s.group === g).map(renderSection))));
      settingsLoaded = true;
      showSettingsTab(settingsTab && slug(settingsTab));
    } catch (err) {
      container.replaceChildren(el('div', { class: 'note err', text: 'Could not load settings: ' + err.message }));
    }
  }

  function showSettingsTab(tabSlug) {
    const panels = [...document.querySelectorAll('#settings .tab-panel')];
    if (panels.length === 0) return;
    settingsTab = settingsGroups[tabSlug] || settingsTab || panels[0].dataset.group;
    panels.forEach(p => p.classList.toggle('active', p.dataset.group === settingsTab));
    document.querySelectorAll('#settings-tabs a').forEach(a => a.classList.toggle('active', a.dataset.group === settingsTab));
    if (settingsTab === 'Timelapse') refreshTimelapseSettings();
  }

  // Section-specific additions to the generic form (live estimates, tool status, …)
  const sectionExtras = {};

  function renderSection(section) {
    const inputs = {};
    const rows = {};
    const grid = el('div', { class: 'grid-2' }, section.fields.map(field => {
      // Sub-heading: spans both columns, the fields below belong to it
      if (field.type === 'heading') {
        return el('div', { class: 'subheading' }, [
          el('h3', { text: field.label }),
          field.description ? el('div', { class: 'desc', text: field.description }) : null,
        ]);
      }

      const control = renderField(field, section.values[field.key]);
      if (control.get) inputs[field.key] = control;
      const text = el('div', {}, [
        el('div', { class: 'label', text: field.label }),
        field.description ? el('div', { class: 'desc', text: field.description }) : null,
      ]);
      rows[field.key] = text;
      return el('div', { class: 'row' }, [
        text,
        el('div', { class: 'control' }, [control.node]),
      ]);
    }));

    const note = el('span', { class: 'note' });
    const save = el('button', { class: 'btn', type: 'button', text: 'Save' });
    save.addEventListener('click', async () => {
      const body = {};
      for (const [key, control] of Object.entries(inputs)) body[key] = control.get();
      save.disabled = true;
      note.className = 'note';
      note.textContent = 'Saving…';
      try {
        const result = await api(`/api/settings/${encodeURIComponent(section.id)}`, { method: 'PUT', body: JSON.stringify(body) });
        showSaved(note, result.message, result.restartRequired);
        for (const [key, value] of Object.entries(result.values || {})) if (inputs[key]) inputs[key].set(value);

        // The printer link adds or removes fields on other tabs and the Printer page: render everything again
        if (section.id === 'printer') {
          settingsNotice = { id: section.id, message: result.message, restart: result.restartRequired };
          await loadSettings(true);
          refreshPrinterNav();
        }
      } catch (err) {
        note.className = 'note err';
        note.textContent = err.message;
      } finally {
        save.disabled = false;
      }
    });

    const node = el('div', { class: 'section' }, [
      el('h2', { text: section.title }),
      section.description ? el('p', { class: 'section-desc', text: section.description }) : null,
      grid,
      el('div', { class: 'section-footer' }, [save, note]),
    ]);
    if (settingsNotice && settingsNotice.id === section.id) {
      showSaved(note, settingsNotice.message, settingsNotice.restart);
      settingsNotice = null;
    }
    if (sectionExtras[section.id]) sectionExtras[section.id]({ node, grid, inputs, rows, section });
    return node;
  }

  function showSaved(note, message, restartRequired) {
    note.className = 'note ok';
    note.replaceChildren(message || 'Saved.');
    if (restartRequired) note.append(' ', el('a', { href: '#system', style: 'text-decoration: underline', text: 'Restart now' }));
  }

  const formatSettingValue = (field, value) => {
    if (field.type === 'toggle') return value ? 'On' : 'Off';
    if (field.type === 'choice') return ((field.choices || []).find(c => c.value === String(value)) || {}).label || String(value ?? '');
    return String(value ?? '—');
  };

  // Settings → Printer: what the link enables elsewhere (fields tagged "printer") with their current values
  sectionExtras.printer = ({ grid, section }) => {
    if (!section.values.linked) return;
    const related = [];
    for (const other of settingsSections) {
      for (const field of other.fields) {
        if (field.tag !== 'printer' || other.id === 'printer') continue;
        related.push(el('div', { class: 'row' }, [
          el('div', {}, [
            el('div', { class: 'label', text: field.label }),
            el('div', { class: 'sub' }, [`${other.group} › ${other.title} · `,
              el('a', { href: '#settings/' + slug(other.group), style: 'text-decoration: underline', text: 'change' })]),
          ]),
          el('div', { class: 'control' }, [el('span', { class: 'value', text: formatSettingValue(field, other.values[field.key]) })]),
        ]));
      }
    }
    if (related.length === 0) return;
    grid.after(el('div', { class: 'related' }, [
      el('div', { class: 'subheading' }, [
        el('h3', { text: 'Uses of the printer connection' }),
        el('div', { class: 'desc', text: 'Settings on other tabs that work with the linked printer, and their current state.' }),
      ]),
      el('div', { class: 'rows' }, related),
    ]));
  };

  function renderField(field, value) {
    switch (field.type) {
      case 'info': {
        const span = el('span', { class: 'value info', text: value ?? '—' });
        return { node: span, set: v => { span.textContent = v ?? '—'; } };
      }
      case 'toggle': {
        const btn = el('button', { class: 'btn toggle', type: 'button', role: 'switch' });
        const set = v => { btn.setAttribute('aria-checked', v ? 'true' : 'false'); btn.textContent = v ? 'On' : 'Off'; };
        btn.addEventListener('click', () => set(btn.getAttribute('aria-checked') !== 'true'));
        set(!!value);
        return { node: btn, get: () => btn.getAttribute('aria-checked') === 'true', set };
      }
      case 'choice': {
        const select = el('select', {}, (field.choices || []).map(c => el('option', { value: c.value, text: c.label })));
        const set = v => { select.value = String(v); };
        set(value);
        return { node: select, get: () => select.value, set };
      }
      case 'number': {
        const input = el('input', { type: 'number', min: field.min, max: field.max, step: field.step ?? 'any' });
        const set = v => { input.value = v ?? ''; };
        set(value);
        return { node: input, get: () => input.value === '' ? null : Number(input.value), set };
      }
      default: {
        const input = el('input', { type: 'text' });
        const set = v => { input.value = v ?? ''; };
        set(value);
        return { node: input, get: () => input.value, set };
      }
    }
  }

  // -------------------------------------------------------------- timelapse

  let timelapse = null;           // last GET /api/timelapse: { switchId, status }; null = not loaded / module off
  let timelapseDisabled = false;  // the API answered 404: module turned off
  let timelapseVideosKey = null;  // the video list is re-rendered only when it changes
  const AssumedFrameBytes = 300 * 1024;  // estimate basis while the camera delivers no frame

  const formatSize = b => b == null ? '—'
    : b >= 1073741824 ? `${(b / 1073741824).toFixed(1)} GB`
    : b >= 1048576 ? `${Math.round(b / 1048576)} MB`
    : `${Math.max(1, Math.round(b / 1024))} kB`;
  const formatDuration = seconds => {
    const s = Math.round(seconds);
    if (s < 60) return `${s} s`;
    if (s < 3600) return s % 60 ? `${Math.floor(s / 60)} min ${s % 60} s` : `${s / 60} min`;
    if (s >= 172800) return `${formatCount(s / 86400)} days`;
    return `${Math.floor(s / 3600)} h ${String(Math.floor(s % 3600 / 60)).padStart(2, '0')} min`;
  };
  const formatCount = n => Math.round(n).toLocaleString('en-US');
  const pendingKey = t => t ? t.status.pending.map(p => p.name + ':' + p.state).join('|') : '';

  async function fetchTimelapse() {
    try {
      timelapse = await api('/api/timelapse');
      timelapseDisabled = false;
    } catch (err) {
      timelapse = null;
      timelapseDisabled = err.message.startsWith('404');
    }
    $('.sidebar a[data-page="timelapse"]').style.display = timelapseDisabled ? 'none' : '';
    return timelapse;
  }

  async function loadTimelapse() {
    await fetchTimelapse();
    renderTimelapseStatus();
    await loadTimelapseVideos();
  }

  async function refreshTimelapse() {
    const before = pendingKey(timelapse);
    await fetchTimelapse();
    if (!$('#timelapse-status .btn.armed')) renderTimelapseStatus(); // keep an armed "Discard" until it is confirmed
    if (pendingKey(timelapse) !== before) loadTimelapseVideos(); // a render finished or failed
  }

  // Two-step button for destructive actions: first click arms it, second click within 5 s runs the action.
  function confirmButton(label, action) {
    const btn = el('button', { class: 'btn', type: 'button', text: label });
    let armed = null;
    const disarm = () => { clearTimeout(armed); armed = null; btn.classList.remove('armed'); btn.textContent = label; };
    btn.addEventListener('click', async () => {
      if (!armed) {
        btn.classList.add('armed');
        btn.textContent = 'Confirm';
        armed = setTimeout(disarm, 5000);
        return;
      }
      disarm();
      btn.disabled = true;
      try {
        await action();
      } catch (err) {
        showTimelapseError(err.message);
      } finally {
        btn.disabled = false;
      }
    });
    return btn;
  }

  function showTimelapseError(message) {
    const note = $('#timelapse-note');
    note.className = 'note err';
    note.textContent = message;
  }

  function renderTimelapseStatus() {
    const container = $('#timelapse-status');
    if (!timelapse) {
      container.replaceChildren(el('div', { class: 'empty', text: timelapseDisabled
        ? 'The timelapse module is turned off (Modules:Timelapse:Enabled in appsettings.Local.json).'
        : 'Could not load the timelapse status.' }));
      return;
    }

    const s = timelapse.status;
    const cur = s.current;
    const toggle = el('button', {
      class: 'btn toggle', type: 'button', role: 'switch',
      'aria-checked': s.recording ? 'true' : 'false', text: s.recording ? 'On' : 'Off',
      title: s.recording ? 'Stop the recording and render the video' : 'Start a new recording',
      onclick: () => sendOnOff(timelapse.switchId, !s.recording).then(refreshTimelapse),
    });

    const settingsLink = el('a', { href: '#settings/timelapse', style: 'text-decoration: underline', text: 'Settings' });
    const recordingText = cur
      ? [`Since ${new Date(cur.startedUtc).toLocaleString()} · ${formatDuration((Date.now() - Date.parse(cur.startedUtc)) / 1000)} · `
          + `${formatCount(cur.frames)} frames · ${formatSize(cur.bytes)}`]
      : [`A frame every ${s.intervalSeconds} s; stops automatically after ${s.maxDurationHours} h. `, settingsLink];
    const triggerText = s.trigger === 'layers' ? 'Frames follow the printer layers'
      : s.trigger === 'layersAndInterval' ? `Frames follow the printer layers, and every ${s.intervalSeconds} s during long layers`
      : null;
    if (cur && triggerText) recordingText.push(` · ${triggerText}` + (s.lastLayer != null ? ` (last: layer ${s.lastLayer})` : ''));

    const rows = [
      el('div', { class: 'row' }, [
        el('div', {}, [
          el('div', { class: 'label', text: s.recording ? 'Recording' : 'Not recording' }),
          el('div', { class: 'sub' }, recordingText),
        ]),
        el('div', { class: 'control' }, [el('span', { class: 'dot ' + (s.recording ? 'on' : 'off') }), toggle]),
      ]),
    ];

    const warning = text => el('div', { class: 'row' }, [el('div', { class: 'note err' }, text), el('div')]);
    if (!s.ffmpeg.available) {
      rows.push(warning(['ffmpeg is not installed, so videos cannot be rendered (frames are still saved). ',
        el('a', { href: '#settings/timelapse', style: 'text-decoration: underline', text: 'Install it in Settings' }), '.']));
    }
    if (s.recording && !s.cameraAvailable) {
      rows.push(warning('The camera is off or not delivering frames – nothing is being saved until it runs again.'));
    }

    // How long the disk lasts at the current interval (frames + the video rendered from them)
    const { frameBytes, videoBytes } = timelapseSizes(s);
    const hoursLeft = s.freeBytes != null ? s.freeBytes / ((frameBytes + videoBytes) * 3600 / s.intervalSeconds) : null;
    rows.push(el('div', { class: 'row' }, [
      el('div', {}, [
        el('div', { class: 'label', text: 'Storage' }),
        el('div', { class: 'sub', text: hoursLeft == null ? 'Free space unknown'
          : `Room for about ${formatDuration(hoursLeft * 3600)} of recording at the current interval` }),
      ]),
      el('div', { class: 'control' }, [el('span', { class: 'value', text: `${formatSize(s.freeBytes)} free` })]),
    ]));

    for (const p of s.pending) {
      const state = p.state === 'rendering' ? `Rendering · ${Math.round((p.progress || 0) * 100)} %`
        : p.state === 'queued' ? 'Waiting to be rendered'
        : 'Rendering failed' + (p.error ? ': ' + p.error : '');
      const control = el('div', { class: 'control' });
      if (p.state === 'failed') {
        control.append(
          el('button', { class: 'btn', type: 'button', text: 'Retry', onclick: () =>
            api(`/api/timelapse/recordings/${encodeURIComponent(p.name)}/render`, { method: 'POST' })
              .then(refreshTimelapse).catch(err => showTimelapseError(err.message)) }),
          confirmButton('Discard', async () => {
            await api(`/api/timelapse/recordings/${encodeURIComponent(p.name)}`, { method: 'DELETE' });
            await refreshTimelapse();
          }));
      } else {
        control.append(el('span', { class: 'dot ' + (p.state === 'rendering' ? 'on' : 'off') }));
      }
      rows.push(el('div', { class: 'row' }, [
        el('div', {}, [
          el('div', { class: 'label', text: p.name }),
          el('div', { class: 'sub' + (p.state === 'failed' ? ' err' : ''), text: `${state} · ${formatCount(p.frames)} frames · ${formatSize(p.bytes)}` }),
        ]),
        control,
      ]));
    }

    container.replaceChildren(el('div', { class: 'rows' }, rows));
  }

  async function loadTimelapseVideos() {
    const list = $('#timelapse-videos');
    if (!timelapse) {
      list.replaceChildren();
      timelapseVideosKey = null;
      return;
    }

    let videos;
    try {
      videos = await api('/api/timelapse/videos');
    } catch (err) {
      showTimelapseError('Could not load the videos: ' + err.message);
      return;
    }

    const key = videos.map(v => v.name + ':' + v.sizeBytes).join('|');
    if (key === timelapseVideosKey) return;
    timelapseVideosKey = key;

    if (videos.length === 0) {
      list.replaceChildren(el('div', { class: 'empty', text: 'No videos yet. Start a recording above; the video appears here once it is rendered.' }));
      return;
    }

    list.replaceChildren(...videos.map(v => {
      const base = `/api/timelapse/videos/${encodeURIComponent(v.name)}`;
      const recorded = v.startedUtc && v.stoppedUtc ? (Date.parse(v.stoppedUtc) - Date.parse(v.startedUtc)) / 1000 : null;
      const details = [
        v.durationSeconds != null ? `${formatDuration(v.durationSeconds)} video` : null,
        v.frames != null ? `${formatCount(v.frames)} frames at ${v.fps} fps` : null,
        recorded != null ? `recorded ${formatDuration(recorded)}` : null,
        formatSize(v.sizeBytes),
      ].filter(Boolean).join(' · ');

      return el('div', { class: 'row video' }, [
        el('div', { class: 'video-info' }, [
          el('a', { class: 'thumb', href: base + '.mp4', target: '_blank', rel: 'noopener', title: 'Play' },
            v.hasThumbnail ? [el('img', { src: base + '.jpg', alt: '', loading: 'lazy' })] : []),
          el('div', {}, [
            el('div', { class: 'label', text: new Date(v.startedUtc || v.createdUtc).toLocaleString() }),
            el('div', { class: 'sub', text: v.name }),
            el('div', { class: 'sub', text: details }),
          ]),
        ]),
        el('div', { class: 'control' }, [
          el('a', { class: 'btn', href: base + '.mp4', target: '_blank', rel: 'noopener', text: 'Play' }),
          el('a', { class: 'btn', href: base + '.mp4?download=true', download: v.name + '.mp4', text: 'Download' }),
          confirmButton('Delete', async () => {
            await api(base, { method: 'DELETE' });
            await loadTimelapseVideos();
          }),
        ]),
      ]);
    }));
  }

  // Settings → Timelapse: ffmpeg status with an install button, live storage estimates under the fields
  let timelapseSettingsView = null;

  sectionExtras.timelapse = ({ grid, inputs, rows }) => {
    const dot = el('span', { class: 'dot' });
    const value = el('span', { class: 'value' });
    const note = el('div', { class: 'note' });
    const install = el('button', { class: 'btn', type: 'button', text: 'Install', style: 'display:none' });
    install.addEventListener('click', async () => {
      install.disabled = true;
      try {
        await api('/api/timelapse/ffmpeg/install', { method: 'POST' });
      } catch (err) {
        note.className = 'note err';
        note.textContent = err.message;
      }
      refreshTimelapseSettings();
    });

    grid.before(el('div', { class: 'row' }, [
      el('div', {}, [
        el('div', { class: 'label', text: 'ffmpeg' }),
        el('div', { class: 'desc', text: 'Required: renders the saved frames into an MP4 video. Installed from the Raspberry Pi OS packages (apt install ffmpeg, about 100 MB).' }),
        note,
      ]),
      el('div', { class: 'control' }, [dot, value, install]),
    ]));

    const diskValue = el('span', { class: 'value' });
    const diskSub = el('div', { class: 'desc' });
    grid.before(el('div', { class: 'row' }, [
      el('div', {}, [el('div', { class: 'label', text: 'Free disk space' }), diskSub]),
      el('div', { class: 'control' }, [diskValue]),
    ]));

    const intervalEstimate = el('div', { class: 'estimate' });
    const maxEstimate = el('div', { class: 'estimate' });
    rows.interval.append(intervalEstimate);
    rows.maxHours.append(maxEstimate);
    for (const key of ['interval', 'maxHours', 'videoFps']) {
      inputs[key].node.addEventListener('input', updateTimelapseEstimates);
      inputs[key].node.addEventListener('change', updateTimelapseEstimates);
    }

    timelapseSettingsView = { dot, value, note, install, diskValue, diskSub, intervalEstimate, maxEstimate, inputs };
  };

  async function refreshTimelapseSettings() {
    await fetchTimelapse();
    const v = timelapseSettingsView;
    if (!v || !timelapse) return;

    const f = timelapse.status.ffmpeg;
    const installing = f.installState === 'running';
    v.dot.className = 'dot ' + (f.available ? 'on' : installing ? '' : 'err');
    v.value.textContent = f.available ? 'Installed' + (f.version ? ' · ' + f.version : '') : installing ? 'Installing…' : 'Not installed';
    v.install.style.display = !f.available && f.canInstall ? '' : 'none';
    v.install.disabled = installing;
    v.note.className = 'note' + (f.installState === 'failed' ? ' err' : f.installState === 'succeeded' ? ' ok' : '');
    v.note.textContent = f.installState !== 'idle' ? (f.installMessage || '')
      : !f.available && !f.canInstall ? 'Install it on the device: sudo apt install ffmpeg' : '';
    updateTimelapseEstimates();
  }

  function updateTimelapseEstimates() {
    const v = timelapseSettingsView;
    if (!v) return;
    const s = timelapse && timelapse.status;
    const interval = Number(v.inputs.interval.get());
    const hours = Number(v.inputs.maxHours.get());
    const fps = Number(v.inputs.videoFps.get()) || 30;
    const { frameBytes, videoBytes } = timelapseSizes(s);
    const basis = (s && s.averageFrameBytes
        ? `${formatSize(frameBytes)} per frame (${s.current && s.current.frames ? 'average of this recording' : 'current camera picture'})`
        : `an assumed ${formatSize(frameBytes)} per frame – turn the camera on for a real figure`)
      + (s && s.videoBytesPerFrame ? ', video size measured on your videos' : ', video assumed at 15 % of the frames');

    const free = s ? s.freeBytes : null;
    v.diskValue.textContent = free == null ? 'Unknown' : `${formatSize(free)} free`;
    v.diskSub.textContent = free != null && interval > 0
      ? `Currently, on the disk holding the timelapse data. Enough for about ${formatDuration(free / ((frameBytes + videoBytes) * 3600 / interval) * 3600)} `
        + 'of recording at this interval, including rendering the video.'
      : 'Currently, on the disk holding the timelapse data.';

    if (!(interval > 0)) {
      v.intervalEstimate.textContent = v.maxEstimate.textContent = '';
      return;
    }

    // While a recording is rendered, its frames and the new video are on disk together; afterwards only the video stays
    const framesPerHour = 3600 / interval;
    v.intervalEstimate.textContent = `1 hour of recording ≈ ${formatCount(framesPerHour)} frames: ${formatSize(framesPerHour * frameBytes)} of frames `
      + `+ about ${formatSize(framesPerHour * videoBytes)} for ${formatDuration(framesPerHour / fps)} of video. Based on ${basis}.`;

    if (!(hours > 0)) {
      v.maxEstimate.textContent = '';
      return;
    }
    const frames = framesPerHour * hours;
    const peak = frames * (frameBytes + videoBytes);
    const tooBig = free != null && peak > free;
    v.maxEstimate.className = 'estimate' + (tooBig ? ' err' : '');
    v.maxEstimate.textContent = `${hours} h ≈ ${formatCount(frames)} frames: ${formatSize(frames * frameBytes)} of frames + about `
      + `${formatSize(frames * videoBytes)} video (${formatDuration(frames / fps)}) = ${formatSize(peak)} needed while rendering. `
      + 'Afterwards the frames are deleted and only the video stays.'
      + (tooBig ? ` Only ${formatSize(free)} is free – a recording stops early when the disk is nearly full.` : '');
  }

  // Bytes per saved frame and per frame of rendered video (same 15 % fallback as the server's disk guard)
  function timelapseSizes(s) {
    const frameBytes = (s && s.averageFrameBytes) || AssumedFrameBytes;
    return { frameBytes, videoBytes: (s && s.videoBytesPerFrame) || frameBytes * 0.15 };
  }

  // ---------------------------------------------------------------- printer

  let printer = null;        // last GET /api/printer; null = module missing or not loaded
  let printerLogAfter = 0;   // highest log sequence received
  const printerLog = [];     // received log entries (max 500, like the server)

  async function fetchPrinter() {
    try {
      printer = await api('/api/printer');
    } catch {
      printer = null;
    }
    return printer;
  }

  // The Printer page is listed only while the printer is linked
  async function refreshPrinterNav() {
    await fetchPrinter();
    $('.sidebar a[data-page="printer"]').style.display = printer && printer.linked ? '' : 'none';
  }

  async function loadPrinter() {
    await fetchPrinter();
    renderPrinterStatus();
    await loadPrinterLog();
  }

  const formatTemp = (value, target) => value == null ? '—'
    : `${value.toFixed(1)} °C` + (target ? ` / ${Math.round(target)} °C` : '');
  const formatTime = utc => {
    const d = new Date(utc);
    return d.toLocaleTimeString([], { hour12: false }) + '.' + String(d.getMilliseconds()).padStart(3, '0');
  };

  function renderPrinterStatus() {
    const container = $('#printer-status');
    if (!printer) {
      container.replaceChildren(el('div', { class: 'empty', text: 'The printer module is not available.' }));
      return;
    }
    if (!printer.linked) {
      container.replaceChildren(el('div', { class: 'empty' }, [
        'The printer is not linked. Switch on the printer connection under ',
        el('a', { href: '#settings/printer', style: 'text-decoration: underline', text: 'Settings → Printer' }), '.',
      ]));
      return;
    }

    const p = printer;
    const info = p.info || {};
    const row = (label, sub, value, dot) => el('div', { class: 'row' }, [
      el('div', {}, [el('div', { class: 'label', text: label }), sub ? el('div', { class: 'sub', text: sub }) : null]),
      el('div', { class: 'control' }, [dot ? el('span', { class: 'dot ' + dot }) : null, el('span', { class: 'value', text: value })]),
    ]);

    const state = !p.connected ? 'Not connected'
      : p.printing ? 'Printing' + (p.progress != null ? ` · ${p.progress} %` : '')
      : 'Idle';
    const position = [p.layer != null ? `layer ${p.layer}` : null, p.z != null ? `Z ${p.z} mm` : null].filter(Boolean).join(' · ');

    container.replaceChildren(
      row('Connection', p.connected ? p.port : (p.error || 'Waiting for the printer (switched off?)'),
        p.connected ? 'Connected' : 'Offline', p.connected ? 'on' : 'off'),
      row('Printer', [info.firmware, info.uuid ? 'UUID ' + info.uuid : null].filter(Boolean).join(' · ') || null,
        info.machineType || 'Unknown'),
      row('State', p.printing ? [p.file, position].filter(Boolean).join(' · ') : position || null, state, p.printing ? 'on' : null),
      row('Nozzle', null, formatTemp(p.nozzleTemperature, p.nozzleTarget)),
      row('Bed', null, formatTemp(p.bedTemperature, p.bedTarget)),
      row('Last message', p.lastReceivedUtc ? new Date(p.lastReceivedUtc).toLocaleString() : 'Nothing received yet',
        p.lastReceivedUtc ? formatDuration((Date.now() - Date.parse(p.lastReceivedUtc)) / 1000) + ' ago' : '—'),
    );
  }

  async function loadPrinterLog() {
    let entries;
    try {
      entries = await api('/api/printer/log?after=' + printerLogAfter);
    } catch {
      return;
    }
    if (entries.length === 0) return;
    printerLogAfter = entries[entries.length - 1].sequence;
    printerLog.push(...entries);
    if (printerLog.length > 500) printerLog.splice(0, printerLog.length - 500);
    renderPrinterLog();
  }

  // Temperature reports and "busy" keepalives (every 2 s during heating, homing, probing) would bury everything else
  const isRoutineLine = e => e.direction === 'in' && /^((ok\s+)?T:|echo:busy:)/.test(e.text.trim());

  function renderPrinterLog() {
    const box = $('#printer-log');
    const atBottom = box.scrollHeight - box.scrollTop - box.clientHeight < 40;
    const showTemps = $('#printer-log-temps').checked;
    const marks = { in: '<', out: '>', info: '·' };
    const lines = printerLog.filter(e => showTemps || !isRoutineLine(e)).map(e =>
      el('div', { class: 'log-line dir-' + e.direction }, [
        el('span', { class: 'log-time', text: formatTime(e.utc) }),
        el('span', { class: 'log-dir', text: marks[e.direction] || '?' }),
        el('span', { text: e.text }),
      ]));
    box.replaceChildren(...(lines.length ? lines : [el('div', { class: 'empty', text: 'No communication yet.' })]));
    if (atBottom) box.scrollTop = box.scrollHeight;
  }

  $('#printer-log-temps').addEventListener('change', renderPrinterLog);

  $('#printer-info-btn').addEventListener('click', async () => {
    const btn = $('#printer-info-btn');
    const note = $('#printer-info-note');
    btn.disabled = true;
    note.className = 'note';
    note.textContent = 'Asking the printer…';
    try {
      const info = await api('/api/printer/info', { method: 'POST' });
      note.className = 'note ok';
      note.textContent = [info.machineType, info.firmware].filter(Boolean).join(' · ') || 'The printer answered.';
    } catch (err) {
      note.className = 'note err';
      note.textContent = err.message;
    } finally {
      btn.disabled = false;
      loadPrinter();
    }
  });

  // ----------------------------------------------------------------- system

  const formatUptime = seconds => {
    const d = Math.floor(seconds / 86400), h = Math.floor(seconds % 86400 / 3600), m = Math.floor(seconds % 3600 / 60);
    return d ? `${d} d ${h} h` : h ? `${h} h ${m} min` : `${m} min`;
  };
  const formatBytes = b => `${(b / 1048576).toFixed(0)} MB`;

  async function loadSystem() {
    const container = $('#system');
    try {
      const s = await api('/api/system');
      $('#device-name').textContent = s.device;
      const rows = [
        ['Device', s.device, s.deviceId],
        ['Version', s.version, null],
        ['Host', s.hostname, `${s.os} · ${s.architecture}`],
        ['Runtime', s.runtime, s.underSystemd ? 'running under systemd' : 'not running under systemd'],
        ['Uptime', formatUptime(s.uptimeSeconds), 'since ' + new Date(s.startedUtc).toLocaleString()],
        ['Memory', formatBytes(s.workingSetBytes), `${formatBytes(s.managedMemoryBytes)} managed heap`],
      ];
      container.replaceChildren(...rows.map(([label, value, sub]) => el('div', { class: 'row' }, [
        el('div', {}, [el('div', { class: 'label', text: label }), sub ? el('div', { class: 'sub', text: sub }) : null]),
        el('div', { class: 'control' }, [el('span', { class: 'value', text: value })]),
      ])));
    } catch (err) {
      container.replaceChildren(el('div', { class: 'note err', text: 'Could not load system information: ' + err.message }));
    }
  }

  // Two-step restart: first click arms the button, second click within 6 s restarts.
  (() => {
    const btn = $('#restart-btn');
    const note = $('#restart-note');
    let armed = null;
    btn.addEventListener('click', async () => {
      if (!armed) {
        btn.classList.add('armed');
        btn.textContent = 'Confirm';
        note.className = 'note';
        note.textContent = 'Click again to restart the service.';
        armed = setTimeout(() => { armed = null; btn.classList.remove('armed'); btn.textContent = 'Restart'; note.textContent = ''; }, 6000);
        return;
      }
      clearTimeout(armed); armed = null;
      btn.classList.remove('armed');
      btn.disabled = true;
      btn.textContent = 'Restarting';
      try {
        await api('/api/system/restart', { method: 'POST' });
        note.textContent = 'Restarting… this page reloads when the service is back.';
        await waitForHealth();
        location.reload();
      } catch (err) {
        note.className = 'note err';
        note.textContent = err.message;
        btn.disabled = false;
        btn.textContent = 'Restart';
      }
    });
  })();

  async function waitForHealth() {
    await new Promise(r => setTimeout(r, 3000));
    for (let i = 0; i < 60; i++) {
      try {
        const r = await fetch('/health', { cache: 'no-store' });
        if (r.ok) return;
      } catch { /* not up yet */ }
      await new Promise(r => setTimeout(r, 2000));
    }
    throw new Error('The service did not come back within two minutes.');
  }

  // --------------------------------------------------------- live updates

  function setConnection(state, text) {
    $('#conn-dot').className = 'dot ' + state;
    $('#conn-text').textContent = text;
  }

  function connectEvents() {
    const events = new EventSource('/api/events');
    events.onopen = () => setConnection('on', 'Connected');
    events.onerror = () => setConnection('err', 'Reconnecting…');
    events.addEventListener('state', ev => {
      const e = JSON.parse(ev.data);
      if (!entities[e.id]) entityOrder.push(e.id);
      entities[e.id] = e;
      renderEntities();
      if (e.kind === 'camera' || e.id.endsWith('_active')) {
        renderCameras();
        updateCameraStreams();
      }
      const timelapseId = timelapse ? timelapse.switchId : 'timelapse';
      if ((e.id === timelapseId || e.id === timelapseId + '_rendering') && currentPage === 'timelapse') refreshTimelapse();
    });
  }

  // Timelapse progress (frames, render percentage, ffmpeg installation) is polled while it is on screen.
  setInterval(() => {
    if (currentPage === 'timelapse') refreshTimelapse();
    else if (currentPage === 'settings' && settingsTab === 'Timelapse') refreshTimelapseSettings();
  }, 3000);

  // Printer status and communication while the Printer page is open
  setInterval(() => {
    if (currentPage === 'printer') loadPrinter();
  }, 2000);

  // Camera entity state (frame age) does not raise change events – refresh it while the camera page is open.
  setInterval(async () => {
    if (currentPage !== 'camera' && currentPage !== 'status') return;
    for (const id of entityOrder) {
      const e = entities[id];
      if (!e || e.kind !== 'camera') continue;
      try {
        entities[id] = await api(`/api/entities/${encodeURIComponent(id)}`);
      } catch { /* ignore */ }
    }
    renderEntities();
    renderCameras();
  }, 5000);

  // ------------------------------------------------------------------ init

  (async () => {
    try {
      const all = await api('/api/entities');
      entityOrder = all.map(e => e.id);
      for (const e of all) entities[e.id] = e;
    } catch (err) {
      setConnection('err', 'API unavailable');
    }
    renderEntities();
    renderCameras();
    await fetchTimelapse(); // hides the Timelapse page when the module is off
    await refreshPrinterNav(); // shows the Printer page while the printer is linked
    route();
    connectEvents();
    api('/api/system').then(s => { $('#device-name').textContent = s.device; document.title = `${s.device} · Enclosure Hub`; }).catch(() => {});
  })();
})();
