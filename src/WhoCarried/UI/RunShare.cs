using System.Text;
using System.Text.Json;
using Godot;
using WhoCarried.Core;
using WhoCarried.Game;
using WhoCarried.Localization;

namespace WhoCarried.UI;

/// <summary>Sends the open recap to the share site from <see cref="Settings.ShareUrl"/>.</summary>
internal static class RunShare
{
    /// <summary>
    /// Posts the recap. <paramref name="onDone"/> runs on the main thread with the page URL, or an error message.
    /// </summary>
    public static void Send(string dataDir, RecapView view, Action<string?> onDone)
    {
        (Settings settings, string? loadError) = Settings.Load(dataDir);
        if (loadError != null) Tracker.Note($"settings: {loadError}");
        string origin = (settings.ShareUrl ?? "").Trim().TrimEnd('/');
        if (origin.Length == 0)
        {
            onDone(Loc.Text("WHO_CARRIED.share.need_url"));
            return;
        }
        if (!origin.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            onDone(Loc.Text("WHO_CARRIED.share.bad_url"));
            return;
        }

        string json = JsonSerializer.Serialize(view, RecapJson.Default.RecapView);
        var http = new HttpRequest { Timeout = 20, Name = "WhoCarriedShare" };
        http.RequestCompleted += (result, code, _, body) =>
        {
            http.QueueFree();
            if (result != (long)HttpRequest.Result.Success || code < 200 || code >= 300)
            {
                Tracker.Note($"share failed: result {result} http {code}");
                onDone(Loc.Text("WHO_CARRIED.share.failed"));
                return;
            }
            onDone(ReadUrl(body));
        };

        SceneTree tree = (SceneTree)Engine.GetMainLoop();
        tree.Root.AddChild(http);
        Error error = http.Request(origin + "/api/runs", new[] { "Content-Type: application/json" }, Godot.HttpClient.Method.Post, json);
        if (error != Error.Ok)
        {
            http.QueueFree();
            Tracker.Note($"share failed: {error}");
            onDone(Loc.Text("WHO_CARRIED.share.failed"));
        }
    }

    private static string? ReadUrl(byte[] body)
    {
        try
        {
            ShareResult? share = JsonSerializer.Deserialize(Encoding.UTF8.GetString(body), RecapJson.Default.ShareResult);
            return string.IsNullOrEmpty(share?.Url) ? Loc.Text("WHO_CARRIED.share.bad_link") : share.Url;
        }
        catch (Exception e)
        {
            Tracker.LogError("share response", e);
            return Loc.Text("WHO_CARRIED.share.bad_link");
        }
    }
}
