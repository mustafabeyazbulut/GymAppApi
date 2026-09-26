namespace GymAppApi.Application.Common.Localization;

// Backend'in döndürdüğü HER hata/uyarı metninin tek kaynağı burası - hiçbir
// exception veya validator artık mesajı doğrudan (hardcode) içermiyor, sadece
// buradaki bir anahtarı ve varsa {0}/{1} yer tutucularına geçecek argümanları
// taşıyor. Gerçek metin, isteğin diline (mobilin gönderdiği Accept-Language
// header'ı, bkz. Program.cs'teki UseRequestLocalization) göre burada, tek
// merkezden seçiliyor - "backend mobildeki seçili dil paketiyle çalışacak"
// talimatının karşılığı budur.
//
// Yeni bir anahtar eklerken: hem "en" hem "tr" karşılığını birlikte ekle,
// ikisi de eksiksiz olmalı. Uygulamanın standart/varsayılan dili İngilizce.
public static class AppMessages
{
    private static readonly Dictionary<string, (string En, string Tr)> Catalog = new()
    {
        // Genel / ortak
        ["UnexpectedError"] = ("An unexpected error occurred.", "Beklenmeyen bir hata oluştu."),
        ["TooManyRequests"] = (
            "Too many attempts. Please wait a moment and try again.",
            "Çok fazla deneme yapıldı. Lütfen biraz bekleyip tekrar deneyin."),
        // Şifre sıfırlama kodu (SMS gövdesi ve e-posta konusu/gövdesi) - alıcının
        // PreferredLanguage'ına göre seçilir ({0} = kod).
        ["PasswordResetEmailSubject"] = ("GymApp Password Reset", "GymApp Şifre Sıfırlama"),
        ["PasswordResetCodeMessage"] = (
            "Your GymApp password reset code: {0}. The code is valid for 10 minutes.",
            "GymApp şifre sıfırlama kodunuz: {0}. Kod 10 dakika geçerlidir."),
        ["PasswordResetCodeSentIfAccountExists"] = (
            "If your account exists, a password reset code has been sent.",
            "Hesabınız varsa, şifre sıfırlama kodu gönderildi."),

        // NotFoundException kodları ({0} = kimlik/değer)
        ["BranchNotFound"] = ("Branch {0} not found.", "Şube {0} bulunamadı."),
        ["CompanyNotFound"] = ("Company {0} not found.", "Firma {0} bulunamadı."),
        ["UserNotFound"] = ("User {0} not found.", "Kullanıcı {0} bulunamadı."),
        ["AssignmentNotFound"] = ("Assignment {0} not found.", "Atama {0} bulunamadı."),
        ["NotificationNotFound"] = ("Notification {0} not found.", "Bildirim {0} bulunamadı."),
        ["PackageNotFound"] = ("Package {0} not found.", "Paket {0} bulunamadı."),
        ["PackageAssignmentNotFound"] = ("Package assignment {0} not found.", "Paket ataması {0} bulunamadı."),
        ["ReservationNotFound"] = ("Reservation {0} not found.", "Rezervasyon {0} bulunamadı."),
        ["ForbiddenCancelReservation"] = ("You are not authorized to cancel this reservation.", "Bu rezervasyonu iptal etme yetkiniz yok."),
        ["ForbiddenCreateReservation"] = ("You are not authorized to create a reservation for this package assignment.", "Bu paket ataması için rezervasyon oluşturma yetkiniz yok."),
        ["ForbiddenMarkNoShow"] = ("You are not authorized to mark this reservation as a no-show.", "Bu rezervasyonu 'gelmedi' olarak işaretleme yetkiniz yok."),
        ["PhoneNotRegistered"] = ("No registered user was found with the phone number '{0}'.", "'{0}' numaralı kayıtlı bir kullanıcı bulunamadı."),
        ["ContentItemNotFound"] = ("Content item {0} not found.", "İçerik {0} bulunamadı."),
        ["MediaFileNotFound"] = ("Media file {0} not found.", "Medya dosyası {0} bulunamadı."),
        ["ZoneNotFound"] = ("Zone {0} not found.", "Bölge {0} bulunamadı."),
        ["DoorNotFound"] = ("Door {0} not found.", "Kapı {0} bulunamadı."),
        ["ZoneAccessRuleNotFound"] = ("Zone access rule {0} not found.", "Bölge erişim kuralı {0} bulunamadı."),

        // ForbiddenException kodları
        ["ForbiddenCreateBranch"] = ("You are not authorized to create a branch for this company.", "Bu firma için şube oluşturma yetkiniz yok."),
        ["ForbiddenSetBranchActive"] = ("You are not authorized to activate/deactivate this branch.", "Bu şubeyi aktif/pasif yapma yetkiniz yok."),
        ["ForbiddenUpdateBranch"] = ("You are not authorized to update this branch.", "Bu şubeyi güncelleme yetkiniz yok."),
        ["ForbiddenCreatePackage"] = ("You are not authorized to create a package for this company/branch.", "Bu firma/şube için paket oluşturma yetkiniz yok."),
        ["ForbiddenAssignPackage"] = ("You are not authorized to assign this package.", "Bu paketi atama yetkiniz yok."),
        ["ForbiddenManagePackage"] = ("You are not authorized to activate/deactivate this package.", "Bu paketi aktif/pasif yapma yetkiniz yok."),
        ["ForbiddenFreezePackageAssignment"] = ("You are not authorized to freeze this package assignment.", "Bu paket atamasını dondurma yetkiniz yok."),
        ["ForbiddenUnfreezePackageAssignment"] = ("You are not authorized to unfreeze this package assignment.", "Bu paket atamasını aktifleştirme yetkiniz yok."),
        ["ForbiddenCancelPackageAssignment"] = ("You are not authorized to cancel this package assignment.", "Bu paket atamasını iptal etme yetkiniz yok."),
        ["ForbiddenPackageAssignmentPayments"] = ("You are not authorized to view this package assignment's payments.", "Bu paket atamasının ödemelerini görme yetkiniz yok."),
        ["ForbiddenRecordPayment"] = ("You are not authorized to record a payment for this package assignment.", "Bu paket ataması için ödeme kaydetme yetkiniz yok."),
        ["ForbiddenCheckIn"] = ("You are not authorized to check in this package assignment.", "Bu paket ataması için check-in yapma yetkiniz yok."),
        ["ForbiddenViewCheckIns"] = ("You are not authorized to view this package assignment's check-ins.", "Bu paket atamasının check-in'lerini görme yetkiniz yok."),
        ["ForbiddenRecordProgressNote"] = ("You are not authorized to record a progress note for this package assignment.", "Bu paket ataması için ilerleme notu kaydetme yetkiniz yok."),
        ["ForbiddenViewProgressNotes"] = ("You are not authorized to view this package assignment's progress notes.", "Bu paket atamasının ilerleme notlarını görme yetkiniz yok."),
        ["ForbiddenViewReservations"] = ("You are not authorized to view this package assignment's reservations.", "Bu paket atamasının rezervasyonlarını görme yetkiniz yok."),
        ["ForbiddenViewTrainers"] = ("You are not authorized to view this package assignment's trainers.", "Bu paket atamasının antrenörlerini görme yetkiniz yok."),
        ["ForbiddenRemoveAssignment"] = ("You are not authorized to remove this assignment.", "Bu atamayı kaldırma yetkiniz yok."),
        ["ForbiddenAddStaffToBranch"] = ("You are not authorized to add staff to this branch.", "Bu şubeye personel ekleme yetkiniz yok."),
        ["ForbiddenInviteGymAdmin"] = ("You are not authorized to send a GymAdmin invitation for this company.", "Bu firma için Gym Admin daveti gönderme yetkiniz yok."),
        ["ForbiddenCreateContentItem"] = ("You are not authorized to upload content for this company/branch.", "Bu firma/şube için içerik yükleme yetkiniz yok."),
        ["ForbiddenSetContentItemActive"] = ("You are not authorized to activate/deactivate this content item.", "Bu içeriği aktif/pasif yapma yetkiniz yok."),
        ["ForbiddenViewMedia"] = ("You are not authorized to view this media file.", "Bu medya dosyasını görüntüleme yetkiniz yok."),
        ["InvalidActiveAssignment"] = (
            "The selected role is not valid for your account. Please select one of your active roles again.",
            "Seçili rol hesabınız için geçerli değil. Lütfen aktif rollerinizden birini yeniden seçin."),
        ["ForbiddenManageDoorAccess"] = ("You are not authorized to manage door access for this branch.", "Bu şube için kapı erişimini yönetme yetkiniz yok."),

        // ConflictException kodları (isimlendirilmiş exception sınıfları)
        ["ConflictingAssignmentRole"] = (
            "This user already holds the GymAdmin or BranchManager role at this company; a single company cannot have both for the same person.",
            "Bu kullanıcı bu firmada zaten Gym Admin veya Branch Manager rolüne sahip; aynı firmada ikisi birden olamaz."),
        ["LastGymAdmin"] = (
            "A company must always have at least one GymAdmin. Removing the last one requires a SuperAdmin.",
            "Bir firmanın en az bir Gym Admin'i olmalı. Son Gym Admin'i kaldırmak için Super Admin gerekir."),
        ["UserAlreadyAssigned"] = ("This user is already linked to this company.", "Bu kullanıcı zaten bu firmaya bağlı."),

        // UnauthorizedException kodları
        ["InvalidAssignmentInvitationCode"] = (
            "The code is incorrect, expired, or too many attempts were made.",
            "Kod hatalı, süresi dolmuş veya çok fazla deneme yapıldı."),
        ["PhoneCodeInvalid"] = (
            "The phone code is incorrect, expired, or too many attempts were made.",
            "Telefon kodu hatalı, süresi dolmuş veya çok fazla deneme yapıldı."),
        ["EmailCodeInvalid"] = (
            "The email code is incorrect, expired, or too many attempts were made.",
            "E-posta kodu hatalı, süresi dolmuş veya çok fazla deneme yapıldı."),
        ["PhoneAndEmailCodeInvalid"] = (
            "The phone and email codes are incorrect, expired, or too many attempts were made.",
            "Telefon ve e-posta kodu hatalı, süresi dolmuş veya çok fazla deneme yapıldı."),
        ["EmailAlreadyRegistered"] = ("An account already exists with this email address.", "Bu e-posta adresiyle zaten bir hesap var."),
        ["PhoneAlreadyRegistered"] = ("An account already exists with this phone number.", "Bu telefon numarasıyla zaten bir hesap var."),
        ["InvalidCredentials"] = ("Incorrect phone number/email or password.", "Telefon numarası/e-posta veya şifre hatalı."),
        ["InvalidRefreshToken"] = ("Your session has expired, please log in again.", "Oturum süresi doldu, lütfen tekrar giriş yapın."),
        ["InvalidResetCode"] = (
            "The code is incorrect, expired, or too many attempts were made.",
            "Kod hatalı, süresi dolmuş veya çok fazla deneme yapıldı."),
        ["TooManyVerificationRequests"] = (
            "Too many code requests were sent. Please try again later.",
            "Çok fazla kod isteği gönderildi. Lütfen daha sonra tekrar deneyin."),

        // FluentValidation .WithMessage kodları
        ["UnsupportedLanguage"] = ("Language must be one of: {0}.", "Dil şunlardan biri olmalıdır: {0}."),
        ["RoleMustBeTrainerOrBranchManager"] = ("Role must be Trainer or BranchManager.", "Rol Trainer veya BranchManager olmalıdır."),
        ["InvalidPhoneNumber"] = (
            "Please enter a valid phone number with its country code (e.g. +905551234567).",
            "Lütfen ülke koduyla birlikte geçerli bir telefon numarası girin (ör. +905551234567)."),
        ["DurationDaysRequiredForDurationPackage"] = (
            "DurationDays is required and must be greater than 0 for a Duration package.",
            "Süreli paket için DurationDays zorunludur ve 0'dan büyük olmalıdır."),
        ["SessionCountMustBeNullForDurationPackage"] = (
            "SessionCount must not be set for a Duration package.",
            "Süreli paket için SessionCount belirtilmemelidir."),
        ["SessionCountRequiredForSessionBasedPackage"] = (
            "SessionCount is required and must be greater than 0 for a SessionBased package.",
            "Seans bazlı paket için SessionCount zorunludur ve 0'dan büyük olmalıdır."),
        ["MaxFreezeDaysMustBeGreaterThanZero"] = (
            "MaxFreezeDays must be greater than 0 when set.",
            "MaxFreezeDays belirtildiğinde 0'dan büyük olmalıdır."),

        // Packages özelliği - isimlendirilmiş exception'lar
        ["FreezeLimitExceeded"] = (
            "The maximum freeze duration allowed for this package ({0} days) has already been used.",
            "Bu paket için izin verilen maksimum dondurma süresi ({0} gün) zaten kullanıldı."),
        ["InvalidPackageAssignmentInvitationCode"] = (
            "The code is incorrect, expired, or too many attempts were made.",
            "Kod hatalı, süresi dolmuş veya çok fazla deneme yapıldı."),
        ["MemberAlreadyHasThisPackage"] = ("This member already has an active assignment for this package.", "Bu üyenin bu pakete zaten aktif bir ataması var."),
        ["PackageAssignmentNotActive"] = ("Only an assignment in Active status can be frozen.", "Sadece aktif durumdaki bir paket ataması dondurulabilir."),
        ["PackageAssignmentNotFrozen"] = ("Only an assignment in Frozen status can be unfrozen.", "Sadece dondurulmuş durumdaki bir paket ataması açılabilir."),
        ["PaymentExceedsRemainingBalance"] = ("This payment exceeds the package's remaining balance.", "Bu ödeme, paketin kalan bakiyesinden fazla."),

        // Reservations özelliği - isimlendirilmiş exception'lar
        ["InvalidReservationCode"] = (
            "No reservation still in 'Booked' status was found for this code.",
            "Bu koda ait, hala 'Booked' durumunda bir rezervasyon bulunamadı."),
        ["NoRemainingSessions"] = ("This package assignment has no remaining sessions.", "Bu paket atamasının kalan seans hakkı yok."),
        ["PackageAssignmentNotEligibleForReservation"] = (
            "This package assignment is not eligible for a reservation (must be active, session-based, and have remaining sessions).",
            "Bu paket ataması rezervasyon için uygun değil (aktif, seans bazlı ve seans hakkı kalmış olmalı)."),
        ["ReservationConflict"] = ("This trainer already has a reservation at this time.", "Bu antrenörün bu saatte zaten bir rezervasyonu var."),
        ["ReservationNotBooked"] = ("This reservation is no longer in 'Booked' status.", "Bu rezervasyon artık 'Booked' durumunda değil."),

        // ClassScheduling özelliği
        ["ClassSessionNotFound"] = ("Class session {0} not found.", "Ders programı {0} bulunamadı."),
        ["ClassEnrollmentNotFound"] = ("Class enrollment {0} not found.", "Ders kaydı {0} bulunamadı."),
        ["ForbiddenCreateClassSession"] = ("You are not authorized to create a class session for this branch.", "Bu şube için ders programı oluşturma yetkiniz yok."),
        ["ForbiddenEnrollInClassSession"] = ("You are not authorized to enroll this package assignment in a class session.", "Bu paket ataması için ders kaydı oluşturma yetkiniz yok."),
        ["ForbiddenCancelClassEnrollment"] = ("You are not authorized to cancel this class enrollment.", "Bu ders kaydını iptal etme yetkiniz yok."),
        ["ClassSessionEndTimeMustBeAfterStartTime"] = ("EndTime must be after StartTime.", "Bitiş saati başlangıç saatinden sonra olmalıdır."),
        ["ClassSessionFull"] = ("This class session is full.", "Bu ders programının kapasitesi dolu."),
        ["PackageAssignmentNotEligibleForClass"] = (
            "This package assignment is not eligible for this class session (must match the class category and be active with availability remaining).",
            "Bu paket ataması bu ders için uygun değil (ders kategorisiyle eşleşen, aktif ve hakkı kalmış bir paket olmalı)."),
        ["AlreadyEnrolledInClassSession"] = ("This package assignment is already enrolled in this class session.", "Bu paket ataması bu ders programına zaten kayıtlı."),
        ["ClassEnrollmentNotReserved"] = ("This class enrollment is no longer in 'Reserved' status.", "Bu ders kaydı artık 'Reserved' durumunda değil."),
    };

    public static string Resolve(string code, string language, params object[] args)
    {
        if (!Catalog.TryGetValue(code, out var pair))
        {
            // Katalogda olmayan bir anahtar - kod hatası anlamına gelir, en
            // azından kodun kendisini dönerek sessizce yutmamış oluyoruz.
            return code;
        }

        var template = language == "tr" ? pair.Tr : pair.En;
        return args.Length == 0 ? template : string.Format(template, args);
    }
}
