| Scenario | sqlclient median | MDS median | sqlclient p95 | MDS p95 | sqlclient CPU/iter | MDS CPU/iter | CPU ratio | sqlclient process CPU | MDS process CPU | sqlclient RSS | MDS RSS | same result |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| `connect`: open (TCP, TLS, login) and close, no pool | 697.68 ms | 694.02 ms | 787.67 ms | 739.44 ms | 2.00 ms | 40.48 ms | 20.2x | 0.03 s | 0.67 s | 16 MB | 90 MB | yes |
| `select1`: SELECT 1 on a warm connection | 107.31 ms | 104.59 ms | 119.88 ms | 125.77 ms | 0.10 ms | 6.06 ms | 59.8x | 0.00 s | 0.47 s | 15 MB | 97 MB | yes |
| `param`: a parameterized single-row lookup by primary key | 104.03 ms | 109.99 ms | 121.15 ms | 122.55 ms | 0.11 ms | 7.24 ms | 63.3x | 0.01 s | 0.54 s | 15 MB | 100 MB | yes |
| `rows`: read 100,000 rows of six types, every value | 441.88 ms | 449.06 ms | 649.95 ms | 705.20 ms | 23.90 ms | 52.71 ms | 2.2x | 0.15 s | 0.71 s | 17 MB | 105 MB | yes |
| `insert`: 100 parameterized INSERTs in a transaction | 10998.93 ms | 11050.25 ms | 11322.62 ms | 11829.13 ms | 9.02 ms | 42.16 ms | 4.7x | 0.04 s | 0.78 s | 15 MB | 101 MB | yes |
| `batch`: the same 100 INSERTs as one batch (SqlBatch) | 352.66 ms | 356.88 ms | 566.09 ms | 448.64 ms | 0.46 ms | 26.36 ms | 57.6x | 0.00 s | 0.36 s | 16 MB | 94 MB | yes |
| `stream`: a 16 MB varbinary(max) read in 64 KB pieces (GetStream) | 792.22 ms | 789.98 ms | 879.61 ms | 886.66 ms | 13.43 ms | 78.58 ms | 5.9x | 0.08 s | 0.95 s | 15 MB | 102 MB | yes |
| `bigvalue`: a 1 MB nvarchar(max) parameter echoed back | 610.48 ms | 767.31 ms | 872.36 ms | 1514.23 ms | 12.34 ms | 64.23 ms | 5.2x | 0.15 s | 1.24 s | 28 MB | 147 MB | yes |
| `json`: FOR JSON of 10,000 rows, joined | 168.05 ms | 161.34 ms | 261.70 ms | 211.34 ms | 7.41 ms | 45.51 ms | 6.1x | 0.09 s | 0.85 s | 18 MB | 109 MB | yes |
| `pool`: 16 tasks at once x 25 SELECT 1 through a pool of 16 | 2965.58 ms | 3052.77 ms | 3164.88 ms | 5621.18 ms | 14.92 ms | 122.07 ms | 8.2x | 0.11 s | 1.67 s | 19 MB | 105 MB | yes |
