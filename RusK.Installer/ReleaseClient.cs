using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Script.Serialization;

namespace RusK.Installer;

/// <summary>リリースに添付されたファイル (Mod の DLL など)</summary>
internal sealed class ReleaseAsset
{
    public string Name;        // 例: RuskModel.dll
    public string Tag;         // 例: model-v1.2.5
    public string Version;     // 例: 1.2.5 (タグの "-v" の後ろ)
    public long Size;
    public string DownloadUrl; // ブラウザ用の URL (公開リポジトリ)
    public string ApiUrl;      // API の URL (トークンで非公開リポジトリから落とすとき)
}

/// <summary>
/// GitHub のリリースから Mod の最新の DLL を探してダウンロードする。
/// Mod ごとにリリースを分けている (例: 「Custom VRM Loader Mod v1.2.5」= タグ model-v1.2.5 に RuskModel.dll) ので、
/// リリースを新しい順に見て、そのファイル名がある最初のリリースを最新とする。
/// 環境変数 RUSK_GITHUB_TOKEN があれば、それで認証する (非公開リポジトリでのテスト用)
/// </summary>
internal sealed class ReleaseClient
{
    public const string Repo = "DonutSuZu/RusK";

    private readonly HttpClient _http;
    private readonly string _token;
    private List<ReleaseAsset> _assets;

    public ReleaseClient()
    {
        // .NET Framework 4.8 は既定で TLS 1.2 を使わないことがある (GitHub は TLS 1.2 以上が必要)
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        _token = Environment.GetEnvironmentVariable("RUSK_GITHUB_TOKEN");
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"RusK-Setup/{InstallEngine.PayloadVersion}");
        if (!string.IsNullOrEmpty(_token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", _token);
    }

    /// <summary>リリースの一覧を取得して、ファイル名ごとに最新のものを覚える</summary>
    public IReadOnlyList<ReleaseAsset> Fetch()
    {
        if (_assets != null) return _assets;
        var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases?per_page=100");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        var res = _http.SendAsync(req).GetAwaiter().GetResult();
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub: {(int)res.StatusCode} {res.ReasonPhrase}");
        var json = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        var list = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as object[];
        var result = new List<ReleaseAsset>();
        foreach (var o in list ?? Array.Empty<object>())
        {
            if (o is not Dictionary<string, object> rel) continue;
            if (rel.TryGetValue("draft", out var d) && d is bool draft && draft) continue;
            if (rel.TryGetValue("prerelease", out var p) && p is bool pre && pre) continue;
            string tag = rel.TryGetValue("tag_name", out var t) ? t as string : "";
            if (!rel.TryGetValue("assets", out var a) || a is not IEnumerable assets) continue;
            foreach (var ao in assets)
            {
                if (ao is not Dictionary<string, object> asset) continue;
                result.Add(new ReleaseAsset
                {
                    Name = asset.TryGetValue("name", out var n) ? n as string : "",
                    Tag = tag,
                    Version = VersionOf(tag),
                    Size = asset.TryGetValue("size", out var s) ? Convert.ToInt64(s) : 0,
                    DownloadUrl = asset.TryGetValue("browser_download_url", out var u) ? u as string : null,
                    ApiUrl = asset.TryGetValue("url", out var au) ? au as string : null,
                });
            }
        }
        _assets = result;
        return _assets;
    }

    /// <summary>そのファイル名がある、いちばん新しいリリースのファイル (無ければ null)</summary>
    public ReleaseAsset Latest(string assetName) =>
        Fetch().FirstOrDefault(a => string.Equals(a.Name, assetName, StringComparison.OrdinalIgnoreCase));

    /// <summary>ファイルをダウンロードする。progress(受け取ったバイト数, 全体のバイト数)</summary>
    public void Download(ReleaseAsset asset, string destPath, Action<long, long> progress)
    {
        HttpRequestMessage req;
        if (!string.IsNullOrEmpty(_token) && asset.ApiUrl != null)
        {
            req = new HttpRequestMessage(HttpMethod.Get, asset.ApiUrl);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        }
        else
        {
            req = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
        }

        using var res = _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)res.StatusCode} {res.ReasonPhrase}");
        long total = res.Content.Headers.ContentLength ?? asset.Size;

        // いったん .part に書いて、最後まで落とせたら置き換える (途中で失敗しても前の DLL が壊れない)
        var part = destPath + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(destPath));
        using (var src = res.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
        using (var dst = File.Create(part))
        {
            var buf = new byte[81920];
            long got = 0;
            int n;
            while ((n = src.Read(buf, 0, buf.Length)) > 0)
            {
                dst.Write(buf, 0, n);
                got += n;
                progress(got, total);
            }
        }
        if (File.Exists(destPath)) File.Delete(destPath);
        File.Move(part, destPath);
    }

    /// <summary>
    /// GitHub 以外のファイル (BepInEx の公式の zip) をダウンロードする。GitHub の認証は付けない。
    /// sha256 を渡すと、落としたファイルと比べて違えば消して例外にする (壊れた・すり替えられたファイルを使わない)
    /// </summary>
    public static void DownloadFile(string url, string destPath, string sha256, Action<long, long> progress)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"RusK-Setup/{InstallEngine.PayloadVersion}");
        using var res = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)res.StatusCode} {res.ReasonPhrase}");
        long total = res.Content.Headers.ContentLength ?? 0;
        Directory.CreateDirectory(Path.GetDirectoryName(destPath));
        using (var src = res.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
        using (var dst = File.Create(destPath))
        {
            var buf = new byte[81920];
            long got = 0;
            int n;
            while ((n = src.Read(buf, 0, buf.Length)) > 0)
            {
                dst.Write(buf, 0, n);
                got += n;
                progress(got, total);
            }
        }
        if (!string.IsNullOrEmpty(sha256))
        {
            string actual;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var f = File.OpenRead(destPath))
                actual = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
            if (actual != sha256.ToLowerInvariant())
            {
                File.Delete(destPath);
                throw new InvalidOperationException(Strings.T("ダウンロードしたファイルが壊れています (SHA256 が一致しません)"));
            }
        }
    }

    private static string VersionOf(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return "";
        int i = tag.LastIndexOf("-v", StringComparison.OrdinalIgnoreCase);
        if (i >= 0) return tag.Substring(i + 2);
        return tag.TrimStart('v', 'V');
    }

    public static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";
}
