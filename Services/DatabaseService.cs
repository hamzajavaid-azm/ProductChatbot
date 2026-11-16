using Npgsql;

namespace ProductChatbot.Services
{
    public class DatabaseService
    {
        public NpgsqlDataSource DataSource { get; }

        public DatabaseService(string connectionString)
        {
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            builder.UseVector();
            DataSource = builder.Build();
        }
    }
}
