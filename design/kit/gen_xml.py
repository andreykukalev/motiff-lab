# Generate a plausible Für Elise (A section) MusicXML, 2 staves, 3/8, divisions=4 (16th=1)
def note(step, octave, dur, alter=None, staff=1, chord=False, typ=None):
    t = {1:'16th',2:'eighth',3:'eighth',4:'quarter',6:'quarter'}[dur]
    s = '<note>'
    if chord: s += '<chord/>'
    s += f'<pitch><step>{step}</step>'
    if alter: s += f'<alter>{alter}</alter>'
    s += f'<octave>{octave}</octave></pitch><duration>{dur}</duration><voice>{staff}</voice><type>{t}</type>'
    if dur==3: s += '<dot/>'
    if alter==1: s += '<accidental>sharp</accidental>'
    s += f'<staff>{staff}</staff></note>'
    return s
def rest(dur, staff):
    t = {1:'16th',2:'eighth',3:'eighth',6:'quarter'}[dur]
    s=f'<note><rest/><duration>{dur}</duration><voice>{staff}</voice><type>{t}</type>'
    if dur==3: s+='<dot/>'
    return s+f'<staff>{staff}</staff></note>'
P = lambda s: (s[0], int(s[-1]), 1 if '#' in s else None)
def rh(seq):  # list of (pitch, dur) or ('r',dur)
    out=''
    for p,d in seq:
        if p=='r': out+=rest(d,1)
        else:
            st,o,a=P(p); out+=note(st,o,d,a,1)
    return out
def lh(seq):
    out=''
    for p,d in seq:
        if p=='r': out+=rest(d,2)
        else:
            st,o,a=P(p); out+=note(st,o,d,a,2)
    return out
A1 = [('E5',1),('D#5',1),('E5',1),('B4',1),('D5',1),('C5',1)]
bars=[]
# pickup
bars.append((rh([('E5',1),('D#5',1)]), lh([('r',2)]), 2))
def Abar(end='a'):
    b=[]
    b.append((rh(A1), lh([('r',6)]),6))
    b.append((rh([('A4',2),('r',1),('C4',1),('E4',1),('A4',1)]), lh([('A2',1),('E3',1),('A3',1),('r',3)]),6))
    b.append((rh([('B4',2),('r',1),('E4',1),('G#4',1),('B4',1)]), lh([('E2',1),('G#2',1),('B2',1),('r',3)]),6))
    if end=='a':
        b.append((rh([('C5',2),('r',1),('E4',1),('E5',1),('D#5',1)]), lh([('A2',1),('E3',1),('A3',1),('r',3)]),6))
        b.append((rh(A1), lh([('r',6)]),6))
        b.append((rh([('A4',2),('r',1),('C4',1),('E4',1),('A4',1)]), lh([('A2',1),('E3',1),('A3',1),('r',3)]),6))
        b.append((rh([('B4',2),('r',1),('E4',1),('C5',1),('B4',1)]), lh([('E2',1),('G#2',1),('B2',1),('r',3)]),6))
        b.append((rh([('A4',2),('r',1),('B4',1),('C5',1),('D5',1)]), lh([('A2',1),('E3',1),('A3',1),('r',3)]),6))
    else:
        b.append((rh([('C5',2),('r',1),('E4',1),('E5',1),('D#5',1)]), lh([('A2',1),('E3',1),('A3',1),('r',3)]),6))
        b.append((rh(A1), lh([('r',6)]),6))
        b.append((rh([('A4',2),('r',1),('C4',1),('E4',1),('A4',1)]), lh([('A2',1),('E3',1),('A3',1),('r',3)]),6))
        b.append((rh([('B4',2),('r',1),('E4',1),('C5',1),('B4',1)]), lh([('E2',1),('G#2',1),('B2',1),('r',3)]),6))
        b.append((rh([('A4',2),('r',1),('E5',1),('D#5',1),('E5',1)]), lh([('A2',1),('E3',1),('A3',1),('r',3)]),6))
    return b
def Bbar():
    b=[]
    b.append((rh([('E5',3),('G5',1),('F5',1),('E5',1)]), lh([('C3',1),('G3',1),('C4',1),('r',3)]),6))
    b.append((rh([('D5',3),('F5',1),('E5',1),('D5',1)]), lh([('G2',1),('G3',1),('B3',1),('r',3)]),6))
    b.append((rh([('C5',3),('E5',1),('D5',1),('C5',1)]), lh([('A2',1),('E3',1),('A3',1),('r',3)]),6))
    b.append((rh([('B4',2),('r',1),('E4',1),('E5',1),('E5',1)]), lh([('E2',1),('E3',1),('r',1),('r',3)]),6))
    b.append((rh([('E5',1),('E6',1),('r',1),('D#5',1),('E5',1),('r',1)]), lh([('r',6)]),6))
    b.append((rh([('D#5',1),('E5',1),('D#5',1),('E5',1),('D#5',1),('E5',1)]), lh([('r',6)]),6))
    return b
bars += (Abar('a') + Abar('b') + Bbar()) * 4 + Abar('b')
xml = ['<?xml version="1.0" encoding="UTF-8"?><!DOCTYPE score-partwise PUBLIC "-//Recordare//DTD MusicXML 4.0 Partwise//EN" "http://www.musicxml.org/dtds/partwise.dtd"><score-partwise version="4.0"><work><work-title>Für Elise</work-title></work><identification><creator type="composer">L. van Beethoven</creator></identification><part-list><score-part id="P1"><part-name></part-name></score-part></part-list><part id="P1">']
for i,(r,l,dur) in enumerate(bars):
    m = f'<measure number="{i}"'
    if i==0: m+=' implicit="yes"'
    m+='>'
    if i==0:
        m+='<attributes><divisions>4</divisions><key><fifths>0</fifths><mode>minor</mode></key><time><beats>3</beats><beat-type>8</beat-type></time><staves>2</staves><clef number="1"><sign>G</sign><line>2</line></clef><clef number="2"><sign>F</sign><line>4</line></clef></attributes><direction placement="above"><direction-type><words font-style="italic">Poco moto</words></direction-type><staff>1</staff></direction><direction placement="below"><direction-type><dynamics><pp/></dynamics></direction-type><staff>1</staff></direction>'
    m += r + f'<backup><duration>{dur}</duration></backup>' + l + '</measure>'
    xml.append(m)
xml.append('</part></score-partwise>')
open('elise.musicxml','w').write(''.join(xml))
print(len(bars),'bars')

# ---- phrase excerpt (bars 9-16) for focus mode, plus ossia (RH only, staccato) and note list for the piano roll
import json
sel = bars[9:17]
def part_xml(mlist, staves=2, stacc=False):
    out=['<?xml version="1.0" encoding="UTF-8"?><score-partwise version="4.0"><part-list><score-part id="P1"><part-name></part-name></score-part></part-list><part id="P1">']
    for i,(r,l,dur) in enumerate(mlist):
        m=f'<measure number="{9+i}">'
        if i==0:
            m+=f'<attributes><divisions>4</divisions><key><fifths>0</fifths><mode>minor</mode></key><time><beats>3</beats><beat-type>8</beat-type></time><staves>{staves}</staves><clef number="1"><sign>G</sign><line>2</line></clef>'
            if staves==2: m+='<clef number="2"><sign>F</sign><line>4</line></clef>'
            m+='</attributes>'
        rr = r.replace('</note>','<notations><articulations><staccato/></articulations></notations></note>') if stacc else r
        rr = rr.replace('<notations><articulations><staccato/></articulations></notations></note>','</note>') if False else rr
        m+=rr
        if staves==2: m+=f'<backup><duration>{dur}</duration></backup>'+l
        m+='</measure>'; out.append(m)
    out.append('</part></score-partwise>')
    return ''.join(out)
open('phrase.musicxml','w').write(part_xml(sel,2))
# ossia: staccato only on real notes (rests have no notations issue since replace applies to rests too -> strip for rests)
oss = part_xml(sel,1,True).replace('<rest/>','<rest/>')
import re
oss = re.sub(r'(<note><rest/>.*?)<notations><articulations><staccato/></articulations></notations>(</note>)', r'\1\2', oss)
open('ossia.musicxml','w').write(oss)
# note list: parse our own sequences
NOTE_RE = re.compile(r'<note>(<chord/>)?<pitch><step>(\w)</step>(?:<alter>(-?\d)</alter>)?<octave>(\d)</octave></pitch><duration>(\d)</duration><voice>(\d)</voice>')
REST_RE = re.compile(r'<note><rest/><duration>(\d)</duration><voice>(\d)</voice>')
semis={'C':0,'D':2,'E':4,'F':5,'G':7,'A':9,'B':11}
notes=[]
for bi,(r,l,dur) in enumerate(sel):
    for staff,src in ((1,r),(2,l)):
        t=0
        for m in re.finditer(r'<note>.*?</note>', src):
            s=m.group(0)
            mm=NOTE_RE.match(s)
            if mm:
                _,st,al,oc,d,_=mm.groups()
                midi=12*(int(oc)+1)+semis[st]+(int(al) if al else 0)
                notes.append({'bar':9+bi,'start':bi*6+t,'dur':int(d),'midi':midi,'hand':'R' if staff==1 else 'L'})
                t+=int(d)
            else:
                rm=REST_RE.match(s); t+=int(rm.group(1))
json.dump(notes, open('phrase_notes.json','w'))
print('phrase notes', len(notes))
