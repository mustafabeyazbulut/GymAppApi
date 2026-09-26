using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace GymAppApi.WebApi.Security;

// Ters proxy / yük dengeleyici arkasında gerçek istemci IP'si (rate limit ve
// hesap+IP giriş kilidi bunu kullanır) X-Forwarded-For'dan alınır - AMA sadece
// yapılandırmada açıkça güvenilen proxy'lerden gelen isteklerde:
//   "ForwardedHeaders": { "KnownProxies": ["10.0.0.5"], "KnownNetworks": ["10.0.0.0/8"] }
// Liste boşsa X-Forwarded-For'a HİÇ güvenilmez (herkes sahte başlıkla kendi
// IP'sini seçebilirdi) ve Development dışında açılışta uyarı loglanır.
public static class ForwardedHeadersSetup
{
    private const string KnownProxiesKey = "ForwardedHeaders:KnownProxies";
    private const string KnownNetworksKey = "ForwardedHeaders:KnownNetworks";

    public static IServiceCollection AddConfiguredForwardedHeaders(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Varsayılan (loopback) güven listesi temizlenir - sadece yapılandırılanlar.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in ReadList(configuration, KnownProxiesKey))
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }
            foreach (var network in ReadList(configuration, KnownNetworksKey))
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });
        return services;
    }

    // Pipeline'ın EN BAŞINDA çağrılmalı (rate limiter ve auth'tan önce).
    public static WebApplication UseConfiguredForwardedHeaders(this WebApplication app)
    {
        var hasTrustedProxies = ReadList(app.Configuration, KnownProxiesKey).Length > 0
                                || ReadList(app.Configuration, KnownNetworksKey).Length > 0;
        if (!hasTrustedProxies)
        {
            if (!app.Environment.IsDevelopment())
            {
                app.Logger.LogWarning(
                    "ForwardedHeaders:KnownProxies/KnownNetworks yapılandırılmamış - X-Forwarded-For yok sayılıyor. " +
                    "Ters proxy arkasındaysanız KnownProxies ayarlayın; aksi hâlde rate limit ve giriş kilidi tüm istemcileri proxy IP'sinde toplar.");
            }
            return app;
        }

        app.UseForwardedHeaders();
        return app;
    }

    private static string[] ReadList(IConfiguration configuration, string key) =>
        configuration.GetSection(key).Get<string[]>()?.Where(v => !string.IsNullOrWhiteSpace(v)).ToArray() ?? Array.Empty<string>();
}
