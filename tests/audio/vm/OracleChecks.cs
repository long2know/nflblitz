using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Blitz.Gameplay;
using Blitz.Presentation;

internal static partial class Program
{
    private static void CompareActualC(string casesPath, string oraclePath)
    {
        var stream = new InputStream();
        GameSimulation game = Create(stream);
        using (var cases = File.ReadLines(casesPath).GetEnumerator())
        foreach (string line in File.ReadLines(oraclePath))
        {
            Check(cases.MoveNext(), "native sound fixture cardinality");
            int[] values = cases.Current.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
            using (JsonDocument document = JsonDocument.Parse(line))
            {
                JsonElement expected = document.RootElement;
                int actorId = values[4], partnerId = (actorId + 1) % 14;
                ActorState actor = game.Model.Players[actorId];
                ActorInputs(game, actor, values[2], values[3], values[5],
                    values[6] == 0 ? -1 : partnerId, values[7], values[8], values[9], values[10]);
                var binding = functions.VmBindings[values[15]];
                Equal(values[11], binding.Argument0, "actual native tuple argument0");
                Equal(values[12], binding.Argument1, "actual native tuple argument1");
                Equal(values[13], binding.Argument2, "actual native tuple argument2");
                SourceSoundVmDispatch actual = ExecuteProbe(game, stream, binding, actor, 3,
                    values.Skip(16).ToArray());
                string label = "actual VM/original C case=" + values[0];
                Equal(expected.GetProperty("rawConsumed").GetInt32(), actual.Result.RandomCalls, label + " draw count");
                Equal(expected.GetProperty("calls").GetArrayLength(), actual.Result.Calls.Count, label + " call count");
                Equal(values[2], actual.StateAtCall.ActorFlags.Value, label + " actual raw flag word");
                Equal(values[3], actual.StateAtCall.ActorRawPlayerMode.Value, label + " original mode, not ActorRole");
                Equal(actorId, actual.StateAtCall.ActorPlayerNumber.Value, label + " actual player number");
                Equal(values[5], actual.StateAtCall.ActorStation.Value, label + " actual station int");
                Equal(values[9], actual.StateAtCall.CurrentBallCarrier.Value, label + " current carrier");
                Equal(values[10], actual.StateAtCall.CurrentIntendedReceiver.Value, label + " current int_receiver");
                Equal("players", actual.StateAtCall.CurrentPlayerBank, label + " actual current bank");
                Equal(binding.SourceLine, actual.OriginalCueContext.SourceLine, label + " original ANI_CODE site");
                Equal(binding.ScriptSymbol, actual.OriginalCueContext.ExecutingSymbol, label + " actual VM script");
                Equal(actorId, actual.RandomContext.ActorId, label + " actual process receiver");
                Equal("player_proc", actual.RandomContext.Process, label + " actual RNG process");
                for (int index = 0; index < actual.Result.RandomDraws.Count; index++)
                {
                    SourceRandomDraw draw = actual.Result.RandomDraws[index];
                    JsonElement reference = expected.GetProperty("random")[index];
                    Equal(reference.GetProperty("raw").GetInt32(), draw.Raw.Value, label + " raw draw");
                    Equal(reference.GetProperty("bound").GetInt32(), draw.Site.BoundOrModulus, label + " original bound");
                    Equal(reference.GetProperty("value").GetInt32(), draw.Value, label + " original selected value");
                    Equal(3 + index, draw.Site.CallIndex, label + " source offset");
                    Equal(actual.RandomContext.ProcessOrder, draw.Site.ProcessOrder, label + " actual RNG dispatch order");
                    Check(draw.Site.SourceIdentity.Contains("sounds.c:" + reference.GetProperty("line").GetInt32() + ":"),
                        label + " callee source line identity");
                }
                for (int index = 0; index < actual.Result.Calls.Count; index++)
                {
                    SourceAudioResolvedOriginalSoundCall call = actual.Result.Calls[index];
                    JsonElement reference = expected.GetProperty("calls")[index];
                    Equal(reference.GetProperty("id").GetInt32(), call.PlaylistId, label + " raw original playlist ID");
                    Equal(reference.GetProperty("line").GetInt32(), call.CalleeSourceSite.SourceLine, label + " actual callee line");
                    Equal(reference.GetProperty("randomCalls").GetInt32(), call.RandomCalls, label + " cumulative emission draws");
                    Equal(reference.GetProperty("volume").GetUInt32(), call.Volume, label + " full volume");
                    Equal(reference.GetProperty("pan").GetUInt32(), call.Pan, label + " full pan");
                    Equal(reference.GetProperty("priority").GetUInt32(), call.PriorityWord, label + " raw priority word");
                    Equal(index, call.SubcallOrder, label + " original subcall order");
                    Equal("players", call.Bank, label + " raw current bank");
                    Check(ReferenceEquals(actual.OriginalCue, call.OriginalCue), label + " original cue paired immutably");
                    Equal(functions.SoundsGitBlob, call.CalleeSourceSite.SourceGitBlob, label + " exact function source pin");
                }
                nativeCases++;
            }
        }
        Equal(15912, nativeCases, "all original six-site C branch/input cases exercised through actual Core VM");
    }
}
