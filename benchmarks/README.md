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
Server 2025 about 98 ms away (ping), TLS for the login only. Three rounds; medians over every timed iteration.

| Scenario | sqlclient | MDS | sqlclient CPU / iteration | MDS CPU / iteration | MDS ÷ sqlclient CPU | sqlclient peak RSS | MDS peak RSS |
|---|---:|---:|---:|---:|---:|---:|---:|
| `connect` | 685 ms | 674 ms | 2.40 ms | 33.94 ms | 14x | 16 MB | 90 MB |
| `select1` | 101 ms | 102 ms | 0.15 ms | 8.13 ms | 53x | 15 MB | 94 MB |
| `param` | 109 ms | 102 ms | 0.17 ms | 9.98 ms | 58x | 15 MB | 98 MB |
| `rows` | 454 ms | 448 ms | 59.87 ms | 118.20 ms | 2x | 17 MB | 103 MB |
| `insert` | 10716 ms | 10734 ms | 15.28 ms | 77.02 ms | 5x | 15 MB | 92 MB |
| `batch` | 352 ms | 341 ms | 0.47 ms | 27.37 ms | 58x | 16 MB | 94 MB |
| `bigvalue` | 612 ms | 630 ms | 20.10 ms | 99.61 ms | 5x | 29 MB | 154 MB |
| `json` | 231 ms | 170 ms | 6.60 ms | 36.81 ms | 6x | 18 MB | 106 MB |
| `pool` | 3062 ms | 3098 ms | 22.34 ms | 195.60 ms | 9x | 18 MB | 106 MB |

Every scenario returned the same checksum from both clients. Wall times are within the network's noise for every
scenario: with a 98 ms round trip, each client spends nearly all its time waiting. `json` shows this well: run after run,
either client takes from 170 ms to 450 ms. sqlclient uses 2x (`rows`) to 50x (`select1`, `param`) less CPU per
operation, and 5–6x less peak memory. MDS's whole-process CPU, start-up and JIT included, is 0.6–2.5 s for each
scenario's run; sqlclient's is 0.02–0.4 s. Run it against a server on the same machine or network to see throughput
instead of latency.
