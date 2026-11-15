# sqlclient against Microsoft.Data.SqlClient

Two programs do the same work against the same SQL Server: `ceesafe/` with sqlclient, and `dotnet/` with
Microsoft.Data.SqlClient 6.1.1 on .NET 10. `run.py` builds both, runs each scenario in a process of its own for
each client, alternating which client goes first in each round, and checks that both read the same values.

```sh
CEESAFEC=ceesafec \
MSSQL_CONNECTION='server=host,1433;database=sqlclient_test;user id=…;password=…;TrustServerCertificate=True;' \
    python3 benchmarks/run.py --rounds 3
```

The connection string is read from the environment only; never write it into a file. The first run creates
`dbo.bench_rows` (100,000 rows: `int` key, `nvarchar(50)` with non-ASCII text, `decimal(18,2)`, `datetime2(7)`,
`bit`, `uniqueidentifier`). The results go to `results/results.md` and `results/results.json`.

## Scenarios

| Scenario | One iteration |
|---|---|
| `connect` | open a connection (TCP, PRELOGIN, TLS, LOGIN7) and close it; no pool on either side |
| `select1` | `SELECT 1` on an open connection |
| `param` | `SELECT id, name … WHERE id = @p1` with an `int` parameter |
| `rows` | every value of 100,000 rows, read as its type (MDS with `SequentialAccess`; sqlclient streams 4,096-row batches) |
| `insert` | 100 parameterized `INSERT`s into a temporary table in a transaction, rolled back |
| `batch` | the same 100 `INSERT`s as one batch: `MsSqlBatch`, and MDS's `SqlBatch` |
| `bigvalue` | a 1 MB `nvarchar(max)` parameter sent and read back |
| `stream` | a 16 MB `varbinary(max)` read in 64 KB pieces: `readChunk`, and `GetStream` with `SequentialAccess` |
| `json` | `FOR JSON PATH` over 10,000 rows, the pieces joined into one string |
| `pool` | 16 tasks at once, each leasing a connection 25 times from a pool of 16 and running `SELECT 1` |

Each program prints one `sample <ns>` per timed iteration, `cpu <ns>` for the timed iterations (process CPU time,
user + system, every thread, measured inside the process so that start-up and JIT compilation are left out), and
`check <n>`, a checksum of what it read. `run.py` also records each process's whole CPU time and peak resident
memory with `/usr/bin/time`.

## Reading the results

When the server is far away, a scenario that waits on round trips takes as long with either client, and the wall
time says little about the client. The CPU time per iteration is then the measure of the client's own cost, and
the whole-process CPU and memory are the cost of running it.

## Results, 2026-11-15

Apple Silicon Mac (macOS 27), sqlclient built with ceesafec 0.47.1, Microsoft.Data.SqlClient 6.1.1 on .NET 10, SQL
Server 2025 about 98 ms away (ping), the whole session encrypted. Three rounds; medians over every timed iteration.
`results/` holds this run; `../tools/benchmark-results/` the earlier ones.

| Scenario | sqlclient | MDS | sqlclient CPU / iteration | MDS CPU / iteration | MDS ÷ sqlclient CPU | sqlclient peak RSS | MDS peak RSS |
|---|---:|---:|---:|---:|---:|---:|---:|
| `connect` | 698 ms | 694 ms | 2.00 ms | 40.48 ms | 20x | 16 MB | 90 MB |
| `select1` | 107 ms | 105 ms | 0.10 ms | 6.06 ms | 60x | 15 MB | 97 MB |
| `param` | 104 ms | 110 ms | 0.11 ms | 7.24 ms | 63x | 15 MB | 100 MB |
| `rows` | 442 ms | 449 ms | 23.90 ms | 52.71 ms | 2x | 17 MB | 105 MB |
| `insert` | 10999 ms | 11050 ms | 9.02 ms | 42.16 ms | 5x | 15 MB | 101 MB |
| `batch` | 353 ms | 357 ms | 0.46 ms | 26.36 ms | 58x | 16 MB | 94 MB |
| `stream` | 792 ms | 790 ms | 13.43 ms | 78.58 ms | 6x | 15 MB | 102 MB |
| `bigvalue` | 610 ms | 767 ms | 12.34 ms | 64.23 ms | 5x | 28 MB | 147 MB |
| `json` | 168 ms | 161 ms | 7.41 ms | 45.51 ms | 6x | 18 MB | 109 MB |
| `pool` | 2966 ms | 3053 ms | 14.92 ms | 122.07 ms | 8x | 19 MB | 105 MB |

Every scenario returned the same checksum from both clients. Wall times are within the network's noise for every
scenario: with a 98 ms round trip, each client spends nearly all its time waiting (`json` and `bigvalue` swing by a
round trip or two between runs, for either client). `batch` shows what round trips cost: the same 100 INSERTs take
11 s one by one and 0.35 s as one batch, with either client. sqlclient uses 2x (`rows`) to 63x (`param`) less CPU per
operation, and about a sixth of the memory; `stream` keeps a 16 MB value at the process's baseline of 15 MB. MDS's
whole-process CPU, start-up and JIT included, is 0.4 to 1.7 s per scenario's run; sqlclient's is under 0.2 s. Run it
against a server on the same machine or network to see throughput instead of latency.
