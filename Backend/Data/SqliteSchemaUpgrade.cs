using Microsoft.EntityFrameworkCore;
using System.Data;

namespace AcxiomCRM.Data;

public static class SqliteSchemaUpgrade
{
    private static readonly (string Table, string Column, string Definition)[] AddedColumns =
    [
        ("Customers", "Notes", "TEXT NULL"),
        ("Customers", "ModifiedDate", "TEXT NULL"),
        ("Leads", "Priority", "TEXT NOT NULL DEFAULT 'Normal'"),
        ("Leads", "Notes", "TEXT NULL"),
        ("Leads", "ModifiedDate", "TEXT NULL"),
        ("Opportunities", "Source", "TEXT NULL"),
        ("Opportunities", "ModifiedDate", "TEXT NULL"),
        ("FollowUps", "OpportunityId", "INTEGER NULL REFERENCES Opportunities(OpportunityId) ON DELETE SET NULL"),
        ("FollowUps", "Subject", "TEXT NOT NULL DEFAULT ''"),
        ("Activities", "OpportunityId", "INTEGER NULL REFERENCES Opportunities(OpportunityId) ON DELETE SET NULL")
    ];

    public static async Task ApplyAsync(ApplicationDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var closeWhenDone = connection.State == ConnectionState.Closed;
        if (closeWhenDone)
            await connection.OpenAsync();

        try
        {
            foreach (var (table, column, definition) in AddedColumns)
            {
                await using var check = connection.CreateCommand();
                check.CommandText = $"PRAGMA table_info(\"{table}\")";
                await using var reader = await check.ExecuteReaderAsync();
                var exists = false;
                while (await reader.ReadAsync())
                {
                    if (reader.GetString(1) == column)
                    {
                        exists = true;
                        break;
                    }
                }

                await reader.CloseAsync();
                if (!exists)
                {
                    await using var alter = connection.CreateCommand();
                    alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition}";
                    await alter.ExecuteNonQueryAsync();
                }
            }

            await using var index = connection.CreateCommand();
            index.CommandText = """
                CREATE INDEX IF NOT EXISTS "IX_FollowUps_OpportunityId" ON "FollowUps" ("OpportunityId");
                CREATE INDEX IF NOT EXISTS "IX_Activities_OpportunityId" ON "Activities" ("OpportunityId");
                """;
            await index.ExecuteNonQueryAsync();
        }
        finally
        {
            if (closeWhenDone)
                await connection.CloseAsync();
        }
    }
}
