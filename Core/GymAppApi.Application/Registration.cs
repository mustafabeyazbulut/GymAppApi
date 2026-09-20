// Core/GymAppApi.Application/Registration.cs
using System.Reflection;
using FluentValidation;
using GymAppApi.Application.Common.Behaviors;
using GymAppApi.Application.Common.Reminders;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace GymAppApi.Application;

public static class Registration
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        services.AddTransient<GymAppApi.Application.Features.Branches.Rules.BranchRules>();

        services.AddScoped<IMembershipExpiryReminderService, MembershipExpiryReminderService>();

        return services;
    }
}
