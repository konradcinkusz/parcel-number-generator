# 01 — Current state

> What this repository was before the modernization, recorded from its source rather than
> from its README. Measured against
> [`architecture-standards/docs/architecture/00-REFERENCE-ARCHITECTURE.md`](https://github.com/konradcinkusz/architecture-standards/blob/master/docs/architecture/00-REFERENCE-ARCHITECTURE.md).
>
> Written at the point of the rewrite, describing the tree at commit `c1258ed`. It is kept
> because the gap analysis and the decisions record are only readable against it.

## 1. What PNG stands for

**PNG = ParcelNumberGenerator.** Nothing to do with the image format.

It issues parcel tracking numbers from a fixed pool, never issuing the same number twice, and
honouring a sub-range that is withheld from allocation. The repository was originally called
`LosowaniePaczek` — Polish for *parcel draw* — and the name survives in the git history, the
WinForms root namespace and the first entry of the old `.gitignore`.

## 2. The tree as it stood

```
ParcelNumberGenerator.sln          Visual Studio 14 (2015) format
├── ParcelNumberGenerator/         Console app — six generator implementations, ADO.NET
├── ParcelNumberGenerator.DAL/     Class library — EF6, three migrations, one entity
└── ParcelNumberGenerator.Win/     WinForms UI over the console project's classes
```

| Property | Value |
|---|---|
| Target framework | .NET Framework 4.5.2, all three projects |
| Package management | `packages.config`, restored to a solution-level `packages/` folder |
| Data access | Hand-written ADO.NET (`SqlConnection`, string-interpolated SQL) in the console project; Entity Framework 6 in the DAL |
| Tests | None |
| CI | None |
| Container | None |
| Documentation | `Readme.md`, 33 lines, Polish, describing five classes by name |
| Last substantive commit | 2018 |

The two data-access stacks never met. `ParcelNumberGenerator.DAL` defines a `UsedNumber`
entity with a surrogate `Id`, and three EF migrations that create it — and nothing
references the DAL project. The working code in the console and WinForms projects talks to a
different table, `USED_NUMBERS`, with a different shape (`usedNumber INT`, no key), created
by a hand-run `CreateTable.sql`. The EF model is a parallel, unused description of a schema
that does not match the one in use.

## 3. The domain, as implemented

Six classes implement `INumberPoolGenerator`, all doing the same job:

| Class | Distinguishing feature | Status in source |
|---|---|---|
| `NumberPoolDB` | The original | `[Obsolete(error: true)]` — will not compile if used |
| `NumberPoolDBv2` | "Most optimized in terms of C# code" | The base of everything else |
| `NumberPoolDatabase` | Chain of responsibility — five `Step` subclasses | — |
| `NumberPoolDBFunc` | The same conditions extracted into functions | — |
| `NumberPoolDBv2WithRangeOff` | Adds an excluded range | Used by the WinForms UI |
| `NumberPoolDBv2WithUBS` | Uniform binary search, iterative and recursive | Used by the WinForms UI |

`Program.cs` is a benchmark harness: it constructs all six, runs each *n* times, and prints
which was fastest. That is the actual purpose of the console application — the variants are
not alternatives a deployment would choose between, they are entries in a race.

The shared algorithm, in `NumberPoolDBv2.Generate()`:

1. Count the used numbers in the range (two `SELECT COUNT(*)` subqueries).
2. Compute the range size.
3. If the counts are equal, the pool is full — throw.
4. Otherwise draw a random number and binary-search the used table for it; repeat until the
   search misses.
5. Insert the number.

## 4. Defects in that algorithm

Recorded here rather than in the gap analysis because they are facts about the code, not
deviations from a standard. Each is covered by a test in the rewrite.

**D1 — Off-by-one in `ElementsInRange`.** It returns `second - first + 1` where both operands
are already counts of an inclusive range, so every pool reports one element more than it
holds. At the boundary this makes a pool with one number left report itself as full.

**D2 — The draw ignores the pool's lower bound.** `StockNumberFromSortedRange(range)` returns
`rand.Next(1, range)` — always from `1`, never from `range.Item1`, and never returning the
upper bound because `Random.Next` is exclusive at the top. A pool configured as `[500, 600]`
issues numbers in `[1, 100]`.

**D3 — The binary search compares a row index with a number.** `BinarySearch` walks
`ROW_NUMBER()` positions but compares the value at each position against the search target,
then returns `leftIndex - 1` on a hit. `NumberPoolDBv2WithRangeOff` extends the confusion by
testing `leftIndex` — a row position — against the excluded *number* range, so it skips
whichever rows happen to sit at those positions.

**D4 — One SQL round trip per comparison.** Every step of the binary search is its own
`SELECT` over a `ROW_NUMBER()` subquery with no index. A single allocation is O(log n) round
trips before the insert, and each of those queries is O(n) server-side.

**D5 — Nothing prevents a duplicate.** The table has no key, no unique index and no
constraint. The check and the insert are separate statements with no transaction, so two
callers that interleave between them are both told the number is free and both write it.
This is the defect that matters: the service exists to not issue a number twice, and its
schema permits exactly that.

**D6 — `new Random()` per draw.** Constructed inside `StockNumberFromSortedRange`, so on
.NET Framework — where the seed comes from a low-resolution tick count — a tight loop gets
the same seed repeatedly and therefore the same "random" number.

**D7 — `throw ex`.** In all four data-access copies, resetting the stack trace to the rethrow
point. The accompanying comment says logging is intended and it is not implemented.

**D8 — SQL built by string interpolation.** Table and column names, and the inserted value,
are interpolated into the command text. The values are internally generated integers today,
so this is not currently reachable as an injection — but the pattern is one parameter away
from being one, and it is in five files.

**D9 — Duplicated data access.** `GetDataFromDB`, `GetNumberFromDatabaseTableByRowId`,
`ElementsInRange` and `BinarySearch` are copied verbatim into `NumberPoolDB`,
`NumberPoolDBv2`, `NumberPoolDatabase.Step` and `DBOperation` — four copies of the same
thirty lines, each free to drift.

**D10 — `DBOperation.SaveNew()` writes a constant.** `private int generateNumber { get; } = 33;`
— the method inserts the literal 33 whatever the caller wanted.

## 5. Other findings

**Configuration is hardcoded, five times over.** The connection string
`Integrated Security=SSPI;Initial Catalog=ParcelNumberGenerator;Data Source=localhost\SQLEXPRESS;`
appears as a literal in five files, along with the table name, the column name and the pool
range. The range disagrees between classes: `1..10,000,000` in three, `1..100,000` in a
fourth. Which pool you get depends on which class you construct.

No credential is present — the string uses integrated authentication — so nothing needs
rotating. That is luck rather than design; nothing in the repository would have prevented a
password being pasted into the same literal.

**The WinForms UI is the only real user interface**, and it takes the connection string from
a textbox, applying `Replace(@"\\", @"\")` to undo an escaping problem it introduces itself.

**Reference data ships as schema.** `CreateTable.sql` is 1,001 lines: one `CREATE TABLE` and
a thousand literal `INSERT` statements of sample numbers.

**The README describes the class list**, not the system. It documents five implementations by
name — including the deprecated one — and says nothing about what a parcel number is, what
pool is issued from, or how the application is deployed.

## 6. Against the compliance checklist

Seventeen items in the reference architecture's §3 checklist. The count before this work:

| Answer | Count | Notes |
|---|---|---|
| Yes | 0 | — |
| No | 17 | — |

Not a criticism of the original — it was written in 2018, years before the architecture it is
now measured against, for a desktop deployment on a Windows workstation. The gap is a
statement of distance, not of negligence.

## 7. Mode: MODERNIZE, not RECOVER

The playbook's signals point both ways. The target framework is long out of support, which
its table maps to RECOVER — but the archaeology that mode exists for is not needed here:
every source file is present, every package is a public one still on nuget.org, the schema is
fully described by `CreateTable.sql` plus the EF migrations, and there is no live deployment
whose state has to be recovered. There is one small archaeological finding, recorded in §2:
the EF model and the working schema disagree, and the EF model is the one nobody uses.

No security-immediate document is needed either, because §5 found no credential in source or
history.

So: MODERNIZE, with the caveat that "incremental strangling" was never available. The
reasoning is in [`05-DECISIONS.md`](05-DECISIONS.md), D-1.
