using System.Text;

namespace ServiceLib.Services;

public sealed record FreeConfigSource(string Name, string Url);

/// <summary>
/// Matixray: reads public lists of share links ("free configs") and imports them into one group
/// (<see cref="GroupName"/>) of the normal server list, so the usual test / connect tools work on them.
/// </summary>
public static class FreeConfigService
{
    public const string GroupName = "Matixray Free";

    private const int PerSourceLimit = 40;
    private const int TotalLimit = 400;
    private const int ParallelSources = 16;
    private const int SourceTimeoutSeconds = 8;

    private static readonly string[] Schemes = ["vless://", "vmess://", "trojan://", "ss://", "hysteria2://", "hy2://"];

    public static readonly List<FreeConfigSource> Sources =
    [
        new("Mahsa NG", "https://raw.githubusercontent.com/MahsaNetConfigTopic/config/main/xray_final.txt"),
        new("Mahsa NG | mirror", "https://raw.githubusercontent.com/mhcfpTopic/config/main/xray_final.txt"),
        new("Mahsa NG | vless", "https://raw.githubusercontent.com/MahsaNetConfigTopic/config/main/xray_final_vless.txt"),
        new("Mahsa NG | mix", "https://raw.githubusercontent.com/MahsaNetConfigTopic/config/main/mix.txt"),
        new("Matix | Xfinder Top20", "https://imatixofficel.github.io/Xfinder/output/top20.txt"),
        new("Matix | Xfinder All", "https://imatixofficel.github.io/Xfinder/output/all.txt"),
        new("OpenRay Iran top100", "https://raw.githubusercontent.com/sakha1370/OpenRay/refs/heads/main/output_iran/iran_top100_checked.txt"),
        new("Iran-configs", "https://raw.githubusercontent.com/therealaleph/Iran-configs/refs/heads/main/ir_configs.txt"),
        new("Sub_Checker_Creator", "https://raw.githubusercontent.com/hamedp-71/Sub_Checker_Creator/refs/heads/main/final.txt"),
        new("F0rc3Run best", "https://raw.githubusercontent.com/F0rc3Run/F0rc3Run/refs/heads/main/Best-Results/sub.txt"),
        new("patterniha", "https://raw.githubusercontent.com/patterniha/Free-Configs/main/configs.txt"),
        new("port-based top100", "https://raw.githubusercontent.com/hamedcode/port-based-v2ray-configs/main/sub/top100.txt"),
        new("0xRadikal top100", "https://raw.githubusercontent.com/0xRadikal/Free-v2ray-Configs/main/top100.txt"),
        new("Epodonios", "https://raw.githubusercontent.com/Epodonios/v2ray-configs/main/All_Configs_Sub.txt"),
        new("Epodonios Sub1", "https://raw.githubusercontent.com/Epodonios/v2ray-configs/refs/heads/main/Sub1.txt"),
        new("Epodonios Sub2", "https://raw.githubusercontent.com/Epodonios/v2ray-configs/refs/heads/main/Sub2.txt"),
        new("barry-far", "https://raw.githubusercontent.com/barry-far/V2ray-Config/main/All_Configs_Sub.txt"),
        new("barry-far vless", "https://raw.githubusercontent.com/barry-far/V2ray-Config/main/Splitted-By-Protocol/vless.txt"),
        new("barry-far Sub1", "https://raw.githubusercontent.com/barry-far/V2ray-config/main/Sub1.txt"),
        new("ebrasha", "https://raw.githubusercontent.com/ebrasha/free-v2ray-public-list/refs/heads/main/V2Ray-Config-By-EbraSha.txt"),
        new("ebrasha vless", "https://raw.githubusercontent.com/ebrasha/free-v2ray-public-list/refs/heads/main/vless_configs.txt"),
        new("MatinGhanbari", "https://raw.githubusercontent.com/MatinGhanbari/v2ray-configs/main/subscriptions/v2ray/super-sub.txt"),
        new("TVC (yebekhe)", "https://raw.githubusercontent.com/yebekhe/TVC/main/subscriptions/xray/base64/mix"),
        new("V2RayAggregator", "https://raw.githubusercontent.com/mahdibland/V2RayAggregator/master/sub/sub_merge.txt"),
        new("SSAggregator", "https://raw.githubusercontent.com/mahdibland/SSAggregator/master/sub/sub_merge.txt"),
        new("Telegram collector", "https://raw.githubusercontent.com/soroushmirzaei/telegram-configs-collector/main/splitted/mixed"),
        new("Telegram collector (mohamadfg)", "https://raw.githubusercontent.com/mohamadfg-dev/telegram-v2ray-configs-collector/refs/heads/main/category/vless.txt"),
        new("Pawdroid", "https://raw.githubusercontent.com/Pawdroid/Free-servers/main/sub"),
        new("ermaozi", "https://raw.githubusercontent.com/ermaozi/get_subscribe/main/subscribe/v2ray.txt"),
        new("4Diana", "https://raw.githubusercontent.com/EEvanescence/4Diana/main/AllConfigsSub.txt"),
        new("NiREvil", "https://raw.githubusercontent.com/NiREvil/vless/refs/heads/main/sub/SSTime"),
        new("Argh94", "https://raw.githubusercontent.com/Argh94/V2RayAutoConfig/refs/heads/main/configs/Hysteria2.txt"),
        new("V2RayRoot", "https://raw.githubusercontent.com/V2RayRoot/V2RayConfig/refs/heads/main/Config/vless.txt"),
        new("multi-proxy fetcher", "https://raw.githubusercontent.com/4n0nymou3/multi-proxy-config-fetcher/refs/heads/main/configs/proxy_configs.txt"),
        new("free-v2ray-collector", "https://raw.githubusercontent.com/iboxz/free-v2ray-collector/main/main/mix.txt"),
        new("NewCollector", "https://raw.githubusercontent.com/rango-cfs/NewCollector/refs/heads/main/v2ray_links.txt"),
        new("kamaji", "https://raw.githubusercontent.com/shabane/kamaji/master/hub/merged.txt"),
        new("awesome-vpn", "https://raw.githubusercontent.com/awesome-vpn/awesome-vpn/master/all"),
        new("xray-config-toolkit", "https://raw.githubusercontent.com/wuqb2i4f/xray-config-toolkit/refs/heads/main/output/base64/mix-uri"),
        new("freeSub", "https://raw.githubusercontent.com/Ruk1ng001/freeSub/main/v2ray"),
        new("freenode", "https://raw.githubusercontent.com/ripaojiedian/freenode/main/sub"),
    ];

    #region Group

    /// <summary>The id of the free-configs group, or an empty string when it was not created yet.</summary>
    public static async Task<string> FindGroupAsync()
    {
        var subs = await AppManager.Instance.SubItems() ?? [];
        return subs.FirstOrDefault(t => t.Remarks == GroupName)?.Id ?? string.Empty;
    }

    /// <summary>The id of the free-configs group; the group is created when it does not exist.</summary>
    public static async Task<string> EnsureGroupAsync(Config config)
    {
        var id = await FindGroupAsync();
        if (id.Length > 0)
        {
            return id;
        }

        SubItem item = new()
        {
            Id = string.Empty,
            Remarks = GroupName,
            Url = string.Empty,
            Enabled = true,
        };
        await ConfigHandler.AddSubItem(config, item);
        return await FindGroupAsync();
    }

    #endregion Group

    #region Parse

    /// <summary>Share links found in a downloaded list (plain lines, or a base64 body).</summary>
    public static List<string> ExtractLinks(string? body)
    {
        if (body.IsNullOrEmpty())
        {
            return [];
        }

        var direct = LinksIn(body);
        if (direct.Count > 0)
        {
            return direct;
        }

        var decoded = TryBase64(body);
        return decoded is null ? [] : LinksIn(decoded);
    }

    private static List<string> LinksIn(string text)
    {
        return text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length is >= 12 and <= 4000 && Schemes.Any(s => l.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToList();
    }

    private static string? TryBase64(string input)
    {
        try
        {
            var clean = new string(input.Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace('-', '+').Replace('_', '/');
            if (clean.Length == 0)
            {
                return null;
            }
            clean = clean.PadRight(clean.Length + ((4 - (clean.Length % 4)) % 4), '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(clean));
        }
        catch
        {
            return null;
        }
    }

    #endregion Parse

    #region Fetch

    /// <summary>
    /// Reads all sources at the same time and imports each source's links as soon as it answers.
    /// <paramref name="onStatus"/> gets a short line per source; <paramref name="onBatchAdded"/> is called after every
    /// import so the screen can show the new servers one by one. Returns how many servers were added.
    /// </summary>
    public static async Task<int> FetchAsync(Config config, string subId, Func<string, Task> onStatus, Func<Task> onBatchAdded, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var gate = new SemaphoreSlim(ParallelSources);
        var downloader = new DownloadService();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var total = 0;

        var tasks = Sources.Select(s => DownloadSourceAsync(downloader, s, gate, cts.Token)).ToList();
        try
        {
            while (tasks.Count > 0)
            {
                var finished = await Task.WhenAny(tasks);
                tasks.Remove(finished);
                ct.ThrowIfCancellationRequested();

                var (source, links) = await finished;
                var fresh = links.Where(seen.Add).Take(Math.Max(0, TotalLimit - total)).ToList();
                if (fresh.Count == 0)
                {
                    await onStatus($"{source.Name}: 0");
                    continue;
                }

                var added = await ConfigHandler.AddBatchServers(config, string.Join('\n', fresh), subId, false);
                added = Math.Max(added, 0);
                total += added;
                if (added > 0)
                {
                    await onBatchAdded();
                }
                await onStatus($"{source.Name}: +{added}  ({total})");

                if (total >= TotalLimit)
                {
                    break;
                }
            }
        }
        finally
        {
            cts.Cancel();
        }

        return total;
    }

    private static async Task<(FreeConfigSource Source, List<string> Links)> DownloadSourceAsync(DownloadService downloader, FreeConfigSource source, SemaphoreSlim gate, CancellationToken ct)
    {
        try
        {
            await gate.WaitAsync(ct);
        }
        catch
        {
            return (source, []);
        }

        try
        {
            var userAgent = Utils.GetVersion(false);
            var text = await TryGetAsync(downloader, source.Url, false, userAgent, ct);
            if (text.IsNullOrEmpty())
            {
                // not reachable directly: try through the local proxy (works when a server is already connected)
                text = await TryGetAsync(downloader, source.Url, true, userAgent, ct);
            }

            var links = ExtractLinks(text);
            return (source, links.OrderBy(_ => Random.Shared.Next()).Take(PerSourceLimit).ToList());
        }
        catch
        {
            return (source, []);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<string?> TryGetAsync(DownloadService downloader, string url, bool viaProxy, string userAgent, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(SourceTimeoutSeconds));
        try
        {
            return await downloader.TryDownloadString(url, viaProxy, userAgent, timeout.Token);
        }
        catch
        {
            return null;
        }
    }

    #endregion Fetch
}
