namespace Hub;

public interface IConnectionStringManager
{
    void SetConnectionString(string connectionString);
    string GetConnectionString();
}

public class ConnectionStringManager : IConnectionStringManager
{
    private string _connectionString;

    public void SetConnectionString(string connectionString) => _connectionString = connectionString;

    public string GetConnectionString() => _connectionString;

    public bool IsConnectionStringSet() => !string.IsNullOrEmpty(_connectionString);
}