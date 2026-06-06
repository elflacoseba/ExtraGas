---
name: pr-review-dotnet
description: Perform a comprehensive review of a GitHub Pull Request for .NET, ASP.NET Core MVC, Entity Framework Core and enterprise applications. Accepts PR identifiers such as #4, PR 4 or Pull Request 4 and supports output languages spa (Spanish) and eng (English).
compatibility: opencode
---

# PR Review for .NET Applications

## Purpose

Perform a deep technical review of a GitHub Pull Request for a .NET application.

Supported project types:

- ASP.NET Core MVC
- ASP.NET Core Web API
- Entity Framework Core
- Clean Architecture
- Layered Architecture
- Monolithic Applications
- Enterprise Applications

The skill accepts two parameters:

| Parameter | Description | Examples |
|------------|-------------|------------|
| PR Number | Pull Request identifier | #4, 4, PR 4 |
| Language | Output language | spa, eng |

Examples:

text Review PR #4 spa Review PR #4 eng Analyze PR 15 spa Use pr-review-dotnet #27 eng 

Parameter rules:

- First parameter found matching a Pull Request identifier is the PR number.
- First parameter found matching "spa" or "eng" is the output language.
- If no language is specified, default to "spa".

---

# Review Workflow

## Step 1 - Retrieve Pull Request Information

Obtain:

- Pull Request title
- Pull Request description
- Author
- Source branch
- Target branch
- Changed files
- Complete diff
- Commits included
- Existing review comments
- CI/CD status

If the PR cannot be found:

- Stop the analysis
- Explain the reason
- Request the correct PR identifier

---

## Step 1.1 - Determine Output Language

Detect the language parameter.

Supported values:

| Value | Language |
|---------|------------|
| spa | Spanish |
| eng | English |

Rules:

- If "spa" is provided, generate the entire review in Spanish.
- If "eng" is provided, generate the entire review in English.
- Do not mix languages.
- If no language is specified, use Spanish.

Examples:

text Review PR #4 spa 

Output language:

text Spanish 

Example:

text Review PR #4 eng 

Output language:

text English 

---

## Step 2 - Understand the Context

Before reviewing:

1. Understand the purpose of the Pull Request.
2. Identify the affected modules.
3. Determine whether the changes are:
   - New functionality
   - Bug fixes
   - Refactoring
   - Performance improvements
   - Security improvements
   - Infrastructure changes

Review changes within their business and architectural context.

---

# Technical Review Criteria

## Functional Correctness

Identify:

- Bugs
- Incorrect business rules
- Missing validations
- Null reference risks
- Unhandled exceptions
- Concurrency issues
- Race conditions
- Edge cases
- Regression risks

Questions to ask:

- Does the implementation satisfy the intended behavior?
- Are all failure scenarios handled?
- Can invalid input break the flow?

---

## Architecture Review

Verify:

### SOLID Principles

- Single Responsibility Principle
- Open/Closed Principle
- Liskov Substitution Principle
- Interface Segregation Principle
- Dependency Inversion Principle

### Separation of Concerns

Ensure responsibilities remain separated between:

- Controllers
- Services
- Repositories
- Domain Models
- DTOs
- ViewModels

### Architectural Consistency

Identify:

- Layer violations
- Incorrect dependencies
- Business logic in Controllers
- Business logic in Views
- Infrastructure leakage
- Tight coupling

---

## Code Quality

Review:

### Readability

- Clear naming
- Intent-revealing code
- Self-documenting code

### Maintainability

- Excessive complexity
- Long methods
- Large classes
- Nested conditionals
- Code duplication

### Clean Code

Identify:

- Dead code
- Unused dependencies
- Magic strings
- Magic numbers
- Inconsistent patterns

---

# ASP.NET Core Review

## Controllers

Verify:

- Thin controllers
- Proper dependency injection
- Proper HTTP responses
- Validation handling

Controllers should orchestrate.

Controllers should NOT contain business logic.

---

## Services

Verify:

- Business logic location
- Clear responsibilities
- Proper abstraction

Services should contain business rules.

---

## DTOs and ViewModels

Verify:

- Proper usage
- No exposure of internal entities
- Validation attributes when appropriate

---

## Dependency Injection

Verify:

- Correct lifetime selection:
  - Singleton
  - Scoped
  - Transient

Identify:

- Service locator patterns
- Unnecessary dependencies

---

# Entity Framework Core Review

## Query Efficiency

Review:

- N+1 query problems
- Excessive Includes
- Missing Includes
- Premature materialization
- Inefficient LINQ

---

## Tracking

Identify:

- Missing AsNoTracking
- Unnecessary tracking

---

## SaveChanges Usage

Review:

- Multiple SaveChanges calls
- Transaction handling
- Unit-of-work consistency

---

## Async Usage

Verify:

- Async database operations
- Proper await usage

Identify:

- Blocking calls
- Synchronous database access

---

# Security Review

## Authentication

Verify:

- Correct authentication checks
- Protected endpoints

---

## Authorization

Verify:

- Roles
- Policies
- Resource access rules

---

## Input Validation

Review:

- User input validation
- Model validation

---

## Common Vulnerabilities

Check for:

- SQL Injection
- XSS
- CSRF
- Sensitive data exposure
- Hardcoded secrets
- Unsafe logging

---

# Performance Review

## Database

Review:

- Inefficient queries
- Missing pagination
- Excessive round trips

---

## Memory

Review:

- Large allocations
- Unnecessary object creation

---

## Application

Review:

- Expensive loops
- Repeated computations
- Missing caching opportunities

---

# Testing Review

## Unit Tests

Verify:

- Presence of tests
- Coverage of business rules

---

## Integration Tests

Verify:

- Critical paths tested

---

## Missing Scenarios

Identify:

- Untested edge cases
- Regression risks

---

# Severity Levels

## 🔴 Critical

Issues that may cause:

- Production failures
- Security vulnerabilities
- Data corruption
- Significant business impact

---

## 🟠 Important

Issues that:

- Increase technical debt
- Hurt maintainability
- Create future risks

---

## 🟡 Recommendation

Suggestions that improve:

- Readability
- Architecture
- Consistency
- Performance

---

# Output Format

## Executive Summary

Provide:

- Overall score (1-10)
- Risk assessment
- Main strengths
- Main weaknesses

---

## Findings

For every finding include:

### Severity

Critical / Important / Recommendation

### Location

text File: Line: 

### Description

Explain the problem.

### Why It Matters

Explain the technical impact.

### Suggested Fix

Provide a concrete recommendation.

---

## Positive Findings

List noteworthy good practices.

---

## Architectural Assessment

Provide an overall evaluation of:

- Architecture quality
- Maintainability
- Scalability
- Testability

---

## Final Verdict

Choose exactly one:

### ✅ Approve

Ready to merge.

### ⚠️ Approve With Comments

Can be merged but improvements are recommended.

### ❌ Request Changes

Must be corrected before merging.

Provide clear justification.

---

# Language Enforcement

Generate ALL sections in the selected language.

This includes:

- Executive Summary
- Findings
- Positive Findings
- Architectural Assessment
- Final Verdict
- Recommendations

---

## When language is spa

Use:

- Resumen Ejecutivo
- Hallazgos
- Observaciones Positivas
- Evaluación Arquitectónica
- Veredicto Final
- Recomendaciones

Use professional technical Spanish.

Do not mix English terminology unless it is the official name of a framework, class, method, pattern or technology.

---

## When language is eng

Use:

- Executive Summary
- Findings
- Positive Findings
- Architectural Assessment
- Final Verdict
- Recommendations

Use professional technical English.

---

# Reviewer Mindset

Act as a Senior Software Architect performing a review for a production enterprise application.

Prioritize:

1. Correctness
2. Security
3. Reliability
4. Maintainability
5. Performance

Avoid comments that focus only on formatting or personal preferences.

Every finding must explain:

- What is wrong
- Why it matters
- How to fix it

Focus on actionable feedback that helps the development team improve the codebase.

---

# Publish Results to GitHub

After completing the analysis, publish the review results to the Pull Request on GitHub.

## Step 1 - Post the Review

Use the `github_pull_request_review_write` tool with the following parameters:

- `method`: "create"
- `owner`: Repository owner
- `repo`: Repository name
- `pullNumber`: PR number
- `body`: The full review text formatted according to the Output Format section
- `event`: Choose one of:
  - `"APPROVE"` — if the verdict is ✅ Approve
  - `"COMMENT"` — if the verdict is ⚠️ Approve With Comments
  - `"REQUEST_CHANGES"` — if the verdict is ❌ Request Changes

## Step 2 - Post Individual Line Comments (Optional)

For findings that reference specific lines, also post individual review comments using `github_add_comment_to_pending_review` or `github_add_reply_to_pull_request_comment` with:

- `path`: File path relative to the repo root
- `line`: Line number of the finding
- `body`: The specific finding description
- `side`: "RIGHT" (the new code)

Post these comments **before** submitting the review (Step 3).

## Step 3 - Submit the Review

Once all individual comments are posted, submit the pending review using `github_pull_request_review_write`:

- `method`: "submit_pending"
- `owner`: Repository owner
- `repo`: Repository name
- `pullNumber`: PR number
- `event`: The appropriate event from Step 1
- `body`: The full review summary

## Step 4 - Inform the User

After publishing, inform the user:

- That the review has been published on the PR
- The URL of the PR (construct as `https://github.com/{owner}/{repo}/pull/{pullNumber}`)
- The verdict given (Approve / Approve With Comments / Request Changes)

## Rules

- Always publish the review. Do not skip this step.
- If the GitHub API call fails, output the review to the terminal and explain the failure.
- Do not post duplicate reviews if the skill is run multiple times on the same PR.