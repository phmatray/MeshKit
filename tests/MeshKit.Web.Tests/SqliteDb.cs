using MeshKit.Web.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MeshKit.Web.Tests;

/// <summary>An in-memory SQLite database that lives as long as its connection — the real provider, not InMemory.</summary>
public sealed class SqliteDb : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public SqliteDb()
    {
        _connection.Open();
        using var db = Create();
        db.Database.EnsureCreated();
    }

    public ApplicationDbContext Create() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);

    public void Dispose() => _connection.Dispose();
}
