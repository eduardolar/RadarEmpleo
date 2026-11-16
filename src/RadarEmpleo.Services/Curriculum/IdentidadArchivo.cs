namespace RadarEmpleo.Services.Curriculum;

/// <summary>
/// Quién es de verdad un archivo o una carpeta en Windows: el volumen y el índice del archivo identifican el mismo
/// objeto del disco aunque se llegue por otro nombre (8.3 como <c>USUARI~1</c>, junction, enlace, mayúsculas o
/// <c>\\?\</c>). <see cref="Enlaces"/> cuenta sus nombres (más de 1 si tiene enlaces duros).
/// </summary>
public sealed record IdentidadArchivo(uint Volumen, ulong Indice, uint Enlaces)
{
    /// <summary>¿Son el mismo objeto del disco?</summary>
    public bool MismoQue(IdentidadArchivo otra) => Volumen == otra.Volumen && Indice == otra.Indice;

    /// <summary>La identidad de un archivo o carpeta, o null si no existe, no se puede abrir o no es Windows.</summary>
    public static IdentidadArchivo? Leer(string ruta)
    {
        if (!OperatingSystem.IsWindows() || !(File.Exists(ruta) || Directory.Exists(ruta))) return null;
        try
        {
            var (volumen, indice, enlaces) = ArchivosWindows.Identidad(ruta);
            return new IdentidadArchivo(volumen, indice, enlaces);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
