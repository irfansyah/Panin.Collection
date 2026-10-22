// Harness uji beban awal (tingkat HTTP): login massal -> halaman shell -> logout.
//
//   dotnet run --project tools/Panin.LoadTest -c Release -- seed --count 700
//   dotnet run --project tools/Panin.LoadTest -c Release -- run --url http://localhost:5122 --users 700 --concurrency 150 --hold 30
//
// Yang diukur: latensi tiap langkah (p50/p95/p99), tingkat kegagalan, dan memori server (/_diag,
// aktifkan dengan Diagnostics:Enabled=true). Belum membuka sirkuit Blazor (WebSocket): memori per
// sirkuit diukur terpisah (lihat docs/LOAD-TESTING.md).
//
// Keamanan: perintah seed menolak server selain lokal dan hanya menyentuh user berawalan LT.
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

var cfg = new ConfigurationBuilder().AddUserSecrets<Marker>().Build();
var options = ParseArgs(args);
var command = args.Length > 0 ? args[0] : "help";

switch (command)
{
    case "seed":
        return await Seed(cfg, options);
    case "run":
        return await Run(cfg, options);
    default:
        Console.WriteLine("Perintah: seed --count N | run --url URL --users N --concurrency C [--hold DETIK] [--keep]");
        return 1;
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 1; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        var key = args[i][2..];
        d[key] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
    }

    return d;
}

static string UserId(int n) => $"LT{n:D4}";

static string Sha1Hex(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)));

static async Task<int> Seed(IConfiguration cfg, Dictionary<string, string> o)
{
    var count = int.Parse(o.GetValueOrDefault("count", "700"), System.Globalization.CultureInfo.InvariantCulture);
    var cs = cfg["ConnectionStrings:Login"] ?? throw new InvalidOperationException("ConnectionStrings:Login belum diisi");
    var b = new SqlConnectionStringBuilder(cs);
    var host = b.DataSource.Split(',')[0].Trim().ToLowerInvariant();
    if (host is not ("localhost" or "127.0.0.1" or "." or "(local)"))
    {
        Console.Error.WriteLine($"Ditolak: seed hanya untuk server lokal, bukan '{b.DataSource}'.");
        return 1;
    }

    var password = cfg["LoadTest:Password"];
    if (string.IsNullOrEmpty(password))
    {
        password = "Lt" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)) + "x9";
        Console.WriteLine("Password uji beban belum ada di user-secrets (LoadTest:Password). Simpan dengan:");
        Console.WriteLine($"  dotnet user-secrets set \"LoadTest:Password\" \"{password}\" --id f53902af-5c32-489a-8b41-f0cba6e4934a");
        Console.WriteLine("lalu jalankan seed lagi.");
        return 2;
    }

    var hash = Sha1Hex(password);
    string[] groups = ["dms01", "FESPV", "AUD"]; // 3 modul, 2 modul, 1 modul
    await using var conn = new SqlConnection(cs);
    await conn.OpenAsync();
    await Exec(conn, "delete from SCALLUSERFLAG where USERID like 'LT[0-9][0-9][0-9][0-9]'; delete from SCALLUSER where USERID like 'LT[0-9][0-9][0-9][0-9]'");

    var users = new System.Data.DataTable();
    users.Columns.Add("USERID");
    users.Columns.Add("GROUPID");
    users.Columns.Add("SU_FULLNAME");
    users.Columns.Add("SU_PWD");
    users.Columns.Add("SU_ACTIVE", typeof(bool));
    users.Columns.Add("SU_PWDEXPDATE", typeof(DateTime));
    var flags = new System.Data.DataTable();
    flags.Columns.Add("USERID");
    flags.Columns.Add("SU_LOGON");
    flags.Columns.Add("SU_REVOKE");
    flags.Columns.Add("SU_FALSEPWDCOUNT", typeof(int));
    for (var i = 1; i <= count; i++)
    {
        users.Rows.Add(UserId(i), groups[i % groups.Length], $"Load Test {i:D4}", hash, true, DateTime.UtcNow.AddYears(1));
        flags.Rows.Add(UserId(i), "0", "0", 0);
    }

    // Trigger INS_USER aktif di tabel; pakai bulk copy dengan FireTriggers agar perilaku sama seperti insert biasa.
    using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.FireTriggers, null) { DestinationTableName = "SCALLUSER" })
    {
        foreach (System.Data.DataColumn c in users.Columns)
        {
            bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
        }

        await bulk.WriteToServerAsync(users);
    }

    using (var bulk = new SqlBulkCopy(conn) { DestinationTableName = "SCALLUSERFLAG" })
    {
        foreach (System.Data.DataColumn c in flags.Columns)
        {
            bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
        }

        await bulk.WriteToServerAsync(flags);
    }

    Console.WriteLine($"Dibuat {count} user {UserId(1)}..{UserId(count)} di {b.DataSource}/{b.InitialCatalog}.");
    return 0;
}

static async Task Exec(SqlConnection c, string sql)
{
    await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 120 };
    await cmd.ExecuteNonQueryAsync();
}

static async Task<int> Run(IConfiguration cfg, Dictionary<string, string> o)
{
    var baseUrl = new Uri(o.GetValueOrDefault("url", "http://localhost:5122"));
    var users = int.Parse(o.GetValueOrDefault("users", "100"), System.Globalization.CultureInfo.InvariantCulture);
    var concurrency = int.Parse(o.GetValueOrDefault("concurrency", "50"), System.Globalization.CultureInfo.InvariantCulture);
    var holdSeconds = int.Parse(o.GetValueOrDefault("hold", "0"), System.Globalization.CultureInfo.InvariantCulture);
    var keep = o.ContainsKey("keep");
    var password = cfg["LoadTest:Password"] ?? throw new InvalidOperationException("LoadTest:Password belum diisi (jalankan seed).");

    using var diag = new HttpClient { BaseAddress = baseUrl };
    var before = await Diag(diag);
    Console.WriteLine($"Target {baseUrl} | {users} user | konkurensi {concurrency} | tahan {holdSeconds}s");
    if (before is not null)
    {
        Console.WriteLine($"Server sebelum : {before}");
    }

    var results = new Result[users];
    var clients = new HttpClient[users];
    var gate = new SemaphoreSlim(concurrency);
    var wall = Stopwatch.StartNew();

    async Task Login(int i)
    {
        await gate.WaitAsync();
        try
        {
            clients[i] = NewClient(baseUrl);
            results[i] = await LoginFlow(clients[i], UserId(i + 1), password);
        }
        finally
        {
            gate.Release();
        }
    }

    await Task.WhenAll(Enumerable.Range(0, users).Select(Login));
    var loginWall = wall.Elapsed;

    if (holdSeconds > 0)
    {
        Console.WriteLine($"Semua login selesai dalam {loginWall.TotalSeconds:F1}s; menahan {holdSeconds}s...");
        await Task.Delay(TimeSpan.FromSeconds(holdSeconds));
    }

    var during = await Diag(diag);

    if (!keep)
    {
        var logoutGate = new SemaphoreSlim(concurrency);
        await Task.WhenAll(Enumerable.Range(0, users).Select(async i =>
        {
            await logoutGate.WaitAsync();
            try
            {
                if (results[i].LoggedIn)
                {
                    results[i].LogoutMs = await Logout(clients[i], results[i].LogoutToken);
                }
            }
            finally
            {
                logoutGate.Release();
            }
        }));
    }

    var after = await Diag(diag);
    foreach (var c in clients)
    {
        c?.Dispose();
    }

    Report(results, wall.Elapsed, loginWall);
    if (during is not null)
    {
        Console.WriteLine($"Server saat tahan : {during}");
    }

    if (after is not null)
    {
        Console.WriteLine($"Server sesudah    : {after}");
    }

    return results.Count(r => !r.LoggedIn) == 0 ? 0 : 3;
}

static HttpClient NewClient(Uri baseUrl)
{
    var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() };
    return new HttpClient(handler) { BaseAddress = baseUrl, Timeout = TimeSpan.FromSeconds(60) };
}

static async Task<Result> LoginFlow(HttpClient http, string user, string password)
{
    var r = new Result { User = user };
    var sw = Stopwatch.StartNew();
    try
    {
        var loginPage = await http.GetStringAsync("/login");
        r.LoginPageMs = sw.Elapsed.TotalMilliseconds;
        var token = Field(loginPage, "__RequestVerificationToken");
        var handler = Field(loginPage, "_handler");

        sw.Restart();
        using var post = await http.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["_handler"] = handler,
            ["Model.UserId"] = user,
            ["Model.Password"] = password,
        }));
        var postBody = await post.Content.ReadAsStringAsync();
        r.LoginPostMs = sw.Elapsed.TotalMilliseconds;
        if (!post.Headers.Contains("Set-Cookie") && !post.Headers.Contains("Location"))
        {
            r.Error = "login ditolak: " + (Regex.Match(postBody, "role=\"alert\">([^<]*)").Groups[1].Value is { Length: > 0 } m ? m : $"HTTP {(int)post.StatusCode}");
            return r;
        }

        sw.Restart();
        var shell = await http.GetStringAsync("/");
        r.ShellMs = sw.Elapsed.TotalMilliseconds;
        if (!shell.Contains("Logout", StringComparison.Ordinal))
        {
            r.Error = "halaman shell tidak berisi Logout";
            return r;
        }

        r.LogoutToken = Field(shell, "__RequestVerificationToken");
        r.LoggedIn = true;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
    {
        r.Error = ex.GetType().Name + ": " + ex.Message;
    }

    return r;
}

static async Task<double> Logout(HttpClient http, string token)
{
    var sw = Stopwatch.StartNew();
    try
    {
        using var resp = await http.PostAsync("/auth/logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
    }
    catch (HttpRequestException)
    {
        // dicatat lewat sesi yang tersisa di /_diag
    }

    return sw.Elapsed.TotalMilliseconds;
}

static string Field(string html, string name)
{
    var m = Regex.Match(html, $"name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"");
    return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : throw new InvalidOperationException($"Field {name} tidak ditemukan");
}

static async Task<string?> Diag(HttpClient http)
{
    try
    {
        var d = await http.GetFromJsonAsync<Dictionary<string, long>>("/_diag");
        return d is null ? null : string.Join(", ", d.Select(kv => $"{kv.Key}={kv.Value}"));
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
    {
        return null;
    }
}

static void Report(Result[] results, TimeSpan total, TimeSpan loginWall)
{
    var ok = results.Where(r => r.LoggedIn).ToArray();
    Console.WriteLine();
    Console.WriteLine($"Berhasil login: {ok.Length}/{results.Length} | waktu total {total.TotalSeconds:F1}s | login massal {loginWall.TotalSeconds:F1}s ({results.Length / Math.Max(loginWall.TotalSeconds, 0.001):F1} login/detik)");
    Console.WriteLine($"{"langkah",-14}{"p50",8}{"p95",8}{"p99",8}{"maks",8}  (ms)");
    Line("GET /login", ok.Select(r => r.LoginPageMs));
    Line("POST /login", ok.Select(r => r.LoginPostMs));
    Line("GET / (shell)", ok.Select(r => r.ShellMs));
    Line("POST logout", ok.Where(r => r.LogoutMs > 0).Select(r => r.LogoutMs));

    foreach (var g in results.Where(r => r.Error is not null).GroupBy(r => r.Error!).OrderByDescending(g => g.Count()).Take(5))
    {
        Console.WriteLine($"  GAGAL x{g.Count()}: {g.Key}");
    }
}

static void Line(string name, IEnumerable<double> values)
{
    var v = values.Order().ToArray();
    if (v.Length == 0)
    {
        return;
    }

    double P(double p) => v[Math.Min(v.Length - 1, (int)Math.Ceiling(p * v.Length) - 1)];
    Console.WriteLine($"{name,-14}{P(0.5),8:F0}{P(0.95),8:F0}{P(0.99),8:F0}{v[^1],8:F0}");
}

internal sealed class Result
{
    public string User { get; set; } = string.Empty;

    public bool LoggedIn { get; set; }

    public string? Error { get; set; }

    public string LogoutToken { get; set; } = string.Empty;

    public double LoginPageMs { get; set; }

    public double LoginPostMs { get; set; }

    public double ShellMs { get; set; }

    public double LogoutMs { get; set; }
}

internal sealed class Marker;
