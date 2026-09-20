namespace GymAppApi.Infrastructure.Storage;

public class MediaStorageOptions
{
    // Bos birakilirsa Registration.AddInfrastructure, uygulamanin calisma
    // dizini altinda "media-storage" klasorunu varsayilan olarak kullanir -
    // gelistirme ortaminda appsettings'e elle deger girmeye gerek kalmaz.
    public string RootPath { get; set; } = "";
}
