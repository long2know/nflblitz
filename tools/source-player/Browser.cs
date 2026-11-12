using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Blitz.SourcePlayer;

[SupportedOSPlatform("browser")]
public static partial class Browser
{
    private static PlayerHost? host;
    private static BrowserRecordStorage? recordStorage;
    private static SourcePlayerRecordStore? recordStore;
    private static PlayerHost Host => host ?? throw new InvalidOperationException("The actual source C# kernel has not loaded.");
    public static void Main() { }

    [JSExport]
    public static void Start(byte[] manifest, string url) => StartCore(manifest, url, null);

    [JSExport]
    public static async Task StartCompleteOriginal(byte[] manifest, string url)
    {
        if (!AudioReady())
            throw new InvalidOperationException("The explicit complete-original game requires a real public gesture and resumed AudioContext.");
        var catalog = new DataCatalog(manifest, ReadFile);
        var options = PreviewOptions.FromUrl(url, catalog.Profile.StableId);
        if (options.RecordsEnabled)
        {
            recordStorage = new BrowserRecordStorage();
            recordStore = new SourcePlayerRecordStore(recordStorage,
                catalog.Profile.IsCompleteAv306TwoStationRecords
                    ? SourceRecordSavePolicy.CustomBlockHistoryV3 : SourceRecordSavePolicy.LegacyV2);
            await recordStore.LoadAsync();
        }
        try { StartCore(manifest, url, null, completeOriginal: true, loadedRecords: recordStore); }
        catch
        {
            recordStorage?.Dispose(); recordStorage = null; recordStore = null;
            throw;
        }
    }

    // Deliberately typed, not a URL option or a JS-exported bank/admission factory.
    public static void StartDecodedAudio(byte[] manifest, string url, SourceAudioPlaybackAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        if (!AudioReady())
            throw new InvalidOperationException("Decoded source audio requires a completed public user-gesture Web Audio unlock before composition.");
        StartCore(manifest, url, admission);
    }

    private static void StartCore(byte[] manifest, string url, SourceAudioPlaybackAdmission? admission,
        bool completeOriginal = false, SourcePlayerRecordStore? loadedRecords = null)
    {
        if (host != null) throw new InvalidOperationException("The source player is already initialized.");
        var catalog = new DataCatalog(manifest, ReadFile);
        DataCatalog.Require(catalog.Profile.UsesCompleteSourcePrograms == completeOriginal,
            "The complete-original data requires its explicit audio-enabled entry; historical startup is not an alias.");
        var options = PreviewOptions.FromUrl(url, catalog.Profile.StableId);
        var data = new PlayerData(catalog, Progress, decodedAudio: admission, cabinetOptions: options);
        var loaded = new PlayerHost(data, new WebGlSink(), Progress, options, recordStore: loadedRecords);
        catalog.RequireAllInputsConsumed();
        host = loaded;
    }

    [JSExport]
    public static int Frame(double elapsedSeconds, int width, int height, double cssWidth, double cssHeight, double dpr) =>
        Host.Advance(elapsedSeconds, width, height, cssWidth, cssHeight, dpr);

    [JSExport]
    public static int FrameObserved(double elapsedSeconds, double observedSeconds, int width, int height,
        double cssWidth, double cssHeight, double dpr)
    {
        return Host.AdvanceObserved(elapsedSeconds, observedSeconds, width, height, cssWidth, cssHeight, dpr);
    }

    [JSExport]
    public static bool HasWholeSourceStepDebt() => Host.HasWholeSourceStepDebt;

    [JSExport]
    public static string SourceStepCosts() => Host.DescribeSourceStepCosts();

    [JSExport]
    public static void Capture(double[] axes, int[] buttons) => Host.Capture(axes, buttons);

    [JSExport]
    public static void CaptureObserved(double[] axes, int[] buttons, double observedSeconds) =>
        Host.CaptureObserved(axes, buttons, observedSeconds);

    [JSExport]
    public static int[] CabinetStations() => Host.CabinetInputStations;

    [JSExport]
    public static void Continue(int station) => Host.Continue(station);

    [JSExport]
    public static void SetActive(bool value) => Host.SetActive(value);

    [JSExport]
    public static void Restart() => Host.Restart();

    [JSExport]
    public static bool CanImportRecords() => Host.Session?.Records?.CanImport == true;
    [JSExport]
    public static bool CanExportRecords() => Host.Session?.Records?.CanExport == true;
    [JSExport]
    public static void ImportRecords(string envelope)
    {
        var records = Host.Session?.Records;
        if (records?.CanImport != true) throw new InvalidOperationException("Record import requires its explicit current menu action.");
        records.StagePortableImport(envelope);
    }
    [JSExport]
    public static Task<string> ExportRecords()
    {
        var records = Host.Session?.Records;
        if (records?.CanExport != true) throw new InvalidOperationException("Record export requires its explicit current menu action.");
        return records.ExportPortableAsync();
    }
    [JSExport]
    public static void Shutdown()
    {
        host?.SetActive(false);
        recordStorage?.Dispose(); recordStorage = null; recordStore = null;
    }

    [JSExport]
    public static string Status() => Host.LastStatus;

    [JSImport("readFile", "source-player")]
    private static partial byte[] ReadFile(string path);
    [JSImport("progress", "source-player")]
    private static partial void Progress(string message);
    [JSImport("audioReady", "source-player")]
    private static partial bool AudioReady();
    [JSImport("audioClockElapsed", "source-player")]
    private static partial double AudioClockElapsed(double epoch);
    [JSImport("audioSample", "source-player")]
    private static partial void AudioSample(string id, int sampleRate, int channels, byte[] pcm16Le, string metadata);
    [JSImport("audioCommands", "source-player")]
    private static partial void AudioCommands(string commands);
    [JSImport("texture", "source-player")]
    private static partial void Texture(string id, int width, int height, byte[] rgba, int mipCount,
        bool repeat, bool linear, string sha256, string origin);
    [JSImport("mesh", "source-player")]
    private static partial void Mesh(string id, double[] vertices, int[] indices);
    [JSImport("worldDraw", "source-player")]
    private static partial void WorldDraw(string mesh, string texture, string state);
    [JSImport("model", "source-player")]
    private static partial void Model(int id, string joints);
    [JSImport("appearance", "source-player")]
    private static partial void Appearance(int id, string metadata);
    [JSImport("freeBall", "source-player")]
    private static partial void FreeBall(string mesh, string texture, string alternateTexture, string metadata);
    [JSImport("effects", "source-player")]
    private static partial void Effects(double[] projection, double[] vertices, int[] textures,
        int[] rawFlags, double[] flashAlpha, string metadata);
    [JSImport("present", "source-player")]
    private static partial void Present(double[] camera, double[] joints, int[] models, int[] appearances,
        double[] ball, double[] hudVertices, int[] hudColors, string[] hudTextures, int[] hudCounts, string status);

    private sealed class WebGlSink : IRenderSink, ISourceAudioSink, ISourceWorldRenderSink, ISourceAudioClockWitness
    {
        private SourceWorldRenderPacket? pendingWorld;
        public void WorldPresentation(SourceWorldRenderPacket packet)
        {
            if (pendingWorld != null)
                throw new InvalidOperationException("SOURCE_WORLD_DELIVERY_DUPLICATE: the prior world packet has not reached Effects.");
            pendingWorld = packet ?? throw new ArgumentNullException(nameof(packet));
        }
        public void AudioSample(string id, int sampleRate, int channels, byte[] pcm16Le, string metadata) =>
            Browser.AudioSample(id, sampleRate, channels, pcm16Le, metadata);
        public void AudioCommands(string commands) => Browser.AudioCommands(commands);
        public double ReadSourceElapsed(long epoch) => Browser.AudioClockElapsed(epoch);
        public void Texture(string id, int width, int height, byte[] rgba, int mipCount, bool repeat,
            bool linear, string sha256, string origin) =>
            Browser.Texture(id, width, height, rgba, mipCount, repeat, linear, sha256, origin);
        public void Mesh(string id, double[] vertices, int[] indices) => Browser.Mesh(id, vertices, indices);
        public void WorldDraw(string mesh, string texture, string state) => Browser.WorldDraw(mesh, texture, state);
        public void Model(int id, string joints) => Browser.Model(id, joints);
        public void Appearance(int id, string metadata) => Browser.Appearance(id, metadata);
        public void FreeBall(string mesh, string texture, string alternateTexture, string metadata) =>
            Browser.FreeBall(mesh, texture, alternateTexture, metadata);
        public void Effects(double[] projection, double[] vertices, int[] textures, int[] rawFlags,
            double[] flashAlpha, string metadata)
        {
            Browser.Effects(projection, vertices, textures, rawFlags, flashAlpha,
                pendingWorld == null ? metadata : pendingWorld.AttachEffects(metadata));
            pendingWorld = null;
        }
        public void Present(double[] camera, double[] joints, int[] models, int[] appearances, double[] ball,
            double[] hudVertices, int[] hudColors, string[] hudTextures, int[] hudCounts, string status) =>
            Browser.Present(camera, joints, models, appearances, ball, hudVertices, hudColors, hudTextures, hudCounts, status);
    }
}
