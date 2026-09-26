using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace GymAppApi.WebApi.Security;

// "ForwardedHeaders" yapılandırma bölümü:
//   "ForwardedHeaders": { "KnownProxies": ["10.0.0.5"], "KnownNetworks": ["10.0.0.0/8"], "ForwardLimit": 1 }
// ForwardLimit, X-Forwarded-For'un sağından kaç girişin işleneceğidir: tek
// proxy için 1 (varsayılan); CDN/LB -> iç proxy -> API gibi iki katmanda 2
// (ve iki proxy de güven listesinde olmalı).
public class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    public string[] KnownProxies { get; set; } = Array.Empty<string>();
    public string[] KnownNetworks { get; set; } = Array.Empty<string>();
    public int ForwardLimit { get; set; } = 1;

    public IEnumerable<string> Proxies => KnownProxies.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim());
    public IEnumerable<string> Networks => KnownNetworks.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim());
    public bool HasTrustedProxies => Proxies.Any() || Networks.Any();
}

// Ters proxy / yük dengeleyici arkasında gerçek istemci IP'si (rate limit ve
// hesap+IP giriş kilidi bunu kullanır) X-Forwarded-For'dan alınır - AMA sadece
// yapılandırmada açıkça güvenilen proxy'lerden gelen isteklerde. Liste boşsa
// X-Forwarded-For'a HİÇ güvenilmez (herkes sahte başlıkla kendi IP'sini
// seçebilirdi) ve Development dışında açılışta uyarı loglanır. Hatalı girdi
// ilk istekte değil açılışta uygulamayı durdurur.
public static class ForwardedHeadersSetup
{
    public static IServiceCollection AddConfiguredForwardedHeaders(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersSettings>()
            .BindConfiguration(ForwardedHeadersSettings.SectionName)
            .Validate(s => s.Proxies.All(p => IPAddress.TryParse(p, out _)),
                "ForwardedHeaders:KnownProxies contains an invalid IP address.")
            .Validate(s => s.Networks.All(n => System.Net.IPNetwork.TryParse(n, out _)),
                "ForwardedHeaders:KnownNetworks contains an invalid CIDR network (e.g. 10.0.0.0/8).")
            .Validate(s => s.ForwardLimit >= 1,
                "ForwardedHeaders:ForwardLimit must be at least 1.")
            .ValidateOnStart();

        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<ForwardedHeadersSettings>>((options, settings) =>
        {
            var value = settings.Value;
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = value.ForwardLimit;
            // Varsayılan (loopback) güven listesi temizlenir - sadece yapılandırılanlar.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in value.Proxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }
            foreach (var network in value.Networks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });
        return services;
    }

    // Pipeline'ın EN BAŞINDA çağrılmalı (rate limiter ve auth'tan önce).
    // Ayarları burada okumak doğrulamayı da tetikler: hatalı liste açılışı durdurur.
    public static WebApplication UseConfiguredForwardedHeaders(this WebApplication app)
    {
        var settings = app.Services.GetRequiredService<IOptions<ForwardedHeadersSettings>>().Value;
        if (!settings.HasTrustedProxies)
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
}
