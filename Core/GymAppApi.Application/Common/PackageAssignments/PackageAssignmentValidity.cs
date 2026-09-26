using System.Linq.Expressions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Application.Common.PackageAssignments;

// "Geçerli (kullanılabilir) paket" kuralının TEK tanımı - senaryo §3.2: sadece
// o gym'de geçerli paketi olan kullanıcı o gym'in üyesi sayılır. Expired
// saklanan bir durum olmadığından (bkz. PackageAssignmentStatus) Status
// tek başına yeterli değil:
//   Active && (EndDate yok veya gelecekte) && (seans bazlıysa hakkı kalmış).
// Ders programı, derse kayıt, rezervasyon, içerik listesi ve medya indirme
// hepsi bunu kullanır; modüle özgü ek şartlar (kategori eşleşmesi, sadece
// seans bazlı paket vb.) ilgili handler'da bunun ÜSTÜNE eklenir.
public static class PackageAssignmentValidity
{
    private static readonly Expression<Func<PackageAssignment, DateTime, bool>> Definition =
        (pa, now) => pa.Status == PackageAssignmentStatus.Active &&
                     (pa.EndDate == null || pa.EndDate > now) &&
                     (pa.RemainingSessions == null || pa.RemainingSessions > 0);

    private static readonly Func<PackageAssignment, DateTime, bool> CompiledDefinition = Definition.Compile();

    // Bellekteki tek bir atama için (ör. derse kayıt / rezervasyon kontrolü).
    public static bool IsUsable(PackageAssignment assignment, DateTime now) => CompiledDefinition(assignment, now);

    // EF sorgusu için: tüm geçerli paketler (ör. firma bazlı üye sayımı).
    public static Expression<Func<PackageAssignment, bool>> Usable(DateTime now)
    {
        var assignment = Definition.Parameters[0];
        var usableAtNow = new ParameterReplacer(Definition.Parameters[1], Expression.Constant(now)).Visit(Definition.Body);
        return Expression.Lambda<Func<PackageAssignment, bool>>(usableAtNow, assignment);
    }

    // EF sorgusu için: bir üyenin kendi geçerli paketleri. Aynı Definition
    // gövdesi SQL'e çevrilebilir bir predicate'e dönüştürülür - kural iki
    // ayrı yerde elle tekrarlanmaz.
    public static Expression<Func<PackageAssignment, bool>> UsableOwnedBy(int memberUserId, DateTime now)
    {
        var assignment = Definition.Parameters[0];
        var usableAtNow = new ParameterReplacer(Definition.Parameters[1], Expression.Constant(now)).Visit(Definition.Body);
        var ownedByMember = Expression.Equal(
            Expression.Property(assignment, nameof(PackageAssignment.MemberUserId)),
            Expression.Constant(memberUserId));
        return Expression.Lambda<Func<PackageAssignment, bool>>(Expression.AndAlso(ownedByMember, usableAtNow), assignment);
    }

    private sealed class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _parameter;
        private readonly Expression _replacement;

        public ParameterReplacer(ParameterExpression parameter, Expression replacement)
        {
            _parameter = parameter;
            _replacement = replacement;
        }

        protected override Expression VisitParameter(ParameterExpression node) =>
            node == _parameter ? _replacement : base.VisitParameter(node);
    }
}
