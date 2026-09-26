using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using GymAppApi.Persistence.Context;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GymAppApi.IntegrationTests;

// Diske yazmayan medya deposu - yüklenen içerik GET /api/media/{id} ile geri okunabilir.
public class InMemoryMediaStorage : IMediaStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new();

    public async Task<string> SaveAsync(Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var key = $"mem/{Guid.NewGuid()}";
        _files[key] = buffer.ToArray();
        return key;
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new MemoryStream(_files.TryGetValue(storagePath, out var bytes) ? bytes : Array.Empty<byte>()));
}

public class InMemoryMediaWebApplicationFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMediaStorage>();
            services.AddSingleton<IMediaStorage, InMemoryMediaStorage>();
        });
    }
}

// Genel (platform) içerik: CompanyId = null. Giriş yapmış herkese görünür ve
// indirilebilir (paket gerekmez); sadece aktif bağlamı Sistem Sahibi olan
// (header'sız SuperAdmin) kullanıcı yükler ve aktif/pasif yapar. Gym içeriği
// kuralları değişmez.
public class PlatformContentTests : IClassFixture<InMemoryMediaWebApplicationFactory>
{
    private readonly InMemoryMediaWebApplicationFactory _factory;

    public PlatformContentTests(InMemoryMediaWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055519{Random.Shared.Next(10000, 99999)}";

    private sealed record Seed(
        int CompanyId, int BranchId, int GymContentId, int GymMediaFileId, int SuperAdminGymAdminAssignmentId,
        string SuperAdminToken, string SuperAdminWithGymRoleToken, string GymAdminToken, string MemberToken);

    private async Task<Seed> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var company = new Company { Name = "Firma", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var branch = new Branch { CompanyId = company.Id, Name = "Merkez", Address = "..." };
        db.Branches.Add(branch);

        var superAdmin = new User { FullName = "Sistem Sahibi", Phone = UniquePhone(), PasswordHash = "x" };
        var superAdminWithGymRole = new User { FullName = "SA + Gym Admin", Phone = UniquePhone(), PasswordHash = "x" };
        var gymAdmin = new User { FullName = "Gym Admin", Phone = UniquePhone(), PasswordHash = "x" };
        var member = new User { FullName = "Paketsiz Üye", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.AddRange(superAdmin, superAdminWithGymRole, gymAdmin, member);
        await db.SaveChangesAsync();

        var saGymAdminAssignment = new Assignment { UserId = superAdminWithGymRole.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true };
        db.Assignments.AddRange(
            new Assignment { UserId = superAdmin.Id, Role = AssignmentRole.SuperAdmin, IsActive = true },
            new Assignment { UserId = superAdminWithGymRole.Id, Role = AssignmentRole.SuperAdmin, IsActive = true },
            saGymAdminAssignment,
            new Assignment { UserId = gymAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true });

        var gymContent = new ContentItem
        {
            CompanyId = company.Id, Title = "Gym içeriği", RequiredAccessTier = PackageAccessTier.Standard,
            MediaFile = new MediaFile { StoragePath = "mem/yok", ContentType = "video/mp4", SizeBytes = 1, UploadedByUserId = gymAdmin.Id },
            CreatedByUserId = gymAdmin.Id,
        };
        db.ContentItems.Add(gymContent);
        await db.SaveChangesAsync();

        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        string Token(User u) => jwt.GenerateAccessToken(new AccessTokenClaims(u.Id, u.FullName, u.Email, u.Phone)).Token;
        return new Seed(company.Id, branch.Id, gymContent.Id, gymContent.MediaFileId, saGymAdminAssignment.Id,
            Token(superAdmin), Token(superAdminWithGymRole), Token(gymAdmin), Token(member));
    }

    private HttpClient ClientFor(string token, int? activeAssignmentId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (activeAssignmentId is not null)
        {
            client.DefaultRequestHeaders.Add("X-Active-Assignment-Id", activeAssignmentId.ToString());
        }
        return client;
    }

    private static MultipartFormDataContent Upload(string title, int? branchId = null, string body = "platform-video")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(title), "title" },
            { new StringContent("Standard"), "requiredAccessTier" },
        };
        if (branchId is not null)
        {
            form.Add(new StringContent(branchId.Value.ToString()), "branchId");
        }
        var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(body));
        file.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        form.Add(file, "file", "video.mp4");
        return form;
    }

    private async Task<JsonElement> UploadPlatformAsync(Seed seed, string title)
    {
        var response = await ClientFor(seed.SuperAdminToken).PostAsync("/api/content-items", Upload(title));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<List<JsonElement>> ListAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/content-items");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
    }

    [Fact]
    public async Task SuperAdminWithoutHeader_Uploads_AsPlatformContentWithNoCompany()
    {
        var seed = await SeedAsync();

        var created = await UploadPlatformAsync(seed, "Isınma rehberi");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var item = await db.ContentItems.IgnoreQueryFilters().SingleAsync(c => c.Id == created.GetProperty("id").GetInt32());
        Assert.Null(item.CompanyId);
        Assert.Null(item.BranchId);
    }

    [Fact]
    public async Task Member_Upload_Returns403()
    {
        var seed = await SeedAsync();

        var response = await ClientFor(seed.MemberToken).PostAsync("/api/content-items", Upload("izinsiz"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdminActingAsGymAdmin_UploadsGymContent_NotPlatformContent()
    {
        var seed = await SeedAsync();

        var response = await ClientFor(seed.SuperAdminWithGymRoleToken, seed.SuperAdminGymAdminAssignmentId)
            .PostAsync("/api/content-items", Upload("firma içeriği"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        Assert.Equal(seed.CompanyId, (await db.ContentItems.IgnoreQueryFilters().SingleAsync(c => c.Id == id)).CompanyId);
    }

    [Fact]
    public async Task MemberWithoutPackage_SeesPlatformContent_ButNotGymContent()
    {
        var seed = await SeedAsync();
        var platformId = (await UploadPlatformAsync(seed, "Esneme")).GetProperty("id").GetInt32();

        var items = await ListAsync(ClientFor(seed.MemberToken));

        var platform = Assert.Single(items, i => i.GetProperty("id").GetInt32() == platformId);
        Assert.Equal("Platform", platform.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, platform.GetProperty("companyId").ValueKind);
        Assert.True(platform.GetProperty("hasAccess").GetBoolean());
        Assert.DoesNotContain(items, i => i.GetProperty("id").GetInt32() == seed.GymContentId);
    }

    [Fact]
    public async Task Staff_SeeGymAndPlatformContent_InOneList_WithSource()
    {
        var seed = await SeedAsync();
        var platformId = (await UploadPlatformAsync(seed, "Kardiyo")).GetProperty("id").GetInt32();

        var items = await ListAsync(ClientFor(seed.GymAdminToken));

        Assert.Equal("Platform", items.Single(i => i.GetProperty("id").GetInt32() == platformId).GetProperty("source").GetString());
        Assert.Equal("Gym", items.Single(i => i.GetProperty("id").GetInt32() == seed.GymContentId).GetProperty("source").GetString());
    }

    [Fact]
    public async Task PlatformMedia_IsDownloadableByAnyLoggedInUser()
    {
        var seed = await SeedAsync();
        var created = await UploadPlatformAsync(seed, "İndirilebilir");

        var response = await ClientFor(seed.MemberToken).GetAsync($"/api/media/{created.GetProperty("mediaFileId").GetInt32()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("platform-video", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GymMedia_StillRequiresAPackage()
    {
        var seed = await SeedAsync();

        var response = await ClientFor(seed.MemberToken).GetAsync($"/api/media/{seed.GymMediaFileId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CanDeactivateAndReactivatePlatformContent()
    {
        var seed = await SeedAsync();
        var id = (await UploadPlatformAsync(seed, "Dönemlik")).GetProperty("id").GetInt32();
        var superAdmin = ClientFor(seed.SuperAdminToken);

        var deactivate = await superAdmin.PatchAsJsonAsync($"/api/content-items/{id}/active", new { isActive = false });
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        Assert.DoesNotContain(await ListAsync(ClientFor(seed.MemberToken)), i => i.GetProperty("id").GetInt32() == id);
        var hidden = (await ListAsync(superAdmin)).Single(i => i.GetProperty("id").GetInt32() == id);
        Assert.False(hidden.GetProperty("isActive").GetBoolean());

        var reactivate = await superAdmin.PatchAsJsonAsync($"/api/content-items/{id}/active", new { isActive = true });
        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);
        Assert.Contains(await ListAsync(ClientFor(seed.MemberToken)), i => i.GetProperty("id").GetInt32() == id);
    }

    [Fact]
    public async Task GymAdmin_CannotDeactivatePlatformContent()
    {
        var seed = await SeedAsync();
        var id = (await UploadPlatformAsync(seed, "Korumalı")).GetProperty("id").GetInt32();

        var response = await ClientFor(seed.GymAdminToken).PatchAsJsonAsync($"/api/content-items/{id}/active", new { isActive = false });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CannotToggleGymContent()
    {
        var seed = await SeedAsync();

        var response = await ClientFor(seed.SuperAdminToken).PatchAsJsonAsync($"/api/content-items/{seed.GymContentId}/active", new { isActive = false });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SaveChanges_DoesNotStampTheAmbientCompanyOntoPlatformContent()
    {
        var options = new DbContextOptionsBuilder<GymAppApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new GymAppApiDbContext(options, new AmbientTenantContext { CompanyId = 5 });
        var media = new MediaFile { StoragePath = "x", ContentType = "video/mp4", SizeBytes = 1, UploadedByUserId = 1 };
        db.MediaFiles.Add(media);
        await db.SaveChangesAsync();

        var item = new ContentItem { CompanyId = null, Title = "Platform", RequiredAccessTier = PackageAccessTier.Standard, MediaFileId = media.Id, CreatedByUserId = 1 };
        db.ContentItems.Add(item);
        await db.SaveChangesAsync();

        Assert.Null(item.CompanyId);
    }
}
