using Etl.Pipeline;

try
{
    var root = EtlRunner.FindRoot();
    var resumo = new EtlRunner(root).Run();
    Console.WriteLine(resumo);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERRO: {ex.Message}");
    Console.Error.WriteLine(ex.ToString());
    return 1;
}
