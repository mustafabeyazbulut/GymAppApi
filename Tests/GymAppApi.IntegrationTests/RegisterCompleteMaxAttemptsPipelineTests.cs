using System.Net;
using System.Net.Http.Json;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

// This test goes through the REAL ASP.NET Core HTTP pipeline (real IMediator,
// real MediatR pipeline behaviors including TransactionBehavior), unlike
// every other RegisterComplete test in this codebase, which constructs the
// handler directly and therefore bypasses TransactionBehavior entirely.
// RegisterCompleteCommand deliberately does NOT implement ITransactionalRequest
// (see the plan's grounding facts) - if that were mistakenly reintroduced,
// each failed attempt below would have its AttemptCount increment rolled back
// by the pipeline's ambient transaction, and this test would fail (the 6th
// attempt, with the correct code, would wrongly succeed instead of still
// being rejected for exceeding the attempt cap).
public class RegisterCompleteMaxAttemptsPipelineTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public RegisterCompleteMaxAttemptsPipelineTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Complete_AfterFiveWrongCodesThroughRealPipeline_RejectsSixthEvenWithCorrectCode()
    {
        var client = _factory.CreateClient();
        const string phone = "+905551237777";

        var requestOtpResponse = await client.PostAsJsonAsync("/api/auth/register/request-otp", new { phone, email = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, requestOtpResponse.StatusCode);

        string correctCode;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
            var pending = await db.PendingContactVerifications.SingleAsync(p => p.Channel == ContactChannel.Phone && p.Target == phone);
            correctCode = pending.Code;
        }

        for (var i = 0; i < 5; i++)
        {
            var wrongAttempt = await client.PostAsJsonAsync("/api/auth/register/complete", new
            {
                fullName = "Test Kullanici", phone, phoneCode = "000000", email = (string?)null, emailCode = (string?)null, password = "Sifre123!",
            });
            Assert.Equal(HttpStatusCode.Unauthorized, wrongAttempt.StatusCode);
        }

        // 6th attempt, now WITH the correct code - should STILL be rejected
        // (max attempts already reached), proving all 5 prior AttemptCount
        // increments genuinely persisted across 5 separate real
        // MediatR-pipeline requests, not just in a single in-process call.
        var sixthAttempt = await client.PostAsJsonAsync("/api/auth/register/complete", new
        {
            fullName = "Test Kullanici", phone, phoneCode = correctCode, email = (string?)null, emailCode = (string?)null, password = "Sifre123!",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, sixthAttempt.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
            var userExists = await db.Users.AnyAsync(u => u.Phone == phone);
            Assert.False(userExists);
        }
    }
}
