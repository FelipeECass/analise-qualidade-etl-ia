namespace AnatelEtl.Models;

/// <summary>
/// Uma torre/estação, identificada por <c>Uf</c> + <c>Fistel</c> + <c>NumEstacao</c>.
/// A UF faz parte da chave porque a mesma combinação Fistel/NumEstacao aparece em UFs diferentes.
/// </summary>
public sealed record Estacao(
    string Fistel,
    string NumEstacao,
    string Uf,
    string? CodMunicipio,
    string? Municipio,
    string? Endereco,
    double? Latitude,
    double? Longitude,
    bool CoordenadaValida,
    string? Entidade,
    string Operadora)
{
    public (string Uf, string Fistel, string NumEstacao) Key => (Uf, Fistel, NumEstacao);
}
