using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.PersonalLogs.Queries.GetPersonalLogs;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.UnitTests.TestHelpers;
using Moq;

namespace GymAppApi.UnitTests.Features.PersonalLogs;

public class GetPersonalLogsQueryHandlerTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static GetPersonalLogsQueryHandler Handler(params PersonalLog[] logs)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PersonalLog>()).Returns(FakeReadRepository.For(logs).Object);
        return new GetPersonalLogsQueryHandler(uow.Object);
    }

    [Theory]
    [InlineData(365)]
    [InlineData(366)]
    public async Task RangeUpTo366Days_IsAccepted(int days)
    {
        var result = await Handler().Handle(new GetPersonalLogsQuery { UserId = 1, From = Today.AddDays(-days), To = Today }, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task RangeOver366Days_IsABadRequest()
    {
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            Handler().Handle(new GetPersonalLogsQuery { UserId = 1, From = Today.AddDays(-367), To = Today }, CancellationToken.None));
        Assert.Equal("PersonalLogRangeTooLong", ex.Code);
    }

    [Fact]
    public async Task FromAfterTo_IsABadRequest()
    {
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            Handler().Handle(new GetPersonalLogsQuery { UserId = 1, From = Today, To = Today.AddDays(-1) }, CancellationToken.None));
        Assert.Equal("PersonalLogRangeInvalid", ex.Code);
    }

    [Fact]
    public async Task WithoutRange_DefaultsToTheLastYear_OwnLogsOnly()
    {
        var handler = Handler(
            new PersonalLog { Id = 1, UserId = 1, Date = Today, Kind = PersonalLogKind.Workout, Title = "bugün" },
            new PersonalLog { Id = 2, UserId = 1, Date = Today.AddDays(-400), Kind = PersonalLogKind.Workout, Title = "çok eski" },
            new PersonalLog { Id = 3, UserId = 2, Date = Today, Kind = PersonalLogKind.Workout, Title = "başkası" });

        var result = await handler.Handle(new GetPersonalLogsQuery { UserId = 1 }, CancellationToken.None);

        Assert.Equal("bugün", Assert.Single(result).Title);
    }
}
