// Overlay helpers for score mockups
function phraseRanges(total) {
  const ps = [[0, 4]]; let s = 5;
  while (s < total) { ps.push([s, s + 3], [s + 4, s + 11], [s + 12, s + 17]); s += 18; }
  return ps.filter(p => p[0] < total).map(p => [p[0], Math.min(p[1], total - 1)]);
}
function measureBoxes(pageEl, rootEl, offset) {
  const rb = rootEl.getBoundingClientRect(); const out = [];
  pageEl.querySelectorAll('g.system').forEach((sys, si) => {
    sys.querySelectorAll('g.measure').forEach(m => {
      const b = m.getBoundingClientRect();
      const staves = [...m.querySelectorAll('g.staff')].map(s => s.getBoundingClientRect());
      const top = Math.min(...staves.map(s => s.top)), bottom = Math.max(...staves.map(s => s.bottom));
      out.push({ idx: offset + out.length, sys: si, x: b.left - rb.left, w: b.width, top: top - rb.top, bottom: bottom - rb.top, el: m });
    });
  });
  return out;
}
function drawBrackets(root, boxes, phrases, opts) {
  opts = opts || {};
  phrases.forEach((p, pi) => {
    const ms = boxes.filter(b => b.idx >= p[0] && b.idx <= p[1]);
    if (!ms.length) return;
    const bySys = {}; ms.forEach(b => (bySys[b.sys] = bySys[b.sys] || []).push(b));
    let first = true;
    Object.values(bySys).forEach(seg => {
      const x = seg[0].x + 2, w = seg[seg.length - 1].x + seg[seg.length - 1].w - x - 4;
      const y = Math.min(...seg.map(b => b.top)) - 14;
      const el = document.createElement('div');
      el.className = 'bracket' + (opts.on === pi ? ' on' : '');
      el.style.cssText = `left:${x}px;top:${y}px;width:${w}px`;
      if (first) el.innerHTML = `<span class="n">${pi + 1}</span>` + (opts.saved && opts.saved.includes(pi) ? '<span class="dot"></span>' : '');
      first = false; root.appendChild(el);
    });
  });
}
function drawBand(root, boxes, range, cls, handles) {
  const ms = boxes.filter(b => b.idx >= range[0] && b.idx <= range[1]);
  const bySys = {}; ms.forEach(b => (bySys[b.sys] = bySys[b.sys] || []).push(b));
  const segs = Object.values(bySys);
  segs.forEach((seg, i) => {
    const x = seg[0].x, w = seg[seg.length - 1].x + seg[seg.length - 1].w - x;
    const top = Math.min(...seg.map(b => b.top)) - 8, bottom = Math.max(...seg.map(b => b.bottom)) + 8;
    const el = document.createElement('div'); el.className = 'band ' + (cls || '');
    el.style.cssText = `left:${x}px;top:${top}px;width:${w}px;height:${bottom - top}px`; root.appendChild(el);
    if (handles) {
      const mid = (top + bottom) / 2;
      if (i === 0) { const h = document.createElement('div'); h.className = 'handle'; h.style.cssText = `left:${x}px;top:${mid}px`; root.appendChild(h); }
      if (i === segs.length - 1) { const h = document.createElement('div'); h.className = 'handle'; h.style.cssText = `left:${x + w}px;top:${mid}px`; root.appendChild(h); }
    }
  });
  return segs;
}
function ringNotes(root, boxes, range, rootEl, every) {
  const rb = rootEl.getBoundingClientRect(); let k = 0;
  boxes.filter(b => b.idx >= range[0] && b.idx <= range[1]).forEach(b => {
    const st = b.el.querySelector('g.staff');
    st.querySelectorAll('g.note g.notehead').forEach(n => {
      if (every && (k++ % every)) return;
      const r = n.getBoundingClientRect(); const el = document.createElement('div'); el.className = 'ring';
      const d = Math.max(r.width, r.height) + 8;
      el.style.cssText = `left:${r.left + r.width / 2 - rb.left}px;top:${r.top + r.height / 2 - rb.top}px;width:${d}px;height:${d}px`; root.appendChild(el);
    });
  });
}
function rollRender(host, notes, bars, lo, hi, rowH, opts) {
  opts = opts || {};
  const W = host.clientWidth, H = host.clientHeight; const left = opts.left || 40; const px = (W - left) / (bars * 6);
  const black = [1, 3, 6, 8, 10]; const names = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'];
  let html = '';
  for (let m = hi; m >= lo; m--) {
    const y = (hi - m) * rowH;
    html += `<div class="row ${black.includes(m % 12) ? 'black' : ''}" style="top:${y}px;height:${rowH}px"></div>`;
    if (m % 12 === 0 || m % 12 === 4 || m % 12 === 9) html += `<div class="nm ${m % 12 === 0 ? 'c' : ''}" style="top:${y + rowH / 2}px">${names[m % 12]}${Math.floor(m / 12) - 1}</div>`;
  }
  for (let b = 0; b <= bars; b++) { html += `<div class="barl" style="left:${left + b * 6 * px}px"></div>`; for (let q = 1; q < 3; q++) html += `<div class="beat" style="left:${left + (b * 6 + q * 2) * px}px"></div>`; }
  notes.forEach(n => {
    const x = left + n.start * px, y = (hi - n.midi) * rowH + 1, w = n.dur * px - 2;
    const nm = names[n.midi % 12] + (Math.floor(n.midi / 12) - 1);
    html += `<div class="nt ${n.hand} ${opts.sel && opts.sel(n) ? 'sel' : ''}" style="left:${x}px;top:${y}px;width:${w}px;height:${rowH - 2}px;font:500 ${Math.max(8, rowH - 3)}px/${rowH - 2}px var(--font-ui);color:var(--on-accent);padding-left:4px;overflow:hidden">${w > 20 && n.hand === 'R' ? nm : ''}</div>`;
  });
  if (opts.playhead != null) html += `<div class="playhead" style="left:${left + opts.playhead * px}px;top:0;bottom:0"></div>`;
  host.innerHTML = html;
}
