using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Blitz.SourcePlayer;

[SupportedOSPlatform("browser")]
public sealed partial class BrowserRecordStorage : ISourcePlayerRecordStorage, IDisposable
{
    private int handle;

    public BrowserRecordStorage()
    {
        try { handle = CreateRecordStorage(); }
        catch (Exception error) when (Recoverable(error)) { throw new SourceRecordStorageException(); }
    }

    public async Task<SourceRecordStorageImage> ReadAsync()
    {
        try
        {
            RequireOpen();
            SourceRecordStorageImage image = SourceRecordStorageImage.DecodeBrowserJson(await ReadRecords(handle));
            RequireWireImage(image);
            return image;
        }
        catch (Exception error) when (Recoverable(error)) { throw new SourceRecordStorageException(); }
    }

    public async Task<bool> CompareExchangeAsync(string? expectedPrimary, SourceRecordStorageImage replacement)
    {
        try
        {
            RequireOpen();
            ArgumentNullException.ThrowIfNull(replacement);
            RequireWireString(expectedPrimary);
            RequireWireImage(replacement);
            return await CompareExchangeRecords(handle, expectedPrimary, replacement.EncodeBrowserJson());
        }
        catch (Exception error) when (Recoverable(error)) { throw new SourceRecordStorageException(); }
    }

    public void Dispose()
    {
        if (handle == 0) return;
        int previous = handle;
        handle = 0;
        try { CloseRecordStorage(previous); }
        catch (Exception error) when (Recoverable(error)) { throw new SourceRecordStorageException(); }
    }

    private void RequireOpen()
    {
        if (handle == 0) throw new SourceRecordStorageException();
    }

    private static bool Recoverable(Exception error)
        => error is not OutOfMemoryException and not AccessViolationException;

    private static void RequireWireImage(SourceRecordStorageImage image)
    {
        RequireWireString(image.Primary);
        RequireWireString(image.Backup);
        RequireWireString(image.Recovery);
    }

    private static void RequireWireString(string? value)
    {
        if (value == null) return;
        // System.Text.Json's encoder replaces unpaired surrogates; fail before that lossy write.
        for (int i = 0; i < value.Length; i++)
        {
            if (!char.IsSurrogate(value[i])) continue;
            if (!char.IsHighSurrogate(value[i]) || i + 1 == value.Length || !char.IsLowSurrogate(value[++i]))
                throw new SourceRecordStorageException();
        }
    }

    [JSImport("createRecordStorage", "source-player-records")]
    private static partial int CreateRecordStorage();

    [JSImport("readRecords", "source-player-records")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> ReadRecords(int storageHandle);

    [JSImport("compareExchangeRecords", "source-player-records")]
    [return: JSMarshalAs<JSType.Promise<JSType.Boolean>>]
    private static partial Task<bool> CompareExchangeRecords(int storageHandle, string? expectedPrimary, string replacementJson);

    [JSImport("closeRecordStorage", "source-player-records")]
    private static partial void CloseRecordStorage(int storageHandle);
}
