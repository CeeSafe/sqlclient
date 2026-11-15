// The Microsoft.Data.SqlClient side of the benchmark against sqlclient (benchmarks/run.py runs both).
// Usage: Bench <scenario> <warmup> <iterations>, with the connection string in MSSQL_CONNECTION.
// Prints one `sample <nanoseconds>` line per timed iteration, the CPU time of the timed iterations as
// `cpu <nanoseconds>`, then `check <value>`, as the CeeSafe side does; each
// scenario does the same work, reads the same values as their types, and folds them into the same checksum.
using System.Data;
using System.Diagnostics;
using System.Text;
using Microsoft.Data.SqlClient;

const int PoolTasks = 16;
const int PoolQueries = 25;
const int Inserts = 100;

var connectionString = Environment.GetEnvironmentVariable("MSSQL_CONNECTION")
    ?? throw new InvalidOperationException("set MSSQL_CONNECTION");
var scenario = args.Length > 0 ? args[0] : "select1";
var warmup = args.Length > 1 ? int.Parse(args[1]) : 3;
var iterations = args.Length > 2 ? int.Parse(args[2]) : 20;

// No pool for the single-connection scenarios, as sqlclient's MsSqlConnection has none; the pool scenario uses
// ADO.NET's own pool, of the same size as sqlclient's MsSqlPool.
var plain = new SqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
var pooled = new SqlConnectionStringBuilder(connectionString) { Pooling = true, MaxPoolSize = PoolTasks }.ConnectionString;

if (scenario == "setup")
{
    Console.WriteLine("check unsupported: run the CeeSafe side's setup");
    return;
}

await using var db = new SqlConnection(plain);
await db.OpenAsync();
if (scenario == "insert" || scenario == "batch")
{
    await using var create = new SqlCommand("CREATE TABLE #bench_insert (id int, name nvarchar(100), amount decimal(18,2))", db);
    await create.ExecuteNonQueryAsync();
}
var big = new StringBuilder(1024 * 1024).Insert(0, "ab12CD34", 1024 * 1024 / 8).ToString();

ulong check = 0;
for (var i = 0; i < warmup; i++)
{
    check = await Once(scenario, (ulong)i);
}
var output = new StringBuilder();
var cpuStarted = Process.GetCurrentProcess().TotalProcessorTime;
for (var i = 0; i < iterations; i++)
{
    var started = Stopwatch.GetTimestamp();
    check = await Once(scenario, (ulong)i);
    var elapsed = Stopwatch.GetElapsedTime(started);
    output.Append("sample ").Append((long)(elapsed.Ticks * 100)).Append('\n');
}
var cpuUsed = Process.GetCurrentProcess().TotalProcessorTime - cpuStarted;
output.Append("cpu ").Append((long)(cpuUsed.Ticks * 100)).Append('\n');
output.Append("check ").Append(check).Append('\n');
Console.Write(output);

async Task<ulong> Once(string name, ulong i)
{
    switch (name)
    {
        case "connect":
        {
            await using var fresh = new SqlConnection(plain);
            await fresh.OpenAsync();
            return 1;
        }
        case "select1":
        {
            await using var cmd = new SqlCommand("SELECT 1", db);
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            return (ulong)reader.GetInt32(0);
        }
        case "param":
        {
            await using var cmd = new SqlCommand("SELECT id, name FROM dbo.bench_rows WHERE id = @p1", db);
            cmd.Parameters.Add("@p1", SqlDbType.Int).Value = (int)(i % 100000) + 1;
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            return (ulong)reader.GetInt32(0) + (ulong)Encoding.UTF8.GetByteCount(reader.GetString(1));
        }
        case "rows":
        {
            ulong sum = 0;
            await using var cmd = new SqlCommand("SELECT id, name, amount, created, flag, tag FROM dbo.bench_rows ORDER BY id", db);
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
            while (await reader.ReadAsync())
            {
                sum += (ulong)reader.GetInt32(0);
                sum += (ulong)Encoding.UTF8.GetByteCount(reader.GetString(1)); // sqlclient's text length is in UTF-8 bytes
                var amount = reader.GetDecimal(2);
                sum += amount == decimal.Truncate(amount) ? (ulong)(long)amount : 1;
                sum += (ulong)reader.GetDateTime(3).Second;
                sum += reader.GetBoolean(4) ? 1UL : 0UL;
                _ = reader.GetGuid(5);
            }
            return sum;
        }
        case "insert":
        {
            await using var tx = (SqlTransaction)await db.BeginTransactionAsync();
            await using var cmd = new SqlCommand("INSERT #bench_insert (id, name, amount) VALUES (@p1, @p2, @p3)", db, tx);
            var id = cmd.Parameters.Add("@p1", SqlDbType.Int);
            var text = cmd.Parameters.Add("@p2", SqlDbType.NVarChar, 4000);
            var amount = cmd.Parameters.Add("@p3", SqlDbType.Decimal);
            amount.Precision = 38;
            amount.Scale = 2;
            for (var k = 0; k < Inserts; k++)
            {
                id.Value = k;
                text.Value = "a row of the insert benchmark";
                amount.Value = 12345.67m;
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.RollbackAsync();
            return Inserts;
        }
        case "batch":
        {
            await using var tx = (SqlTransaction)await db.BeginTransactionAsync();
            await using var batch = new SqlBatch(db, tx);
            for (var k = 0; k < Inserts; k++)
            {
                var command = new SqlBatchCommand("INSERT #bench_insert (id, name, amount) VALUES (@p1, @p2, @p3)");
                command.Parameters.Add(new SqlParameter("@p1", SqlDbType.Int) { Value = k });
                command.Parameters.Add(new SqlParameter("@p2", SqlDbType.NVarChar, 4000) { Value = "a row of the insert benchmark" });
                command.Parameters.Add(new SqlParameter("@p3", SqlDbType.Decimal) { Precision = 38, Scale = 2, Value = 12345.67m });
                batch.BatchCommands.Add(command);
            }
            var affected = await batch.ExecuteNonQueryAsync();
            await tx.RollbackAsync();
            return (ulong)affected;
        }
        case "stream":
        {
            await using var cmd = new SqlCommand("SELECT CAST(REPLICATE(CAST('0123456789abcdef' AS varchar(max)), 1048576) AS varbinary(max))", db);
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
            var buffer = new byte[65536];
            ulong total = 0;
            while (await reader.ReadAsync())
            {
                await using var stream = reader.GetStream(0);
                int n;
                while ((n = await stream.ReadAsync(buffer)) > 0)
                {
                    total += (ulong)n;
                }
            }
            return total;
        }
        case "bigvalue":
        {
            await using var cmd = new SqlCommand("SELECT @p1", db);
            cmd.Parameters.Add("@p1", SqlDbType.NVarChar, -1).Value = big;
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            return (ulong)reader.GetString(0).Length;
        }
        case "json":
        {
            var text = new StringBuilder();
            await using var cmd = new SqlCommand("SELECT TOP (10000) id, name, amount FROM dbo.bench_rows ORDER BY id FOR JSON PATH", db);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                text.Append(reader.GetString(0));
            }
            return (ulong)Encoding.UTF8.GetByteCount(text.ToString());
        }
        case "pool":
        {
            var tasks = new Task[PoolTasks];
            for (var t = 0; t < PoolTasks; t++)
            {
                tasks[t] = Task.Run(async () =>
                {
                    for (var q = 0; q < PoolQueries; q++)
                    {
                        await using var conn = new SqlConnection(pooled);
                        await conn.OpenAsync();
                        await using var cmd = new SqlCommand("SELECT 1", conn);
                        if ((int)(await cmd.ExecuteScalarAsync())! != 1)
                        {
                            throw new InvalidOperationException("SELECT 1");
                        }
                    }
                });
            }
            await Task.WhenAll(tasks);
            return PoolTasks * PoolQueries;
        }
        default:
            throw new ArgumentException($"unknown scenario {name}");
    }
}
