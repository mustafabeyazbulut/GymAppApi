using GymAppApi.Domain.Common;

namespace GymAppApi.Domain.Entities;

// Bir antrenörün bir üye için bıraktığı ilerleme değerlendirmesi - teknik ve
// kondisyon puanları antrenörün öznel değerlendirmesi, katılım ise KASITLI
// OLARAK burada yok (bir alan olarak elle girilmiyor) çünkü gerçek CheckIn
// kayıtlarından hesaplanabiliyor - iki ayrı "gerçek" kaynağın birbirinden
// sapmasını önlemek için.
public class ProgressNote : EntityBase, ICompanyScoped
{
    public int PackageAssignmentId { get; set; }
    public PackageAssignment? PackageAssignment { get; set; }

    // PackageAssignment.CompanyId/BranchId'nin kayıt anındaki anlık görüntüsü
    // - PackageAssignmentPayment/CheckIn'in kendi CompanyId snapshot deseniyle
    // aynı.
    public int CompanyId { get; set; }
    public Company? Company { get; set; }
    public int? BranchId { get; set; }

    public int RecordedByUserId { get; set; }

    // 0-100 aralığında, antrenörün öznel değerlendirmesi.
    public int TechniqueScore { get; set; }
    public int ConditionScore { get; set; }

    // Notun ne zaman bırakıldığı için EntityBase.CreatedAt zaten yeterli -
    // GymAppApiDbContext.SaveChangesAsync tarafından otomatik dolduruluyor,
    // burada ayrıca bir alan tekrar edilmiyor.
    public string? NoteText { get; set; }

    // null = medyasız not (mevcut davranış). Opsiyonel "önce/sonra" fotoğraf/
    // video eki - bkz. docs/superpowers/specs/2026-09-20-progress-media-design.md.
    // ContentItem'in kullandığı aynı MediaFile/IMediaStorage altyapısını
    // paylaşır, ayrı bir depolama mekanizması icat edilmez.
    public int? MediaFileId { get; set; }
    public MediaFile? MediaFile { get; set; }
}
