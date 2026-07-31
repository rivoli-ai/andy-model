# Claude Code Assistant Configuration

This file contains authorized commands and configuration for Claude Code Assistant sessions.

## Authorized Commands

### Testing and Coverage

- `dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults` - Run tests with code coverage collection
- `reportgenerator -reports:"./TestResults/*/coverage.cobertura.xml" -targetdir:"./TestResults/CoverageReport" -reporttypes:Html` - Generate HTML coverage report
- `reportgenerator -reports:"./TestResults/*/coverage.cobertura.xml" -targetdir:"./TestResults/CoverageReport" -reporttypes:TextSummary` - Generate text summary coverage report

### Build and Development

- `dotnet build` - Build the solution
- `dotnet restore` - Restore NuGet packages
- `dotnet run --project <project_path>` - Run a .NET project (macOS/cross-platform)

### Platform-Specific Notes (macOS)

- **NEVER use mono or .exe files** - This is a .NET 10.0 project running natively on macOS
- **Use `dotnet run`** instead of compiling to .exe and running with mono
- **Use `dotnet <command>`** for all .NET operations (build, test, run, etc.)

### Git Operations

- `git status` - Check repository status
- `git add .` - Stage all changes
- `git commit -m "message"` - Commit changes
- `git pull` - Pull changes from remote
- `git push` - Push changes to remote

## Project Information

- **Target Framework**: .NET 10.0
- **Test Framework**: xUnit
- **Coverage Tool**: Coverlet
- **Report Generator**: ReportGenerator global tool

## Development Workflow Instructions

### Task Completion Tracking

When completing a set of tasks or milestones:

1. **Track work in GitHub issues**: review the relevant issue(s) and their acceptance criteria to ensure everything is covered.
2. **Mark checklists**: when a task list lives in a docs markdown file with `[ ]` items, mark them `[x]` as they complete.
3. **Update project status**: reflect notable changes in README.md.
4. **Commit changes**: use descriptive commit messages and do not mention Claude Code, Anthropic, or any other code assistant in commit messages, issue descriptions, PRs, or merges.

**IMPORTANT**: Before closing an issue, systematically review all of its acceptance criteria to ensure nothing was missed.

### Code Quality Standards

- Always write tests in the tests/ assemblies for new code or code changes in the src/ directory
- Run `dotnet format` before committing to ensure consistent formatting (CI runs `dotnet format --verify-no-changes`)
- Ensure all tests pass: `dotnet test`
- Generate coverage reports for significant changes; CI enforces a line-coverage threshold

### Testing Requirements for Code Changes

**CRITICAL**: When making code changes or claiming fixes:

1. **Modify or create tests** - Update existing tests or create new tests to verify the fix
2. **Run tests before claiming completion** - Always run `dotnet test` to confirm fixes work
3. **Test implementation location** - Tests must be implemented in .NET within existing test folders and assemblies
4. **Verify the actual behavior** - Don't assume a fix works; verify it through tests and actual execution
5. **Update test expectations** - If behavior changes are intentional, update test expectations accordingly

### Documentation Updates

- Keep README.md current with the supported .NET version and features
- Update development/setup guidance when adding new tools or processes

## Notes

- Coverage reports are generated in `./TestResults/CoverageReport/`
- Andy.Model is a pure in-memory conversation/orchestration library with no CLI or filesystem side effects of its own
