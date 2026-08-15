## What changed

<!-- One or two sentences. What a reader of the commit log needs to know. -->

## Why

<!-- The problem, not the patch. If this closes an issue, link it. -->

## Architecture

<!-- Which principles in architecture-standards/docs/architecture/00-REFERENCE-ARCHITECTURE.md
     this touches, and whether it moves toward or away from them. "None" is a valid answer;
     silence is not. If it introduces a deviation, record it in docs/architecture/DEVIATIONS.md
     with a date. -->

## How it was verified

- [ ] `dotnet test` passes locally
- [ ] New behaviour has a test that fails without the change
- [ ] Schema change ships with a migration for both providers
- [ ] No secret in source, config, or comment
