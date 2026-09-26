using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;

namespace GymAppApi.Application.Common.Seeding;

public enum SuperAdminPhoneSeedOutcome
{
    SeedUserMissing,
    Updated,
    AlreadyCustomized,
    PlaceholderRemains,
}

// Migration'daki seed SuperAdmin (HasData, Id = -1) gerçek bir numara
// olmayan "+900000000000" ile oluşturuluyor ve telefon doğrulamasından
// geçmiyor. HasData'yı yapılandırmaya bağlamak modeli ortama göre değişken
// yapar ve değer her değiştiğinde bir UpdateData migration'ı gerektirir -
// o migration da elle düzeltilmiş kayıtların (ör. dev DB) üzerine yazardı.
// Bunun yerine açılışta bir kez: "Seed:SuperAdminPhone" yapılandırılmışsa
// ve seed kullanıcının telefonu HÂLÂ placeholder ise kanonik E.164'e
// güncellenir. Elle değiştirilmiş bir telefona asla dokunulmaz.
public class SuperAdminPhoneSeeder
{
    public const int SeedUserId = -1;
    public const string PlaceholderPhone = "+900000000000";
    public const string ConfigurationKey = "Seed:SuperAdminPhone";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPhoneNumberNormalizer _phoneNumberNormalizer;

    public SuperAdminPhoneSeeder(IUnitOfWork unitOfWork, IPhoneNumberNormalizer phoneNumberNormalizer)
    {
        _unitOfWork = unitOfWork;
        _phoneNumberNormalizer = phoneNumberNormalizer;
    }

    public async Task<SuperAdminPhoneSeedOutcome> ApplyAsync(string? configuredPhone, CancellationToken cancellationToken)
    {
        // Geçersiz bir değer sessizce yok sayılmaz: yanlış yazılmış bir
        // yapılandırma, SuperAdmin'in hiç giriş yapamamasına yol açardı.
        string? canonicalPhone = null;
        if (!string.IsNullOrWhiteSpace(configuredPhone))
        {
            if (!_phoneNumberNormalizer.TryNormalize(configuredPhone, out var e164))
            {
                throw new InvalidOperationException(
                    $"'{ConfigurationKey}' yapılandırması geçerli bir telefon numarası değil. " +
                    "Ülke koduyla birlikte gerçek bir numara girin (ör. +905551234567).");
            }

            canonicalPhone = e164;
        }

        var seedUser = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Id == SeedUserId, enableTracking: true, cancellationToken: cancellationToken);
        if (seedUser is null)
        {
            return SuperAdminPhoneSeedOutcome.SeedUserMissing;
        }

        if (seedUser.Phone != PlaceholderPhone)
        {
            return SuperAdminPhoneSeedOutcome.AlreadyCustomized;
        }

        if (canonicalPhone is null)
        {
            return SuperAdminPhoneSeedOutcome.PlaceholderRemains;
        }

        seedUser.Phone = canonicalPhone;
        _unitOfWork.GetWriteRepository<User>().Update(seedUser);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return SuperAdminPhoneSeedOutcome.Updated;
    }
}
