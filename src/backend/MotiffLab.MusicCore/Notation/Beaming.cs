using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>The beams and tuplets of a layer: eighths and shorter beamed within the beat, each triplet beat's elements in a tuplet.</summary>
internal static class Beaming
{
    /// <summary>The span eighths and shorter are beamed within.</summary>
    public static long BeamGroup(TimeSignature signature)
    {
        if (BarValues.IsCompound(signature))
        {
            return 3 * signature.BeatTicks;
        }

        if (signature.BeatTicks >= CanonicalTime.TicksPerQuarter)
        {
            return signature.BeatTicks;
        }

        // 2/8 and 3/8 beam the whole bar; 5/8 and 7/8 by the quarter.
        return signature.Numerator <= 3 ? signature.BarTicks : CanonicalTime.TicksPerQuarter;
    }

    /// <summary>The elements of a layer with each triplet beat's in a tuplet, and eighths and shorter beamed.</summary>
    public static List<Element> Group(List<Element> elements, long origin, long beamGroup)
    {
        var result = new List<Element>();
        for (int i = 0; i < elements.Count;)
        {
            TupletSpan? tuplet = elements[i].Tuplet;
            int next = i;
            while (next < elements.Count && elements[next].Tuplet == tuplet)
            {
                next++;
            }

            List<Element> run = elements[i..next];
            if (tuplet is { } span)
            {
                result.Add(new TupletGroup([.. Beam(run, span.Start, span.Length)], span));
            }
            else
            {
                result.AddRange(Beam(run, origin, beamGroup));
            }

            i = next;
        }

        return result;
    }

    private static List<Element> Beam(List<Element> elements, long origin, long beamGroup)
    {
        var result = new List<Element>();
        var group = new List<Element>();
        long groupIndex = -1;

        void Flush()
        {
            if (group.Count >= 2)
            {
                result.Add(new Beamed([.. group]));
            }
            else
            {
                result.AddRange(group);
            }

            group.Clear();
        }

        foreach (Element element in elements)
        {
            long first = (element.Start - origin) / beamGroup;
            long lastTick = (element.Start + element.Ticks - 1 - origin) / beamGroup;
            bool beamable = element is ChordPiece { IsGrace: false } && element.Dur >= 8 && first == lastTick;
            if (!beamable || first != groupIndex)
            {
                Flush();
            }

            if (beamable)
            {
                group.Add(element);
                groupIndex = first;
            }
            else
            {
                result.Add(element);
                groupIndex = -1;
            }
        }

        Flush();
        return result;
    }
}