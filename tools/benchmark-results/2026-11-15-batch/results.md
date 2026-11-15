| Scenario | sqlclient median | MDS median | sqlclient p95 | MDS p95 | sqlclient CPU/iter | MDS CPU/iter | CPU ratio | sqlclient process CPU | MDS process CPU | sqlclient RSS | MDS RSS | same result |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| `batch`: the same 100 INSERTs as one batch (SqlBatch) | 352.34 ms | 341.29 ms | 359.68 ms | 356.19 ms | 0.47 ms | 27.37 ms | 58.0x | 0.01 s | 0.36 s | 16 MB | 94 MB | yes |
