import re, json, os
D = os.path.dirname(os.path.abspath(__file__))
rd = lambda f: open(os.path.join(D, f), encoding='utf8').read()
import glob
NP = len(glob.glob(os.path.join(D,'page*_Leland.svg')))
P = {i: rd(f'page{i}_Leland.svg') for i in range(1, NP+1)}
counts = {i: P[i].count('class="measure"') for i in P}
starts = {1: 0}
for i in range(2, NP+1): starts[i] = starts[i - 1] + counts[i - 1]
PHRASE = rd('phrase.svg'); OSSIA = rd('ossia.svg'); NOTES = rd('phrase_notes.json')
ICONS = rd('icons.js'); SCORE = rd('score.js')
PYICONS = dict(re.findall(r"^\s+(\w+): '(.*)',?$", ICONS, re.M))
def ic(name, cls=''):
    return f'<svg class="ic {cls}" viewBox="0 0 20 20" aria-hidden="true">{PYICONS[name]}</svg>'

HEAD = '''<!doctype html><html data-theme="{theme}" {pagemode}><head><meta charset="utf-8"><link rel="stylesheet" href="tokens.css"><link rel="stylesheet" href="ds.css"><style>{style}</style></head><body><script>{icons}{score}</script>'''

def head(theme='light', style='', slate=False):
    return HEAD.format(theme=theme, pagemode='data-page="slate"' if slate else '', style=style, icons=ICONS, score=SCORE)

def bar(title, pages, on=None):
    acts = ''.join(f'<span class="act {"on" if a == on else ""}">{ic(a,"l")}</span>' for a in ['phrases', 'pen', 'roll', 'insight', 'play'])
    return f'''<div class="bar"><span class="act">{ic("back","l")}</span><span class="ttl">{title}</span><span class="pg">{pages}</span><span class="sp"></span>{acts}<span class="act">{ic("more","l")}</span></div>'''

def pill(items):
    return f'<div class="pill"><span class="play">{ic("playf","l")}</span>' + ''.join(items) + '</div>'

def it(icon, text, on=False):
    return f'<span class="it {"on" if on else ""}">{ic(icon)}{text}</span>'

def spread(sel=None, menu=False, brackets=True, on=None, saved=(2,), playhead=None, rings=None, ids=('p1', 'p2')):
    return f'''
<div class="page" id="{ids[0]}" style="left:30px;top:20px;width:552px;height:780px"><div class="cover"></div>{P[1]}<span class="no" style="left:24px">1</span></div>
<div class="page" id="{ids[1]}" style="left:598px;top:20px;width:552px;height:780px">{P[2]}<span class="no" style="right:24px">2</span></div>
<div class="ov" id="ov" style="inset:0"></div>
<script>
(function(){{
  const root=document.getElementById('scr'), ov=document.getElementById('ov');
  const b1=measureBoxes(document.getElementById('{ids[0]}'),root,{starts[1]}), b2=measureBoxes(document.getElementById('{ids[1]}'),root,{starts[2]});
  const boxes=b1.concat(b2.map(b=>Object.assign(b,{{sys:b.sys+100}})));
  const ph=phraseRanges({sum(counts.values())});
  {"drawBrackets(ov,boxes,ph,{on:" + (str(on) if on is not None else 'null') + ",saved:" + json.dumps(list(saved)) + "});" if brackets else ''}
  {"const segs=drawBand(ov,boxes," + json.dumps(sel) + ",'',true);" if sel else ''}
  {"ringNotes(ov,boxes," + json.dumps(rings) + ",root,1);" if rings else ''}
  {f"""const seg=segs[0]; const bx=seg[0].x, bw=seg[seg.length-1].x+seg[seg.length-1].w-bx, by=Math.min(...seg.map(b=>b.top))-8;
  const m=document.createElement('div'); m.className='menu'; m.innerHTML='<span class="it">'+ic('loop')+'Loop</span><span class="it">'+ic('insight')+'Focus</span><span class="it">'+ic('note')+'Note</span><span class="it">'+ic('bookmark')+'Bookmark</span><span class="div"></span><span class="it">'+ic('roll')+'Piano roll</span><span class="it">'+ic('search')+'Insights</span><span class="div"></span><span class="it">'+ic('more')+'</span>';
  root.appendChild(m); m.style.left=(bx+bw/2-m.offsetWidth/2)+'px'; m.style.top=(by-m.offsetHeight-32)+'px';""" if menu else ''}
  {f"""const pb=boxes[{playhead}]; const p=document.createElement('div'); p.className='playhead'; p.style.cssText=`left:${{pb.x+pb.w*0.42}}px;top:${{pb.top-10}}px;height:${{pb.bottom-pb.top+20}}px`; ov.appendChild(p);""" if playhead is not None else ''}
}})();
</script>'''

def cover():
    return '''<style>.cover{position:absolute;left:0;right:0;top:54px;text-align:center;pointer-events:none}.cover .c1{font:500 26px/30px var(--font-display);color:var(--notation)}.cover .c2{font:italic 400 13px/18px var(--font-display);color:var(--ink-2);margin-top:2px}</style>
<script>document.querySelector('.cover').innerHTML='<div class="c1">Für Elise</div><div class="c2">Bagatelle No. 25 in A minor, WoO 59 · L. van Beethoven</div>';</script>'''

out = {}

# ---------- S2: score page, default
out['s2_score_page.html'] = head('light') + f'''<div class="screen ipad-l" id="scr">{spread()}{cover()}{bar('Für Elise', f'pp. 1–2 of {NP}')}</div></body></html>'''

# ---------- S3: selection + menu + pill
out['s3_selection.html'] = head('light') + f'''<div class="screen ipad-l" id="scr">{spread(sel=[9,16], menu=True, on=2, playhead=10)}{cover()}{bar('Für Elise', f'pp. 1–2 of {NP}', on='phrases')}
{pill([it('loop','Loop',True), it('dial','75%'), it('hands','Right hand'), '<span class="div"></span>', it('metronome',''), it('count','')])}</div></body></html>'''

# ---------- S6: insight sheet
out['s6_insight.html'] = head('light') + f'''<div class="screen ipad-l" id="scr">{spread(sel=[9,16], on=2, rings=[9,11])}{cover()}{bar('Für Elise', f'pp. 1–2 of {NP}', on='insight').replace('class="bar"','class="bar" style="right:404px"')}
<div class="sheet"><span class="grab"></span>
<div class="hd"><div class="overline">Phrase 3 · bars 9–16 · 54 notes</div></div>
<div style="display:flex;gap:6px;padding:10px 20px 12px 24px;border-bottom:1px solid var(--hairline)"><span class="chip on" style="height:26px">All</span><span class="chip" style="height:26px">Rhythm</span><span class="chip" style="height:26px">Melody</span><span class="chip" style="height:26px">Harmony</span><span class="chip" style="height:26px">Form</span></div>
<div class="bd">
<div class="overline" style="margin-bottom:8px">Rhythm · 6 facts</div>
<div class="fact on"><div class="t">Bars 9, 10 and 11 share one rhythm.</div><div class="m"><span class="tag detected">detected</span><span>15 places</span><a>Evidence</a></div><div class="m" style="margin-top:8px;gap:6px"><span style="color:var(--ink-3)">Try</span><span class="chip on" style="height:24px;padding:0 10px">Dotted</span><span class="chip" style="height:24px;padding:0 10px">Syncopate</span><span class="chip" style="height:24px;padding:0 10px">Half-time</span><span style="flex:1"></span><a style="font-weight:600">Keep</a><a>Adjust</a></div></div>
<div class="fact"><div class="t">Every attack sits on the sixteenth grid; 12 of 24 fall on a beat.</div><div class="m"><span class="tag detected">detected</span><a>Evidence</a></div></div>
<div class="fact"><div class="t">No syncopation: nothing off the beat is held across it.</div><div class="m"><span class="tag assumed">assumed</span><a>Evidence</a></div></div>
<div class="overline" style="margin:16px 0 8px">Melody · 4 facts</div>
<div class="fact"><div class="t">The line rises a sixth, then falls stepwise to the tonic.</div><div class="m"><span class="tag detected">detected</span><a>Evidence</a></div></div>
<div class="fact"><div class="t">The upper voice is the melody throughout.</div><div class="m"><span class="tag assumed">assumed</span><a>Evidence</a></div></div>
<div class="overline" style="margin:16px 0 8px">Key · 2 facts</div>
<div class="fact"><div class="t">A minor, with the leading tone G♯ in the left hand.</div><div class="m"><span class="tag detected">detected</span><a>Evidence</a></div></div>
</div>
<div style="margin-top:auto;padding:12px 24px;border-top:1px solid var(--hairline)" class="caption">Tap a fact to see its places on the score · swipe right to put away</div>
</div></div></body></html>'''

# ---------- S4/S5: focus
def focus(theme, slate=False):
    notes = NOTES
    return head(theme, slate=slate) + f'''<div class="screen ipad-l" id="scr">{spread(on=2, ids=('q1','q2'))}{cover()}<div class="veil"></div>
<div class="bar" style="background:color-mix(in srgb, var(--surface) 94%, transparent)"><span class="act">{ic("back","l")}</span><span class="label" style="font-size:15px">Back to page</span><span class="sp"></span><span class="label" style="font-size:15px;color:var(--ink-2)">p. 1 · bars 9–16 · <b style="color:var(--ink);font-weight:600">phrase 3 of 16</b></span><span class="sp"></span><span class="seg"><span>Original</span><span class="on">Variation</span><span>Alternate</span></span><span class="act">{ic("more","l")}</span></div>
<div style="position:absolute;left:130px;top:66px;width:920px;height:86px;border:1.5px dashed var(--plum);border-radius:8px;background:color-mix(in srgb, var(--paper) 92%, transparent);color:var(--notation)"><span class="overline" style="position:absolute;left:12px;top:-8px;background:var(--paper);padding:0 6px;color:var(--plum)">ossia · variation “give the melody more air”</span><div class="nota" style="position:absolute;left:40px;top:6px;width:840px">{OSSIA}</div></div>
<div class="page" style="left:40px;top:160px;width:1100px;height:192px;border-radius:10px;box-shadow:var(--shadow-sheet), 0 0 0 2px var(--accent)"><div style="position:absolute;left:46px;top:12px;width:1008px">{PHRASE}</div>
<div class="playhead" style="left:412px;top:10px;height:172px"></div>
<span class="btn icon secondary" style="position:absolute;left:4px;top:76px;width:36px;height:36px;border-radius:50%">{ic("back")}</span><span class="btn icon secondary" style="position:absolute;right:4px;top:76px;width:36px;height:36px;border-radius:50%">{ic("fwd")}</span></div>
<div class="pill" style="bottom:auto;top:362px">{'<span class="play">'+ic("pause","l")+'</span>'}{it('loop','Loop',True)}{it('dial','75%')}{it('hands','Right hand')}{it('leadin','Lead-in',True)}<span class="div"></span>{it('metronome','')}{it('count','')}</div>
<div class="drawer" style="height:400px"><span class="grab"></span>
<div style="position:absolute;left:24px;right:24px;top:18px;display:flex;align-items:center;gap:12px"><span class="overline">Piano roll · bars 9–16</span><span class="sp" style="flex:1"></span><span class="chip on">{ic("dot","s")}Note names</span><span class="chip">Colour: hands</span><span class="chip">{ic("more","s")}</span></div>
<div style="position:absolute;left:16px;right:16px;top:50px;bottom:8px"><div class="roll" id="roll"></div></div></div>
<script>
const PN={notes}; const host=document.getElementById('roll');
rollRender(host, PN, 8, 40, 76, 9.5, {{left:44, playhead:17, sel:n=>n.bar===11}});
document.querySelectorAll('.pill .play').forEach((e,i)=>{{ if(i==0) e.innerHTML=ic('pause','l'); }});
</script></div></body></html>'''
out['s4_focus_light.html'] = focus('light')
out['s5_focus_dark.html'] = focus('dark', slate=True)

# ---------- S1: library
def thumb(svg): return f'<div class="thumb">{svg}</div>'
out['s1_library.html'] = head('light') + f'''<div class="screen ipad-l" id="scr" style="padding:40px 48px">
<div style="display:flex;align-items:center;gap:12px"><span class="display">Motiff Lab</span><span class="caption" style="margin-left:4px;font-style:italic;font-family:var(--font-display);font-size:14px">Hear. Understand. Transform.</span><span style="flex:1"></span><span class="btn primary">{ic("plus")}Add piece</span><span class="btn icon quiet">{ic("more","l")}</span><span style="width:40px;height:40px;border-radius:50%;background:var(--accent-tint);color:var(--accent);display:grid;place-items:center;font:600 15px var(--font-ui)">A</span></div>
<div class="overline" style="margin:40px 0 12px">Your pieces</div>
<div style="display:grid;grid-template-columns:repeat(3,1fr);gap:20px">
<div class="card"><div style="display:flex;gap:16px">{thumb(P[1])}<div><div class="ttl">Für Elise</div><div class="caption" style="margin-top:4px">Beethoven · {NP} pages · 16 phrases</div><div style="margin-top:10px"><span class="tag saved">3 ideas</span></div></div></div><div class="rule"></div><div><div class="overline">Recent work</div><div style="margin-top:4px">Bars 9–24, slow practice</div><div class="caption">yesterday · p. 1 · phrase 3</div></div><div style="display:flex;gap:8px"><span class="btn secondary compact">{ic("play","s")}Resume</span><span class="btn quiet compact">Open page 1</span></div></div>
<div class="card hover"><div style="display:flex;gap:16px">{thumb(P[3])}<div><div class="ttl">All my joy all my pain</div><div class="caption" style="margin-top:4px">From a recording · 5 pages · 9 phrases</div><div style="margin-top:10px"><span class="tag warn">Check · 7 bars to review</span></div></div></div><div class="rule"></div><div><div class="overline">Recent work</div><div style="margin-top:4px">Transcription review</div><div class="caption">today · p. 1</div></div><div style="display:flex;gap:8px"><span class="btn secondary compact">{ic("play","s")}Resume</span><span class="btn quiet compact">Open page 1</span></div></div>
<div class="card add" style="gap:8px;min-height:300px"><span style="width:48px;height:48px;border-radius:50%;background:var(--accent-tint);color:var(--accent);display:grid;place-items:center;margin-bottom:6px">{ic("plus","l")}</span><div class="heading">Add a piece</div><div class="caption">Open a MIDI or MusicXML file</div><div class="caption">Scan a page from PDF or photo</div><div class="caption">Transcribe a recording</div><div class="caption" style="margin-top:10px">or try the demo piece</div></div>
</div>
<div class="caption" style="position:absolute;left:48px;bottom:28px">Privacy · Terms</div>
</div></body></html>'''

# ---------- S7: portrait with annotations
out['s7_portrait_annotations.html'] = head('light') + f'''<div class="screen ipad-p" id="scr">
<div class="page" id="pp" style="left:24px;top:44px;width:772px;height:1092px">{P[2]}<span class="no" style="right:28px">2</span></div>
<div class="ov" id="ov" style="inset:0"></div>
<div class="bar" style="left:24px;right:24px"><span class="act">{ic("back","l")}</span><span class="ttl">Für Elise</span><span class="pg">p. 2 of 4</span><span class="sp"></span><span class="act on">{ic("pen","l")}</span><span class="act">{ic("layers","l")}</span><span class="act">{ic("more","l")}</span></div>
<div class="menu" style="left:390px;top:70px;height:48px"><span class="it on" style="background:var(--accent-tint)"><span style="width:18px;height:18px;border-radius:50%;background:var(--ink-graphite)"></span></span><span class="it"><span style="width:18px;height:18px;border-radius:50%;background:var(--ink-carmine)"></span></span><span class="it"><span style="width:18px;height:18px;border-radius:50%;background:var(--ink-forest)"></span></span><span class="div"></span><span class="it">{ic("highlight")}</span><span class="it">{ic("pen")}eraser</span><span class="div"></span><span class="it">{ic("undo")}</span></div>
<script>
(function(){{
 const root=document.getElementById('scr'), ov=document.getElementById('ov');
 const boxes=measureBoxes(document.getElementById('pp'),root,{starts[2]});
 drawBrackets(ov,boxes,phraseRanges({sum(counts.values())}),{{saved:[2]}});
 // highlight over bars 39-40 (page 2 local), tag bracket over 43-44, margin note at 36, fingering on bar 33, ink circle at 47, check flag at 50
 function seg(a,b){{ const ms=boxes.filter(x=>x.idx>=a&&x.idx<=b); return {{x:ms[0].x,w:ms[ms.length-1].x+ms[ms.length-1].w-ms[0].x,top:Math.min(...ms.map(m=>m.top)),bottom:Math.max(...ms.map(m=>m.bottom)),first:ms[0]}}; }}
 let s=seg(39,40); ov.insertAdjacentHTML('beforeend',`<div class="hl" style="left:${{s.x}}px;top:${{s.top-4}}px;width:${{s.w}}px;height:${{(s.bottom-s.top)/2}}px"></div>`);
 s=seg(37,38); ov.insertAdjacentHTML('beforeend',`<div class="tagb" style="left:${{s.x+2}}px;top:${{s.top-30}}px;width:${{s.w-4}}px"><span class="w">difficult — slow, then RH alone</span></div>`);
 s=seg(36,36); ov.insertAdjacentHTML('beforeend',`<div class="marker" style="left:${{s.x-12}}px;top:${{s.top-36}}px">1</div>`);
 ov.insertAdjacentHTML('beforeend',`<div class="dialog" style="position:absolute;left:${{s.x-330}}px;top:${{s.top-150}}px;width:300px;padding:14px 16px;border-radius:12px"><div class="overline">Note · bar 36</div><div style="margin-top:4px">Breathe before the return — don’t rush the pickup. Listen for the bass A.</div><div class="caption" style="margin-top:6px">yesterday · tap to edit · delete</div></div>`);
 // fingering: numbers above noteheads in bar 33 RH
 const m33=boxes.find(b=>b.idx===33); const rb=root.getBoundingClientRect(); const fs=['3','2','3','1','3','2'];
 [...m33.el.querySelector('g.staff').querySelectorAll('g.note g.notehead')].slice(0,6).forEach((n,i)=>{{ const r=n.getBoundingClientRect(); ov.insertAdjacentHTML('beforeend',`<div class="finger" style="left:${{r.left+r.width/2-rb.left}}px;top:${{r.top-rb.top-14}}px">${{fs[i]}}</div>`); }});
 // ink: a hand-drawn circle around bar 47 and an arrow
 s=seg(47,47); const cx=s.x+s.w/2, cy=(s.top+s.bottom)/2, rx=s.w/2+10, ry=(s.bottom-s.top)/2+6;
 ov.insertAdjacentHTML('beforeend',`<svg class="ink" style="left:0;top:0;width:820px;height:1180px" viewBox="0 0 820 1180"><path d="M${{cx-rx}} ${{cy}} C ${{cx-rx}} ${{cy-ry*1.3}}, ${{cx+rx*1.1}} ${{cy-ry*1.2}}, ${{cx+rx}} ${{cy+2}} S ${{cx-rx*0.9}} ${{cy+ry*1.3}}, ${{cx-rx+6}} ${{cy-8}}" stroke="var(--ink-carmine)" stroke-width="2.2" opacity=".85"/><path d="M${{cx+rx+8}} ${{cy-ry-20}} q 30 -30 70 -24" stroke="var(--ink-carmine)" stroke-width="2.2" opacity=".85"/></svg>`);
 ov.insertAdjacentHTML('beforeend',`<div style="position:absolute;left:${{cx+rx+70}}px;top:${{cy-ry-54}}px;font:italic 500 15px/18px var(--font-display);color:var(--ink-carmine);transform:rotate(-4deg)">pedal!</div>`);
 s=seg(50,50); ov.insertAdjacentHTML('beforeend',`<div class="flag" style="left:${{s.x+s.w-20}}px;top:${{s.top-26}}px">?</div>`);
}})();
</script></div></body></html>'''

# ---------- Boards
SW = lambda name, hexv, var, dark=False: f'<div class="sw"><div class="c" style="background:{hexv}"></div><div class="n">{name}</div><div class="h">{hexv}</div><div class="v">{var}</div></div>'
def palette(theme):
    L = theme == 'light'
    rows = [
      ('Surfaces', [('Desk', '#E8E3D9' if L else '#141311', '--desk'), ('Surface', '#F7F4ED' if L else '#1F1D19', '--surface'), ('Surface 2', '#FFFFFF' if L else '#2A2723', '--surface-2'), ('Paper', '#FCF9F2' if L else '#E7E0D1', '--paper'), ('Slate paper', '#24221E', '--paper (slate)') if not L else ('Notation', '#16130F', '--notation')]),
      ('Text', [('Ink', '#1F1C18' if L else '#EEE9DF', '--ink'), ('Ink 2', '#5E584E' if L else '#A9A295', '--ink-2'), ('Ink 3', '#8F887B' if L else '#78715F', '--ink-3'), ('Hairline', 'rgba(31,28,24,.12)' if L else 'rgba(238,233,223,.12)', '--hairline')]),
      ('Accent & hues', [('Cobalt', '#2F5BD6' if L else '#7D9CFF', '--accent'), ('Cobalt tint', 'rgba(47,91,214,.14)' if L else 'rgba(125,156,255,.20)', '--accent-tint'), ('Plum', '#7B3F9E' if L else '#B98CD8', '--plum'), ('Amber', '#F0B429' if L else '#F3C14E', '--amber'), ('Teal', '#1F8A8A' if L else '#4FBFB4', '--teal')]),
      ('Semantic', [('Success', '#2E7D4F' if L else '#5CBB7D', '--success'), ('Warning', '#965D04' if L else '#F3C14E', '--warning'), ('Danger', '#C8352B' if L else '#F07C70', '--danger')]),
      ('User ink', [('Graphite', '#3B3733', '--ink-graphite'), ('Carmine', '#C8352B', '--ink-carmine'), ('Forest', '#2E7D4F', '--ink-forest'), ('Ochre', '#9C6A0C', '--ink-ochre'), ('Highlighter', 'rgba(240,180,41,.38)', '--amber-tint')]),
    ]
    html = ''.join(f'<div class="grp"><div class="overline">{g}</div><div class="sws">' + ''.join(SW(*s) for s in sws) + '</div></div>' for g, sws in rows)
    return f'<div data-theme="{theme}" class="pal"><div class="heading" style="margin-bottom:12px">{"Daylight (light theme)" if L else "Night stand (dark theme)"}</div>{html}</div>'
out['b1_palette.html'] = head('light', style='''body{padding:28px;background:#fff}.pal{padding:24px;border-radius:16px;background:var(--desk);color:var(--ink);margin-bottom:20px}.grp{margin-top:14px}.sws{display:flex;gap:12px;margin-top:8px;flex-wrap:wrap}.sw{width:118px}.sw .c{height:56px;border-radius:10px;border:1px solid var(--hairline)}.sw .n{font:600 13px/18px var(--font-ui);margin-top:6px}.sw .h{font:400 12px/16px var(--font-ui);color:var(--ink-2)}.sw .v{font:400 11px/14px var(--font-ui);color:var(--ink-3)}''') + palette('light') + palette('dark') + '</body></html>'

out['b2_type.html'] = head('light', style='body{padding:40px;background:var(--surface)}.row{display:grid;grid-template-columns:180px 1fr;gap:24px;align-items:baseline;padding:14px 0;border-bottom:1px solid var(--hairline)}.spec{font:400 12px/16px var(--font-ui);color:var(--ink-2)}') + '''
<div class="overline">Typography</div>
<div class="row"><div class="spec">Display · Source Serif 4 500 · 28/32</div><div class="display">Für Elise · Bagatelle in A minor</div></div>
<div class="row"><div class="spec">Title · Source Serif 4 500 · 20/26</div><div class="title">Phrase 3 of 16 · bars 9–16</div></div>
<div class="row"><div class="spec">Heading · Source Sans 3 600 · 16/22</div><div class="heading">Bars 9, 10 and 11 share one rhythm</div></div>
<div class="row"><div class="spec">Body · Source Sans 3 400 · 15/22</div><div>Every attack sits on the sixteenth grid; 12 of 24 fall on a beat. The upper voice carries the melody throughout, whichever hand plays it.</div></div>
<div class="row"><div class="spec">Label · Source Sans 3 500 · 13/18</div><div class="label">Loop &nbsp; Tempo 75% &nbsp; Right hand &nbsp; Metronome &nbsp; Count-in</div></div>
<div class="row"><div class="spec">Caption · Source Sans 3 400 · 12/16</div><div class="caption">detected · 15 places · Evidence · yesterday · p. 1 · phrase 3</div></div>
<div class="row"><div class="spec">Overline · 600 · 11/14 · +6% tracking</div><div class="overline">Rhythm · 6 facts</div></div>
<div class="row"><div class="spec">Numerals · tabular</div><div style="font-variant-numeric:tabular-nums">bar 112 · beat 3 · ♩ = 72 · 00:48 / 03:12</div></div>
<div class="row"><div class="spec">Handwritten marks · Source Serif 4 italic</div><div style="font:italic 500 16px var(--font-display);color:var(--ink-carmine)">difficult — slow, then RH alone &nbsp; <span style="color:var(--ink-graphite)">pedal!</span></div></div>
<div class="row"><div class="spec">Notation · Leland (SMuFL) via Verovio</div><div class="nota" style="color:var(--notation);background:var(--paper);padding:8px 12px;border-radius:6px;width:520px">''' + OSSIA.replace('<svg ', '<svg style="width:500px;height:auto" ', 1) + '''</div></div>
</body></html>'''

def comp_col(theme):
    return f'''<div data-theme="{theme}" class="col" style="background:var(--desk);color:var(--ink)">
<div class="overline" style="margin-bottom:10px">{"Light" if theme=="light" else "Dark"}</div>
<div class="g"><span class="lbl">Buttons</span><div class="r"><span class="btn primary">{ic("play")}Play</span><span class="btn secondary">Resume</span><span class="btn quiet">Cancel</span><span class="btn danger">Delete</span><span class="btn primary disabled">Disabled</span></div></div>
<div class="g"><span class="lbl">States</span><div class="r"><span class="btn secondary">Default</span><span class="btn secondary hover">Hover</span><span class="btn secondary pressed">Pressed</span><span class="btn secondary focus">Focused</span><span class="chip on">Selected</span></div></div>
<div class="g"><span class="lbl">Segmented</span><div class="r"><span class="seg"><span class="on">Original</span><span>Variation</span><span>Alternate</span></span><span class="seg"><span>All</span><span class="on">Right</span><span>Left</span><span>Melody</span></span></div></div>
<div class="g"><span class="lbl">Chips & tags</span><div class="r"><span class="chip">{ic("dot","s")}Note names</span><span class="chip on">Loop</span><span class="tag detected">detected</span><span class="tag assumed">assumed</span><span class="tag warn">Check · 7 bars</span><span class="tag saved">3 ideas</span></div></div>
<div class="g"><span class="lbl">Transport pill</span><div class="r" style="position:relative;height:60px"><div class="pill" style="position:static;transform:none">{'<span class="play">'+ic("playf","l")+'</span>'}{it('loop','Loop',True)}{it('dial','75%')}{it('hands','Right')}<span class="div"></span>{it('metronome','')}{it('count','')}</div></div></div>
<div class="g"><span class="lbl">Contextual menu</span><div class="r" style="position:relative;height:60px"><div class="menu" style="position:static"><span class="it">{ic("loop")}Loop</span><span class="it hover">{ic("insight")}Focus</span><span class="it">{ic("note")}Note</span><span class="it">{ic("bookmark")}Bookmark</span><span class="div"></span><span class="it">{ic("roll")}Piano roll</span><span class="it">{ic("more")}</span></div></div></div>
<div class="g"><span class="lbl">Inputs</span><div class="r"><span class="field" style="width:220px"><span class="ph">Name this bookmark</span></span><span class="field focus" style="width:180px">Slow practice|</span><span class="switch on"></span><span class="switch"></span><span class="check on">{ic("check","s")}</span><span class="check"></span></div></div>
<div class="g"><span class="lbl">Sheet filters & fact</span><div class="r" style="display:block;background:var(--surface);border:1px solid var(--hairline);border-radius:12px;width:360px;overflow:hidden"><div style="display:flex;gap:6px;padding:10px 16px;border-bottom:1px solid var(--hairline)"><span class="chip on" style="height:26px">All</span><span class="chip" style="height:26px">Rhythm</span><span class="chip" style="height:26px">Melody</span><span class="chip" style="height:26px">Harmony</span></div><div style="padding:12px 24px"><div class="fact on"><div class="t">Bars 9, 10 and 11 share one rhythm.</div><div class="m"><span class="tag detected">detected</span><span>15 places</span><a>Evidence</a></div><div class="m" style="margin-top:8px;gap:6px"><span style="color:var(--ink-3)">Try</span><span class="chip on" style="height:24px;padding:0 10px">Dotted</span><span class="chip" style="height:24px;padding:0 10px">Syncopate</span><span style="flex:1"></span><a style="font-weight:600">Keep</a><a>Adjust</a></div></div><div class="fact"><div class="t">No syncopation.</div><div class="m"><span class="tag assumed">assumed</span><a>Evidence</a></div></div></div></div></div>
<div class="g"><span class="lbl">Dialog & toast</span><div class="r" style="align-items:flex-start"><div class="dialog" style="width:300px;padding:18px"><div class="title" style="font-size:18px">Delete this piece?</div><div class="caption" style="margin-top:4px">Its bookmarks, marks and ideas go with it.</div><div style="display:flex;gap:8px;justify-content:flex-end;margin-top:14px"><span class="btn quiet compact">Keep</span><span class="btn danger compact">Delete</span></div></div><span class="toast">{ic("check")}Variation saved to Ideas</span></div></div>
<div class="g"><span class="lbl">Score marks</span><div class="r" style="position:relative;height:118px;background:var(--paper);border-radius:8px;width:490px;display:block"><div class="band" style="left:16px;top:12px;width:100px;height:40px"></div><div class="handle" style="left:16px;top:32px"></div><div class="band focus" style="left:150px;top:12px;width:100px;height:40px"></div><div class="hl" style="left:290px;top:18px;width:60px;height:28px"></div><div class="marker" style="left:372px;top:22px">1</div><div class="flag" style="left:408px;top:23px">?</div><div class="playhead" style="left:450px;top:8px;height:48px"></div><div class="bracket" style="left:16px;top:88px;width:90px"><span class="n">4</span></div><div class="bracket on" style="left:130px;top:88px;width:90px"><span class="n">5</span><span class="dot"></span></div><div class="tagb" style="left:260px;top:94px;width:100px"><span class="w">difficult</span></div><div class="ring" style="left:420px;top:92px;width:22px;height:22px"></div><div class="finger" style="left:460px;top:98px">3</div></div></div></div>
</div>'''
out['b3_components.html'] = head('light', style='body{display:grid;grid-template-columns:1fr 1fr;gap:0;background:#fff}.col{padding:28px 32px}.g{display:grid;grid-template-columns:120px 1fr;gap:16px;align-items:center;padding:12px 0;border-bottom:1px solid var(--hairline)}.lbl{font:600 12px/16px var(--font-ui);color:var(--ink-3)}.r{display:flex;gap:10px;align-items:center;flex-wrap:wrap}') + comp_col('light') + comp_col('dark') + '</body></html>'

out['b4_icons.html'] = head('light', style='body{padding:32px;background:var(--surface)}.grid{display:grid;grid-template-columns:repeat(12,1fr);gap:14px}.i{display:flex;flex-direction:column;align-items:center;gap:6px;padding:10px 4px;border-radius:8px;background:var(--surface-2);border:1px solid var(--hairline)}.i .n{font:400 11px/14px var(--font-ui);color:var(--ink-2)}') + '''<div class="overline" style="margin-bottom:12px">Icons · 20 px grid · 1.5 px stroke · round caps and joins · 24 px / 1.75 px in the bar</div><div class="grid" id="g"></div>
<div style="display:flex;gap:28px;align-items:center;margin-top:24px"><span class="caption">Sizes</span><span>'''+'''</span></div>
<script>document.getElementById('g').innerHTML=Object.keys(ICONS).filter(k=>k!=='playf').map(k=>`<div class="i">${ic(k)}<span class="n">${k}</span></div>`).join('');
document.querySelector('div[style*="gap:28px"] span:last-child').innerHTML=ic('loop','s')+' 16 &nbsp; '+ic('loop')+' 20 &nbsp; '+ic('loop','l')+' 24 &nbsp;&nbsp; <span class="act" style="display:inline-grid;place-items:center;width:44px;height:44px;border-radius:8px;background:var(--accent-tint);color:var(--accent);vertical-align:middle">'+ic('loop','l')+'</span> <span class="caption">44 px touch target, tinted when on</span>';</script></body></html>'''

for k, v in out.items():
    open(os.path.join(D, k), 'w', encoding='utf8').write(v)
print(counts, starts, list(out))

# ---------- rev 3: four more screens
MA, MB, MC = rd('motif_a.svg'), rd('motif_b.svg'), rd('motif_c.svg')

# S8 bookmarks: Contents drawer on the left, bookmark brackets on the score
out['s8_bookmarks.html'] = head('light') + f'''<div class="screen ipad-l" id="scr">{spread(sel=[41,44], on=None, saved=(2,))}{cover()}{bar('Für Elise', f'pp. 1–2 of {NP}').replace('class="bar"','class="bar" style="left:344px"')}
<div class="sheet left"><span class="grab"></span>
<div class="hd"><div class="title" style="font-size:19px">Für Elise</div><div class="caption">Contents · 9 bookmarks</div></div>
<div class="tabs" style="margin-top:8px"><span class="on">Contents</span><span>Notes</span><span>Pages</span></div>
<div class="bd" style="padding-top:12px">
<div style="display:flex;align-items:center;justify-content:space-between"><span class="seg"><span class="on">Score order</span><span>My order</span></span><span class="btn quiet compact">{ic("plus","s")}Category</span></div>
<div class="cat"><span class="d" style="background:var(--ink-carmine)"></span>Practise · 3</div>
<div class="entry"><span class="t">Opening, left hand alone</span><span class="c">bars 1–8</span></div>
<div class="entry"><span class="t">The leap</span><span class="c">bars 23–24 · 2 ideas</span></div>
<div class="entry"><span class="t">Coda runs</span><span class="c">bars 48–56</span></div>
<div class="cat"><span class="d" style="background:var(--ink-forest)"></span>Performance · 2</div>
<div class="entry"><span class="t">Breathe</span><span class="c">bar 33</span></div>
<div class="entry on"><span class="t">Pedal from here</span><span class="c">bars 41–44</span></div>
<div class="cat"><span class="d" style="background:var(--ink-graphite)"></span>Structure · 4</div>
<div class="entry"><span class="t">A</span><span class="c">bars 1–8</span></div>
<div class="entry"><span class="t">A′</span><span class="c">bars 9–16</span></div>
<div class="entry"><span class="t">B</span><span class="c">bars 17–22</span></div>
<div class="entry"><span class="t">Coda</span><span class="c">bars 48–56</span></div>
</div>
<div style="margin-top:auto;padding:12px 24px;border-top:1px solid var(--hairline)" class="caption">Tap to go there · tap again to focus · drag to reorder</div>
</div>
<script>
(function(){{
 const root=document.getElementById('scr'), ov=document.getElementById('ov'); const rb=root.getBoundingClientRect();
 const b1=measureBoxes(document.getElementById('p1'),root,{starts[1]}), b2=measureBoxes(document.getElementById('p2'),root,{starts[2]});
 const boxes=b1.concat(b2.map(b=>Object.assign(b,{{sys:b.sys+100}})));
 function segs(a,b){{ const ms=boxes.filter(x=>x.idx>=a&&x.idx<=b); const by={{}}; ms.forEach(m=>(by[m.sys]=by[m.sys]||[]).push(m)); return Object.values(by).map(s=>({{x:s[0].x+2,w:s[s.length-1].x+s[s.length-1].w-s[0].x-4,top:Math.min(...s.map(m=>m.top))}})); }}
 const used={{}}; function bm(a,b,title,c){{ segs(a,b).forEach((s,i)=>{{ const key=Math.round(s.top)+':'+Math.round(s.x); const lvl=(used[key]||0); used[key]=lvl+1; ov.insertAdjacentHTML('beforeend',`<div class="bmb" style="--c:${{c}};left:${{s.x}}px;top:${{s.top-32-lvl*30}}px;width:${{s.w}}px">${{i==0?'<span class="w">'+title+'</span>':''}}</div>`); }}); }}
 bm(0,8,'Opening, left hand alone','var(--ink-carmine)');
 bm(0,8,'A','var(--ink-graphite)'); bm(9,16,'A′','var(--ink-graphite)'); bm(17,22,'B','var(--ink-graphite)');
 bm(41,44,'Pedal from here','var(--ink-forest)'); bm(33,33,'Breathe','var(--ink-forest)');
 bm(23,24,'The leap','var(--ink-carmine)');
}})();
</script></div></body></html>'''

# S9 assistant: Insights sheet with a conversation
out['s9_assistant.html'] = head('light') + f'''<div class="screen ipad-l" id="scr">{spread(sel=[9,16], on=2, rings=[11,11])}{cover()}{bar('Für Elise', f'pp. 1–2 of {NP}', on='insight').replace('class="bar"','class="bar" style="right:404px"')}
<div class="sheet"><span class="grab"></span>
<div class="hd"><div class="overline">Ask</div><div style="display:flex;gap:6px;margin-top:8px"><span class="chip on">This phrase · 9–16</span><span class="chip">Whole piece</span></div></div>
<div class="bd" style="display:flex;flex-direction:column;gap:10px;padding-top:12px">
<div class="caption">▸ Facts · 12 · Tries · 1 kept</div>
<div class="caption">▸ What it knows · looped 9–16 at 75% for 12 min · kept Dotted · 1 note</div>
<div class="msg me">I keep stumbling at the leap in bar 11, right hand.</div>
<div class="msg ai">The leap is E4 to E5 on beat 2 of bar 11, and you have been looping all eight bars. Isolate the two beats around it, slower, right hand only:<div class="acts"><span class="act-chip on">{ic("loop","s")}Loop 11.1–11.3</span><span class="act-chip">{ic("dial","s")}60%</span><span class="act-chip">{ic("hands","s")}Right hand</span><span class="act-chip">{ic("bookmark","s")}Bookmark · Practise</span></div></div>
<div class="msg ai">Fingering 1–5 on the leap keeps the hand quiet. Want it on the score?<div class="acts"><span class="act-chip">{ic("search","s")}Show</span><span class="act-chip">{ic("note","s")}Add as note</span></div></div>
<div class="msg ai" style="border-color:var(--plum-tint)"><span class="caption" style="color:var(--plum)">Pro</span><div style="margin-top:2px">A practice plan for this week, built from what you looped.</div><div class="acts"><span class="act-chip pro">Make the plan</span></div></div>
</div>
<div style="margin-top:auto;padding:12px 20px 14px 24px;border-top:1px solid var(--hairline)"><div class="field" style="height:44px;border-radius:22px"><span class="ph" style="flex:1">Ask or say what you noticed…</span>{ic("mic")}</div></div>
</div></div></body></html>'''

# S10 settings: global sheet over the dimmed score + contextual popover on the Phrases action
def sw(on): return f'<span class="switch {"on" if on else ""}"></span>'
rows = [('Phrase brackets','Detected phrases as brackets above the staff',True),('Insights strip in focus','Three strongest facts under the phrase',True),('Hints and coach marks','First-use explanations',False),('Suggest focus after 3 loops','',False),('Assistant hints','A quiet “Ask?” when something is notable',False),('Transport pill appears with a selection','',True),('Bar fades after 2 s','',True),('Check flags','Uncertain bars from scan or transcription',True),('Show Pro offers','',True)]
rows_html = ''.join(f'<div class="setrow"><div class="l"><div class="t">{t}</div>{("<div class=\"c\">"+c+"</div>") if c else ""}</div>{sw(on)}</div>' for t,c,on in rows)
out['s10_settings.html'] = head('light') + f'''<div class="screen ipad-l" id="scr">{spread(on=None, brackets=True, ids=('q1','q2'))}{cover()}{bar('Für Elise', f'pp. 1–2 of {NP}', on='phrases')}<div class="veil" style="background:rgba(31,28,24,.28)"></div>
<div class="dialog" style="position:absolute;left:30px;top:70px;width:560px;height:700px;padding:0;display:flex;flex-direction:column;overflow:hidden">
<div style="display:flex;align-items:center;padding:16px 24px;border-bottom:1px solid var(--hairline)"><span class="title">Settings</span><span style="flex:1"></span><span class="btn icon quiet">{ic("close","l")}</span></div>
<div style="display:flex;flex:1;min-height:0"><div class="nav" style="padding-left:12px"><div>Level</div><div class="on">Guidance</div><div>Score</div><div>Marks and layers</div><div>Playback</div><div>Piano roll</div><div>Assistant</div><div>Appearance</div><div>Account</div></div>
<div style="flex:1;padding:16px 24px;overflow:hidden"><div class="overline">Guidance</div>
<div style="display:flex;align-items:center;gap:12px;margin:10px 0 4px"><span class="seg"><span>Learning</span><span class="on">Practising</span><span>Clean</span></span></div>
{rows_html}
<div style="display:flex;align-items:center;gap:12px;margin-top:12px"><span class="caption">This piece: granularity Short · bookmarks layer off</span><span style="flex:1"></span><span class="btn quiet compact">Reset to preset</span></div>
</div></div></div>
<div class="dialog" style="position:absolute;right:270px;top:66px;width:300px;padding:14px 16px;border-radius:12px"><div class="overline">Phrases</div>
<div class="setrow" style="min-height:44px"><div class="l"><div class="t">Granularity</div></div><span class="seg" style="height:28px"><span style="line-height:22px">Short</span><span class="on" style="line-height:22px">Phrases</span><span style="line-height:22px">Sentences</span></span></div>
<div class="setrow" style="min-height:44px"><div class="l"><div class="t">Show brackets</div></div>{sw(True)}</div>
<div class="setrow" style="min-height:44px;border:0"><div class="l"><div class="t">Reset to detected</div></div></div>
<div style="display:flex;align-items:center;gap:8px;margin-top:8px"><span class="seg" style="height:28px"><span class="on" style="line-height:22px">This piece</span><span style="line-height:22px">Everywhere</span></span><span style="flex:1"></span><a class="label" style="color:var(--accent)">All settings ›</a></div>
<div style="position:absolute;right:112px;top:-7px;width:12px;height:12px;background:var(--surface-2);border-left:1px solid var(--hairline);border-top:1px solid var(--hairline);transform:rotate(45deg)"></div></div>
</div></body></html>'''

# S11 Piece DNA
secs = [('A',0,8),('A′',8,16),('B',16,22),('A',22,30),('A′',30,38),('B',38,44),('Coda',44,56)]
W = 1100; L = 150; px = (W-L)/56
def sx(b): return L + b*px
sec_html = ''.join(f'<div class="dna-sec" style="left:{sx(a)}px;width:{(b-a)*px}px;background:{"var(--plum-tint)" if n=="B" else "var(--hairline)" if n=="Coda" else "var(--accent-tint)" if n=="A" else "rgba(47,91,214,.07)"}">{n}</div>' for n,a,b in secs)
arcs = ''.join(f'<path d="M{sx(a)+ (b-a)*px/2} 60 A {((c-a))*px/2} 46 0 0 1 {sx(c)+(d-c)*px/2} 60" fill="none" stroke="var(--accent)" stroke-opacity=".55" stroke-width="1.2"/>' for (a,b),(c,d) in [((0,8),(22,30)),((8,16),(30,38)),((16,22),(38,44)),((22,30),(44,56))])
def lane(y, name, marks):
    return f'<div class="lane" style="top:{y}px"><span class="n">{name}</span>' + ''.join(f'<div class="mk {k}" style="left:{sx(b)}px"></div>' for b,k in marks) + '</div>'
la = lane(0,'a · the turn',[(0.5,''),(2.5,''),(4.5,''),(8.5,''),(10.5,''),(12.5,'o'),(22.5,''),(24.5,''),(26.5,''),(30.5,''),(32.5,''),(34.5,'o'),(46,'o'),(52,'v')])
lb = lane(34,'b · the answer',[(2,''),(6,'o'),(10,''),(14,'o'),(24,''),(28,'o'),(32,''),(36,'o'),(50,'o')])
lc = lane(68,'c · the climb',[(16.5,''),(18.5,''),(20.5,'o'),(38.5,''),(40.5,''),(42.5,'o')])
keys = ''.join(f'<div style="position:absolute;left:{sx(a)}px;width:{(b-a)*px-2}px;top:0;height:18px;border-radius:4px;background:{"var(--plum-tint)" if k=="C major" else "var(--accent-tint)"};font:var(--t-caption);padding:1px 6px;color:var(--ink-2)">{k}</div>' for a,b,k in [(0,16,'A minor'),(16,22,'C major'),(22,38,'A minor'),(38,44,'C major'),(44,56,'A minor')])
def card(svg,title,body,n):
    return f'<div class="dna-card"><div class="ex">{svg}</div><div class="heading" style="margin-top:10px">{title}</div><div class="caption" style="margin-top:4px">{body}</div><div style="display:flex;gap:8px;margin-top:10px"><span class="btn secondary compact">{ic("play","s")}Hear</span><span class="btn quiet compact">In the score</span><span style="flex:1"></span><span class="caption">{n}</span></div></div>'
out['s11_piece_dna.html'] = head('light') + f'''<div class="screen ipad-l" id="scr">
<div class="bar"><span class="act">{ic("back","l")}</span><span class="ttl">Für Elise</span><span class="pg">Piece DNA</span><span class="sp"></span><span class="caption">97 bars · 7 sections · 3 ideas · A minor</span><span class="act">{ic("more","l")}</span></div>
<div style="position:absolute;left:40px;top:76px;width:{W}px">
<svg style="position:absolute;left:0;top:0;width:{W}px;height:62px;overflow:visible" viewBox="0 0 {W} 62">{arcs}</svg>
<div style="position:relative;height:28px;margin-top:62px"><span class="overline" style="position:absolute;left:0;top:7px">Form</span>{sec_html}</div>
<div style="position:relative;height:14px;font:var(--t-caption);color:var(--ink-3)">{''.join(f'<span style="position:absolute;left:{sx(a)}px">{a+1}</span>' for n,a,b in secs)}</div>
<div style="position:relative;height:102px;margin-top:14px"><span class="overline" style="position:absolute;left:0;top:-14px">Ideas and where they occur</span>{la}{lb}{lc}</div>
<div style="position:relative;height:18px;margin-top:10px"><span class="overline" style="position:absolute;left:0;top:2px">Key</span>{keys}</div><div class="caption" style="text-align:right;margin-top:4px">● exact · ○ varied · ▼ inverted</div>
<div style="display:grid;grid-template-columns:repeat(3,1fr);gap:16px;margin-top:18px">
{card(MA,'a · the turn','E–D♯–E, the opening cell. 10 times; 3 varied, once inverted in the coda.','10 ×')}
{card(MB,'b · the answer','A–C–E rising, closes each A. 5 times; varied at every return.','5 ×')}
{card(MC,'c · the climb','Stepwise descent over a C pedal, in B only. 6 times, shortened the second time.','6 ×')}
</div>
<div style="display:flex;gap:10px;margin-top:16px"><span class="btn primary compact">{ic("bookmark","s")}Add Structure bookmarks</span><span class="btn secondary compact">{ic("search","s")}Ask about the piece</span><span class="btn secondary compact">{ic("play","s")}Play the map</span><span style="flex:1"></span><span class="btn quiet compact">{ic("layers","s")}More lanes</span></div>
</div></div></body></html>'''

for k in ['s8_bookmarks.html','s9_assistant.html','s10_settings.html','s11_piece_dna.html']:
    open(os.path.join(D, k), 'w', encoding='utf8').write(out[k])
print('rev3 ok')
