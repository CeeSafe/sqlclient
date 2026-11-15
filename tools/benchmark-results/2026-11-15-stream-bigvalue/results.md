| Scenario | sqlclient median | MDS median | sqlclient p95 | MDS p95 | sqlclient CPU/iter | MDS CPU/iter | CPU ratio | sqlclient process CPU | MDS process CPU | sqlclient RSS | MDS RSS | same result |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| `stream`: a 16 MB varbinary(max) read in 64 KB pieces (GetStream) | 808.70 ms | 824.07 ms | 835.71 ms | 885.53 ms | 17.28 ms | 123.81 ms | 7.2x | 0.11 s | 1.27 s | 16 MB | 102 MB | yes |
| `bigvalue`: a 1 MB nvarchar(max) parameter echoed back | 635.01 ms | 610.15 ms | 1300.25 ms | 679.10 ms | 14.48 ms | 80.99 ms | 5.6x | 0.22 s | 1.56 s | 29 MB | 148 MB | yes |
