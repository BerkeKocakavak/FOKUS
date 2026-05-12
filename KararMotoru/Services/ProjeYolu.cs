using System.IO;

namespace FokusKararMotoru.Services;

public static class ProjeYolu
{
    public static string Bul()
    {
        DirectoryInfo? aday = new(Directory.GetCurrentDirectory());

        while (aday is not null)
        {
            if (File.Exists(Path.Combine(aday.FullName, "kamera_test.py")) ||
                File.Exists(Path.Combine(aday.FullName, "FOKUS.sln")))
            {
                return aday.FullName;
            }

            aday = aday.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
