using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

// Bir Zone'un birden fazla kuralı varsa hepsi sağlanmalı (VE) - master
// spec'teki kural, ama bu iskelet kapsamında hiçbir kod bunu DEĞERLENDİRMEZ.
public class ZoneAccessRule : EntityBase, ICompanyScoped
{
    public int ZoneId { get; set; }
    public Zone? Zone { get; set; }

    // Zone.CompanyId'nin kayıt anındaki anlık görüntüsü.
    public int CompanyId { get; set; }

    public ZoneAccessRuleType RuleType { get; set; }
    // RuleType=AllActiveMembers'ta null, diğerlerinde ör. "Kadın"/"Trainer"/"PT".
    public string? RuleValue { get; set; }
}
