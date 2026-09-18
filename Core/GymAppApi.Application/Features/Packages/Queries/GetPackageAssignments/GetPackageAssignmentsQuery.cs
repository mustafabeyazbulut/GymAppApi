using MediatR;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackageAssignments;

// Staff'ın (ödeme kaydetmek veya durumu görmek için) kendi şirketindeki paket
// atamalarını listeleyebilmesi için - şimdiye kadar bu listeyi görebileceği
// hiçbir uç nokta yoktu, bu da RecordPackageAssignmentPayment'ın hedef
// PackageAssignmentId'sini bulmanın gerçek dünyada imkansız olduğu anlamına
// geliyordu.
public class GetPackageAssignmentsQuery : IRequest<IReadOnlyList<PackageAssignmentDto>>
{
    public GetPackageAssignmentsQuery(string? memberPhone)
    {
        MemberPhone = memberPhone;
    }

    // Verilirse sonuçlar bu telefon numarasına (tam veya kısmi) sahip üyeyle
    // sınırlanır - personelin belirli bir üyenin atamasını bulması için.
    public string? MemberPhone { get; }
}
