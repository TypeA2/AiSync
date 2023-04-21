using System.Net;
using System.IO.MemoryMappedFiles;
using Ceen.Httpd;
using Ceen;
using System.Diagnostics;

namespace AiSync {
    public class AiServeSingleFile : IHttpModule, IWithShutdown {
        private readonly string mime;
        private readonly FileInfo finfo;
        private readonly MemoryMappedFile file;

        public AiServeSingleFile(string path, string mime) {
            this.mime = mime;
            finfo = new FileInfo(path);
            file = MemoryMappedFile.CreateFromFile(finfo.FullName, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        }

        public Task ShutdownAsync() {
            return Task.Run(file.Dispose);
        }

        public Task<bool> HandleAsync(IHttpContext ctx) {
            try {
                IHttpRequest request = ctx.Request;
                IHttpResponse response = ctx.Response;

                if (!ValidateRequest(request, out long start, out long end)) {
                    response.StatusCode = Ceen.HttpStatusCode.UnprocessableEntity;
                    return Task.FromResult(false); 
                }

                long length = end - start;

                response.ContentLength = length;
                response.StatusCode = Ceen.HttpStatusCode.PartialContent;
                response.ContentType = mime;

                response.AddHeader(
                    "Content-Disposition",
                    $"inline; filename={finfo.Name.ReplaceNonAscii()}; filename*=utf-8''{finfo.Name.HttpHeaderEncode()}"
                );
                response.AddHeader("Accept-Ranges", "bytes");
                response.AddHeader("Content-Range", $"bytes {start}-{end - 1}/{finfo.Length}");

                /* Read as needed */
                using MemoryMappedViewStream accessor = file.CreateViewStream(start, length, MemoryMappedFileAccess.Read);
                //await accessor.CopyToAsync(response.GetResponseStream());
                accessor.CopyTo(response.GetResponseStream());
            } catch (IOException) {
                /* Socket closed, ignore, this is okay (kind of) */
            }

            return Task.FromResult(true);
        }

        private bool ValidateRequest(IHttpRequest request, out long start, out long end) {
            start = 0;
            end = 0;
            
            if (!request.Headers.ContainsKey("Range")) {
                return false;
            }

            start = 0;

            {
                /* Must request a range of bytes */
                string? range = request.Headers["Range"];
                if (range == null || !range.StartsWith("bytes=")) {
                    return false;
                }

                /* Specific range specification */
                range = range["bytes=".Length..];

                string[] range_arr = range.Split('-');

                /* Require start of range */
                if (String.IsNullOrWhiteSpace(range_arr[0])) {
                    return false;
                }

                start = Int64.Parse(range_arr[0]);

                /* Until EOF when no limit is given */
                end = String.IsNullOrWhiteSpace(range_arr[1]) ? finfo.Length : Int64.Parse(range_arr[1]);

                if (start < 0 || end > finfo.Length) {
                    return false;
                }
            }

            return true;
        }
    }

    public class AiFileServer : IDisposable {

        private readonly CancellationTokenSource cts = new();
        private readonly Task server;

        public AiFileServer(IPAddress addr, ushort port, string file, string mime)
            : this(new IPEndPoint(addr, port), file, mime) { }

        public AiFileServer(IPEndPoint endpoint, string file, string mime) {
            ServerConfig cfg = new();
            cfg.AddRoute("/", new AiServeSingleFile(file, mime));

            server = HttpServer.ListenAsync(endpoint, false, cfg, cts.Token);
        }

        public async void Dispose() {
            cts.Cancel();
            await server;

            GC.SuppressFinalize(this);
        }
    }
}
