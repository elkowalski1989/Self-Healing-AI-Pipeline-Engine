# Self-Healing AI Pipeline Engine

A WPF desktop app that runs build/test pipelines and uses [Claude Code](https://docs.anthropic.com/en/docs/claude-code) to automatically fix failing code until all success markers pass.

Define what "working" looks like (markers), define the steps to check it, and let the engine loop: **run steps → check markers → Claude fixes code → repeat** until everything passes or the iteration limit is reached.

## How it works

```
┌─────────────┐     ┌──────────────┐     ┌────────────────┐     ┌───────────┐
│  Run Steps  │ ──► │  Evaluate    │ ──► │  All markers   │ YES │  Done!    │
│  (build,    │     │  Markers     │     │  pass?         │ ──► │  Success  │
│   test,     │     │  (exit code, │     └───────┬────────┘     └───────────┘
│   extract)  │     │   regex,     │             │ NO
└─────────────┘     │   file)      │     ┌───────▼────────┐
       ▲            └──────────────┘     │  Claude Code   │
       │                                 │  fixes code    │
       └─────────────────────────────────┤  (agentic,     │
                                         │   full access) │
                                         └────────────────┘
```

## Features

- **Agentic Setup** — Describe what you want in plain English. Claude explores the project, figures out build commands, and generates a pipeline JSON automatically.
- **Pipeline Editor** — Visual editor for steps, markers, and file access rules. Drag-drop step reordering, inline validation, Ctrl+S to save.
- **Self-Healing Engine** — Iterative loop that runs steps, checks markers, and invokes Claude Code CLI to fix code when markers fail. Streams Claude's output live.
- **Markers** — Define success criteria: exit codes, regex matches on output, JSON path values, file existence checks. Supports numeric and string comparisons.
- **Run History** — Every run is saved with full iteration history. Marker progression charts show improvement over time.
- **Safety** — File access rules control what Claude can edit. Regression detection aborts if markers keep getting worse. Prompt size budgeting prevents token overflow.
- **Dark/Light themes**

## Requirements

- Windows 10+
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code) installed and authenticated

## Getting started

```bash
git clone https://github.com/elkowalski1989/Self-Healing-AI-Pipeline-Engine.git
cd Self-Healing-AI-Pipeline-Engine
dotnet run
```

Or open `Self-Healing-AI-Pipeline-Engine.sln` in Visual Studio / Rider.

## Quick start

1. **Setup tab** — Paste a project path and describe what you want (e.g. "make the build pass with zero warnings"). Claude explores the project and generates a pipeline.
2. **Edit tab** — Review the generated pipeline. Tweak steps, markers, file access rules. Save as JSON.
3. **Run tab** — Click Start. Watch the engine iterate. Claude streams its analysis and code fixes live.
4. **History tab** — Review past runs, see marker progression, inspect changes made.

## Pipeline JSON format

Pipelines are JSON files with this structure:

```json
{
  "Name": "Fix build warnings",
  "Description": "Build the project and eliminate all warnings",
  "TargetProjectPath": "C:\\path\\to\\project",
  "MaxIterations": 10,
  "Steps": [
    {
      "Id": "build",
      "Name": "Build project",
      "Type": "Execute",
      "Command": "dotnet build",
      "OutputKey": "build_output",
      "Timeout": 120,
      "FailBehavior": "Continue"
    }
  ],
  "Markers": [
    {
      "Id": "build_ok",
      "Name": "Build succeeds",
      "Type": "ExitCode",
      "Source": "build_output",
      "Operator": "Equals",
      "TargetValue": "0"
    }
  ],
  "FileAccessRules": [
    { "PathPattern": "**/*.cs", "AccessLevel": "Editable" },
    { "PathPattern": ".git/**", "AccessLevel": "Excluded" }
  ],
  "HealingPromptTemplate": "Context for Claude about the project..."
}
```

### Step types

| Type | Description |
|------|-------------|
| `Execute` | Run a shell command, capture exit code and output |
| `Extract` | Run a command or read a file, optionally apply a regex to extract a value |
| `Validate` | Run a command, pass/fail based on exit code |

### Marker types

| Type | Source format | Description |
|------|--------------|-------------|
| `ExitCode` | `step_output_key` | Compare step's exit code |
| `Regex` | `output_key:pattern` | Extract a value via regex from step output |
| `JsonPath` | `output_key:json.path` | Extract a value from JSON output |
| `FileExists` | `relative/path` | Check if a file exists in the target project |

### Comparison operators

`Equals`, `NotEquals`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `Contains`

## Project structure

```
├── Agents/              Claude Code CLI wrapper (streaming NDJSON)
├── Engine/              Core pipeline engine
│   ├── PipelineEngine   Main run loop (steps → markers → heal → repeat)
│   ├── StepExecutor     Runs pipeline steps (Execute/Extract/Validate)
│   ├── MarkerEvaluator  Evaluates success markers against step data
│   └── HealingLoop      Invokes Claude for code fixes, manages transcript
├── Helpers/
│   ├── PromptBuilder    Constructs healing prompts with budget management
│   └── ProcessRunner    Async process execution with timeout
├── Models/              Pipeline, Step, Marker, Iteration, RunSession
├── Persistence/         Atomic JSON read/write for pipelines and history
├── Views/               WPF views (Setup, Editor, Run, History, Settings)
├── Controls/            Reusable WPF controls (LogEntry, MarkerBadge, etc.)
└── Resources/Themes/    Dark and light theme dictionaries
```

## Keyboard shortcuts

| Shortcut | View | Action |
|----------|------|--------|
| `Enter` | Setup | Send message |
| `Escape` | Setup | Cancel current Claude request |
| `Ctrl+S` | Editor | Save pipeline |
| `Escape` | Run | Stop pipeline (with confirmation) |

## Settings

Configure in the Settings tab:

- **Claude CLI Path** — Path to the Claude Code executable (default: `claude` from PATH)
- **Default Max Iterations** — How many build-fix cycles before giving up (0 = unlimited)
- **Default Step Timeout** — Max time per step in seconds
- **Claude Max Turns** — How many tool-use turns Claude gets per healing round
