namespace FokusKararMotoru.Services;

public static class ProjeYolu
{
    public static string Bul()
    {
        DirectoryInfo? aday = new(Directory.GetCurrentDirectory());

        while (aday is not null)
        {
            if (File.Exists(Path.Combine(aday.FullName, "kamera_test.py")) ||
                File.Exists(Path.Combine(aday.FullName, "aktif_odak.txt")))
            {
                return aday.FullName;
            }

            aday = aday.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
