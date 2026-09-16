using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace FractalExplorerWPF.Infrastructure.Cloud;

public sealed class CloudConnection
{
    public string Server { get; set; } = "";
    public bool UseCaddyRoot { get; set; } = true;

    public static CloudConnection Load()
    {
        string path = AppPaths.GetSettingsFile("cloud-connection.json");
        if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "cloud-connection.local.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize<CloudConnection>(File.ReadAllText(path)) ?? new()
            : new();
    }

    public Uri GetServerUri()
    {
        if (string.IsNullOrWhiteSpace(Server))
            throw new InvalidOperationException("Укажите адрес FractalCloud в Settings/cloud-connection.json в каталоге данных приложения или в cloud-connection.local.json рядом с приложением.");
        if (!Uri.TryCreate(Server, UriKind.Absolute, out Uri? uri) || uri.Scheme != "https" ||
            uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("Адрес облака должен иметь вид https://сервер:порт без пути и пароля.");
        return uri;
    }

    internal HttpClient CreateHttpClient()
    {
        X509Certificate2? root = null;
        if (UseCaddyRoot)
        {
            using Stream stream = typeof(CloudConnection).Assembly.GetManifestResourceStream(
                "FractalExplorerWPF.Assets.Certificates.fractalcloud-caddy-root.crt")
                ?? throw new InvalidOperationException("Отсутствует корневой сертификат FractalCloud.");
            using var reader = new StreamReader(stream);
            root = X509Certificate2.CreateFromPem(reader.ReadToEnd());
        }
        return new HttpClient(CreateHandler(root)) { BaseAddress = GetServerUri(), Timeout = TimeSpan.FromSeconds(60) };
    }

    internal static SocketsHttpHandler CreateHandler(X509Certificate2? root)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        if (root is not null)
        {
            var policy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                VerificationFlags = X509VerificationFlags.NoFlag,
                // Caddy's private CA does not publish CRL/OCSP. Chain, name, EKU and dates remain checked.
                RevocationMode = X509RevocationMode.NoCheck
            };
            policy.CustomTrustStore.Add(root);
            policy.ApplicationPolicy.Add(new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1"));
            handler.SslOptions = new SslClientAuthenticationOptions { CertificateChainPolicy = policy };
        }
        // No certificate callback: SslStream performs hostname and full chain validation.
        return handler;
    }
}
