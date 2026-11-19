using AnatelEtl;

try
{
    Pipeline.Run(Pipeline.FindRoot());
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Falha no ETL: {error.Message}");
    return 1;
}
