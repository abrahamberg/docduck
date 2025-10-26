# SonarQube Issue Remediation Prompt

## Purpose

This prompt is designed to systematically fix all SonarQube issues in the docduck-api project, prioritizing reliability issues first, followed by maintainability issues, while ensuring code quality and test coverage.

## Original Prompt

```
Connect to the MCP SonarQube server and retrieve all issues for the project with key `docduck-api`.

Prioritize fixing reliability issues first, followed by maintainability issues.

After each fix, ensure the code compiles and passes existing tests.

Then, generate new unit tests to improve code coverage, aiming to exceed the quality gate threshold.

Continue this process iteratively until all issues are resolved and the quality gate is passed.

The project is written in C# and should follow best practices for readability, performance, and testability.
```

## Prerequisites

1. **MCP SonarQube Server Connection**: Ensure you have access to the SonarQube MCP server
2. **Project Key**: `docduck-api`
3. **Development Environment**:
   - .NET 8 SDK installed
   - Access to the repository
   - SonarQube analyzer tools configured

## Execution Strategy

### Phase 1: Issue Analysis

1. Connect to SonarQube MCP server
2. Retrieve all issues for project key `docduck-api`
3. Categorize issues by severity:
   - **CRITICAL**: Cognitive complexity, major bugs
   - **MAJOR**: Reliability issues, code smells
   - **MINOR/INFO**: Code style, maintainability

### Phase 2: Systematic Remediation

#### Step 1: Fix CRITICAL Reliability Issues

- Focus on cognitive complexity (S3776)
- Refactor complex methods into smaller, focused functions
- Apply SOLID principles pragmatically
- Extract helper methods with clear, descriptive names

#### Step 2: Fix MAJOR Reliability Issues

- Address null reference issues (CS8602, CS8601)
- Fix async/await patterns (S6966)
- Correct floating point comparisons (S1244)
- Fix nested ternary operations
- Address React key prop issues

#### Step 3: Fix INFO/MINOR Maintainability Issues

- Modernize collection initialization (IDE0028)
- Extract string literal constants (S1192)
- Remove unused variables and imports (S1481, S1128)
- Handle deprecated code warnings (S1133)
- Update xUnit test attributes (xUnit1004)
- Add ObsoleteAttribute messages (CA1041)

#### Step 4: Verify After Each Batch

```bash
# Build the solution
dotnet build docduck.sln

# Run existing tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Phase 3: Improve Test Coverage

1. Identify under-tested areas
2. Generate unit tests for:
   - Public API methods
   - Edge cases
   - Error handling paths
   - Business logic
3. Aim for >80% code coverage
4. Ensure tests are:
   - Fast and reliable
   - Focused on single responsibilities
   - Following AAA pattern (Arrange, Act, Assert)

### Phase 4: Quality Gate Verification

1. Run SonarQube analysis
2. Check quality gate metrics:
   - Code coverage
   - Duplications
   - Maintainability rating
   - Reliability rating
   - Security rating
3. Address any remaining blockers
4. Verify all tests pass

## Code Standards

Follow the project's `.github/copilot-instructions.md` guidelines:

### General Principles

- **Clarity over cleverness**: Optimize for readability
- **Pragmatic SOLID**: Avoid ceremony and unnecessary layers
- **Small, composable units**: Clear responsibilities and names
- **Explicit behavior**: Bias toward explicitness

### C# Specific Guidelines

- Use C# 12 language features
- Target .NET 8 LTS
- Enable nullable reference types
- Prefer `async/await` end-to-end
- Use `record`/`record struct` for immutable DTOs
- Accept and pass `CancellationToken` on public async APIs
- Use collection expressions (`[]` instead of `new List<>()`)
- Use `ArgumentNullException.ThrowIfNull(...)` for guards
- Log with structured messages (no string concatenation)

### Error Handling

- Fail fast on programmer errors
- Handle recoverable errors with context-rich messages
- Don't swallow exceptions
- Validate inputs at boundaries

### Testing

- Use xUnit for tests
- Add minimal happy-path test plus 1-2 edge cases
- Keep tests focused and fast
- Follow AAA pattern

## Expected Outcomes

1. **Zero CRITICAL/MAJOR issues** in SonarQube
2. **All builds succeed** with no compilation errors
3. **All tests pass** (existing + new)
4. **Improved code coverage** exceeding quality gate threshold
5. **Quality gate PASSED**
6. **Maintainable codebase** following best practices

## Iterative Process

For each batch of fixes:

1. ✅ Fix issues in the current category
2. ✅ Build solution (`dotnet build docduck.sln`)
3. ✅ Run tests (`dotnet test`)
4. ✅ Verify no regressions
5. ✅ Commit changes with descriptive message
6. 🔄 Move to next category

## Notes

- **Don't over-engineer**: Apply patterns only when there are 2+ use cases
- **Avoid premature abstraction**: Keep it simple until complexity is clearly justified
- **Prefer composition**: Over inheritance
- **Document assumptions**: If requirements are ambiguous, note them in comments/PR text

## Tracking Progress

Use a todo list to track:

- [ ] CRITICAL issues fixed
- [ ] MAJOR issues fixed
- [ ] MINOR/INFO issues fixed
- [ ] TypeScript/React issues fixed (if applicable)
- [ ] Tests added for coverage
- [ ] Quality gate passed

## Related Resources

- Project coding guidelines: `.github/copilot-instructions.md`
- SonarQube workflow: `.github/workflows/sonarqube.yml`
- Test project: `tests/Api.Tests/`
- Documentation: `docs/`
