using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// The skill-icon cache: download-once + local cache pipeline, offline and
/// hostile-response degradation, and the drawn-placeholder contract (a missing
/// icon is never an error, the screen must work without any network).
/// </summary>
public class SpellIconCacheTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public int Calls;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(_responder(request));
        }

        public static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body) };

        public static HttpResponseMessage Bytes(byte[] body) =>
            new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    }

    private static byte[] TinyJpeg()
    {
        using var bitmap = new Bitmap(4, 4);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Jpeg);
        return stream.ToArray();
    }

    private static string NewTempDir() =>
        Path.Combine(Path.GetTempPath(), "mdb-icons-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Cache_Downloads_Once_And_Serves_From_Disk()
    {
        var dir = NewTempDir();
        try
        {
            var jpeg = TinyJpeg();
            var handler = new FakeHandler(request =>
                request.RequestUri!.Host == "nether.wowhead.com"
                    ? FakeHandler.Json("{\"name\":\"Evasion\",\"icon\":\"ability_rogue_evasion\"}")
                    : FakeHandler.Bytes(jpeg));

            var cache = new SpellIconCache(dir, handler);
            using var ready = new ManualResetEventSlim(false);
            cache.IconReady += _ => ready.Set();

            Assert.Null(cache.TryGet(5277));            // not cached yet: placeholder + queued fetch
            Assert.True(ready.Wait(TimeSpan.FromSeconds(5)), "icon fetch did not complete");

            var image = cache.TryGet(5277);
            Assert.NotNull(image);
            Assert.True(File.Exists(Path.Combine(dir, "5277.jpg")));
            Assert.Equal(2, handler.Calls);             // tooltip + image, exactly once
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Cache_Survives_An_Offline_Network()
    {
        var dir = NewTempDir();
        try
        {
            var cache = new SpellIconCache(dir, new FakeHandler(_ => throw new HttpRequestException("offline")));
            Assert.Null(cache.TryGet(5277));
            Thread.Sleep(150);
            Assert.Null(cache.TryGet(5277));            // still the placeholder, never a throw
            Assert.False(File.Exists(Path.Combine(dir, "5277.jpg")));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Cache_Rejects_A_Hostile_Icon_Slug()
    {
        var dir = NewTempDir();
        try
        {
            var cache = new SpellIconCache(dir, new FakeHandler(_ =>
                FakeHandler.Json("{\"icon\":\"../../evil\"}")));
            Assert.Null(cache.TryGet(5277));
            Thread.Sleep(150);
            Assert.Null(cache.TryGet(5277));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(dir)!, "evil.jpg")));
            Assert.False(File.Exists(Path.Combine(dir, "5277.jpg")));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Cache_Uses_The_Official_Slug_With_A_Single_Request()
    {
        var dir = NewTempDir();
        try
        {
            var jpeg = TinyJpeg();
            var handler = new FakeHandler(request =>
                request.RequestUri!.Host == "render.worldofwarcraft.com"
                    ? FakeHandler.Bytes(jpeg)
                    : FakeHandler.Json("{}"));   // tooltip must never be needed

            var cache = new SpellIconCache(dir, handler, slugProvider: id => "ability_rogue_evasion");
            using var ready = new ManualResetEventSlim(false);
            cache.IconReady += _ => ready.Set();

            Assert.Null(cache.TryGet(5277));
            Assert.True(ready.Wait(TimeSpan.FromSeconds(5)), "icon fetch did not complete");
            Assert.NotNull(cache.TryGet(5277));
            Assert.True(File.Exists(Path.Combine(dir, "5277.jpg")));
            Assert.Equal(1, handler.Calls);   // one render-CDN request, no tooltip discovery
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Cache_Ignores_Invalid_Ids()
    {
        var cache = new SpellIconCache(NewTempDir(), new FakeHandler(_ => FakeHandler.Json("{}")));
        Assert.Null(cache.TryGet(0));
        Assert.Null(cache.TryGet(-1));
    }
}
