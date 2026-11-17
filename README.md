# GitPort

> **A high-performance, portable, WebStorm-grade desktop Git client for Windows.**

GitPort combines the intuitive repository and branch workflow of **SourceTree** with the precision, visual elegance, and muscle of **JetBrains WebStorm’s** Git Log, branch graph, and side-by-side diff engine.

Designed for developers who demand a standalone, portable Git GUI with zero host machine dependencies or intrusive background services.

---

## Key Features

### 1. WebStorm-Identical Git Log & Branch Graph
- **Interactive Multi-Lane Graph:** Smooth, colored bezier graph rendering branches, merges, rebases, and octopus joins with exact topological ordering.
- **Branch & Remote Badges:** Distinctive JetBrains-style branch pills (`[main]`, `[origin/main]`, `[HEAD]`, tags) rendered directly inline with commit summaries.
- **Deep Context Menus:** Full WebStorm-grade context actions on commits and branches:
  - `Branch '<branch>' >` submenu:
    - `Merge '<branch>' into '<current>'`
    - `Rebase '<current>' onto '<branch>'`
    - `Checkout` / `New Branch from '<branch>'...`
    - `Pull into '<current>' Using Rebase` / `Pull Using Merge`
    - `Compare with '<current>'` / `Show Diff with Working Tree`
    - `New Worktree...`
  - Revision actions: `Copy Revision Number` (`Ctrl+Alt+Shift+C`), `Create Patch...`, `Cherry-Pick`, `Compare with Local`, `Reset Current Branch to Here...`, and `Revert Commit`.

### 2. Dual-List Working Tree & Staging
- **Split Stage Lists:** Separate, clear views for **Staged Changes** and **Unstaged Changes** with instant single-click checkbox staging.
- **One-Click Batch Actions:** Quick buttons for `⬆ Stage All`, `⬇ Unstage All`, and `↺ Discard`.
- **Dynamic Commit Panel:** Dynamic commit trigger (`Commit (N)`), `Amend Commit` checkbox, and clean multi-line commit message input.

### 3. WebStorm Side-by-Side Diff Viewer
- **True 2-Pane Diff:** Synchronized dual-pane diff engine comparing *Before* vs. *After* revisions.
- **Syntax & Line Stats:** Soft WebStorm color scheme for added lines (green tint) and removed lines (red tint), line number columns, and addition/deletion pill badges (`+12`, `-4`).
- **View Modes:** Instant toggle between **Side by Side** and **Unified** layouts.

### 4. Hierarchical Commit Files Explorer
- **Folder Tree Navigation:** Compact tree grouping for modified files with aggregate counts (`BackOffice 4 files`, `admin/src 2 files`).
- **Status Indicators:** Right-aligned status badges (`M` Modified, `A` Added, `D` Deleted, `U` Untracked).
- **Full-Row Selection:** Clear WebStorm dark theme accent selection.

### 5. Multi-Repository & Worktree Management
- **Repository Catalog:** Manage all repositories in a unified, tabbed interface.
- **Git Worktree Support:** Create, switch, and inspect independent worktrees without switching branches or stashing in-progress edits.

### 6. Portable & Self-Contained
- **Embedded Git Runtime:** Ships with a standalone Git for Windows runtime—no system Git installation or administrative rights required.
- **Zero Host Registry Pollution:** Profiles, local configurations, and SSH settings stay cleanly isolated within the application folder.

---

## Directory Structure

```
gitport/
├── src-cs/
│   ├── GitPort/                  # Core WPF application (.NET 8)
│   │   ├── Controls/             # Custom CommitGraphControl & UI widgets
│   │   ├── Dialogs/              # NewWorktree, InputBox dialogs
│   │   ├── Models/               # GitCommit, GitBranch, Diff, Repo models
│   │   ├── Services/             # GitService, GraphService, DiffBuilder
│   │   └── Themes/               # WebStorm dark theme definitions
│   └── GitPort.Tests/            # Comprehensive xUnit test suite
├── assets/                       # Vector UI icons, branding, ICO resources
├── tools/                        # Native helper tools (e.g., FolderPicker)
├── zip/                          # Release archives
│   ├── gitport1.0-portable.zip   # Zero-install portable bundle
│   └── gitport1.0.zip            # Complete repository archive
└── README.md
```

---

## Getting Started

### Running the Portable Application
1. Download or extract `gitport1.0-portable.zip` from releases or the [zip/](zip/) folder.
2. Run `GitPort.exe`.
3. Open any existing Git repository or select one from the catalog.

### Building from Source

#### Prerequisites
- Windows 10/11 (x64)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

#### Build Commands
```powershell
# Restore dependencies and build
dotnet build src-cs/GitPort/GitPort.csproj -c Release

# Run automated unit tests
dotnet test src-cs/GitPort.Tests/GitPort.Tests.csproj

# Run GitPort
dotnet run --project src-cs/GitPort/GitPort.csproj
```

---

## Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+Enter` | Commit staged changes |
| `Ctrl+Alt+Shift+C` | Copy selected commit revision hash |
| `Ctrl+Alt+N` | Create new branch |
| `F2` | Edit commit message |
| `F5` / Refresh button | Refresh Git log, status, and branches |

---

## Third-Party Components & Licenses

GitPort bundles open-source developer fonts and Git for Windows runtime components:
- **Editor Fonts:** JetBrains Mono, Fira Code, and Source Code Pro (licensed under the SIL Open Font License 1.1).
- **Git Runtime:** Git for Windows (GPLv2).
- Details and license notices are cataloged in [THIRD-PARTY.md](THIRD-PARTY.md).

---

## License

This project is licensed under the MIT License - see the project root for details.
