# three motif excerpts (single staff) for Piece DNA cards
def xml(notes, divisions=4, beats=3, bt=8):
    out=['<?xml version="1.0" encoding="UTF-8"?><score-partwise version="4.0"><part-list><score-part id="P1"><part-name></part-name></score-part></part-list><part id="P1"><measure number="1"><attributes><divisions>4</divisions><key><fifths>0</fifths><mode>minor</mode></key><time><beats>%d</beats><beat-type>%d</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>'%(beats,bt)]
    t=0; m=1; per=beats*(16//bt)
    for p,d in notes:
        if t>=per: out.append('</measure><measure number="%d">'%(m+1)); m+=1; t=0
        typ={1:'16th',2:'eighth',3:'eighth',4:'quarter'}[d]
        if p=='r': out.append('<note><rest/><duration>%d</duration><type>%s</type></note>'%(d,typ))
        else:
            st=p[0]; al='<alter>1</alter>' if '#' in p else ''; oc=p[-1]
            out.append('<note><pitch><step>%s</step>%s<octave>%s</octave></pitch><duration>%d</duration><type>%s</type>%s%s</note>'%(st,al,oc,d,typ,'<dot/>' if d==3 else '','<accidental>sharp</accidental>' if al else ''))
        t+=d
    out.append('</measure></part></score-partwise>'); return ''.join(out)
open('motif_a.musicxml','w').write(xml([('E5',1),('D#5',1),('E5',1),('D#5',1),('E5',1),('B4',1),('D5',1),('C5',1),('A4',2),('r',1)]))
open('motif_b.musicxml','w').write(xml([('A4',2),('r',1),('C4',1),('E4',1),('A4',1),('B4',2),('r',1),('E4',1),('G#4',1),('B4',1)]))
open('motif_c.musicxml','w').write(xml([('E5',3),('G5',1),('F5',1),('E5',1),('D5',3),('F5',1),('E5',1),('D5',1)]))
print('ok')
