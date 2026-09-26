using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using GymAppApi.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymAppApi.IntegrationTests;

// Uygulama global: kullanıcı her ülkeden E.164 numarayla kayıt olup giriş
// yapabilmeli; telefon DB'de her zaman kanonik E.164 saklanmalı ve aynı
// numaranın farklı yazımları (boşluklu, "00" önekli) aynı hesaba eşleşmeli.
// Not: "auth" rate limiter'ı (dakikada 10 istek) bu sınıftaki tüm testlerce
// paylaşılıyor - toplam auth isteği sayısı bunun altında tutuluyor.
public class InternationalPhoneFlowTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "Sifre123!";

    private readonly CustomWebApplicationFactory _factory;

    public InternationalPhoneFlowTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<string> ReadPendingPhoneCodeAsync(string canonicalPhone)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var pending = await db.PendingContactVerifications.SingleAsync(p => p.Channel == ContactChannel.Phone && p.Target == canonicalPhone);
        return pending.Code;
    }

    private async Task<string?> ReadStoredUserPhoneAsync(string canonicalPhone)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AmbientTenantContext>().IsSuperAdmin = true;
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        return (await db.Users.SingleOrDefaultAsync(u => u.Phone == canonicalPhone))?.Phone;
    }

    [Fact]
    public async Task GermanNumber_RegisterWithFormattedInput_StoresE164_AndCanLoginWithAnyWriting()
    {
        var client = _factory.CreateClient();
        const string typedPhone = "+49 151 234-56789";
        const string canonicalPhone = "+4915123456789";

        var requestOtp = await client.PostAsJsonAsync("/api/auth/register/request-otp", new { phone = typedPhone, email = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, requestOtp.StatusCode);

        var code = await ReadPendingPhoneCodeAsync(canonicalPhone);
        var complete = await client.PostAsJsonAsync("/api/auth/register/complete", new
        {
            fullName = "Hans Müller", phone = typedPhone, phoneCode = code, email = (string?)null, emailCode = (string?)null, password = Password,
        });
        Assert.Equal(HttpStatusCode.Created, complete.StatusCode);
        Assert.Equal(canonicalPhone, await ReadStoredUserPhoneAsync(canonicalPhone));

        var loginWithE164 = await client.PostAsJsonAsync("/api/auth/login", new { identifier = canonicalPhone, password = Password });
        Assert.Equal(HttpStatusCode.OK, loginWithE164.StatusCode);
        var tokens = await loginWithE164.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(tokens.GetProperty("accessToken").GetString()));

        var loginWithInternationalPrefix = await client.PostAsJsonAsync("/api/auth/login", new { identifier = "0049 151 23456789", password = Password });
        Assert.Equal(HttpStatusCode.OK, loginWithInternationalPrefix.StatusCode);
    }

    [Fact]
    public async Task TurkishLocalFormat_Register_IsStoredAsPlus90E164()
    {
        var client = _factory.CreateClient();
        const string typedPhone = "0532 111 22 33";
        const string canonicalPhone = "+905321112233";

        var requestOtp = await client.PostAsJsonAsync("/api/auth/register/request-otp", new { phone = typedPhone, email = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, requestOtp.StatusCode);

        var code = await ReadPendingPhoneCodeAsync(canonicalPhone);
        var complete = await client.PostAsJsonAsync("/api/auth/register/complete", new
        {
            fullName = "Ayşe Yılmaz", phone = typedPhone, phoneCode = code, email = (string?)null, emailCode = (string?)null, password = Password,
        });
        Assert.Equal(HttpStatusCode.Created, complete.StatusCode);
        Assert.Equal(canonicalPhone, await ReadStoredUserPhoneAsync(canonicalPhone));
    }

    [Fact]
    public async Task InvalidNumber_RegisterRequestOtp_Returns422WithLocalizedMessage()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("tr"));

        var response = await client.PostAsJsonAsync("/api/auth/register/request-otp", new { phone = "+90555123", email = (string?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var messages = body.RootElement.GetProperty("Errors").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains(messages, m => m!.Contains("geçerli bir telefon numarası"));
    }
}
