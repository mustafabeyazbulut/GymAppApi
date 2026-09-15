using GymAppApi.Infrastructure.Security;

namespace GymAppApi.UnitTests.Security;

public class PasswordHasherAdapterTests
{
    [Fact]
    public void Hash_ThenVerify_WithCorrectInput_Succeeds()
    {
        var hasher = new PasswordHasherAdapter();
        var hash = hasher.Hash("Sifre123!");

        Assert.True(hasher.Verify(hash, "Sifre123!"));
    }

    [Fact]
    public void Verify_WithWrongInput_Fails()
    {
        var hasher = new PasswordHasherAdapter();
        var hash = hasher.Hash("Sifre123!");

        Assert.False(hasher.Verify(hash, "YanlisSifre"));
    }
}
