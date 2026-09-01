# Open-source readiness

Assessed against
[`architecture-standards/docs/guides/OPEN-SOURCE-RELEASE.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/OPEN-SOURCE-RELEASE.md).
That guide is the one-time gate for a repository moving from private to public, and it is
ordered around the single part that cannot be fixed afterwards.

**Status: ready to publish once the four steps in §7 are done.** None of the four is a
commit, which is why they are written down here rather than left to be remembered.

---

## 1. The ordering principle

Going public is irreversible in exactly one way: a pushed public commit is public forever.
Re-privating the repository, deleting the file in a later commit, force-pushing over it —
none of that reaches the clones, forks, and crawlers that already have a copy. Everything
else on this page is fixable after the fact at no cost.

So the history audit came first and everything else after it, and this document is written
in that order.

## 2. Secret audit over full history — clean

**Not a HEAD scan.** `secret-scan.yml` has run on every push since the modernization, but a
scanner says nothing about the commits that predate it, and this repository has nine of
them: the 2018 .NET Framework tree, from `1f1404a` back at the start through `c1258ed`.

Audited 2026-09-01 over **all 16 commits on all branches and all 248 blobs** the object
database holds, not the working tree:

```bash
git cat-file --batch-all-objects --batch-check='%(objectname) %(objecttype)' \
  | awk '$2=="blob"{print $1}' \
  | while read -r b; do git cat-file -p "$b" | grep -aInE '<pattern>'; done
```

Patterns: AWS access keys, GitHub PATs (`ghp_`/`gho_`/`github_pat_`), Stripe live keys,
OpenAI keys, Slack tokens, PEM private-key headers, JWTs, and any `Password=`/`pwd=`
assignment with a value.

**Result: three hits, all benign, all already documented as such.**

| Hit | Verdict |
|---|---|
| `docker-compose.yml` — `Password=local-development-only` | Deliberate, committed on purpose, allowlisted in [`.gitleaks.toml`](../../.gitleaks.toml), and the file says so in its own header |
| `scripts/generate-migrations.sh` — `Password=placeholder` | The literal string `placeholder`; EF needs a syntactically valid connection string to build a migration and never opens it |
| `flyio/SECRETS.md` — `Password=…` | An ellipsis in a documented shape, not a value |

Separately checked and clean: no email address appears in any blob's *content*; the only
addresses anywhere are the commit authors' own public GitHub identities. No RFC-1918
address and no internal hostname beyond the `png-*.internal` Fly private-network names,
which are documented and resolvable only inside the organisation.

**The legacy tree carries no credential**, which is worth stating precisely because the
README and `.gitleaks.toml` both say it hardcoded a connection string in five files. It
did — `NumberPoolDB.cs`, `NumberPoolDBv2.cs`, `NumberPoolDatabase.cs`, `DBOperation.cs` and
`MainForm.Designer.cs` — and every one of them reads:

```
Integrated Security=SSPI;Initial Catalog=ParcelNumberGenerator;Data Source=localhost\SQLEXPRESS;
```

Windows integrated authentication against a local instance: configuration in source, which
is the defect the rewrite fixed, but **no secret**. That is the distinction `.gitleaks.toml`
draws when it says nothing in the old pipeline "would have noticed if it had carried a
password rather than integrated auth". It did not, so nothing needs rotating and nothing
needs scrubbing.

**Nothing to rotate. No history rewrite. No fresh-history extraction.** The guide's escape
hatch — start a new repository when history cannot be cleaned cheaply — does not apply,
and the 2018 history is worth keeping: `01-CURRENT-STATE.md` catalogues ten defects in code
that is only in those commits, and a reader who wants to check that account should be able
to.

## 3. LICENSE — present, and present from before the first public commit

MIT, at [`LICENSE`](../../LICENSE), `Copyright (c) 2019-2026 Konrad Cinkusz`. The estate
default, and nothing here argues for anything else: no patent surface that would call for
Apache-2.0, no copyleft intent.

It is in the tree now, which is the point — retroactively licensing code that someone has
already forked is the mess the guide warns about, and it cannot happen here.

## 4. README for a stranger

The audience is someone with no context deciding in thirty seconds whether to keep reading,
which is a different reader from the teammate P14's documentation rules are written for.

- **What and why in the first two sentences** — yes, and it disambiguates the name in the
  third, which this project needs more than most: `PNG` reads as the image format, and a
  stranger who assumes that closes the tab.
- **A quick start that runs end to end from a clone with zero unwritten prerequisites** —
  yes, and at three levels of what the reader already has installed: `setup.sh` plus the
  AppHost with Docker, three `dotnet run`s without it, `docker compose up --build` with
  neither. No credential is required at any of them, because each service falls back to an
  in-memory database.
- **The reuse shape** — not applicable in the sense `authservice` means it. This repository
  is run, not consumed; it publishes no package. What a reader might reuse is a deployment,
  and [`flyio/`](../../flyio/) with `INFRASTRUCTURE-ANALYSIS.md` is that shape.

One defect found and fixed in this pass, and it was the worst one on the page: every
GitHub URL in the repository named `konradcinkusz/PNG`, **which does not exist**. Both CI
badges resolved to nothing, and the quick start's first line —
`git clone .../PNG && cd PNG` — failed for every reader who pasted it. Five files carried
it: the README, `Directory.Build.props`'s `RepositoryUrl`, and the
`org.opencontainers.image.source` label on all three Dockerfiles. That last one is not
cosmetic: GHCR reads it to link a published package back to its repository, and a label
pointing at a repository that does not exist links to nothing.

The URLs were corrected to the repository's actual name rather than the repository being
renamed to match them, which is also the better public name — `parcel-number-generator`
says what it is and is findable, where a repository called `PNG` competes with every image
library on GitHub.

## 5. Registry package visibility

**Nothing is published yet**, so nothing to check yet — DEV-1: no image has ever been
pushed, because the tag-driven workflow has never run.

Recorded here because the check is easy to miss and easy to misdiagnose. **A package pushed
by CI is created private on its first push regardless of the repository's visibility**, and
making the repository public does not flip it. Confirmed in the estate on
`ghcr.io/konradcinkusz/authservice`, which needed a manual change in the *package's own*
settings after its first `v*` tag.

Three packages will need it here, separately, after the first tag:

- `ghcr.io/konradcinkusz/parcel-number-generator/parcelnumbers-api`
- `ghcr.io/konradcinkusz/parcel-number-generator/notifications`
- `ghcr.io/konradcinkusz/parcel-number-generator/web`

From outside, a private package looks like a "works for me, fails for everyone else"
report from someone who cannot pull.

## 6. Repository metadata

Neither is repository content; neither can be set by a commit or by CI. Both are how the
repository gets found.

**Description.** Currently `PNG application` — a restated title, which is the failure the
guide names, and worse than generic here because it reads as a tool for image files. It
wants one sentence saying what it does and who it is for. Proposed:

> Warehouse parcel-number allocation: issues tracking numbers from a finite pool without
> repeats or volume leakage, records notifications against them, and gives operators one
> console over both. .NET 10, Aspire, PostgreSQL or SQL Server.

**Topics.** None set. Ten to fifteen lowercase hyphenated keywords someone would actually
search. Proposed:

`dotnet` · `csharp` · `aspnetcore` · `aspire` · `minimal-api` · `entity-framework-core` ·
`postgresql` · `sql-server` · `docker` · `flyio` · `warehouse` · `logistics` ·
`number-generator` · `clean-architecture` · `bff`

## 7. What is left, and none of it is a commit

Do them in this order.

1. **Flip the repository to public.** Everything in §2 that would have made this unsafe has
   been checked. Settings → General → Danger Zone.
2. **Set the description and the topics** from §6. One paste each.
3. **Restore the CodeQL job.** Code scanning is free for public repositories, so DEV-4's
   blocker disappears the moment step 1 lands. The job to restore is described in the
   comment that sits where it used to be, in
   [`secret-scan.yml`](../../.github/workflows/secret-scan.yml): `github/codeql-action/init`
   with `languages: csharp`, a build step, `analyze`, and `permissions: actions: read,
   contents: read, security-events: write`. Delete the DEV-4 row when it is green — and
   restore the badge only then, because the previous badge claimed a scan that could not
   run, which is what got both of them removed.
4. **After the first `v*` tag, flip the three GHCR packages to public** — §5. Not before;
   the packages do not exist until then.

Steps 3 and 4 depend on DEV-1, which is separately blocked. Steps 1 and 2 do not depend on
anything.

## 8. What going public does not change

- **The default configuration stays open, on purpose.** Authentication registers only when
  `Jwt__Authority` is set, so a clone runs with no identity provider ([ADR-0002][adr2],
  P8). In Production the startup guards refuse the host without both a connection string
  and an issuer. Making the repository public makes that posture *readable*, which is the
  argument for having written it down: [`SECURITY.md`](../../SECURITY.md) says it is in
  scope as a design decision and out of scope as a vulnerability report.
- **`docker-compose.yml`'s password stays committed.** It is weak on purpose, it says so in
  its own header, and it is allowlisted with a reason. A public reader who finds it has
  found the documentation.
- **The deviation ledger stays honest.** [`DEVIATIONS.md`](DEVIATIONS.md) is more useful
  public than private: it is the thing that tells a stranger what this system has not done
  yet, which is the question a README cannot answer credibly about itself.

[adr2]: ../decisions/0002-token-validation-only.md
