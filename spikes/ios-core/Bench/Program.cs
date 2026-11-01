using MotiffLab.CoreBridge;
using MotiffLab.MusicCore;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: MotiffLab.CoreBench <file.musicxml|file.mid> [iterations] [ru]");
    return 2;
}

string path = args[0];
int iterations = args.Length > 1 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 20;
Words words = args.Length > 2 && args[2] == "ru" ? Words.Russian : Words.English;
Console.WriteLine(CoreBenchmark.Run(Path.GetFileName(path), File.ReadAllBytes(path), iterations, words));
return 0;