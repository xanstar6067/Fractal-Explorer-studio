using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyCloudAsync()
    {
        using var sandbox = DataSandbox.Create("cloud");
        var vault = new MemoryCloudVault { Value = new() { Email = "test@example.invalid", RefreshToken = "initial" } };
        var server = new FakeCloudServer();
        using var client = new FractalCloudClient(new HttpClient(server) { BaseAddress = new("https://cloud.invalid") }, vault);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => client.ListAsync(default)));
        Check(server.Refreshes == 1 && server.MaxRefreshesInFlight == 1, "Concurrent calls must share one refresh.");
        Check(vault.Value?.RefreshToken == "refresh-1", "Rotated refresh token must be persisted.");
        server.UnauthorizedResponses = 1;
        await client.ListAsync(default);
        Check(server.Refreshes == 2, "401 must refresh and retry once.");
        server.UnauthorizedResponses = 2;
        await ExpectCloudAsync<CloudLoginRequiredException>(() => client.ListAsync(default));
        Check(server.Refreshes == 3 && vault.Value is null, "Repeated 401 must stop and erase tokens.");
        await client.LoginAsync("test@example.invalid", "test-password", default);
        Check(server.LastLoginHasPassword && vault.Value?.RefreshToken == "login-refresh", "Login contract and vault.");
        server.FailRefresh = true;
        server.UnauthorizedResponses = 1;
        await ExpectCloudAsync<CloudLoginRequiredException>(() => client.ListAsync(default));
        Check(vault.Value is null, "Failed refresh must clear persistent credentials.");
        server.FailRefresh = false;
        server.LoginLifetime = 1;
        await client.LoginAsync("test@example.invalid", "test-password", default);
        int beforeExpiry = server.Refreshes;
        await Task.Delay(1050);
        await client.ListAsync(default);
        Check(server.Refreshes == beforeExpiry + 1, "expiresIn must be checked before sending saves request.");
        server.LoginLifetime = 3600;
        foreach (HttpStatusCode status in new[] { HttpStatusCode.BadRequest, HttpStatusCode.NotFound, HttpStatusCode.RequestEntityTooLarge })
        {
            server.ForcedStatus = status;
            try { await client.ListAsync(default); throw new InvalidOperationException("HTTP error was swallowed."); }
            catch (CloudApiException error) { Check(error.Status == status, "HTTP status must be preserved without response bodies."); }
        }

        FractalCloudClient.ValidateSave(new string('a', 100), "{\"x\":1}");
        await ExpectCloudAsync<InvalidOperationException>(() => client.CreateAsync(new string('a', 101), "{}", default));
        await ExpectCloudAsync<InvalidOperationException>(() => client.CreateAsync("big", "{\"x\":\"" + new string('я', 524288) + "\"}", default));
        await ExpectCloudAsync<JsonException>(() => client.CreateAsync("bad", "not-json", default));

        var store = new FractalSaveStore<CloudTestState>("Mandelbrot", s => s.SaveName);
        SaveSlot<CloudTestState> slot = store.Save(new("Test", 10));
        File.WriteAllText(slot.PreviewPath, "LOCAL PREVIEW MUST NOT BE SENT");
        LocalCloudSave local = CloudSaveRepository.ReadLocal(slot.FilePath);
        Check(!local.JsonData.Contains("PREVIEW") && !local.JsonData.Contains("FilePath"), "Payload must not contain preview or disk path.");
        var index = new CloudSyncIndex(client.Server, client.Email!);
        int conflicts = 0;
        CloudConflictChoice choice = CloudConflictChoice.Cancel;
        var sync = new CloudSyncService(client, index, (_, remote) =>
        {
            conflicts++;
            Check(remote.JsonData is not null, "Conflict must load current remote data.");
            return choice;
        });
        await sync.UploadAsync(local, default);
        Check(server.Saves.Count == 1 && index.Links.Single().Revision == 1 && server.JsonDataWasString, "Create must upload JSON as a string and retain revision.");
        Guid id = index.Links.Single().Id;
        store.Save(new("Test", 20), slot);
        await sync.UploadAsync(local, default);
        Check(server.Saves[id].Revision == 2, "Local edit must PUT its known revision.");
        server.Edit(id, 30);
        await sync.DownloadAsync(server.Saves[id], default);
        Check(store.Load().Single().Iterations == 30 && !File.Exists(slot.PreviewPath), "Remote-only edit must import and invalidate preview.");
        store.Save(new("Test", 40), slot);
        server.Edit(id, 50);
        await ExpectCloudAsync<OperationCanceledException>(() => sync.UploadAsync(local, default));
        Check(store.Load().Single().Iterations == 40 && server.Iterations(id) == 50 && conflicts == 1, "Cancel must retain both versions.");
        choice = CloudConflictChoice.KeepBoth;
        await sync.UploadAsync(local, default);
        Check(server.Saves.Count == 2 && store.Load().Select(s => s.Iterations).Order().SequenceEqual(new[] { 40, 50 }), "KeepBoth preserves local and cloud versions on disk and server.");
        Check(server.Saves.Values.Select(s => server.Iterations(s.Id)).Order().SequenceEqual(new[] { 40, 50 }), "KeepBoth uploads the alternate version.");
        store.Save(new("Test", 60), slot);
        server.RaceNextPut = true;
        choice = CloudConflictChoice.UseLocal;
        await sync.UploadAsync(local, default);
        Check(server.Iterations(id) == 60 && conflicts == 3, "A racing PUT 409 must fetch and ask before retry.");

        int creates = server.Creates;
        await sync.SynchronizeAllAsync(null, new Progress<string>(), default);
        Check(server.Creates == creates, "Repeated sync must not duplicate saves.");

        // A second computer has its own files and index, but accesses the same server account.
        using (var secondPc = DataSandbox.Create("cloud-pc2"))
        {
            var secondIndex = new CloudSyncIndex(client.Server, client.Email!);
            var secondSync = new CloudSyncService(client, secondIndex, (_, _) => throw new InvalidOperationException("Unexpected second-PC conflict."));
            await secondSync.SynchronizeAllAsync(null, new Progress<string>(), default);
            var secondStore = new FractalSaveStore<CloudTestState>("Mandelbrot", s => s.SaveName);
            Check(secondStore.Load().Count == 2 && !Directory.GetFiles(secondStore.DirectoryPath, "*.png").Any(), "New PC downloads all saves, without previews.");
            SaveSlot<CloudTestState> secondSlot = secondStore.LoadSlots().Slots.Single(s => s.State.SaveName == "Test");
            secondStore.Save(new("Test", 75), secondSlot);
            await secondSync.UploadAsync(CloudSaveRepository.ReadLocal(secondSlot.FilePath), default);
            Check(server.Iterations(id) == 75, "Second PC updates the same cloud id.");
        }
        sandbox.InstallRecycleBin();
        await sync.SynchronizeAllAsync(null, new Progress<string>(), default);
        Check(store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State.Iterations == 75,
            "First PC receives changes made on the second PC.");
        server.Saves.Remove(id);
        await sync.SynchronizeAllAsync(null, new Progress<string>(), default);
        Check(server.Creates == creates && File.Exists(slot.FilePath), "Remote deletion must not erase local data or be silently resurrected.");
        var otherIndex = new CloudSyncIndex(client.Server, "other@example.invalid");
        Check(otherIndex.Links.Count == 0, "Sync mappings must be account-scoped.");

        CloudSave remaining = server.Saves.Values.Single();
        string hostile = remaining.JsonData!.Replace("\"Mandelbrot\"", "\"../Settings\"");
        await ExpectCloudAsync<InvalidOperationException>(() => Task.FromResult(CloudSaveRepository.Import(remaining with { JsonData = hostile })));
        await ExpectCloudAsync<InvalidOperationException>(() => Task.FromResult(CloudSaveRepository.ResolvePath("../Settings/escape.json")));
        await ExpectCloudAsync<InvalidOperationException>(() => Task.FromResult(CloudSaveRepository.Decode(remaining with { JsonData = "{\"format\":\"OtherApp\"}" })));
        server.Edit(remaining.Id, 80);
        await ExpectCloudAsync<CloudApiException>(() => client.DeleteAsync(remaining.Id, remaining.Revision, default));
        Check(server.Saves.ContainsKey(remaining.Id), "Stale delete must preserve the cloud save.");
        CloudSave latest = await client.GetAsync(remaining.Id, default);
        await client.DeleteAsync(latest.Id, latest.Revision, default);
        Check(!server.Saves.ContainsKey(latest.Id), "DELETE uses the current revision and accepts 204.");

        // Real DPAPI in an isolated data root; no real credentials or account are used.
        var dpapi = new CloudCredentialStore("https://verification.invalid");
        dpapi.Write(new() { Email = "test@example.invalid", RefreshToken = "DPAPI-TEST-SECRET" });
        Check(dpapi.Read()?.RefreshToken == "DPAPI-TEST-SECRET", "DPAPI must round-trip for current Windows user.");
        string encryptedFile = Directory.GetFiles(AppPaths.SettingsDirectory, "*.dpapi").Single();
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(encryptedFile)).Contains("DPAPI-TEST-SECRET"), "Refresh token must not be plaintext on disk.");
        dpapi.Delete();
        Check(dpapi.Read() is null, "Logout deletes protected credential.");

        _ = new CloudConflictWindow(local, remaining);
        _ = new CloudLoginWindow(client);
        _ = new CloudSaveManagerWindow(connectOnLoad: false, client: client);
        await VerifyCloudTlsAsync();
        await client.LogoutAsync();
        Console.WriteLine("PASS cloud: rotation, concurrency, expiry, retry limit, DPAPI, JSON limits, sync, conflicts, deletions, account isolation, safe import, TLS, XAML.");
    }

    private static async Task ExpectCloudAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected cloud exception " + typeof(T).Name);
    }

    private sealed record CloudTestState(string SaveName, int Iterations);
    private sealed class MemoryCloudVault : ICloudCredentialStore
    {
        public CloudCredential? Value;
        public CloudCredential? Read() => Value;
        public void Write(CloudCredential credential) => Value = credential;
        public void Delete() => Value = null;
    }

    private sealed class FakeCloudServer : HttpMessageHandler
    {
        public int Refreshes, MaxRefreshesInFlight, UnauthorizedResponses, Creates;
        private int _inFlight;
        public bool FailRefresh, LastLoginHasPassword, JsonDataWasString, RaceNextPut;
        public int LoginLifetime = 3600;
        public HttpStatusCode? ForcedStatus;
        public Dictionary<Guid, CloudSave> Saves { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string path = request.RequestUri!.AbsolutePath;
            JsonObject? body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(token))!.AsObject();
            if (path == "/auth/login")
            {
                LastLoginHasPassword = body?["password"]?.GetValue<string>() == "test-password";
                return Json(new { tokenType = "Bearer", accessToken = "login-access", refreshToken = "login-refresh", expiresIn = LoginLifetime });
            }
            if (path == "/auth/refresh")
            {
                MaxRefreshesInFlight = Math.Max(MaxRefreshesInFlight, Interlocked.Increment(ref _inFlight));
                await Task.Delay(15, token);
                Interlocked.Decrement(ref _inFlight);
                Refreshes++;
                Check(body?["refreshToken"] is not null, "refresh contract");
                return FailRefresh ? new(HttpStatusCode.Unauthorized) : Json(new
                { tokenType = "Bearer", accessToken = "access-" + Refreshes, refreshToken = "refresh-" + Refreshes, expiresIn = 3600 });
            }
            Check(request.Headers.Authorization?.Scheme == "Bearer", "Every save request must carry bearer authorization.");
            if (ForcedStatus is { } forced) { ForcedStatus = null; return new(forced); }
            if (UnauthorizedResponses > 0) { UnauthorizedResponses--; return new(HttpStatusCode.Unauthorized); }
            if (path == "/api/saves" && request.Method == HttpMethod.Get)
                return Json(Saves.Values.Select(s => s with { JsonData = null }).ToList());
            if (path == "/api/saves" && request.Method == HttpMethod.Post)
            {
                JsonDataWasString = body!["jsonData"]!.GetValueKind() == JsonValueKind.String;
                Guid createdId = Guid.NewGuid();
                var created = new CloudSave(createdId, body["name"]!.GetValue<string>(), 1, DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow, body["jsonData"]!.GetValue<string>());
                Saves.Add(createdId, created);
                Creates++;
                return Json(created with { JsonData = null }, HttpStatusCode.Created);
            }
            Guid id = Guid.Parse(path.Split('/').Last());
            if (!Saves.TryGetValue(id, out CloudSave? current)) return new(HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Get) return Json(current);
            if (request.Method == HttpMethod.Put)
            {
                if (RaceNextPut) { RaceNextPut = false; Edit(id, 99); current = Saves[id]; }
                if (body!["revision"]!.GetValue<long>() != current.Revision) return new(HttpStatusCode.Conflict);
                current = current with { Name = body["name"]!.GetValue<string>(), JsonData = body["jsonData"]!.GetValue<string>(), Revision = current.Revision + 1 };
                Saves[id] = current;
                // The actual API's update reply omits CreatedAt and JsonData.
                return Json(new { current.Id, current.Name, current.Revision, current.UpdatedAt });
            }
            if (request.Method == HttpMethod.Delete)
            {
                if (request.RequestUri.Query != "?revision=" + current.Revision) return new(HttpStatusCode.Conflict);
                Saves.Remove(id);
                return new(HttpStatusCode.NoContent);
            }
            throw new InvalidOperationException("Unexpected fake API request.");
        }
        public int Iterations(Guid id) => JsonNode.Parse(Saves[id].JsonData!)!["state"]!["Iterations"]!.GetValue<int>();
        public void Edit(Guid id, int iterations)
        {
            CloudSave save = Saves[id];
            JsonObject payload = JsonNode.Parse(save.JsonData!)!.AsObject();
            payload["state"]!["Iterations"] = iterations;
            Saves[id] = save with { JsonData = payload.ToJsonString(), Revision = save.Revision + 1 };
        }
        private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
    }

    private static async Task VerifyCloudTlsAsync()
    {
        using RSA rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=Verification Root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using X509Certificate2 root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-5), DateTimeOffset.UtcNow.AddDays(5));
        using RSA leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=cloud.test", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("cloud.test");
        leafRequest.CertificateExtensions.Add(san.Build());
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var publicLeaf = leafRequest.Create(root, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(16));
        using var leaf = publicLeaf.CopyWithPrivateKey(leafKey);
        using var expiredPublic = leafRequest.Create(root, DateTimeOffset.UtcNow.AddDays(-3), DateTimeOffset.UtcNow.AddDays(-2), RandomNumberGenerator.GetBytes(16));
        using var expired = expiredPublic.CopyWithPrivateKey(leafKey);
        using RSA otherKey = RSA.Create(2048);
        var otherRequest = new CertificateRequest("CN=Other Root", otherKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        otherRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var otherRoot = otherRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        Check(await TryCloudTlsAsync(root, leaf, "cloud.test"), "TLS accepts selected root and matching name.");
        Check(!await TryCloudTlsAsync(root, leaf, "wrong.test"), "TLS rejects mismatched name.");
        Check(!await TryCloudTlsAsync(otherRoot, leaf, "cloud.test"), "TLS rejects unknown root.");
        Check(!await TryCloudTlsAsync(root, expired, "cloud.test"), "TLS rejects expired certificate.");
    }

    private static async Task<bool> TryCloudTlsAsync(X509Certificate2 root, X509Certificate2 leaf, string hostname)
    {
        // Schannel cannot use the ephemeral key returned by CopyWithPrivateKey.
        // DefaultKeySet uses a temporary key container cleaned up on certificate disposal.
        using var serverCertificate = X509CertificateLoader.LoadPkcs12(leaf.Export(X509ContentType.Pfx), null);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        Task serve = Task.Run(async () =>
        {
            try
            {
                using TcpClient peer = await listener.AcceptTcpClientAsync(timeout.Token);
                using var tls = new SslStream(peer.GetStream());
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = serverCertificate }, timeout.Token);
                using var reader = new StreamReader(tls, Encoding.ASCII, leaveOpen: true);
                while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 }) { }
                await tls.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK"), timeout.Token);
            }
            catch (Exception e) when (e is IOException or System.Security.Authentication.AuthenticationException or OperationCanceledException)
            { }
        });
        using SocketsHttpHandler handler = CloudConnection.CreateHandler(root);
        handler.UseProxy = false;
        handler.ConnectCallback = async (_, token) =>
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(endpoint, token);
            return new NetworkStream(socket, ownsSocket: true);
        };
        using var http = new HttpClient(handler);
        bool success;
        try { using var response = await http.GetAsync("https://" + hostname, timeout.Token); success = response.IsSuccessStatusCode; }
        catch (HttpRequestException) { success = false; }
        await serve;
        return success;
    }

    private static async Task VerifyCloudLiveAsync()
    {
        // Public GET only. Never reads a credential store or writes to the VPS.
        using HttpClient http = CloudConnection.Load().CreateHttpClient();
        using var root = await http.GetAsync("/");
        Check(root.IsSuccessStatusCode && (await root.Content.ReadAsStringAsync()).Contains("FractalCloud"), "VPS HTTPS must validate with embedded root.");
        using var saves = await http.GetAsync("/api/saves");
        Check(saves.StatusCode == HttpStatusCode.Unauthorized, "Save listing must require authentication.");
        Console.WriteLine("PASS cloud-live: real VPS certificate validated; anonymous saves request rejected with 401.");
    }
}
