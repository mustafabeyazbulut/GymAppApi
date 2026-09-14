using GymAppApi.Domain.Common;
using Xunit;

namespace GymAppApi.UnitTests.Domain;

public class EntityBaseTests
{
    private class TestEntity : EntityBase { }

    [Fact]
    public void NewEntity_HasZeroId_AndNoTimestampsSet()
    {
        var entity = new TestEntity();

        Assert.Equal(0, entity.Id);
        Assert.Equal(default, entity.CreatedAt);
        Assert.Null(entity.UpdatedAt);
    }
}
