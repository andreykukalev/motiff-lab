using System.Globalization;
using System.Text;

namespace MotiffLab.MusicCore.Import;

/// <summary>
/// Names as the library compares them (version 0.5, «Импорт папки»): a file or folder name in one Unicode form, so "Für
/// Elise" from a Mac (u and a combining diaeresis) and from Windows (ü) are the same name, and folded for matching. The
/// composition is music-core's own, the same on every runtime: the iPad's native build runs without ICU, where
/// <see cref="string.Normalize()"/> leaves text as it is. It covers the Latin and Cyrillic letters with one or more marks
/// (Latin-1, Latin Extended-A and -B, Latin Extended Additional, Cyrillic), the letters names of pieces and composers use.
/// </summary>
public static class NameText
{
    /// <summary>For each combining mark, the pairs of a letter and that letter with the mark, as Unicode composes them.</summary>
    private static readonly (char Mark, string Pairs)[] s_compositions =
    [
        ('\u0300', "AÀEÈIÌOÒUÙaàeèiìoòuùÜǛüǜNǸnǹЕЀИЍеѐиѝĒḔēḕŌṐōṑWẀwẁÂẦâầĂẰăằÊỀêềÔỒôồƠỜơờƯỪưừYỲyỳ"),
        ('\u0301', "AÁEÉIÍOÓUÚYÝaáeéiíoóuúyýCĆcćLĹlĺNŃnńRŔrŕSŚsśZŹzźÜǗüǘGǴgǵÅǺåǻÆǼæǽØǾøǿГЃКЌгѓкќÇḈçḉĒḖēḗÏḮïḯKḰkḱMḾmḿÕṌõṍŌṒōṓPṔpṕŨṸũṹWẂwẃÂẤâấĂẮăắÊẾêếÔỐôốƠỚơớƯỨưứ"),
        ('\u0302', "AÂEÊIÎOÔUÛaâeêiîoôuûCĈcĉGĜgĝHĤhĥJĴjĵSŜsŝWŴwŵYŶyŷZẐzẑẠẬạậẸỆẹệỌỘọộ"),
        ('\u0303', "AÃNÑOÕaãnñoõIĨiĩUŨuũVṼvṽÂẪâẫĂẴăẵEẼeẽÊỄêễÔỖôỗƠỠơỡƯỮưữYỸyỹ"),
        ('\u0304', "AĀaāEĒeēIĪiīOŌoōUŪuūÜǕüǖÄǞäǟȦǠȧǡÆǢæǣǪǬǫǭÖȪöȫÕȬõȭȮȰȯȱYȲyȳИӢиӣУӮуӯGḠgḡḶḸḷḹṚṜṛṝ"),
        ('\u0306', "AĂaăEĔeĕGĞgğIĬiĭOŎoŏUŬuŭУЎИЙийуўЖӁжӂАӐаӑЕӖеӗȨḜȩḝẠẶạặ"),
        ('\u0307', "CĊcċEĖeėGĠgġIİZŻzżAȦaȧOȮoȯBḂbḃDḊdḋFḞfḟHḢhḣMṀmṁNṄnṅPṖpṗRṘrṙSṠsṡŚṤśṥŠṦšṧṢṨṣṩTṪtṫWẆwẇXẊxẋYẎyẏſẛ"),
        ('\u0308', "AÄEËIÏOÖUÜaäeëiïoöuüyÿYŸЕЁІЇеёіїАӒаӓӘӚәӛЖӜжӝЗӞзӟИӤиӥОӦоӧӨӪөӫЭӬэӭУӰуӱЧӴчӵЫӸыӹHḦhḧÕṎõṏŪṺūṻWẄwẅXẌxẍtẗ"),
        ('\u0309', "AẢaảÂẨâẩĂẲăẳEẺeẻÊỂêểIỈiỉOỎoỏÔỔôổƠỞơởUỦuủƯỬưửYỶyỷ"),
        ('\u030A', "AÅaåUŮuůwẘyẙ"),
        ('\u030B', "OŐoőUŰuűУӲуӳ"),
        ('\u030C', "CČcčDĎdďEĚeěLĽlľNŇnňRŘrřSŠsšTŤtťZŽzžAǍaǎIǏiǐOǑoǒUǓuǔÜǙüǚGǦgǧKǨkǩƷǮʒǯjǰHȞhȟ"),
        ('\u030F', "AȀaȁEȄeȅIȈiȉOȌoȍRȐrȑUȔuȕѴѶѵѷ"),
        ('\u0311', "AȂaȃEȆeȇIȊiȋOȎoȏRȒrȓUȖuȗ"),
        ('\u031B', "OƠoơUƯuư"),
        ('\u0323', "BḄbḅDḌdḍHḤhḥKḲkḳLḶlḷMṂmṃNṆnṇRṚrṛSṢsṣTṬtṭVṾvṿWẈwẉZẒzẓAẠaạEẸeẹIỊiịOỌoọƠỢơợUỤuụƯỰưựYỴyỵ"),
        ('\u0324', "UṲuṳ"),
        ('\u0325', "AḀaḁ"),
        ('\u0326', "SȘsșTȚtț"),
        ('\u0327', "CÇcçGĢgģKĶkķLĻlļNŅnņRŖrŗSŞsşTŢtţEȨeȩDḐdḑHḨhḩ"),
        ('\u0328', "AĄaąEĘeęIĮiįUŲuųOǪoǫ"),
        ('\u032D', "DḒdḓEḘeḙLḼlḽNṊnṋTṰtṱUṶuṷ"),
        ('\u032E', "HḪhḫ"),
        ('\u0330', "EḚeḛIḬiḭUṴuṵ"),
        ('\u0331', "BḆbḇDḎdḏKḴkḵLḺlḻNṈnṉRṞrṟTṮtṯZẔzẕhẖ"),
    ];

    private static readonly Dictionary<(char Letter, char Mark), char> s_composed = Composed();

    private static readonly Dictionary<char, char> s_base = s_composed.ToDictionary(static pair => pair.Value, static pair => pair.Key.Letter);

    /// <summary>
    /// The text in Unicode's composed form (NFC) for the letters this class knows: a letter followed by a combining mark
    /// becomes the one letter that has it. A mark with no composed letter stays as it is.
    /// </summary>
    public static string Composed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Any(static letter => letter is >= '̀' and <= 'ͯ'))
        {
            return text;
        }

        var composed = new StringBuilder(text.Length);
        foreach (char letter in text)
        {
            if (composed.Length > 0 && s_composed.TryGetValue((composed[^1], letter), out char with))
            {
                composed[^1] = with;
            }
            else
            {
                composed.Append(letter);
            }
        }

        return composed.ToString();
    }

    /// <summary>
    /// The text to match names by: composed, in lower case, without the marks over letters ("Für Élise" is "fur elise",
    /// "Ёлка" is "елка"), any run of characters that are not letters or digits one space, trimmed.
    /// </summary>
    public static string Folded(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var folded = new StringBuilder(text.Length);
        bool gap = false;
        foreach (char letter in Composed(text))
        {
            if (char.IsLetterOrDigit(letter))
            {
                if (gap && folded.Length > 0)
                {
                    folded.Append(' ');
                }

                folded.Append(char.ToLowerInvariant(Bare(letter)));
                gap = false;
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(letter) != UnicodeCategory.NonSpacingMark)
            {
                gap = true;
            }
        }

        return folded.ToString();
    }

    /// <summary>The letter without its marks: "ệ" is "e".</summary>
    private static char Bare(char letter)
    {
        while (s_base.TryGetValue(letter, out char bare))
        {
            letter = bare;
        }

        return letter;
    }

    private static Dictionary<(char, char), char> Composed()
    {
        var composed = new Dictionary<(char, char), char>();
        foreach ((char mark, string pairs) in s_compositions)
        {
            for (int index = 0; index + 1 < pairs.Length; index += 2)
            {
                composed[(pairs[index], mark)] = pairs[index + 1];
            }
        }

        return composed;
    }
}