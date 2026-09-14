using GymAppApi.Domain.Common;
using GymAppApi.Domain.Entities;
using Xunit;

namespace GymAppApi.UnitTests.Domain;

public class BranchTests
{
    [Fact]
    public void Branch_IsCompanyScopedAndDeactivatable()
    {
        var branch = new Branch { CompanyId = 1, Name = "Merkez Şube", Address = "..." };

        Assert.IsAssignableFrom<ICompanyScoped>(branch);
        Assert.IsAssignableFrom<IDeactivatable>(branch);
        Assert.True(branch.IsActive); // default must be active
    }
}
