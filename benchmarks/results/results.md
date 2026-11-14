| Scenario | sqlclient median | MDS median | sqlclient p95 | MDS p95 | sqlclient CPU/iter | MDS CPU/iter | CPU ratio | sqlclient process CPU | MDS process CPU | sqlclient RSS | MDS RSS | same result |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| `connect`: open (TCP, TLS, login) and close, no pool | 684.66 ms | 673.50 ms | 716.29 ms | 731.52 ms | 2.40 ms | 33.94 ms | 14.2x | 0.04 s | 0.67 s | 16 MB | 90 MB | yes |
| `select1`: SELECT 1 on a warm connection | 100.51 ms | 101.96 ms | 116.34 ms | 119.09 ms | 0.15 ms | 8.13 ms | 52.9x | 0.02 s | 0.63 s | 15 MB | 94 MB | yes |
| `param`: a parameterized single-row lookup by primary key | 109.18 ms | 101.73 ms | 119.83 ms | 117.40 ms | 0.17 ms | 9.98 ms | 58.4x | 0.02 s | 0.73 s | 15 MB | 98 MB | yes |
| `rows`: read 100,000 rows of six types, every value | 454.40 ms | 447.52 ms | 872.86 ms | 815.82 ms | 59.87 ms | 118.20 ms | 2.0x | 0.38 s | 1.23 s | 17 MB | 103 MB | yes |
| `insert`: 100 parameterized INSERTs in a transaction | 10715.79 ms | 10733.85 ms | 10817.63 ms | 10868.80 ms | 15.28 ms | 77.02 ms | 5.0x | 0.07 s | 1.22 s | 15 MB | 92 MB | yes |
| `bigvalue`: a 1 MB nvarchar(max) parameter echoed back | 612.19 ms | 630.24 ms | 650.11 ms | 712.58 ms | 20.10 ms | 99.61 ms | 5.0x | 0.26 s | 1.98 s | 29 MB | 154 MB | yes |
| `json`: FOR JSON of 10,000 rows, joined | 230.70 ms | 169.91 ms | 283.12 ms | 181.50 ms | 6.60 ms | 36.81 ms | 5.6x | 0.10 s | 0.95 s | 18 MB | 106 MB | yes |
| `pool`: 16 tasks at once x 25 SELECT 1 through a pool of 16 | 3062.19 ms | 3097.68 ms | 3768.33 ms | 5880.66 ms | 22.34 ms | 195.60 ms | 8.8x | 0.17 s | 2.47 s | 18 MB | 106 MB | yes |
