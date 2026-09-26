using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymAppApi.Persistence.Migrations
{
    // Senaryo §10.7: Role=Member ataması sistemden kaldırıldı - gym üyeliği
    // bir atama değil bir pakettir (PackageAssignment). Model değişmiyor (Role
    // string olarak saklanıyor, enum'dan bir değerin kaldırılması şemayı
    // etkilemez), bu yüzden bu sadece bir veri migration'ı: eski modelden
    // kalan "Member" atamaları ve bunlara ait bekleyen davetler SİLİNİR
    // (ürün kararı: sil). Diğer rollerin satırlarına dokunulmaz.
    /// <inheritdoc />
    public partial class RemoveLegacyMemberAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DELETE FROM "PendingAssignmentInvitations" WHERE "Role" = 'Member';""");
            migrationBuilder.Sql("""DELETE FROM "Assignments" WHERE "Role" = 'Member';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Geri alınamaz: silinen Member atamaları yeniden oluşturulamaz ve
            // artık uygulamada karşılığı olan bir rol yok. Down bilerek boş.
        }
    }
}
