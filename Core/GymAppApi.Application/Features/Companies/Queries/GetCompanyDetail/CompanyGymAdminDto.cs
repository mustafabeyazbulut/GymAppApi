namespace GymAppApi.Application.Features.Companies.Queries.GetCompanyDetail;

public class CompanyGymAdminDto
{
    // SuperAdmin bu GymAdmin'i kaldırmak isterse DELETE /api/assignments/{id}
    // çağırabilsin diye - RemoveAssignmentCommandHandler zaten SuperAdmin için
    // "son GymAdmin" korumasını atlıyor.
    public int AssignmentId { get; set; }
    public int UserId { get; set; }
    public string FullName { get; set; } = null!;
    public string Phone { get; set; } = null!;
}
