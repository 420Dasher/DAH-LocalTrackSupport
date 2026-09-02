using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifyTrackHonorific.Spotify;

internal sealed class SpotifyAuthCallbackServer : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource lifetimeCts = new();

    public Uri RedirectUri { get; }

    public SpotifyAuthCallbackServer(Uri redirectUri)
    {
        RedirectUri = redirectUri;

        // HttpListener listens on the directory/prefix, not the individual path.
        var prefix = $"{redirectUri.Scheme}://{redirectUri.Host}:{redirectUri.Port}/";
        listener.Prefixes.Add(prefix);
    }

    public void Start()
    {
        if (!listener.IsListening)
            listener.Start();
    }

    public async Task<HttpListenerContext> WaitForCallbackAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token, cancellationToken);

        while (true)
        {
            var contextTask = listener.GetContextAsync();
            var cancellationTask = Task.Delay(Timeout.Infinite, linked.Token);
            var completed = await Task.WhenAny(contextTask, cancellationTask).ConfigureAwait(false);

            if (completed == cancellationTask)
                throw new TaskCanceledException();

            var context = await contextTask.ConfigureAwait(false);
            if (string.Equals(context.Request.Url?.AbsolutePath, RedirectUri.AbsolutePath, StringComparison.Ordinal))
                return context;

            context.Response.StatusCode = 404;
            context.Response.Close();
        }
    }

    public static async Task ReplyAsync(HttpListenerContext context, bool success, string message)
    {
        try
        {
            var safeMessage = WebUtility.HtmlEncode(message);
            var html = $"<html><body style='font-family:sans-serif'><h2>SpotifyTrackHonorific</h2><p>{safeMessage}</p><p>You can close this tab and return to FFXIV.</p></body></html>";
            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.StatusCode = success ? 200 : 400;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            context.Response.Close();
        }
        catch
        {
            // Browser may be closed before the response finishes writing.
        }
    }

    public void Dispose()
    {
        lifetimeCts.Cancel();
        if (listener.IsListening)
            listener.Stop();
        listener.Close();
        lifetimeCts.Dispose();
    }
}
