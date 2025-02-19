using System;
using Hub.Models;
using Microsoft.EntityFrameworkCore;

namespace Hub
{
    public class DynamicDespatchDbContext(DbContextOptions<DespatchContext> options, IConnectionStringManager connectionStringManager) : DespatchContext(options)
    {
        private string _connectionString;


        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var connectionString = connectionStringManager.GetConnectionString();
                if (string.IsNullOrEmpty(connectionString))
                {
                    throw new InvalidOperationException("Connection string not set. Please ensure you're logged in.");
                }
                optionsBuilder.UseSqlServer(connectionString);
            }
            base.OnConfiguring(optionsBuilder);
        }

        
    }
}
