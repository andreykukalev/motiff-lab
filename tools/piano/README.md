# Playback piano

The piano that Motiff Lab plays notes with (S1-W4) lives in `src/MotiffLab.Web/wwwroot/audio/piano`: 30 MP3 samples (about 1.7 MB) and `piano.json`, which lists each sample's MIDI note and tuning correction. `make_piano.py` rebuilds them from the source download.

## Source and licence

| | |
| --- | --- |
| Instrument | Salamander Grand Piano V3, a Yamaha C5 |
| Author | Alexander Holm |
| Retuning | Markus Fiedler (the `tune` values in `SalamanderGrandPianoV3Retuned.sfz`) |
| Source | <https://archive.org/details/SalamanderGrandPianoV3>, file `SalamanderGrandPianoV3_OggVorbis.tar.bz2` |
| Licence | Creative Commons Attribution 3.0 Unported (CC BY 3.0), <https://creativecommons.org/licenses/by/3.0/> |

Checked on 2026-10-22: the archive.org item's metadata gives `licenseurl` `http://creativecommons.org/licenses/by/3.0/`, and its description, written by the author, says "Licence: CC-by http://creativecommons.org/licenses/by/3.0/". CC BY 3.0 allows copying, redistribution and adaptation, including commercial use, as long as the author is credited, the licence is named and linked, and changes are indicated. It has no share-alike or non-commercial terms, so it fits a hosted PWA and later paid tiers.

How Motiff Lab meets the attribution terms:

- **Credit in the app.** The playback screen shows "Piano: Salamander Grand Piano V3 by Alexander Holm, CC BY 3.0", linked to the source and the licence. Keep it wherever the piano is used.
- **Credit next to the files.** `piano.json` carries the author, licence, source and a summary of changes, and is published with the samples.
- **Changes indicated.** We use one velocity layer (v10 of 16, MIDI velocities 73–80 in the original mapping) of the 30 notes sampled in minor thirds from A0 to C8, mix each to mono, shorten it (6 s below C3, 5 s below C5, 3.5 s above) with a 1 s fade-out, and re-encode it as 96 kbit/s MP3. Release, pedal and string-resonance samples are not used. The playback engine, not the samples, shapes loudness by velocity and the release when a key is let go.

The samples are a recording of an instrument, not of any published music, so they raise no copyright question for the music players import.

## Rebuilding

```bash
curl -LO https://archive.org/download/SalamanderGrandPianoV3/SalamanderGrandPianoV3_OggVorbis.tar.bz2
tar xjf SalamanderGrandPianoV3_OggVorbis.tar.bz2
python3 tools/piano/make_piano.py SalamanderGrandPianoV3_OggVorbis
```

It needs `ffmpeg` with `libmp3lame`. The output is deterministic for a given ffmpeg version. Encoders add a little silence before the attack; the browser layer trims each decoded sample to its attack, so this does not delay notes.
