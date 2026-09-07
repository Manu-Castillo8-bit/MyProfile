using Microsoft.Maui.Storage;

namespace Proyecto.Services;

// Progreso diario de hidratación y descanso visual.
// Se guarda SOLO en el dispositivo (Preferences), no en la base de datos.
public static class ProgresoSalud
{
    private const string ClaveDia = "salud_dia";
    private const string ClaveVasos = "salud_vasos";
    private const string ClaveDescansos = "salud_descansos";

    private static bool EsHoy()
    {
        return Preferences.Default.Get(ClaveDia, "") == DateTime.Today.ToString("yyyyMMdd");
    }

    private static void IniciarDiaSiEsNecesario()
    {
        if (EsHoy())
            return;

        Preferences.Default.Set(ClaveDia, DateTime.Today.ToString("yyyyMMdd"));
        Preferences.Default.Set(ClaveVasos, 0);
        Preferences.Default.Set(ClaveDescansos, 0);
    }

    public static int VasosHoy()
    {
        IniciarDiaSiEsNecesario();
        return Preferences.Default.Get(ClaveVasos, 0);
    }

    public static int DescansosHoy()
    {
        IniciarDiaSiEsNecesario();
        return Preferences.Default.Get(ClaveDescansos, 0);
    }

    public static void SumarVaso(int delta)
    {
        IniciarDiaSiEsNecesario();
        int nuevo = Math.Max(0, VasosHoy() + delta);
        Preferences.Default.Set(ClaveVasos, nuevo);
    }

    public static void SumarDescanso(int delta)
    {
        IniciarDiaSiEsNecesario();
        int nuevo = Math.Max(0, DescansosHoy() + delta);
        Preferences.Default.Set(ClaveDescansos, nuevo);
    }
}