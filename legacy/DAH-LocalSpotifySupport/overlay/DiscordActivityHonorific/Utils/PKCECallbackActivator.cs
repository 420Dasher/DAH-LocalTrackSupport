using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordActivityHonorific.Utils;

// Adapted from SpotifyAPI-NET's PKCE callback example.
public sealed class PKCECallbackActivator : IDisposable
{
    private readonly HttpListener httpListener;
    private readonly CancellationTokenSource cancellationTokenSource = new();

    public Uri RedirectUri { get; }
    public string CallbackPath { get; }

    public PKCECallbackActivator(Uri serverUri, string callbackPath)
    {
        if (!callbackPath.StartsWith('/')) callbackPath = $"/{callbackPath}";

        RedirectUri = new Uri(serverUri, callbackPath);
        CallbackPath = callbackPath;
        httpListener = new HttpListener();
        httpListener.Prefixes.Add(serverUri.ToString());
    }

    public Task Start()
    {
        if (!httpListener.IsListening) httpListener.Start();
        return Task.CompletedTask;
    }

    public async Task<HttpListenerContext> ReceiveContext(CancellationToken cancellationToken)
    {
        using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationTokenSource.Token,
            cancellationToken);

        while (true)
        {
            var contextTask = httpListener.GetContextAsync();
            var cancelTask = Task.Delay(Timeout.Infinite, linkedToken.Token);
            var completed = await Task.WhenAny(contextTask, cancelTask).ConfigureAwait(false);
            if (completed == cancelTask) throw new TaskCanceledException();

            var context = await contextTask.ConfigureAwait(false);
            if (context.Request.Url?.AbsolutePath == CallbackPath) return context;

            context.Response.StatusCode = 404;
            context.Response.Close();
        }
    }

    public void Dispose()
    {
        if (httpListener.IsListening) httpListener.Stop();
        httpListener.Close();
        cancellationTokenSource.Cancel();
        cancellationTokenSource.Dispose();
    }
}
