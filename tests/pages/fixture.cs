#:project ../../src/backend/MotiffLab.MusicCore/MotiffLab.MusicCore.csproj
#:property PublishAot=false

// What the iPad app gives its two pages for a piece, before Verovio engraves it: music-core's notation (MEI), the beat
// lines of its playback score and the roll's payload, asked as the app asks for them (Engraver.engrave; Player.load,
// whose beats ScoreScreen sends the score page; RollController.load).
//   dotnet run tests/pages/fixture.cs -- <file.musicxml> <title>
// prints {"mei": …, "beats": […], "roll": …} as JSON. fixture.mjs engraves the MEI with the app's A4 options and writes
// the fixtures the page scenarios open (tests/contracts/page-scenarios/README.md).
using System.Text.Json;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Facade;

string path = args[0];
string title = args[1];
var facade = new MusicCoreFacade();
string opened = facade.Open(Path.GetFileName(path), File.ReadAllBytes(path), """{"language": "en", "edits": "piece"}""");
int piece = JsonNode.Parse(opened)?["piece"]?.GetValue<int>() ?? throw new InvalidOperationException(opened);
JsonNode notation = JsonNode.Parse(facade.Call(piece, "notation", JsonSerializer.Serialize(new { title })))!;
JsonNode playback = JsonNode.Parse(facade.Call(piece, "playback"))!;
JsonNode roll = JsonNode.Parse(facade.Call(piece, "roll"))!;
var beats = new JsonArray([.. playback["beats"]!.AsArray().Select(beat => JsonValue.Create(beat!["at"]!.GetValue<double>()))]);
Console.WriteLine(new JsonObject { ["mei"] = notation["mei"]?.GetValue<string>(), ["beats"] = beats, ["roll"] = roll }.ToJsonString());