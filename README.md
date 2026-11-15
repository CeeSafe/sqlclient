# sqlclient

An async SQL Server client for CeeSafe, speaking TDS 7.4 (and TDS 8.0) directly: no ODBC, no native client. It is
a port of CosmoSQLClient's MSSQL driver (`CosmoSQLClient.MsSql` and `CosmoSQLClient.Tds.Protocol`), written fresh
for CeeSafe rather than line by line, with that driver's bugs fixed on the way (see [below](#changes-from-cosmosqlclient)).
Postgres has its own package, [PostgresClient](https://github.com/CeeSafe/PostgresClient).

```ceesafe
import sqlclient.MsSqlConfig;
import sqlclient.MsSqlConnection;
import sqlclient.MsSqlParams;

let config = MsSqlConfig.parse("Server=tcp:db.example.org,1433;Database=shop;User Id=app;Password=secret")?;
var db = await MsSqlConnection.connect(config)?;

var params = MsSqlParams.create();
params.text("alice@example.org")?;                // @p1
params.int32(10)?;                                // @p2
let rows = await db.query("SELECT TOP (@p2) id, total FROM orders WHERE email = @p1 ORDER BY id DESC", params)?;
for (r in 0..rows.rowCount()) {
    let id = rows.int64(r, 0)!;
    let total = rows.decimal(r, 1);               // an exact MsDecimal; null for SQL NULL
}

let changed = await db.execute("UPDATE carts SET seen = 1 WHERE email = @p1", params)?;
await db.batch("CREATE TABLE #work (id int)")?;   // no parameters: a plain batch
```

## Getting started

sqlclient needs [ceesafec](https://github.com/CeeSafe/CeeSafe) 0.47.1 or later, OpenSSL 3 (on macOS, Homebrew's
`openssl@3`), and the `tls` package from [CeeSafeLibs](https://github.com/CeeSafe/CeeSafeLibs), which it finds at
`../../CeeSafeLibs/tls`. Clone both into one directory, a level apart:

```sh
mkdir -p ~/src/apps && cd ~/src
git clone https://github.com/CeeSafe/CeeSafeLibs.git
git clone https://github.com/CeeSafe/sqlclient.git apps/sqlclient
cd apps/sqlclient && ceesafec fetch && ceesafec check
```

Then depend on it from your package:

```toml
[dependencies]
sqlclient = { path = "../sqlclient" }
```

Code that uses sqlclient runs natively (`ceesafec build`), not in the interpreter: TLS calls OpenSSL.

## Examples

Each example below is a function of [`examples/tour`](examples/tour/src/main.cee), which runs them all against a
server (temporary tables only):

```sh
cd examples/tour && ceesafec build
MSSQL_CONNECTION='Server=db,1433;Database=test;User Id=…;Password=…;TrustServerCertificate=true' out/tour
```

`db` is an open `MsSqlConnection`, and every function returns `Result<…, MsSqlError>`, so `?` passes errors on.

### Connecting

```ceesafe
let config = MsSqlConfig.parse("Server=tcp:db.example.org,1433;Database=shop;User Id=app;Password=…")?;
var db = await MsSqlConnection.connect(config)?;     // TCP, PRELOGIN, TLS, LOGIN7
let version = db.serverVersion();                    // version.major is 17 for SQL Server 2025
let encrypted = db.isEncrypted();                    // false with Encrypt=false: the login only
// …
await db.close()?;
```

For an availability group listener, add `MultiSubnetFailover=true`; for Azure SQL's serverless databases, which
take a while to resume, `ConnectRetryCount=3;ConnectRetryInterval=10`. A named instance (`Server=db\SQLEXPRESS`) is
looked up through SQL Server Browser. A URL works too:
`sqlserver://app:secret@db.example.org:1433?database=shop`.

### Azure SQL with Microsoft Entra ID

```ceesafe
var config = MsSqlConfig.parse("Server=tcp:myserver.database.windows.net,1433;Database=shop")?;
config.setAccessToken(token)?;                     // an access token for https://database.windows.net/
var why = String.create();
var db = match (await MsSqlConnection.connectExplained(config, mut why)) {
    Ok(c) => c,
    Err(e) => { /* why: the server's reason, e.g. a firewall rule missing */ return Err(e); }
};
```

The token is yours to get and renew (it lasts about an hour): `az account get-access-token --resource
https://database.windows.net/ --query accessToken -o tsv`, MSAL, or a managed identity's endpoint. `examples/query`
takes it from `MSSQL_ACCESS_TOKEN`.

### Typed parameters and values

```ceesafe
var p = MsSqlParams.create();
p.text("Zoë Ångström")?;                                 // @p1 nvarchar(4000)
p.decimal(MsDecimal.parse("1234.50")!)?;                  // @p2 decimal(38, 2), exact
p.dateTime(MsDateTime.of(2026, 11, 15, 9, 30, 0, 0))?;    // @p3 datetime2(7)
p.guid(MsGuid.parse("6f9619ff-8b86-d011-b42d-00c04fc964ff")!)?;
p.nullOf(MsSqlType.NVarChar)?;                            // @p5 a typed NULL
let added = await db.execute(
    "INSERT #orders (customer, total, placed, ref, note) VALUES (@p1, @p2, @p3, @p4, @p5)", p)?;

let none = MsSqlParams.create();
let rows = await db.query("SELECT id, customer, total, placed, ref, note FROM #orders", none)?;
for (r in 0..rows.rowCount()) {
    let id = rows.int64(r, 0) ?? 0;
    let customer = rows.text(r, 1) ?? "";      // UTF-8, whatever the column's collation
    let total = rows.decimal(r, 2)!;           // MsDecimal: total.writeText(mut s), total.toInt64()
    let placed = rows.dateTime(r, 3)!;         // MsDateTime: placed.year, placed.second, placed.writeText(mut s)
    let ref = rows.guid(r, 4)!;
    let noNote = rows.isNull(r, 5);
}
```

Every getter gives an optional, null for SQL NULL. `writeValue(r, c, mut s)` writes any cell as text.

### Named parameters, columns by name

```ceesafe
var p = MsSqlParams.create();
p.name("@who");
p.text("Zoë Ångström")?;
p.name("@least");
p.int32(100)?;
let rows = await db.query("SELECT COUNT(*) AS n FROM #orders WHERE customer = @who AND total >= @least", p)?;
let n = rows.int64(0, rows.columnIndex("n")!) ?? 0;
```

### Streaming a large result

```ceesafe
var batch = MsSqlRows.create();
var total: i64 = 0;
await db.start("SELECT id FROM dbo.big_table", none)?;
while (await db.fetch(mut batch, 1000)?) {         // up to 1,000 rows at a time, in one reused buffer
    for (r in 0..batch.rowCount()) {
        total += batch.int64(r, 0) ?? 0;
    }
}
await db.finish()?;

// Or stop part-way: cancel() sends an attention, and the connection stays usable.
await db.start("SELECT * FROM dbo.big_table", none)?;
let _ = await db.fetch(mut batch, 10)?;
await db.cancel()?;
```

### Streaming one large value

```ceesafe
await db.start("SELECT id, content, name FROM dbo.files", none)?;
var head = MsSqlRows.create();
var buffer = [0u8; 65536];
while (await db.nextRow(mut head)?) {              // the row up to its first (max) column
    let id = head.int64(0, 0);
    loop {
        let n = await db.readChunk(mut buffer[..])?;   // content, a packet at a time; readText(mut s) gives UTF-8
        if (n == 0) {
            break;
        }
        // write buffer[0..n] to a file
    }
    await db.restOfRow(mut head)?;                 // the columns after it: name
}
await db.finish()?;
```

A value of gigabytes needs no more memory than a packet: a 16 MB `varbinary(max)` streams with the process at
16 MB, as with `SELECT 1`. Skipping `readChunk` or `restOfRow` is fine; the next call passes over what is left.

### Pinning the server's certificate

```ceesafe
// Connection string: ServerCertificate=/etc/sqlclient/db.cer (PEM, DER or CER). Or from bytes:
var config = MsSqlConfig.parse("Server=db.internal;Database=shop;User Id=app;Password=…")?;
config.setServerCertificate(pemOrDer)?;            // only this certificate is accepted, name and chain unchecked
var db = await MsSqlConnection.connect(config)?;

var der = Vector<u8>.create();
let encrypted = db.serverCertificate(mut der)?;    // what a session got: save it to pin it next time
```

### Transactions and server errors

```ceesafe
await db.beginWith(MsIsolation.Serializable)?;     // begin() keeps the session's level
match (await db.execute("INSERT #accounts VALUES (@p1, @p2)", p)) {
    Ok(_) => { await db.commit()?; }
    Err(MsSqlError.Server) => {
        let e = db.lastError()!;                    // e.number 2627: a primary key violation
        // e.message, e.severity, e.state, e.procedure, e.line; db.errors() has them all
        if (db.inTransaction()) {
            await db.rollback()?;
        }
    }
    Err(e) => { return Err(e); }
}
```

A server error is reported after the rest of the response is read, so the connection stays usable.
`inTransaction()` follows the server, so a `BEGIN TRAN` in SQL counts too.

### Stored procedures, output parameters, PRINT

```ceesafe
var p = MsSqlParams.create();
p.name("@total");
p.int32(100)?;
p.name("@parts");
p.int32(7)?;
p.name("@each");
p.output();                                        // the next value is an OUTPUT parameter
p.nullOf(MsSqlType.Int)?;
let called = await db.callProcedure("dbo.split", p)?;
let out = called.outputs();                        // one row, a column per output parameter
let each = out.int64(0, out.columnIndex("each")!);
let status = called.returnStatus();                // the procedure's RETURN value
for (m in db.messages()) {                         // PRINT and RAISERROR below severity 11
    // m.message
}
```

Output parameters work with `queryAll` too: `SET @out = …` through `sp_executesql`.

### Several result sets, Markdown and JSON

```ceesafe
let all = await db.queryAll("SELECT 1 AS a, N'one' AS b; SELECT 2 AS a, SYSDATETIME() AS c;", none)?;
let first = all.result(0);                         // all.count() result sets, all.affectedRows()

var markdown = String.create();
first.writeMarkdown(mut markdown)?;                // | a | b | …

var json = String.create();
rows.writeJson(mut json)?;                         // [{"customer":"Zoë Ångström","total":1234.50}]

var built = String.create();                       // FOR JSON, its pieces joined
await db.queryJson("SELECT customer, total FROM #orders FOR JSON PATH", none, mut built)?;
```

### Batches: many commands, one round trip

```ceesafe
var batch = MsSqlBatch.create();
for (k in 0..100) {
    var p = MsSqlParams.create();
    p.int32(k)?;
    p.text("click")?;
    batch.add("INSERT dbo.events (id, kind) VALUES (@p1, @p2)", p)?;   // the batch keeps p
}
batch.addProcedure("dbo.refresh_totals", MsSqlParams.create())?;
let results = await db.executeBatch(batch)?;       // one request instead of 101 round trips
let inserted = results.affectedRows();
let last = results.command(100);                   // each command's result sets, outputs and return status
```

A failing command does not stop the ones after it; the call then fails with `Server`, and `db.errors()` has every
error.

### Table-valued parameters

```ceesafe
// CREATE TYPE dbo.IdList AS TABLE (id int PRIMARY KEY)
var ids = MsSqlTable.create();
ids.column("id", MsSqlType.Int)?;
for (id in 1..6) {
    ids.int32(id * id)?;
}
var p = MsSqlParams.create();
p.table("dbo.IdList", ids)?;                       // declared READONLY; columns in the type's order
let rows = await db.query("SELECT o.* FROM orders o JOIN @p1 i ON i.id = o.id", p)?;
```

`callProcedure` takes them too, for a procedure's `@ids dbo.IdList READONLY`.

### Bulk copy

```ceesafe
var rows = MsSqlTable.create();
rows.column("id", MsSqlType.Int)?;
rows.textColumn("kind", 20)?;                      // nvarchar(20); textColumn(name, 0) is nvarchar(max)
rows.column("at", MsSqlType.DateTime2)?;
rows.decimalColumn("amount", 12, 2)?;
for (k in 0..10000) {
    rows.int32(k)?;
    rows.text("click")?;
    rows.dateTime(MsDateTime.of(2026, 11, 15, 12, 0, 0, 0))?;
    rows.decimal(MsDecimal.ofInt(k as i64, 1))?;   // rescaled to the column's two places
}
var options = MsBulkOptions.defaults();
options.tableLock = true;                          // also checkConstraints, fireTriggers, keepNulls, keepIdentity
let loaded = await db.bulkInsert("dbo.events", rows, options)?;
```

Columns are matched by name, and the table's other columns get their defaults. Each value is checked against its
column as it is added (range, scale, length), so a bad value fails at `rows.int32(…)` and not halfway through a load.
For millions of rows, load in parts: `rows.clearRows()` and refill.

### Timeouts

```ceesafe
db.setCommandTimeout(1000);                        // milliseconds; Command Timeout=30 by default
match (await db.batch("WAITFOR DELAY '00:00:05'")) {
    Err(MsSqlError.TimedOut) => {}                 // the server was told to stop, and did
    Err(e) => { return Err(e); }
    Ok(_) => {}
}
let rows = await db.query("SELECT 'still usable'", none)?;
```

### A pool shared by tasks

```ceesafe
let pool = MsSqlPool.create(MsSqlConfig.parse(text)?, 4)?;    // or MsSqlPool.fromConfig: Max Pool Size
await squares(pool)?;

async fn squares(pool: borrow MsSqlPool) -> Result<void, MsSqlError> {
    await Async.scope(MsSqlError.OutOfMemory, (s) => {
        for (t in 0..8) {
            s.spawn(async move () => {
                var lease = await pool.acquire()?;     // waits while all four are leased
                var p = MsSqlParams.create();
                p.int32(t)?;
                let rows = await lease.connection().query("SELECT @p1 * @p1", p)?;
                return Ok();
            });                                        // the lease goes back here, its session reset
        }
    })?;
    return Ok();
}
```

### Reporting an error

```ceesafe
match (Async.run(run(text))) {
    Ok() => {}
    Err(e) => {
        var why = String.create();
        let _ = MsSqlErrors.explain(e, mut why);   // with OpenSSL's or the system's reason
        Console.writeErrorLine(why.view());
    }
}
```

[`examples/query`](examples/query/src/main.cee) is a small command-line client: it runs a batch and prints every
result set as tab-separated text, then the server's messages.

## What it does

- **Connecting.** `MsSqlConfig.parse` reads ADO.NET connection strings (`Server`, `Database`, `User Id`, `Password`,
  `Encrypt`, `TrustServerCertificate`, `Connect Timeout`, `Command Timeout`, `Packet Size`, `ApplicationIntent`,
  `Max Pool Size`, `Min Pool Size`, `Connection Lifetime`, `IPAddressPreference`, …, quoted values included) and
  `sqlserver://user:pass@host:port/instance?database=…` URLs.
  - Keys match as in ADO.NET, ignoring case and spaces. ADO.NET keys that change nothing here (`Pooling`, `Enlist`,
    `Persist Security Info`, …) are accepted. Those that ask for a feature this client lacks
    (`MultipleActiveResultSets=true`, `Column Encryption Setting=Enabled`, `Failover Partner`, named pipes) fail with
    `MsSqlError.Unsupported`, and `Authentication=ActiveDirectory…` with
    `UnsupportedAuthentication`. A key ADO.NET does not have fails with `Config`, as its "Keyword not supported".
  - The server is `host`, `host,port`, `tcp:host,port`, `[v6],port`, or `host\instance`; a named instance's port is
    asked of SQL Server Browser (UDP 1434).
  - Every address a name resolves to is tried in order (`IPAddressPreference` picks IPv4 or IPv6 first), or all at
    once with `MultiSubnetFailover=true`, for an availability group listener in several subnets: the first to
    answer is used and the others are cancelled. Azure SQL's gateway redirect is followed.
  - A transient login error (an Azure SQL database starting, paused or moving: 4060, 40613, 40197, …) is tried
    again `ConnectRetryCount` times (1 by default), `ConnectRetryInterval` apart (10 s).
  - **Session recovery** (idle connection resiliency). With `ConnectRetryCount` above 0, the login asks for TDS's
    session recovery, and the connection follows the session's state as the server reports it. If the server or the
    network drops the connection while it is idle (a restart, a failover, a `KILL`), the next call logs in again
    with that state, so the database, the language, the `SET` options and the rest are as they were, and then runs:
    the caller sees nothing but `db.recoveries()` going up. A connection in a transaction, or holding state the
    server cannot recover, is not recovered, and its call fails.
  - Each attempt is bounded by the connect timeout (15 s).
- **Encryption.**
  - `Encrypt=true` (the default) encrypts the whole session, with the TLS handshake carried in PRELOGIN packets.
  - `Encrypt=false` encrypts the login only (or nothing, if the server cannot).
  - `Encrypt=strict` is TDS 8.0: TLS first, ALPN `tds/8.0`.
  - The server's certificate is checked against the system roots and the host name (or `HostNameInCertificate`),
    unless `TrustServerCertificate=true`.
  - `ServerCertificate=<file>` (PEM, DER or CER), or `config.setServerCertificate(bytes)`, pins the certificate:
    the server must present exactly it, which suits a self-signed certificate better than trusting any.
    `db.serverCertificate(mut der)` gives the one a session got, to pin on first use.
  - TLS is OpenSSL 3 through the `tls` package's `TlsEngine`; each TDS packet goes in a TLS record of its own, as
    SQL Server requires.
- **Logins.** SQL logins, Windows logins with NTLMv2 (`Integrated Security=true` or `Domain=…`, with a user name
  and password; checked against MS-NLMP's test vectors), and Microsoft Entra ID with an access token
  (`config.setAccessToken(token)`, as SqlConnection.AccessToken: TDS's federated authentication, the token in
  LOGIN7's FEDAUTH feature). `connectExplained(config, mut why)` gives the server's reason when it refuses a login. The login asks for ODBC behaviour (ANSI_NULLS,
  QUOTED_IDENTIFIER and the rest on), as SqlClient does.
- **Queries.**
  - `query` gives the first result set, `queryAll` every result set with output parameters, `execute` the rows
    changed, `batch` runs a parameterless script, `queryJson` joins a `FOR JSON` result.
  - `callProcedure(name, params)` calls a stored procedure with output parameters (`params.output()`) and gives its
    `RETURN` value.
  - `executeBatch(batch)` sends a `MsSqlBatch` of statements and procedure calls, each with its own parameters, as
    one request (SqlBatch), and gives each command's results.
- **Parameters.** `MsSqlParams` sends each value typed, through `sp_executesql`, never spliced into SQL:
  - the integer types, `bit`, `float`/`real`, `decimal(38, s)`, `money`;
  - `nvarchar(4000)`/`(max)`, `varchar` in the server's code page (to match `varchar` columns' indexes),
    `varbinary`;
  - `uniqueidentifier`, `date`, `time(7)`, `datetime2(7)`, `datetimeoffset(7)` (or another scale, with
    `timeWithScale`, `dateTimeWithScale`, `dateTimeOffsetWithScale`), legacy `datetime` and `smalldatetime`;
  - `xml`;
  - typed NULLs.

  Unnamed values are `@p1`, `@p2`, …; `params.name("@id")` names the next one. Names are checked, since they go into
  the declaration list.
- **Results.** A `MsSqlRows` keeps all of a result's cells in one buffer: text as UTF-8 (from UTF-16 or any
  single-byte code page, UTF-8 collations included), `(MAX)` values joined, the rest as sent.
  - Getters decode where they are called: `text`, `bytes`, `int64`, `int32`, `bool`, `float64`, `decimal`
    (`MsDecimal`, exact to 38 digits), `guid`, `dateTime` (`MsDateTime`, 100 ns, with the offset).
  - `writeValue` gives any value as text; `writeJson` and `writeMarkdown` give the whole result.
  - `describeResult(sql, params)` describes a query's result without running it, as `GetSchemaTable` with
    `KeyInfo`: each column's type, nullability, source table and column, identity and key flags, and the hidden key
    columns that tell rows apart.
  - Every SQL Server type is read, `sql_variant`, `xml`, CLR types (as bytes) and the old `text`/`ntext`/`image`
    included.
- **Streaming.** For results too large to hold:
  - `start(sql, params)`, then `fetch(mut rows, n)` up to n rows at a time, `nextResult()`, and `finish()`.
  - `nextRow(mut head)` reads a row up to its first `(max)` value, which `readChunk(buffer)` (bytes as sent) or
    `readText(mut s)` (UTF-8, characters kept whole across pieces) then give piece by piece, as `SequentialAccess`
    with `GetStream` or `GetTextReader` does; `restOfRow(mut head)` reads the columns after it.
  - `cancel()` stops early, by an attention; the connection stays usable.
  - Reading is incremental, packet by packet: a value split across packets is resumed, not re-read from the start.
- **Errors.**
  - `MsSqlError.Server` after the response is read to its end, so the connection stays usable.
  - `errors()` has every ERROR token (number, state, severity, message, procedure, line), `lastError()` the first.
    `PRINT` and other informational messages are in `messages()`.
  - `MsSqlErrors.explain` adds OpenSSL's or the system's reason to a TLS or socket error.
- **Timeouts.** Every operation runs under the command timeout (30 s). When it expires the client sends an
  attention, the server stops the statement, and the client reads up to its acknowledgement: the call fails with
  `TimedOut` and the connection stays usable. A cancellation from outside leaves it `Broken`; `recover()` tries the
  same attention.
- **Transactions.** `begin()`, `beginWith(MsIsolation.Snapshot)`, `commit()`, `rollback()`, and `inTransaction()`,
  which follows the server's transaction descriptor, so `BEGIN TRAN` in SQL counts too.
- **Pool.** `MsSqlPool.fromConfig(config)` shares up to `Max Pool Size` connections between tasks, on any worker.
  - `var lease = await pool.acquire()?;` then `lease.connection()`.
  - `await pool.warmUp()?` opens `Min Pool Size` connections ahead of need. `Connection Lifetime` sets how long a
    connection is reused (an hour by default; 0 for no limit).
  - A connection that failed, is mid-stream, has a transaction open, or is past its lifetime (an hour) is dropped
    instead of reused. One idle over five minutes is replaced, and so is one the server closed while it sat in the
    pool (a restart, a failover, a `KILL`): `isAlive()` finds that without waiting, and the lease gets a new one.
  - A reused connection's session is reset with its next request (TDS's RESETCONNECTION flag:
    temporary tables, `SET` options and the isolation level as new).
- **Table-valued parameters and bulk copy.** `MsSqlTable` holds typed columns and rows. `params.table(type, rows)`
  sends them as a table-valued parameter, and `bulkInsert(table, rows, options)` loads them as SqlBulkCopy does
  (`INSERT BULK`, then the rows in TDS's bulk format), with `TABLOCK`, `CHECK_CONSTRAINTS`, `FIRE_TRIGGERS`,
  `KEEP_NULLS` and `KEEPIDENTITY`.
- **Backups.** `backupDatabase` and `restoreDatabase`, with the name and the path passed as parameters.

## Changes from CosmoSQLClient

Bugs and bottlenecks in the C# driver that this port does not carry over:

- Every unnamed parameter was named `@p`, so a query with two of them failed; here they are `@p1`, `@p2`, ….
- A `DateTimeOffset` parameter was declared `DATETIMEOFFSET` but sent as `DATETIME`, losing the offset and all but
  1/300 s; a `DateTime` was sent as `DATETIME`. Here they are `datetimeoffset(7)` and `datetime2(7)`.
- Decimals were declared `DECIMAL(38,10)` and sent as text; here they are `decimal(38, s)` values, exact.
- Every string went as `NVARCHAR(MAX)`, which defeats plan reuse and seeks on `varchar` columns; here it is
  `nvarchar(4000)` when it fits, and `varchar` on request.
- The login's option flags set INIT_LANG_FATAL where they meant ODBC, so sessions ran with ANSI_NULLS and
  QUOTED_IDENTIFIER off (indexed views and filtered indexes then fail); the driver then sent `SET TEXTSIZE` to make
  up for part of it. Here the ODBC flag is set.
- `Encrypt=false` skipped TLS entirely, even for the login, which servers that answer ENCRYPT_OFF expect encrypted.
- The server's packet size was never taken up (4096 always), and VARCHAR text was read as Latin-1 whatever the
  collation; here the negotiated size and the collation's code page (or UTF-8) are used.
- Named instances set the port to 0 and then fell back to 1433, without asking SQL Server Browser.
- Connection strings were split on `;` without regard to quotes, so a password with `;` broke them.
- `BACKUP`/`RESTORE` spliced the database name and path into SQL.
- A timeout or an abandoned stream closed the connection; here an attention keeps it.
- Results were reassembled into one buffer per response and decoded to objects per cell; here rows are decoded
  incrementally as packets arrive, into one buffer per result, with no allocation per row or value.

## Not here

- The ADO.NET (`DbConnection`) and Entity Framework Core surfaces, and `QueryAsync<T>`'s reflection mapping.
  CeeSafe has no reflection; read cells with the typed getters.
- Kerberos, and getting Entra ID tokens: `Authentication=ActiveDirectory…` (password, service principal, managed
  identity, interactive, device code, default) is `UnsupportedAuthentication`; get the token yourself and call
  `setAccessToken`. Always Encrypted, MARS, `sql_variant`, CLR type and fixed `char`
  parameters (send `nvarchar` or `varbinary`, which the server converts), the UTF-8 feature extension, and the
  double-byte code pages (932, 936, 949, 950) for `varchar` (they read as U+FFFD past ASCII).
- MySQL and Oracle: CosmoSQLClient's other drivers are for separate packages.

## Tests

```sh
ceesafec test                                   # unit tests; the live tests pass without running
MSSQL_TEST_CONNECTION='Server=host,1433;Database=sqlclient_test;User Id=sa;Password=…;TrustServerCertificate=true' \
    ceesafec test                               # everything, against a real server
MSSQL_TEST_BACKUP_PATH=/var/opt/mssql/data/t.bak …  # also the backup test, which writes that file on the server
MSSQL_TEST_CONNECTION='Server=tcp:myserver.database.windows.net,1433;Database=…' \
MSSQL_TEST_ACCESS_TOKEN="$(az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv)" \
    ceesafec test                               # everything, against Azure SQL with Entra ID
```

The live tests pass against SQL Server 2025 with a SQL login and against Azure SQL Database with an Entra ID token.

The unit tests cover MD4, HMAC-MD5 and NTLMv2 (against the RFCs' and MS-NLMP's vectors), UTF-16 and code pages,
decimals, dates and GUIDs, connection strings, PRELOGIN and LOGIN7, the token decoder (every prefix of a row and of
its metadata), `sql_variant`, and the request writer.

The live tests (checked against SQL Server 2025) cover:

- logins, a wrong password, and optional encryption;
- every type each way, injection-shaped text, and 10 MB values;
- multiple result sets, `PRINT`, procedures with output parameters and a return status, and `sp_executesql` outputs;
- errors mid-result, transactions, and 200,000 streamed rows with an early cancel;
- a command timeout answered by an attention, the pool's session reset and warm-up, a pooled connection replaced
  after a `KILL`, transient login errors retried, a killed session recovered with its `SET` options and language
  (and not in a transaction, nor with `ConnectRetryCount=0`), and `FOR JSON`;
- table-valued parameters and bulk copies of fourteen column types, NULLs, defaults, 20,000 rows, and a refused load;
- a batch of 102 commands (inserts, two result sets, a procedure's output), and one with a failing command inside;
- a 6 MB `nvarchar(max)` of emoji streamed as UTF-8 with every character whole, a NULL, a skipped value;
- scaled `time`/`datetime2`/`datetimeoffset`, `smalldatetime` and `xml` parameters, a join's result described with
  its source tables and keys, and a pinned certificate accepted (from bytes and from a file) and a wrong one refused.

## Benchmarks

[`benchmarks/`](benchmarks/README.md) runs the same eight scenarios with sqlclient and with Microsoft.Data.SqlClient
6.1.1 on .NET 10. Against a server 98 ms away, both took the same wall time (each spends it waiting on the
network). sqlclient used 2x to 63x less CPU per operation and about a sixth of the memory:

| Scenario | sqlclient CPU / op | Microsoft.Data.SqlClient CPU / op | Peak RSS |
|---|---:|---:|---:|
| open a connection (TLS, login) | 2.0 ms | 40 ms | 16 MB vs 90 MB |
| `SELECT 1` | 0.10 ms | 6.1 ms | 15 MB vs 97 MB |
| a parameterized lookup | 0.11 ms | 7.2 ms | 15 MB vs 100 MB |
| 100,000 rows, six types | 24 ms | 53 ms | 17 MB vs 105 MB |
| 1 MB `nvarchar(max)` round trip | 12 ms | 64 ms | 28 MB vs 147 MB |
| 16 tasks × 25 queries through a pool | 15 ms | 122 ms | 19 MB vs 105 MB |
| 100 INSERTs as one batch (`SqlBatch`) | 0.46 ms | 26 ms | 16 MB vs 94 MB |
| a 16 MB `varbinary(max)` streamed (`GetStream`) | 13 ms | 79 ms | 15 MB vs 102 MB |


[`CEESAFE_FINDINGS.md`](CEESAFE_FINDINGS.md) lists the gaps and bugs in CeeSafe met during the port; all are fixed
in ceesafec 0.47.0 and 0.47.1.

## Licence

GPL-2.0 only (`COPYING`), with an exception for linking OpenSSL (`COPYING.OPENSSL`).
