using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

// Kapasiteli GRUP dersi (yoga, fitness, dövüş sanatları grup dersi vb.) -
// mevcut 1:1 Reservation/CheckIn modelinden BAĞIMSIZ, ayrı bir modül (bkz.
// docs/superpowers/specs/2026-09-20-group-class-scheduling-design.md). Faz 1
// kapsamında her ClassSession tek tek elle oluşturulur, tekrarlayan şablon
// yok (YAGNI).
public class ClassSession : EntityBase, ICompanyScoped
{
    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    public int BranchId { get; set; }
    public Branch? Branch { get; set; }

    public int TrainerUserId { get; set; }
    public User? TrainerUser { get; set; }

    public ClassSessionCategory Category { get; set; }
    public string Name { get; set; } = null!;

    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public int Capacity { get; set; }
    // İptal için son saat sınırı: StartTime - CancellationCutoffHours'dan
    // önce iptal edilirse seans iade edilir, sonrasında NoShow sayılır (bkz.
    // CancelClassEnrollmentCommandHandler).
    public int CancellationCutoffHours { get; set; }

    public int CreatedByUserId { get; set; }
}
