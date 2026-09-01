# Contributing

Thanks for looking. This is a single-maintainer project, so the honest framing first:
**open an issue before writing a large change.** A pull request that arrives unannounced
and rewrites a subsystem is likely to be declined for a reason that one comment on an
issue would have surfaced in a minute. Small fixes — a bug, a typo, a test that should
have existed — need no preamble.

## Get it running

```bash
git clone https://github.com/konradcinkusz/parcel-number-generator
cd parcel-number-generator
./scripts/setup.sh
```

`setup.sh` checks your .NET SDK, installs the pre-commit secret-scan hook, restores
`dotnet-ef`, and runs the tests. It needs no credentials: each service falls back to an
in-memory database, so a fresh clone runs.

Then either the whole system at once, with Docker:

```bash
dotnet run --project src/ParcelNumberGenerator.AppHost
```

or one service at a time — the ports and the `curl`s are in the
[README](README.md#run-it).

## What CI will check

Everything below runs on your pull request. Run what you can locally; failing CI on
something `dotnet test` would have caught is the slow path.

```bash
dotnet test --solution ParcelNumberGenerator.slnx
dotnet format ParcelNumberGenerator.slnx --verify-no-changes --severity error
```

CI additionally builds all three images and asserts the startup guards still refuse an
unconfigured production host, checks both services' models against their committed
migrations for both providers, runs the architecture guards, scans full history for
secrets, and audits transitive dependencies for advisories.

**Warnings are errors**, including NuGet advisories. That is
[`Directory.Build.props`](Directory.Build.props), and it is deliberate: the code this
replaced compiled with warnings nobody read, which is how `throw ex` survived in four
files.

## The house rules

These are the ones that get a pull request sent back, so they are worth knowing before
you write rather than after.

**A schema change ships with a migration for both providers.** PostgreSQL and SQL Server,
in the same commit. CI fails a model that has drifted from its committed migrations, and
it checks both services:

```bash
scripts/generate-migrations.sh parcelnumbers AddSomething
scripts/generate-migrations.sh notifications AddSomethingElse
```

**Nothing is a literal in source.** Every value comes from configuration. If your change
needs a new knob, it gets a key, a default, and a line in
[`flyio/SECRETS.md`](flyio/SECRETS.md) if it is a secret. An invalid value should stop the
host at startup with a message naming the key — not produce a 500 on the first request in
production.

**No secret in source, config, or comment.** The pre-commit hook will stop you; if it
does, do not just unstage the file. If the value is real, rotate it first — the commit is
public the moment it is pushed, and scrubbing history without rotating is theatre.

**New behaviour has a test that fails without the change.** A test that passes before and
after documents nothing.

**A new dependency needs a reason in the pull request.**
[`ParcelNumberGenerator.Domain`](src/ParcelNumberGenerator.Domain/) has no package
references at all and that is a property worth keeping; the shared kernel has a size
ceiling CI enforces.

**If it moves away from a principle, record it.** The reference architecture is
[`konradcinkusz/architecture-standards`](https://github.com/konradcinkusz/architecture-standards).
A deliberate deviation goes in
[`docs/architecture/DEVIATIONS.md`](docs/architecture/DEVIATIONS.md) with a date and a
reason. An acknowledged deviation is a decision; an unacknowledged one is drift. The pull
request template asks about this and "none" is a valid answer — silence is not.

## Commits and pull requests

Present tense, imperative, and say what changed rather than which file you touched:
*Reject an allocation count above the pool's remaining capacity*, not *update
ParcelNumberEndpoints.cs*.

The [pull request template](.github/PULL_REQUEST_TEMPLATE.md) asks four things: what
changed, why, which principles it touches, and how you verified it. Fill it in — the "why"
is the part a reviewer cannot reconstruct from the diff, and it is the part that is still
useful in two years.

Branch off `master` and target it. `master` is the only supported branch; there are no
release branches and a fix ships forward.

## Reporting things

- **A bug or a feature** — the [issue templates](https://github.com/konradcinkusz/parcel-number-generator/issues/new/choose).
  For a bug, the request and the response beat a description of them.
- **A vulnerability** — never an issue. [`SECURITY.md`](SECURITY.md).

## Licence

By contributing you agree that your contribution is licensed under the
[MIT Licence](LICENSE), the same terms as the rest of the project. There is no CLA.
