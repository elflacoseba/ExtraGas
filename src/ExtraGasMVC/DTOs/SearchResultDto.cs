namespace ExtraGasMVC.DTOs;

/// <summary>
/// Generic paginated search result container.
/// </summary>
/// <typeparam name="T">The type of items in the result set.</typeparam>
public class SearchResultDto<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
    public int Pagina { get; set; }
    public int Tamanio { get; set; }
}