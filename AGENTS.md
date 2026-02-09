# AGENTS

## Repository overview
- This repo contains a C# source generator for Shroud (`src/Shroud.Generator`) plus example and test projects.
- The solution file is `Shroud.slnx` at the repo root.

## Build & test
- Build the solution: `dotnet build Shroud.slnx`
- Run all tests: `dotnet test Shroud.slnx`

## Conventions
- Keep changes aligned with existing C# style in each project.
- Prefer small, focused changes with clear commit messages.
- All code should be unit tested.
- Document all relevant interface changes.
- Update the example project to include examples for new or modified behavior.

## Notes
- Example projects live under `example/` and tests under `test/`.
