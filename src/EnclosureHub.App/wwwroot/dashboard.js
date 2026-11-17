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

  const pages = ['camera', 'status', 'settings', 'system'];
  let currentPage = null;

  function route() {
    const requested = (location.hash || '#camera').slice(1);
    const page = pages.includes(requested) ? requested : 'camera';
    currentPage = page;
    document.querySelectorAll('.page').forEach(s => s.classList.toggle('active', s.id === 'page-' + page));
    document.querySelectorAll('.sidebar a').forEach(a => a.classList.toggle('active', a.dataset.page === page));
    if (page === 'settings') loadSettings();
    if (page === 'system') loadSystem();
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

  function updateCameraView(camera, view) {
    const running = entities[camera.id + '_running'];
    const active = entities[camera.id + '_active'];
    const isActive = !active || active.state.isOn !== false;
    const isRunning = running ? running.state.isOn === true : !!(camera.state && camera.state.hasFrame);

    if (active) {
      view.power.style.display = '';
      view.power.setAttribute('aria-checked', isActive ? 'true' : 'false');
      view.power.textContent = isActive ? 'On' : 'Off';
      view.power.title = isActive ? 'Turn the camera off' : 'Turn the camera on';
    }

    view.dot.className = 'dot ' + (isRunning ? 'on' : 'off');
    view.text.textContent = !isActive ? 'Turned off' : isRunning ? 'Running' : 'Starting…';
    if (!isRunning) {
      view.placeholder.textContent = !isActive ? 'Camera is turned off.' : 'Waiting for the first frame…';
      view.placeholder.style.display = '';
    }
  }

  // Only stream while the camera page is visible and the camera is on (saves CPU on the Pi).
  function updateCameraStreams() {
    for (const [id, view] of Object.entries(cameraViews)) {
      const running = entities[id + '_running'];
      const active = entities[id + '_active'];
      const shouldStream = currentPage === 'camera'
        && (!active || active.state.isOn !== false)
        && (!running || running.state.isOn !== false);
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

  async function loadSettings(force = false) {
    if (settingsLoaded && !force) return;
    const container = $('#settings');
    try {
      const sections = await api('/api/settings');
      container.replaceChildren(...sections.map(renderSection));
      settingsLoaded = true;
    } catch (err) {
      container.replaceChildren(el('div', { class: 'note err', text: 'Could not load settings: ' + err.message }));
    }
  }

  function renderSection(section) {
    const inputs = {};
    const grid = el('div', { class: 'grid-2' }, section.fields.map(field => {
      const control = renderField(field, section.values[field.key]);
      inputs[field.key] = control;
      return el('div', { class: 'row' }, [
        el('div', {}, [
          el('div', { class: 'label', text: field.label }),
          field.description ? el('div', { class: 'desc', text: field.description }) : null,
        ]),
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
        note.className = 'note ok';
        note.replaceChildren(result.message || 'Saved.');
        if (result.restartRequired) {
          note.append(' ', el('a', { href: '#system', style: 'text-decoration: underline', text: 'Restart now' }));
        }
        for (const [key, value] of Object.entries(result.values || {})) if (inputs[key]) inputs[key].set(value);
      } catch (err) {
        note.className = 'note err';
        note.textContent = err.message;
      } finally {
        save.disabled = false;
      }
    });

    return el('div', { class: 'section' }, [
      el('h2', { text: section.title }),
      section.description ? el('p', { class: 'section-desc', text: section.description }) : null,
      grid,
      el('div', { class: 'section-footer' }, [save, note]),
    ]);
  }

  function renderField(field, value) {
    switch (field.type) {
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
      if (e.kind === 'camera' || e.id.endsWith('_running') || e.id.endsWith('_active')) {
        renderCameras();
        updateCameraStreams();
      }
    });
  }

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
    route();
    connectEvents();
    api('/api/system').then(s => { $('#device-name').textContent = s.device; document.title = `${s.device} · Enclosure Hub`; }).catch(() => {});
  })();
})();
