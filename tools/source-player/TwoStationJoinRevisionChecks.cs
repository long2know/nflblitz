using System.Text.Json;
using Blitz.Contracts;
using Blitz.Presentation;
using Blitz.Runtime;
using Blitz.SourcePlayer;

static partial class Checks
{
    private sealed class JoinRevisionSink : RecordingSink, IRenderSink, ISourceAudioSink
    {
        private readonly CompleteAudioSink audio = new();
        internal int Presents;
        void IRenderSink.Present(double[] camera, double[] joints, int[] models, int[] appearances, double[] ball,
            double[] hudVertices, int[] hudColors, string[] hudTextures, int[] hudCounts, string status)
        {
            Presents++;
            Present(camera, joints, models, appearances, ball, hudVertices, hudColors, hudTextures, hudCounts, status);
        }
        public void AudioSample(string id, int rate, int channels, byte[] pcm, string metadata) =>
            audio.AudioSample(id, rate, channels, pcm, metadata);
        public void AudioCommands(string text) => audio.AudioCommands(text);
    }

    private sealed class JoinRevisionStorage : ISourcePlayerRecordStorage
    {
        internal int Writes;
        public Task<SourceRecordStorageImage> ReadAsync() =>
            Task.FromResult(new SourceRecordStorageImage(null));
        public Task<bool> CompareExchangeAsync(string expectedPrimary, SourceRecordStorageImage replacement)
        {
            Writes++;
            return Task.FromResult(false);
        }
    }

    private static void VerifyTwoStationJoinRevision(PlayerData data)
    {
        Check(data.Configuration.Match.StationLayout == SourceStationLayout.TwoStation,
            "The join revision requires an actually admitted TwoStation input configuration, not a coerced layout.");
        var outcomes = new List<object>();
        foreach (int missing in new[] { 1, 0 })
        {
            var storage = new JoinRevisionStorage();
            var store = new SourcePlayerRecordStore(storage, data.Catalog.Profile.IsCompleteAv306TwoStationRecords ?
                SourceRecordSavePolicy.CustomBlockHistoryV3 : SourceRecordSavePolicy.LegacyV2);
            Check(store.LoadAsync().GetAwaiter().GetResult().Status == SourceRecordStoreStatus.NotFound,
                "The join regression opens an actually absent local store without initializing or writing it.");
            var sink = new JoinRevisionSink();
            var host = new PlayerHost(data, sink, observeRendering: true,
                options: PreviewOptions.FromUrl("", data.Catalog.Profile.StableId), recordStore: store);
            int presentBefore = sink.Presents;
            void Frame(int station = -1, int buttons = 0)
            {
                var pressed = new int[4];
                if (station >= 0) pressed[station] = buttons;
                host.Capture(new double[8], pressed);
                host.Advance(1.0 / 60.0, 512, 384, 512, 384, 1);
            }
            void Pulse(int station, int buttons)
            {
                Frame(); Frame();
                Frame(station, buttons); Frame(station, buttons);
                Frame(); Frame();
            }
            bool completed = false;
            try
            {
                Frame(); Frame(); Frame();
                Check(host.Session.Stage == SourceCabinetMenuStage.Joining &&
                    host.Session.ParticipatingStationMask == 3 && !host.Session.Records.Active,
                    "The real public host starts with both original physical stations at the join screen.");
                Pulse(missing, 2);
                int remaining = 1 - missing, remainingMask = 1 << remaining;
                Check(host.Session.Stage == SourceCabinetMenuStage.Joining &&
                    host.Session.ParticipatingStationMask == remainingMask,
                    "An actual physical B edge must remove only the station that left.");
                bool rejectedConfiguration = false;
                try { host.Session.CreateConfiguration(); }
                catch (InvalidDataException) { rejectedConfiguration = true; }
                Check(rejectedConfiguration,
                    "Strict direct configuration admission must still reject a one-human original TwoStation match.");
                long tick = host.PreGameSounds.SourceTick;
                Pulse(remaining, 4);
                Check(host.Session.Stage == SourceCabinetMenuStage.Joining &&
                    host.Session.ParticipatingStationMask == remainingMask &&
                    !host.Session.Playing && !host.Session.HasMatchRequest && !host.Session.Records.Active &&
                    host.PreGameSounds.SourceTick > tick && sink.Presents > presentBefore,
                    "Public C with a missing TwoStation human must remain live at Joining without entering record choices or throwing.");
                Check(storage.Writes == 0 && store.Generation == 0 && store.Current == null,
                    "Blocked join continuation must not fabricate a participant, record, or persistence transaction.");
                Pulse(missing, 1);
                Check(host.Session.Stage == SourceCabinetMenuStage.Joining &&
                    host.Session.ParticipatingStationMask == 3,
                    "The missing station must be able to rejoin through actual public A input without reloading the host.");
                Pulse(remaining, 4);
                Check(host.Session.Stage == SourceCabinetMenuStage.Records &&
                    host.Session.Records.Active && host.Session.Records.View().Kind == SourceRecordScreenKind.Options &&
                    host.Session.ParticipatingStationMask == 3 && !host.Session.Playing && !host.Session.HasMatchRequest,
                    "After public rejoin, C must reach the genuine record-options screen with both physical stations intact.");
                Check(storage.Writes == 0 && store.Generation == 0 && store.Current == null,
                    "Join and record-option recovery require no saved identities or local-store writes.");
                outcomes.Add(new { missingStation = missing, remainingMask, recoveredMask = 3,
                    beforeBlockedSourceTick = tick, afterRecoverySourceTick = host.PreGameSounds.SourceTick,
                    stage = host.Session.Stage.ToString(), recordScreen = host.Session.Records.View().Kind.ToString(),
                    writes = storage.Writes, store.Generation });
                completed = true;
            }
            finally { if (completed) host.SetActive(false); }
        }

        var session = new SourceCabinetSession(data);
        var quantizer = new SourceCabinetQuantizer(data.Hud.Input);
        session.Capture(new double[8], new int[4], quantizer);
        session.Capture(new double[8], new[] { 0, 2, 0, 0 }, quantizer);
        session.Capture(new double[8], new int[4], quantizer);
        session.Continue(0);
        Check(session.Stage == SourceCabinetMenuStage.Joining && session.ParticipatingStationMask == 1,
            "The public non-source Continue caller must also block a partial TwoStation admission without throwing.");
        session.Continue(1);
        session.Capture(new double[8], new int[4], quantizer);
        session.Continue(0);
        Check(session.Stage == SourceCabinetMenuStage.Teams && session.ParticipatingStationMask == 3,
            "The non-source Continue caller must recover after real rejoin without weakening strict configuration admission.");
        Console.WriteLine(JsonSerializer.Serialize(new { twoStationJoinRevision = "passed",
            sourceProfile = data.Catalog.Profile.StableId, manifestSha256 = data.Catalog.ManifestSha256,
            publicCaptureAdvanceCases = outcomes, directContinueCase = "passed", assertions }));
    }
}
