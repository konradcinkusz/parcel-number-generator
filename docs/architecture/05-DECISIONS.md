# 05 — Decisions

> The choices made during this modernization, with the reasoning and the alternatives that
> were rejected. A document that says "we considered X and rejected it because Y" is worth
> more than one that lists commands (P14).

---

## D-1 — Rewrite rather than port

**Status:** accepted · 2026-08-15

The three legacy projects were replaced, not migrated file by file.

**Why.** The system is small — about 900 lines of behaviour, one table, one sentence of
domain logic. The parts a port would have preserved are the parts that were wrong: the pool
arithmetic (three separate defects), the search (one round trip per comparison, comparing row
indices against values), and the write path (no constraint, no transaction). What survives a
rewrite is the *specification*, and it fits in a sentence: issue a number from the pool,
never the same one twice, honouring exclusions.

**Rejected: incremental strangling.** It needs a facade to route through, and there was no
network boundary to put one at — the only interface was a WinForms form. It also needs the
old and new to run side by side, which .NET Framework 4.5.2 and .NET 10 cannot do in one
process.

**Cost.** The commit is large and does not bisect. Mitigated by the old tree remaining in
history at `c1258ed`, and by [`01-CURRENT-STATE.md`](01-CURRENT-STATE.md) recording what it
did before it was replaced.

---

## D-2 — An HTTP service, not a console application or a library

**Status:** accepted · 2026-08-15

**Why.** Number allocation is inherently shared state: the guarantee "never twice" only holds
across every caller, so there has to be exactly one arbiter. A library gives every consuming
process its own copy of the logic and its own connection to the table, which is precisely the
shape that made the duplicate-issue race possible. A service has one owner for the schema
(P3), one place to enforce the constraint, one place to rate-limit, and one place to observe.

It is also the shape the reference architecture is written for: a container, a health probe,
a Fly app, a tag-driven pipeline.

**Rejected: keeping the console benchmark harness.** Its purpose was to race six
implementations against each other. With one interface and three named strategies selected by
configuration, the useful version of that question is a load test against a real database
(Phase 9), not a `Stopwatch` in `Main`.

---

## D-3 — The WinForms UI was removed, not ported

**Status:** accepted · 2026-08-15

**Why.** `ParcelNumberGenerator.Win` is Windows-only, cannot be containerized, cannot run on
Fly, and its whole content is a form with two numeric ranges, a count, a connection-string
textbox and a progress bar. Every one of those is now a configuration key or a query
parameter. Porting it to Avalonia or MAUI would have been building a new application, not
preserving an old one.

The connection-string textbox is worth naming as its own reason: it put a database credential
in a UI control on an operator's desktop, with `Replace(@"\\", @"\")` applied to undo an
escaping problem it introduced itself. That is not a feature to carry forward.

**Consequence.** There is no graphical interface. `curl`, the OpenAPI document at
`/openapi/v1.json` in Development, or a caller's own system. If an operator UI is wanted
later, it is a separate frontend against a documented HTTP API — which is the estate's shape
for frontends anyway.

---

## D-4 — Insert-and-catch, not check-then-insert

**Status:** accepted · 2026-08-15

`TryReserveAsync` inserts the number and reports the duplicate-key failure as a lost race,
rather than querying first and inserting if absent.

**Why.** The check-then-act shape has a window between the two statements in which two
callers both see the number as free. No amount of care in application code closes it; only
the database can arbitrate. Making the number the primary key means the constraint is
enforced on every writer, including one that bypasses this service.

**Consequence.** The catch must distinguish a duplicate from a real failure, and the three
supported providers report a duplicate three different ways — `DbUpdateException` from a
relational provider, `InvalidOperationException` from the in-memory provider's change
tracker, and a bare `ArgumentException` from its table writer.

**Rejected: matching provider error codes** (`23505`, `2627`). It needs the store to know
which provider it is running on, which undoes the provider portability of P4, and it is
silently wrong when a provider is added. Instead the catch is broad and *verified*: it
re-reads the number, returns `false` only if the row is now present, and rethrows otherwise.
One extra query, only on the collision path.

**Rejected: `SELECT ... FOR UPDATE` or a serializable transaction.** Correct, but it
serializes every allocation behind one lock, and it does not work on the in-memory provider,
which would cost the credential-free clone (P8).

---

## D-5 — `adaptive` is the default strategy

**Status:** accepted · 2026-08-15

**Why.** This was a bug found by a test, not a design flourish. `RandomProbeAllocationStrategy`
with a 16-attempt budget cannot drain a pool: drawing the last of 50 numbers uniformly takes
about 50 attempts, so the strategy reports contention and the last few numbers can never be
issued. A batch of 50 from a pool of 50 failed, correctly.

The fix could have been a bigger attempt budget — but the budget that works at 50 numbers is
useless at ten million, because the cost is proportional to `1 / (1 - density)` and unbounded
as the pool fills.

**Why escalate on outcome rather than measure density.** `Contended` already means "free
numbers exist and probing did not find one" — exactly the condition a scan resolves.
Measuring density up front would add a `COUNT` to every allocation, including the
overwhelming majority that succeed on the first probe. Escalation is free on the happy path.

**Consequence.** The two component strategies remain individually selectable by name, so a
deployment that knows its density can pin the cheaper one.

---

## D-6 — Two migration assemblies, PostgreSQL and SQL Server

**Status:** accepted · 2026-08-15

**Why.** P4 makes the provider a configuration switch, and a migration set is provider-specific
DDL — one shared set cannot serve both. Two assemblies is the estate's existing shape
(`konradcinkusz/authservice`), and CI checks both for drift against the model.

**Cost.** Two projects and two `InitialCreate` files for a one-table schema, which is
disproportionate today. Accepted because the alternative — committing only the provider
currently in use — makes `DATABASE_PROVIDER=SqlServer` a configuration value that fails at
first boot rather than a supported choice, and that is worse than verbose.

---

## D-7 — Authentication is conditional, and Production is guarded

**Status:** accepted · 2026-08-15

JWT bearer validation registers only when `Jwt:Authority` is configured; without it the
endpoints are open. In Production the host refuses to start unless an issuer is configured or
`Security:AllowAnonymousAccess` is explicitly `true`.

**Why.** These pull against each other, and both matter. P8 wants `git clone && dotnet run`
to work with no cloud credentials — and it does. P5 wants authentication — and allocation
permanently consumes a finite resource, so an open endpoint is not merely readable, it is
drainable. The guard is where the two meet: the fallback exists, and production is where it
is not allowed silently.

**Why the opt-out exists at all.** A deployment on a private network with no public listener
is a legitimate shape. Making it impossible would push people to a worse workaround; making
it a named, verbose setting makes it visible in a diff.

**Rejected: minting tokens here.** This service holds no key material and validates against a
published JWKS. A symmetric secret shared for verification is also a signing key, and the
estate's most reliably recurring mistake.

---

## D-8 — The 1,001-line `CreateTable.sql` was not carried over

**Status:** accepted · 2026-08-15

It contained one `CREATE TABLE` and a thousand `INSERT` statements of sample numbers.

**Why.** The schema is now described by migrations. The thousand inserts are sample data, not
reference data — no behaviour depends on them, and they exist to make the old benchmark's
binary search have something to search. P4's rule is that migrations describe schema and
reference data is seeded separately; here there is no reference data at all.

**Consequence.** A developer wanting a pre-populated pool allocates into it:
`curl -X POST 'localhost:5180/parcel-numbers?count=1000'`. The file remains in history.

---

## D-9 — English, throughout

**Status:** accepted · 2026-08-15

The old README, and most doc comments, were Polish; the code mixed Polish identifiers
(`pierwszyElement`, `liczba`) with English ones.

**Why.** The reference architecture settles this: English for anything needed to build or
deploy. The repository is public and is measured against a cross-repo standard, so a reader
who does not read Polish should not be blocked at the README. The original Polish name
`LosowaniePaczek` is recorded in [`01-CURRENT-STATE.md`](01-CURRENT-STATE.md) §1 because it
explains the git history.
