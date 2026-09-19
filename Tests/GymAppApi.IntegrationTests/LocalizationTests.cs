using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymAppApi.Domain.Entities;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;

namespace GymAppApi.IntegrationTests;

// Backend'in gerçekten mobilin gönderdiği Accept-Language'a göre farklı
// dilde metin döndürdüğünü uçtan uca doğrular - AppMessages/
// ExceptionMiddleware/Program.cs'teki UseRequestLocalization'ın hepsinin
// birlikte doğru çalıştığının tek gerçek kanıtı budur, birim testleri
// bunu tek başına gösteremez (RequestLocalizationMiddleware gerçek bir
// HTTP isteği olmadan devreye girmez).
public class LocalizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public LocalizationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<string> SeedAuthenticatedUserTokenAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var user = new User { FullName = "Test User", Phone = "+905559998877", PasswordHash = "x" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<GymAppApi.Application.Common.Interfaces.IJwtTokenService>();
        return jwtService.GenerateAccessToken(new GymAppApi.Application.Common.Interfaces.AccessTokenClaims(user.Id, user.FullName, user.Email, user.Phone)).Token;
    }

    [Fact]
    public async Task NotFound_WithoutAcceptLanguage_ReturnsEnglishByDefault()
    {
        var token = await SeedAuthenticatedUserTokenAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/branches/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("BranchNotFound", body.RootElement.GetProperty("Code").GetString());
        var message = body.RootElement.GetProperty("Errors")[0].GetString();
        Assert.Contains("not found", message);
    }

    [Fact]
    public async Task NotFound_WithTurkishAcceptLanguage_ReturnsTurkishText()
    {
        var token = await SeedAuthenticatedUserTokenAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("tr"));

        var response = await client.GetAsync("/api/branches/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("BranchNotFound", body.RootElement.GetProperty("Code").GetString());
        var message = body.RootElement.GetProperty("Errors")[0].GetString();
        Assert.Contains("bulunamadı", message);
    }

    [Fact]
    public async Task ValidationError_WithTurkishAcceptLanguage_ReturnsTurkishText()
    {
        // FluentValidation'ın .WithMessage(Func<T,string>) ile hesaplanan
        // mesajı, ExceptionMiddleware'in kendisininki gibi ayrı bir
        // ExecutionContext dalında değil, doğrudan RequestLocalizationMiddleware'in
        // İÇİNDE (validator, controller action'ın parçası olarak orada
        // çalışıyor) hesaplandığı için ayrı bir doğrulama gerekiyor -
        // NotFoundException testleri bu senaryoyu kapsamıyor.
        var token = await SeedAuthenticatedUserTokenAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("tr"));

        var response = await client.PatchAsJsonAsync("/api/auth/me/language", new { language = "fr" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var message = body.RootElement.GetProperty("Errors")[0].GetString();
        Assert.Contains("olmalıdır", message);
    }

    // Bu test, doğrudan yerelleştirmeyle ilgili değil - ama bu dosyayı
    // yazarken bulunan ÇOK DAHA CİDDİ bir hatayı (aşağıya bakın) kalıcı
    // olarak kilitliyor, bu yüzden burada duruyor.
    //
    // MediatR 12+ ile "where TRequest : IRequest<TResponse>" kısıtı, SADECE
    // IRequest<T> (sonuç dönen) komutlar için çalışıyor - IRequest (void)
    // komutlar için ValidationBehavior/TransactionBehavior DI'dan HİÇ
    // resolve edilmiyordu, yani FluentValidation SESSİZCE hiç çalışmıyordu.
    // Bu, ResetPassword/DeleteMe/FreezeAccount/UnfreezeAccount/
    // UpdatePreferredLanguage/UpdateBranch/UpdateCompanyName/
    // RegisterDeviceToken dahil HER void komutu etkiliyordu - ör. şifre
    // sıfırlama en az 8 karakter kuralını hiç uygulamıyordu. Düzeltme:
    // kısıtı "where TRequest : notnull" yap (bkz. ValidationBehavior.cs).
    [Fact]
    public async Task ResetPassword_WithTooShortPassword_Returns422()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/reset-password", new
        {
            identifier = "+905559998877",
            code = "123456",
            newPassword = "short",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
