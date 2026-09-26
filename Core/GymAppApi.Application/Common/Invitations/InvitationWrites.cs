using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Common.Invitations;

// Davet satırlarına (PendingAssignmentInvitation / PendingPackageAssignmentInvitation)
// yapılan TÜM yazmaların tek geçiş noktası. Satırlar xmin concurrency token
// taşıdığı için eşzamanlı bir yazma DbUpdateConcurrencyException üretir; bu
// yardımcı her yazma türü için doğru sonucu verir - yarışta 500 dönmez.
public static class InvitationWrites
{
    private const int MaxAttemptBurnRetries = 10;

    // Daveti "kullanıldı" (kabul veya red) olarak işaretleyip commit eder.
    // Eşzamanlı ikinci işlemde (çift tıklama, retry, kabul+red yarışı) sadece
    // biri başarır; kaybeden InvitationNotFound (404) ile durur - zaten
    // kullanılmış bir davetle aynı yanıt.
    public static async Task ClaimAsync<TInvitation>(IUnitOfWork unitOfWork, TInvitation invitation, int invitationId, CancellationToken cancellationToken)
        where TInvitation : class, IEntityBase
    {
        unitOfWork.GetWriteRepository<TInvitation>().Update(invitation);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new NotFoundException("InvitationNotFound", invitationId);
        }
    }

    // Yanlış SMS koduyla deneme hakkı yakar. Eşzamanlı yanlış tahminlerde
    // artışlar kaybolmasın diye (aksi hâlde paralel istekler "bedava deneme"
    // kazanırdı) çakışmada güncel satırlar yeniden okunup tekrar denenir.
    // loadLiveInvitations: çağıranın hâlâ geçerli davetlerini döndürür;
    // burnAttempt: bir davetin sayacını artırır (sınıra ulaşmamışsa).
    public static async Task BurnAttemptsAsync<TInvitation>(
        IUnitOfWork unitOfWork,
        Func<Task<IReadOnlyList<TInvitation>>> loadLiveInvitations,
        Func<TInvitation, bool> burnAttempt,
        CancellationToken cancellationToken)
        where TInvitation : class, IEntityBase
    {
        for (var attempt = 1; ; attempt++)
        {
            var invitations = await loadLiveInvitations();
            var writeRepo = unitOfWork.GetWriteRepository<TInvitation>();
            foreach (var invitation in invitations)
            {
                if (burnAttempt(invitation))
                {
                    writeRepo.Update(invitation);
                }
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttemptBurnRetries)
            {
                // Bayat kopyaları bırak, güncel değerlerle tekrar dene. Yanlış kod
                // yolunda bekleyen başka bir değişiklik yok; temizlemek güvenli.
                unitOfWork.ClearChangeTracker();
            }
        }
    }
}
