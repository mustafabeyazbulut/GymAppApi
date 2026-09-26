using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

// GymAdmin/BranchManager'ın şubesi için yüklediği video/döküman içeriği -
// Package.AccessTier'ın var oluş sebebi olan özellik (bkz.
// docs/superpowers/specs/2026-09-20-content-library-design.md). Üye, sadece
// kendi aktif PackageAssignment'larının Package.AccessTier'ına eşit veya
// altındaki içerikleri görebilir.
//
// CompanyId = null: Sistem Sahibi'nin yüklediği genel (platform) içerik -
// giriş yapmış herkese görünür, paket/erişim seviyesi aranmaz.
public class ContentItem : EntityBase, IOptionalCompanyScoped, IDeactivatable
{
    public int? CompanyId { get; set; }
    public Company? Company { get; set; }

    // null = firmanın tüm şubelerinde görünür; set = sadece o şubede.
    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public PackageAccessTier RequiredAccessTier { get; set; }

    public int MediaFileId { get; set; }
    public MediaFile? MediaFile { get; set; }

    public int CreatedByUserId { get; set; }

    // Soft close - geçmişte izlenmiş bir içeriğin linki kırılmasın diye hard
    // delete yok (bkz. spec'in "Kapsam Dışı" bölümü).
    public bool IsActive { get; set; } = true;
}
