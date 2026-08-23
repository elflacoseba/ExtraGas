namespace ExtraGasMVC.Constants;

/// <summary>
/// Canonical state codes for garrafas, matching the database catalog
/// <c>estados_garrafa.codigo</c>. Used instead of magic strings for
/// compile-time safety and discoverability.
/// </summary>
/// <remarks>
/// The state codes themselves are stored in the database — these constants
/// mirror them to give type-safe references in C# code. If a state code is
/// renamed in the database, update both this file and the seed migration.
/// </remarks>
public static class GarrafaEstados
{
    public const string LlenaDeposito = "LLENA_DEPOSITO";
    public const string VaciaDeposito = "VACIA_DEPOSITO";
    public const string EnCliente = "EN_CLIENTE";
    public const string EnTransito = "EN_TRANSITO";
    public const string Danada = "DAÑADA";
    public const string FueraServicio = "FUERA_SERVICIO";
}
