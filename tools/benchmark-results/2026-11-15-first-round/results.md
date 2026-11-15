| Scenario | sqlclient median | MDS median | sqlclient p95 | MDS p95 | speed-up | sqlclient CPU/iter | MDS CPU/iter | sqlclient RSS | MDS RSS | same result |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| `connect`: open (TCP, TLS, login) and close, no pool | 695.28 ms | 704.67 ms | 709.92 ms | 760.26 ms | 1.01x | 4.2 ms | 59.2 ms | 16 MB | 90 MB | yes |
| `select1`: SELECT 1 on a warm connection | 101.41 ms | 99.98 ms | 108.91 ms | 111.03 ms | 0.99x | 0.4 ms | 14.9 ms | 15 MB | 94 MB | yes |
| `param`: a parameterized single-row lookup by primary key | 105.24 ms | 109.42 ms | 115.76 ms | 121.67 ms | 1.04x | 0.7 ms | 16.2 ms | 15 MB | 98 MB | yes |
| `rows`: read 100,000 rows of six types, every value | 417.35 ms | 427.23 ms | 451.19 ms | 439.50 ms | 1.02x | 63.3 ms | 226.7 ms | 16 MB | 102 MB | yes |
| `insert`: 500 parameterized INSERTs in a transaction | 52424.44 ms | 52559.70 ms | 52497.75 ms | 52764.30 ms | 1.00x | 80.0 ms | 550.0 ms | 15 MB | 97 MB | yes |
| `bigvalue`: a 1 MB nvarchar(max) parameter echoed back | 663.30 ms | 639.52 ms | 731.80 ms | 2175.92 ms | 0.96x | 23.3 ms | 151.7 ms | 29 MB | 150 MB | yes |
| `json`: FOR JSON of 10,000 rows, joined | 228.31 ms | 290.20 ms | 245.52 ms | 447.72 ms | 1.27x | 8.3 ms | 82.5 ms | 18 MB | 104 MB | yes |
| `pool`: 16 tasks at once x 25 SELECT 1 through a pool of 16 | 2939.48 ms | 3077.53 ms | 2978.12 ms | 5617.92 ms | 1.05x | 43.3 ms | 423.3 ms | 18 MB | 104 MB | yes |
