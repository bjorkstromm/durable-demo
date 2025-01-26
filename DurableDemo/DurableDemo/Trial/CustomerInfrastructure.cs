namespace DurableDemo.Trial;

public record CustomerInfrastructure
{
    public CustomerInformation Customer { get; init; } = new();
    public Uri Uri => new Uri($"https://{Normalize(Customer.Name)}.example.com");

    private static string Normalize(string name) => name.ToLowerInvariant().Replace(" ", "");
}