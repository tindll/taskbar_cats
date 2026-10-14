# Contributing

- Windows + .NET 10 SDK. `dotnet test TaskbarCats.slnx` must stay green; add a test for any new logic.
- Keep it small: plain WPF, no new dependencies without a reason.
- Privacy is a feature: never read or keep message text, tool inputs/outputs, paths or commands from transcripts.
- Bump `<Version>` in `src/TaskbarCats/TaskbarCats.csproj` and add a CHANGELOG line in the same PR as a release.
