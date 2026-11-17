package gitport;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.time.Instant;
import java.util.*;
import java.util.concurrent.*;
import java.util.function.Consumer;

/** Executes real Git without a command shell. One worker per repository/app instance. */
public final class Git {
    public record Result(int code, String out, String err) {
        public String text() { return out + (err.isBlank() ? "" : "\n" + err); }
        public Result require() throws IOException { if (code != 0) throw new IOException(text()); return this; }
    }
    public record Change(String xy, String path, String original) {
        public boolean conflict() { return Set.of("DD","AU","UD","UA","DU","AA","UU").contains(xy); }
        public boolean staged() { return !xy.equals("??") && xy.charAt(0) != ' ' && !conflict(); }
        @Override public String toString() { return xy + "   " + path; }
    }
    public record Worktree(Path path, String headCommit, String branch, boolean isMain, boolean isLocked, String lockReason, boolean isPrunable) {
        public String displayBranch() {
            if (branch == null || branch.isBlank()) return "Detached HEAD";
            if (branch.startsWith("refs/heads/")) return branch.substring(11);
            return branch;
        }
    }
    private final Portable portable;
    private final Path executable;
    public Path repo;
    public Consumer<String> progress = s -> {};
    public final Map<String,String> authentication = new HashMap<>();
    public Git(Portable portable, Path executable, Path repo) { this.portable = portable; this.executable = executable; this.repo = repo; }

    public synchronized Result run(String... args) throws Exception { return runInput(null, args); }
    public synchronized Result runInput(byte[] input, String... args) throws Exception {
        List<String> command = new ArrayList<>(List.of(executable.toString(), "--no-pager", "-c", "credential.helper=", "-c", "core.quotepath=false", "-c", "color.ui=false", "-C", repo.toString()));
        command.addAll(List.of(args));
        ProcessBuilder pb = new ProcessBuilder(command);
        Map<String,String> env = pb.environment();
        env.keySet().removeIf(k -> k.toUpperCase(Locale.ROOT).startsWith("GIT_") || k.toUpperCase(Locale.ROOT).startsWith("GCM_") || k.toUpperCase(Locale.ROOT).startsWith("SSH_") || k.toUpperCase(Locale.ROOT).startsWith("VSCODE_GIT_"));
        env.put("GIT_CONFIG_GLOBAL", portable.data.resolve("gitconfig").toString());
        env.put("GIT_CONFIG_NOSYSTEM", "1"); env.put("GIT_TERMINAL_PROMPT", "0");
        env.put("GIT_OPTIONAL_LOCKS", "0"); // status must not compete with a foreground index writer
        env.put("GIT_EDITOR", "false"); env.put("GIT_SEQUENCE_EDITOR", "false");
        env.put("TEMP", portable.temp.toString()); env.put("TMP", portable.temp.toString());
        env.put("HOME", portable.data.toString()); env.put("XDG_CONFIG_HOME", portable.data.resolve("xdg").toString());
        env.put("GIT_PAGER", "cat"); env.put("GCM_INTERACTIVE", "never");
        Path gitRoot = executable.getParent().getParent();
        env.put("PATH", gitRoot.resolve("cmd")+File.pathSeparator+gitRoot.resolve("usr/bin")+File.pathSeparator+env.getOrDefault("PATH", ""));
        Path ssh = gitRoot.resolve("usr/bin/ssh.exe");
        if (Files.exists(ssh)) {
            String key = portable.settings.getProperty("sshKey", "");
            String sshCommand = quote(ssh)+" -F NUL -o IdentityAgent=none -o IdentitiesOnly=yes -o BatchMode=yes -o StrictHostKeyChecking=yes -o GlobalKnownHostsFile=none -o UserKnownHostsFile="+quote(portable.data.resolve("known_hosts"));
            sshCommand += " -i " + quote(key.isBlank() ? portable.data.resolve("ssh/id_ed25519") : portable.resolve(key));
            env.put("GIT_SSH_COMMAND", sshCommand); env.put("GIT_SSH_VARIANT", "ssh");
        }
        env.putAll(authentication);
        progress.accept("git " + String.join(" ", Arrays.stream(args).map(Git::redact).toList()));
        Process process = pb.start();
        try (var stdin = process.getOutputStream()) { if (input != null) stdin.write(input); }
        // Both pipes must be drained concurrently, including for large diffs/status outputs.
        ExecutorService readers = Executors.newFixedThreadPool(2);
        try {
            Future<byte[]> stdout = readers.submit(() -> process.getInputStream().readAllBytes());
            Future<byte[]> stderr = readers.submit(() -> {
                ByteArrayOutputStream buffer = new ByteArrayOutputStream(); byte[] chunk = new byte[4096]; int n;
                while ((n = process.getErrorStream().read(chunk)) != -1) {
                    buffer.write(chunk, 0, n);
                    progress.accept(redact(new String(chunk, 0, n, StandardCharsets.UTF_8)));
                }
                return buffer.toByteArray();
            });
            while (!process.waitFor(2, TimeUnit.SECONDS)) progress.accept("Still running - " + args[0] + " (Git output will appear here)");
            return new Result(process.exitValue(), new String(stdout.get(), StandardCharsets.UTF_8), new String(stderr.get(), StandardCharsets.UTF_8));
        } finally { readers.shutdown(); }
    }
    public static String quote(Path path) { return "\"" + path.toAbsolutePath().toString().replace('\\','/') + "\""; }
    public static String redact(String value) { return value.replaceAll("(?i)(https?://)[^/@\\s]+@", "$1[redacted]@"); }
    public Path gitPath(String name) throws Exception {
        Path p = Path.of(run("rev-parse", "--git-path", name).require().out.strip());
        return p.isAbsolute() ? p : repo.resolve(p).normalize();
    }
    public void writable() throws Exception {
        if (Files.exists(gitPath("index.lock"))) throw new IOException("Git index.lock exists. Another tool may be working. GitPort will not delete it or start another write.");
    }
    public List<Change> changes() throws Exception { return parseStatus(run("status", "--porcelain=v1", "-z", "--untracked-files=normal").require().out); }
    public static List<Change> parseStatus(String data) throws IOException {
        List<Change> changes = new ArrayList<>(); String[] fields = data.split("\u0000", -1);
        for (int i=0; i<fields.length; i++) {
            String entry = fields[i]; if (entry.isEmpty()) continue;
            if (entry.length()<4 || entry.charAt(2)!=' ') throw new IOException("Invalid Git status record");
            String xy=entry.substring(0,2), path=entry.substring(3), original="";
            if (xy.indexOf('R')>=0 || xy.indexOf('C')>=0) {
                if (++i >= fields.length || fields[i].isEmpty()) throw new IOException("Missing rename source");
                original=fields[i];
            }
            changes.add(new Change(xy,path,original));
        }
        return changes;
    }
    public Result stage(List<Change> files) throws Exception {
        writable(); List<String> args = new ArrayList<>(List.of("--literal-pathspecs", "add", "--"));
        for (Change c:files) { args.add(c.path); if (!c.original.isBlank()) args.add(c.original); }
        return run(args.toArray(String[]::new)).require();
    }
    public Result unstage(List<Change> files) throws Exception {
        writable(); List<String> args=new ArrayList<>(List.of("--literal-pathspecs", "restore", "--staged", "--"));
        for (Change c:files) { args.add(c.path); if (!c.original.isBlank()) args.add(c.original); }
        return run(args.toArray(String[]::new)).require();
    }
    public Result commit(String message) throws Exception {
        writable(); if (message.isBlank()) throw new IOException("Enter a commit message.");
        return runInput(message.getBytes(StandardCharsets.UTF_8), "commit", "--file=-").require();
    }
    public Result merge(String ref) throws Exception {
        writable(); validateRef(ref);
        return run("merge", "--no-edit", "--no-autostash", "--", ref);
    }
    public static void validateRef(String ref) throws IOException {
        if (ref.isBlank() || ref.startsWith("-") || ref.chars().anyMatch(Character::isISOControl)) throw new IOException("Invalid branch name.");
    }
    // Explicit shelf workflow. Stash is retained even after successful restoration for recovery.
    public Result shelveAndMerge(String ref) throws Exception {
        writable(); validateRef(ref);
        if (Files.exists(gitPath("MERGE_HEAD"))) throw new IOException("Complete or abort the current merge first.");
        Path recovery = portable.data.resolve("pending-shelf.properties");
        if (Files.exists(recovery)) throw new IOException("A saved shelf is pending. Review it before creating another.");
        if (changes().isEmpty()) return merge(ref);
        String label="GitPort-before-merge-"+UUID.randomUUID();
        Properties state=new Properties(); state.setProperty("repo", portable.portablePath(repo)); state.setProperty("label", label); state.setProperty("time", Instant.now().toString());
        saveRecovery(recovery,state); // exists before stash so interruption is discoverable
        run("stash", "push", "--include-untracked", "--message", label).require();
        String oid=run("rev-parse", "refs/stash").require().out.strip();
        state.setProperty("oid",oid); saveRecovery(recovery,state);
        Result result=merge(ref);
        if (result.code!=0) {
            state.setProperty("phase","merge-stopped"); saveRecovery(recovery,state);
            return new Result(result.code,result.out,result.err+"\nYour edits are saved in stash "+oid+". Resolve/abort the merge, then use Restore saved edits.");
        }
        state.setProperty("phase","restore-started"); saveRecovery(recovery,state);
        Result restored=run("stash", "apply", "--index", oid);
        if (restored.code==0) Files.delete(recovery);
        else { state.setProperty("phase","restore-conflicts"); saveRecovery(recovery,state); }
        return new Result(restored.code,result.text()+"\n"+restored.text(),"Backup retained in git stash: "+oid+(restored.code!=0 ? "\nRestore conflicts need attention. Do not apply it again." : ""));
    }
    public static void saveRecovery(Path path, Properties properties) throws IOException {
        try (var out=Files.newOutputStream(path)) { properties.store(out,"GitPort recovery - retained stashes are not automatically deleted"); }
    }

    public List<Worktree> worktrees() throws Exception {
        Result r = run("worktree", "list", "--porcelain");
        if (r.code != 0) return Collections.emptyList();
        List<Worktree> list = new ArrayList<>();
        String[] lines = r.out.split("\n");
        Path currentPath = null;
        String head = null;
        String branch = null;
        boolean locked = false;
        String lockReason = "";
        boolean prunable = false;
        boolean isFirst = true;

        for (String raw : lines) {
            String line = raw.strip();
            if (line.isEmpty()) {
                if (currentPath != null) {
                    list.add(new Worktree(currentPath, head != null ? head : "", branch, isFirst, locked, lockReason, prunable));
                    isFirst = false;
                    currentPath = null;
                    head = null;
                    branch = null;
                    locked = false;
                    lockReason = "";
                    prunable = false;
                }
                continue;
            }
            if (line.startsWith("worktree ")) {
                currentPath = Path.of(line.substring(9).strip());
            } else if (line.startsWith("HEAD ")) {
                head = line.substring(5).strip();
            } else if (line.startsWith("branch ")) {
                branch = line.substring(7).strip();
            } else if (line.equals("detached")) {
                branch = "";
            } else if (line.startsWith("locked")) {
                locked = true;
                lockReason = line.length() > 6 ? line.substring(6).strip() : "";
            } else if (line.startsWith("prunable")) {
                prunable = true;
            }
        }
        if (currentPath != null) {
            list.add(new Worktree(currentPath, head != null ? head : "", branch, isFirst, locked, lockReason, prunable));
        }
        return list;
    }

    public Result worktreeAdd(Path path, String branch, String baseRef, boolean createBranch) throws Exception {
        writable();
        List<String> cmd = new ArrayList<>(List.of("worktree", "add"));
        if (createBranch) {
            validateRef(branch);
            cmd.add("-b");
            cmd.add(branch);
            cmd.add(path.toAbsolutePath().toString());
            if (baseRef != null && !baseRef.isBlank()) cmd.add(baseRef);
        } else {
            cmd.add(path.toAbsolutePath().toString());
            if (branch != null && !branch.isBlank()) cmd.add(branch);
        }
        return run(cmd.toArray(String[]::new)).require();
    }

    public Result worktreeRemove(Path path, boolean force) throws Exception {
        writable();
        List<String> cmd = new ArrayList<>(List.of("worktree", "remove"));
        if (force) cmd.add("--force");
        cmd.add(path.toAbsolutePath().toString());
        return run(cmd.toArray(String[]::new)).require();
    }

    public Result worktreeLock(Path path, String reason) throws Exception {
        writable();
        List<String> cmd = new ArrayList<>(List.of("worktree", "lock"));
        if (reason != null && !reason.isBlank()) { cmd.add("--reason"); cmd.add(reason); }
        cmd.add(path.toAbsolutePath().toString());
        return run(cmd.toArray(String[]::new)).require();
    }

    public Result worktreeUnlock(Path path) throws Exception {
        writable();
        return run("worktree", "unlock", path.toAbsolutePath().toString()).require();
    }

    public Result worktreePrune() throws Exception {
        writable();
        return run("worktree", "prune").require();
    }

    public Result fetch(String remote) throws Exception {
        writable();
        List<String> cmd = new ArrayList<>(List.of("fetch", "--prune"));
        if (remote != null && !remote.isBlank()) cmd.add(remote);
        else cmd.add("--all");
        return run(cmd.toArray(String[]::new)).require();
    }

    public Result fetchAndMerge(String ref, boolean shelf) throws Exception {
        writable();
        validateRef(ref);
        String remote = null;
        if (ref.startsWith("origin/")) remote = "origin";
        else if (ref.contains("/")) {
            String candidate = ref.substring(0, ref.indexOf('/'));
            Result remotes = run("remote");
            if (remotes.code == 0 && Arrays.asList(remotes.out.split("\n")).contains(candidate)) {
                remote = candidate;
            }
        }
        try {
            fetch(remote);
        } catch (Exception ex) {
            progress.accept("Fetch note: " + ex.getMessage());
        }
        return shelf ? shelveAndMerge(ref) : merge(ref);
    }
}
