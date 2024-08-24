namespace UrgentHub
{
    public interface IConnectionStringManager
    {
        void SetConnectionString(string connectionString);
        string GetConnectionString();
        bool IsConnectionStringSet();
    }

    public class ConnectionStringManager : IConnectionStringManager
    {
        private string _connectionString;

        public void SetConnectionString(string connectionString)
        {
            _connectionString = connectionString;
        }

        public string GetConnectionString()
        {
            return _connectionString;
        }

        public bool IsConnectionStringSet()
        {
            return !string.IsNullOrEmpty(_connectionString);
        }
    }
}
