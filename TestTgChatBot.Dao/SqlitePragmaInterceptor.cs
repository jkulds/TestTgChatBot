using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TestTgChatBot.Dao;

public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        // WAL даёт конкурентное чтение, NORMAL снижает fsync, busy_timeout смягчает «database is locked»
        cmd.CommandText = @"
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA busy_timeout=5000;
            PRAGMA foreign_keys=ON;
            PRAGMA cache_size=-20000; -- ~20k страниц в кэше (~20k*4KB ≈ 80MB)
        ";

        cmd.ExecuteNonQuery();
        base.ConnectionOpened(connection, eventData);
    }
}