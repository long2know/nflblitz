using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Blitz.SourcePlayer;

[SupportedOSPlatform("browser")]
public static partial class Browser
{
    private static PlayerHost? host;
    private static PlayerHost Host => host ?? throw new InvalidOperationException("The actual source C# kernel has not loaded.");
    public static void Main() { }

    [JSExport]
    public static void Start(byte[] manifest, string url)
    {
        if (host != null) throw new InvalidOperationException("The source player is already initialized.");
        var catalog = new DataCatalog(manifest, ReadFile);
        var options = PreviewOptions.FromUrl(url, catalog.Profile.StableId);
        var data = new PlayerData(catalog, Progress);
        var loaded = new PlayerHost(data, new WebGlSink(), Progress, options);
        catalog.RequireAllInputsConsumed();
        host = loaded;
    }

    [JSExport]
    public static int Frame(double elapsedSeconds, int width, int height, double cssWidth, double cssHeight, double dpr) =>
        Host.Advance(elapsedSeconds, width, height, cssWidth, cssHeight, dpr);

    [JSExport]
    public static void Capture(double[] axes, int[] buttons) => Host.Capture(axes, buttons);

    [JSExport]
    public static void Continue(int station) => Host.Continue(station);

    [JSExport]
    public static void SetActive(bool value) => Host.SetActive(value);

    [JSExport]
    public static void Restart() => Host.Restart();

    [JSExport]
    public static string Status() => Host.LastStatus;

    [JSImport("readFile", "source-player")]
    private static partial byte[] ReadFile(string path);
    [JSImport("progress", "source-player")]
    private static partial void Progress(string message);
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

    private sealed class WebGlSink : IRenderSink
    {
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
            double[] flashAlpha, string metadata) =>
            Browser.Effects(projection, vertices, textures, rawFlags, flashAlpha, metadata);
        public void Present(double[] camera, double[] joints, int[] models, int[] appearances, double[] ball,
            double[] hudVertices, int[] hudColors, string[] hudTextures, int[] hudCounts, string status) =>
            Browser.Present(camera, joints, models, appearances, ball, hudVertices, hudColors, hudTextures, hudCounts, status);
    }
}
