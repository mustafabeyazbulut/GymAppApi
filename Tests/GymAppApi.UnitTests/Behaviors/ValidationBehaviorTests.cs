using FluentValidation;
using FluentValidation.Results;
using GymAppApi.Application.Common.Behaviors;
using MediatR;
using Moq;
using Xunit;

namespace GymAppApi.UnitTests.Behaviors;

public class ValidationBehaviorTests
{
    public record Ping(string Name) : IRequest<string>;

    [Fact]
    public async Task Handle_WhenValidatorFails_ThrowsValidationException()
    {
        var validator = new Mock<IValidator<Ping>>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<Ping>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("Name", "Name is required") }));

        var behavior = new ValidationBehavior<Ping, string>(new[] { validator.Object });

        await Assert.ThrowsAsync<ValidationException>(() =>
            behavior.Handle(new Ping(""), _ => Task.FromResult("unused"), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenNoValidators_CallsNext()
    {
        var behavior = new ValidationBehavior<Ping, string>(Array.Empty<IValidator<Ping>>());

        var result = await behavior.Handle(new Ping("ok"), _ => Task.FromResult("next-called"), CancellationToken.None);

        Assert.Equal("next-called", result);
    }
}
