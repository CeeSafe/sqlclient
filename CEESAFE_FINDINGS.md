# CeeSafe findings

Gaps, rough edges and bugs in CeeSafe (ceesafec 0.45.1) met while porting CosmoSQLClient's SQL Server driver.
Each entry says what happened, what was expected, and the workaround used in this package. All eight are fixed in
ceesafec 0.47.0 (`~/dev/CeeSafe/spec/decisions/0138-the-sqlclient-findings.md`). This package now requires 0.47.0
and uses the fixes: `?? { return …; }`, `var` views in `setServer` and the browser's parser, `copyWithin` for buffer
compaction, `appendChar`/`appendUtf8` for text, `==` on optionals and `x!!;` in the tests. Findings 9 and 10 are fixed in 0.47.1, which the package now requires.

## 1. No way to append already-valid UTF-8 bytes to a `String`

- **Fixed in ceesafec 0.47.0** (ADR 0138 §6): `s.appendChar(c)` and `s.appendUtf8(bytes)` on `String` and `Vector<u8>`, each `Result<bool, AllocError>`.
- **What:** a decoder that produces UTF-8 into a `Vector<u8>` (here, UTF-16 → UTF-8 for server messages) cannot hand
  those bytes to a `String` without `StringView.fromUtf8(bytes)!` re-validating them, and building straight into a
  `String` needs a temporary `[u8; 4]` plus a `fromUtf8` per character (cosmostack's `SqlCodePages.appendChar` does
  exactly that).
- **Expected:** something like `String.appendUtf8(bytes) -> Result<bool, AllocError>` (validating once, in bulk) or
  `s.appendChar(c: u32)` for a code point.
- **Workaround:** decode into a `Vector<u8>`, then `out.append(StringView.fromUtf8(v.view())!)` (`src/text.cee`,
  `Utf16.decodeInto`).

## 2. `??` does not take a diverging block

- **Fixed in ceesafec 0.47.0** (ADR 0138 §2): `x ?? { return Ok(null); }`, `?? { continue; }`, or a block ending in a value.
- **What:** `pos = skip(data, pos) ?? { return Ok(null); };` is a parse error (N0102, "expected expression, found
  '{'"). `return` is an expression of type `never` elsewhere (`Err(_) => { return …; }` arms), but not as `??`'s
  right side.
- **Expected:** `x ?? { return …; }`, or Rust's `let Some(p) = … else { return …; };`, for the common "unwrap or leave
  the function" step in parsers.
- **Workaround:** a four-line `match (…) { Some(p) => p, null => { return Ok(null); } }` each time, or a helper that
  returns an optional so `?` can be used (`skipNames` in `src/rows.cee`).

## 3. A view-typed local cannot be re-bound

- **Fixed in ceesafec 0.47.0** (ADR 0138 §4): a `var` may hold a `StringView` or `Slice<T>`; assignment re-binds it. `SliceMut<T>` and `borrow` references stay `let`.
- **What:** `var server = text.trim(); if (…) { server = server[4..].trim(); }` is N0246 ("a reference variable must be
  declared with 'let': references cannot be re-pointed"). Narrowing a `StringView` step by step, the usual way to
  parse a `host,port` or `tcp:` prefix, is therefore not possible.
- **Expected:** re-binding a `var` of type `StringView`/`Slice<T>` to another view of the same region (it is a fat
  pointer, not a `borrow T`); or at least a diagnostic that suggests the shape below.
- **Workaround:** a chain of `let`s with `if`/`match` expressions (`MsSqlConfig.setServer` in `src/config.cee`), or
  `usize` offsets into one view.

## 4. Misleading diagnostic for a region-bound slice field

- **Fixed in ceesafec 0.47.0** (ADR 0138 §7): N0204 now says "a slice's region comes first: write 'Slice<r, u8>'".
- **What:** in `struct LoginFields<region r>`, the field `sspi: Slice<u8, r>;` gives N0204 "'Slice' takes one type
  argument: 'Slice<T>'". The right spelling is `Slice<r, u8>` (region first), which the message does not mention.
- **Expected:** "a slice with a region is written `Slice<r, T>`", as N0270 already explains `struct S<region r>`.
- **Workaround:** `Slice<r, u8>`.

## 6. BUG: `Result<void, E>.ok()` (a `void?`) checks, then breaks the interpreter and the C backend

- **Fixed in ceesafec 0.47.0** (ADR 0138 §7): the call is N0226 at the call site, with a hint to use `isOk()`; the same for any generic instance whose signature would put `void` where it is not allowed.
- **What:** `ceesafec check` accepts it, but `run --interp` panics inside the compiler and `build` emits C that does
  not compile (`uint0_t`, a struct field of type `void`, failed layout assertions for `sizeof(void?)`).
- **Repro:**
  ```ceesafe
  fn f() -> Result<void, u32> => Ok();

  fn main() {
      let r = f().ok();
      Console.writeLine(r != null);
  }
  ```
  `ceesafec run --interp main.cee` → `ceesafe panic: index out of bounds at src/interpreter/memory.cee:40:23`;
  `ceesafec build main.cee` → `error: unknown type name 'uint0_t'`, `error: field has incomplete type 'void'`,
  `static assertion failed … sizeof(void?)`.
- **Expected:** either `void?` lowered as a bare tag (a `bool`), or the checker rejecting `ok()` on a `Result<void, E>`
  with a diagnostic pointing at `isOk()`.
- **Workaround:** `r!` or `r.isOk()` instead of `r.ok()!` in the tests.

## 7. `x!;` is not a statement

- **Fixed in ceesafec 0.47.0** (ADR 0138 §5): `x!;`, `x?;` and `f()!!;` are statements.
- **What:** `readRow(…).ok()!!;`, an unwrap kept only for its panic, is N0225 "only calls can be used as statements".
- **Expected:** an unwrap (`!`) as a statement, since its point is the check; minor.
- **Workaround:** `let _ = …!!;`.

## 8. An optional cannot be compared with `==`

- **Fixed in ceesafec 0.47.0** (ADR 0138 §3): `o == Some(3)`, `a != b` on optionals of a Copy type.
- **What:** `rows.int64(0, 0) != Some(1i64)` and `rows.text(0, 1) != Some("x")` are N0207 ("operator '!=' cannot be
  applied to type 'i64?'"), though `o == null` works and `i64`/`StringView` themselves compare.
- **Expected:** `==`/`!=` on `T?` when `T` has them (Rust's `Option<T>: PartialEq`), which tests use constantly.
- **Workaround:** small helpers (`isInt(o, v)`, `isText(o, v)` in `tests/live_tests.cee`) that match.

## 5. No overlapping copy inside one buffer (memmove)

- **Fixed in ceesafec 0.47.0** (ADR 0138 §6): `d.copyWithin(from, to, count)` on a `SliceMut<T: Copy>`, a mutable array or a vector.
- **What:** compacting a receive buffer (moving `v[from..]` to the front) has no library call: `copyFrom` needs two
  disjoint slices, and `Vector` has no `removeRange`/`drain` for a prefix.
- **Expected:** `s.copyWithin(src, dst, count)` (memmove) on `SliceMut<T: Copy>`, or `v.removeRange(lo, hi)`.
- **Workaround:** `shiftDown` in `src/connection.cee` copies in chunks of at most `from` bytes through
  `splitAtMut`, which keeps each source and destination disjoint.

## 9. `??` does not take a borrowed optional

- **Fixed in ceesafec 0.47.1** (ADR 0139 §2): `config.tls() ?? { return Err(MsSqlError.Config); }`, as `login` now writes it.
- **What:** with `fn tls(borrow this) -> borrow (TlsConfig?)`, `config.tls() ?? { return Err(e); }` is N0262 ("'??'
  needs an optional value on its left, found 'borrow (tls.TlsConfig?)'"), though `match (config.tls()) { Some(t) => t,
  null => … }` binds `t` as a `borrow TlsConfig`.
- **Expected:** `??` on a `borrow (T?)` as `match` treats it: the value is a `borrow T` (as `asBorrow()` gives).
- **Workaround:** the `match` (`MsSqlConnection.login` in `src/connection.cee`).

## 10. A cascaded N0235 after a bad left operand of `??`

- **Fixed in ceesafec 0.47.1** (ADR 0139 §3): N0262 alone.
- **What:** when the left of `x ?? { … }` is not an optional (finding 9), the block is reported again as N0235 "a
  block is an expression only as a branch of an 'if' or 'match' expression", which it is here.
- **Expected:** only the N0262; the block checked as a branch, or not at all, when the left side is an error.
