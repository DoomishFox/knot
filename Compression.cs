using System.IO.Compression;

namespace knot;


static class Gz {
    public static async Task<Stream> ExtractGzAsync(this Stream archive) {
        using var gzip = new GZipStream(archive, CompressionMode.Decompress);
        // copy to a memory stream to return
        // something something a gzipstream isnt seekable
        var ms = new MemoryStream();
        await gzip.CopyToAsync(ms);
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }
}