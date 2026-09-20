using FluentValidation;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Application.Features.DoorAccess.Commands.CreateZoneAccessRule;

public class CreateZoneAccessRuleCommandValidator : AbstractValidator<CreateZoneAccessRuleCommand>
{
    public CreateZoneAccessRuleCommandValidator()
    {
        RuleFor(x => x.ZoneId).GreaterThan(0);

        // RuleType=AllActiveMembers'ta RuleValue anlamsız (herkese açık kural),
        // diğer tüm tiplerde bir değer olmadan kural neyi filtreleyeceğini
        // bilemez. İKİ AYRI RuleFor - aynı RuleFor zincirinde art arda
        // .When() çağırmak FluentValidation'da varsayılan olarak
        // ApplyConditionTo.AllValidators davranışına sahip, yani ikinci
        // .When() ZİNCİRDEKİ İLK KURALI da etkileyip onu sessizce iptal
        // edebiliyor - bu yüzden bilerek ayrı RuleFor'lar kullanıldı.
        RuleFor(x => x.RuleValue)
            .Null()
            .When(x => x.RuleType == ZoneAccessRuleType.AllActiveMembers);
        RuleFor(x => x.RuleValue)
            .NotEmpty()
            .When(x => x.RuleType != ZoneAccessRuleType.AllActiveMembers);
    }
}
