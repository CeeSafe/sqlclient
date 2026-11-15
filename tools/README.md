# tools

Scripts, records and reproductions that go with the package but are not part of it.

| Path | What it is |
|---|---|
| `live-tests.sh` | Runs the tests offline, then live against SQL Server (`MSSQL_TEST_CONNECTION`) and Azure SQL with Entra ID (`MSSQL_AZURE_CONNECTION`, the token from `az`). Credentials come from the environment only. |
| `build-ceesafec.sh` | Builds ceesafec from a CeeSafe tag (default `v0.47.1`) into `ceesafec-<version>/`, which git ignores. `live-tests.sh` uses it when it is there. |
| `benchmark-results/` | Runs of `benchmarks/run.py` against SQL Server 2025, about 98 ms away, on 2026-11-15: the first one-round run of every scenario, then `batch` (100 INSERTs as one batch) and `stream` with `bigvalue` (a 16 MB value streamed). `benchmarks/README.md` has the three-round results. |
| `repros/finding-NN/` | The smallest programs that showed each CeeSafe gap or bug in `../CEESAFE_FINDINGS.md`, by its number, all fixed in ceesafec 0.47.0 (1 to 8) and 0.47.1 (9, 10). See below for what each gives with 0.47.1. |
| `probe/` | A small program over the package, used to try a connection by hand. |

With ceesafec 0.47.1 (`tools/ceesafec-0.47.1/ceesafec check <file>`, or `run --interp`):

| Finding | Checks | Is refused, as the fix intends |
|---|---|---|
| 1 `appendChar`/`appendUtf8` | `f1.cee` | |
| 2 `?? { return …; }` | `f2.cee` | |
| 3 re-binding a view-typed local | `f3.cee` | `f3bad.cee`: a view outliving its string, one escaping its function, a change while borrowed |
| 4 slice region diagnostic | | `f4.cee`: N0204 now says "write 'Slice<r, u8>'" |
| 5 `copyWithin` | `f5.cee` | `f5bad.cee`: on an immutable array; `f5panic.cee` checks and panics at run time (out of bounds) |
| 6 `Result<void, E>.ok()` | `is-ok-works.cee` | `ok-on-void.cee`, `generic-result.cee`, `generic-optional.cee`: N0226 at the call instead of a miscompile |
| 7 `x!;` as a statement | `f7.cee` | |
| 8 `==` on optionals | `f8.cee` | `f8bad.cee`: two non-`Copy` optionals |
| 9 `??` on a borrowed optional | `f9.cee` | |
| 10 cascaded N0235 | | `f10.cee`: N0262 alone |

```sh
tools/build-ceesafec.sh                         # once, if the installed ceesafec is older than 0.47.1
MSSQL_TEST_CONNECTION='Server=…;User Id=…;Password=…;TrustServerCertificate=true' \
MSSQL_AZURE_CONNECTION='Server=tcp:myserver.database.windows.net,1433;Database=sqlclient_test' \
    tools/live-tests.sh
tools/ceesafec-0.47.1/ceesafec check tools/repros/finding-09/f9.cee
```
