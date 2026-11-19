# GitPort

> **A high-performance, portable, WebStorm-grade desktop Git client for Windows.**

GitPort combines the intuitive repository and branch workflow of **SourceTree** with the precision, visual elegance, and muscle of **JetBrains WebStorm’s** Git Log, branch graph, and side-by-side diff engine.

Designed as a portable Git GUI for Windows. The standard build requires the .NET 10 Desktop Runtime; the release folder includes Git for Windows.

---

## Functionality review (November 2026)

The application targets **.NET 10 on Windows**. The automated suite exercises disposable repositories and a loopback Git smart HTTP server; it does not connect to your production repositories or accounts.

| Workflow | Review result |
|---|---|
| History, graph, branch tree, file tree, unified and side-by-side diff | Existing implementations; graph/tree/diff tests pass. History now uses topological order. |
| Stage, unstage, commit, amend | Implemented. Fixed partially staged files, Unicode/space paths, renames, unstage before the first commit, and unchecked staged files being included by the commit dialog. |
| Fetch, pull, push | Tested locally over authenticated plain HTTP and against a named remote. Fixed misleading fetch success, session login, destination branch handling, upstream defaults, and remote branch references. |
| Branch create/switch/rename/delete, merge, rebase, cherry-pick, revert, tags, reset | Git operations exercised by integration tests. Fixed nested branch references and comparison actions. Conflicts still require manual resolution. |
| Worktree create/list/remove | Exercised by integration tests. Fixed branch-name collisions in worktree indicators. |
| Repository catalog and tabs | Existing implementation, with catalog tests. Each repository now retains its own Git service and login session. |
| Multiple identities and repository rules | Added. Explicit repository assignment takes precedence over the most specific matching remote rule. |
| Gogs over HTTP, including custom ports | Authenticated fetch/push/pull verified with Git's smart HTTP backend. Your particular Gogs installation still needs a connection check. |
| SSH keys | Per-profile key selection added. Live SSH servers, passphrase prompts, and host-key enrollment are not covered by the automated suite. |
| Clone/init wizard, stash/shelf UI, remote editor, guided conflict resolver, rebase continue/abort | No dedicated UI yet. Use Git externally for these workflows. |

Regex, match-case, and user/date/path filter controls were decorative and have been removed. The working log search matches message, author, hash, and refs; the branch sidebar supplies branch filtering.

### Profiles and automatic repository selection

Click **Profile** in the top-right corner, then **+ New**. Set a profile name, commit author/email, HTTP username, and optionally an SSH private key path. Blank identity fields retain the existing Git configuration.

- **Pin this profile** assigns it to the current repository and restores it whenever that repository opens.
- For automatic selection, clear the pin and set a remote rule such as `gogs.local:3000` or `http://gogs.local:3000/team`. URL rules match complete path segments; longer rules win. With no match, GitPort uses the existing Git settings.
- Commit identities are supplied per Git process. Switching profiles does not rewrite repository or global author configuration.
- Settings live beside the executable in `data/profiles.json`; the existing repository catalog remains in `data/profiles/default/settings.properties`.
- HTTP login works for the current session with **Remember** cleared. Remembered logins are scoped to the profile and repository URL, encrypted with Windows DPAPI, and restored after restart. They can only be decrypted by the same Windows user; moving the portable folder to another user requires signing in again.
- When connecting a new repository, choose an account from **Reuse a saved account on this server** to fill its saved username and password/token. Only accounts with the same protocol, host, and port appear. With **Remember** enabled, GitPort pins the existing profile to the new repository and saves its login for that repository URL; with it cleared, reuse lasts for the current session.
- **Forget saved logins** removes that profile's stored logins when saved. Build updates preserve runtime data; portable archives exclude it.
- When fetch, pull, or push reports invalid credentials or a missing repository, **Resolve Git Connection** shows the server error and lets you correct the clone URL, open the repository in your browser, copy its link, open token settings, or paste a new password/token. **Save & Retry** updates the selected remote and retries the operation. A corrected push URL leaves the fetch URL unchanged.
- **Sign in with browser** uses Git Credential Manager for supported HTTPS hosts (GitHub, GitLab, Bitbucket Cloud, and Azure DevOps). Complete authentication in your browser, return to GitPort, and choose **Save & Retry**. If the helper is unavailable, use password/token entry. Gogs/Gitea and other servers provide website access and manual credentials; signing in to their website alone does not authenticate Git. Browser sign-in can be cancelled and times out after five minutes.

### Gogs without SSL

Use the server's actual remote URL, for example `http://192.168.1.50:3000/team/repository.git`. GitPort preserves HTTP and the custom port. Enter your Gogs username and password or access token when fetch, pull, or push requests authentication. An HTTP server does **not** need the SSL bypass option. HTTP transmits credentials without encryption.

Gogs must have HTTP Git enabled and the account must have repository access. See the [Gogs server configuration](https://github.com/gogs/docs/blob/master/en-US/advanced/configuration_cheat_sheet.md) and [Git credential URL scoping](https://git-scm.com/docs/gitcredentials). For HTTPS certificate failures, verification bypass is optional, unchecked by default, and scoped to that remote in repository-local Git configuration.

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
- **Live Push Progress:** Shows compact object counts, percentage, transferred size, and speed while Git pushes. A reserved, scrollable message area keeps errors readable, with guidance for account limits, authentication, and rejected pushes. Final transfer totals remain in the status bar after completion.
- **Repository Catalog:** Manage all repositories in a unified, tabbed interface.
- **Git Worktree Support:** Create, switch, and inspect independent worktrees without switching branches or stashing in-progress edits.

### 6. Portable Packaging
- **Embedded Git Runtime:** Ships with a standalone Git for Windows runtime—no system Git installation or administrative rights required.
- **Portable Profile Settings:** Application profiles stay beside the executable. Existing Git configuration and credential helpers remain available when no profile overrides them; SSH still uses the user's host-key trust configuration.

---

## Directory Structure

```
gitport/
├── src-cs/
│   ├── GitPort/                  # Core WPF application (.NET 10)
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
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

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
