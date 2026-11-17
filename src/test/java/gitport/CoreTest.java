package gitport;

import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;

public final class CoreTest {
    private static int passed;
    private static Path root, executable;
    private static void check(boolean ok,String message){if(!ok)throw new AssertionError(message);passed++;System.out.println("PASS "+message);}
    private static Git fixture(String name) throws Exception {
        Path dir=root.resolve(name);Files.createDirectories(dir);Portable p=new Portable(root.resolve(name+"-app"));
        Git git=new Git(p,executable,dir);git.run("init","-b","main").require();git.run("config","user.name","Fixture").require();git.run("config","user.email","fixture@example.invalid").require();
        git.run("config","core.autocrlf","false").require();git.run("config","commit.gpgsign","false").require();
        Files.writeString(dir.resolve("a.txt"),"base-a\n");Files.writeString(dir.resolve("b.txt"),"base-b\n");git.run("add",".").require();git.commit("Base");return git;
    }
    private static void topic(Git g) throws Exception {
        g.run("switch","-c","topic").require();Files.writeString(g.repo.resolve("a.txt"),"incoming-a\n");g.run("add","a.txt").require();g.commit("Incoming");g.run("switch","main").require();
    }
    public static void main(String[] args) throws Exception {
        root=Path.of("build","integration-"+System.currentTimeMillis()).toAbsolutePath();Files.createDirectories(root);
        String git=System.getenv("GITPORT_TEST_GIT");executable=git==null?Path.of("C:\\Program Files\\Git\\cmd\\git.exe"):Path.of(git);
        if(!Files.exists(executable))throw new IllegalStateException("Set GITPORT_TEST_GIT to an installed Git for disposable integration tests.");
        List<Git.Change> parsed=Git.parseStatus(" M space [a] ü.txt\0R  renamed.txt\0old.txt\0?? untracked.txt\0UU conflict.txt\0");
        check(parsed.size()==4&&parsed.get(0).path().equals("space [a] ü.txt")&&parsed.get(1).original().equals("old.txt")&&parsed.get(3).conflict(),"NUL status parser preserves Unicode, spaces, rename and conflict fields");
        char[] pass="a long fixture password".toCharArray();byte[] encrypted=Vault.encrypt(pass,"test-token-NOT-A-REAL-SECRET");
        check(Vault.decrypt(pass,encrypted).equals("test-token-NOT-A-REAL-SECRET"),"portable vault round trip");
        check(!new String(encrypted,StandardCharsets.ISO_8859_1).contains("test-token"),"token absent from encrypted vault bytes");
        try{Vault.decrypt("wrong password".toCharArray(),encrypted);throw new AssertionError("Wrong password accepted");}catch(javax.crypto.AEADBadTagException expected){check(true,"wrong vault password rejected");}
        encrypted[encrypted.length-1]^=1;try{Vault.decrypt(pass,encrypted);throw new AssertionError("Tampering accepted");}catch(javax.crypto.AEADBadTagException expected){check(true,"vault tampering rejected");}
        Git dirty=fixture("dirty-safe");topic(dirty);Files.writeString(dirty.repo.resolve("b.txt"),"local-b\n");
        check(dirty.merge("topic").code()==0,"normal merge accepts unrelated uncommitted edits");
        check(Files.readString(dirty.repo.resolve("b.txt")).equals("local-b\n"),"unrelated local edit preserved");
        Git overlap=fixture("overlap");topic(overlap);Files.writeString(overlap.repo.resolve("a.txt"),"local-a\n");
        check(overlap.merge("topic").code()!=0,"overlapping dirty merge refuses safely");check(Files.readString(overlap.repo.resolve("a.txt")).equals("local-a\n"),"overlap refusal does not discard edits");
        Git shelf=fixture("shelf-success");topic(shelf);Files.writeString(shelf.repo.resolve("b.txt"),"local-b\n");Files.writeString(shelf.repo.resolve("new ü.txt"),"untracked backup\n");
        shelf.run("add","b.txt").require();Git.Result shelfResult=shelf.shelveAndMerge("topic");check(shelfResult.code()==0,"explicit save-merge-restore succeeds");
        check(Files.readString(shelf.repo.resolve("new ü.txt")).equals("untracked backup\n"),"untracked file restored");
        check(shelf.changes().stream().anyMatch(c -> c.path().equals("b.txt")&&c.staged()),"staged state restored");
        check(!shelf.run("stash","list").out().isBlank(),"successful merge retains recoverable backup stash");
        check(!Files.exists(root.resolve("shelf-success-app/data/profiles/default/pending-shelf.properties")),"successful restoration clears pending marker");
        Git restoreConflict=fixture("restore-conflict");topic(restoreConflict);Files.writeString(restoreConflict.repo.resolve("a.txt"),"local-a\n");
        check(restoreConflict.shelveAndMerge("topic").code()!=0,"stash restoration conflict reported");
        check(Files.exists(root.resolve("restore-conflict-app/data/profiles/default/pending-shelf.properties"))&&!restoreConflict.run("stash","list").out().isBlank(),"restore conflict retains marker and stash");
        Properties recovery=new Properties();try(var in=Files.newInputStream(root.resolve("restore-conflict-app/data/profiles/default/pending-shelf.properties"))){recovery.load(in);}
        check(recovery.getProperty("phase").equals("restore-conflicts"),"partial restoration is recorded to prevent applying the same shelf twice");
        Git conflict=fixture("merge-conflict");topic(conflict);Files.writeString(conflict.repo.resolve("a.txt"),"main-a\n");conflict.run("add","a.txt").require();conflict.commit("Main divergent");
        Files.writeString(conflict.repo.resolve("b.txt"),"unfinished-b\n");check(conflict.shelveAndMerge("topic").code()!=0,"divergent conflict leaves merge available to resolve");
        check(Files.exists(conflict.gitPath("MERGE_HEAD"))&&conflict.changes().stream().anyMatch(Git.Change::conflict),"conflict records and MERGE_HEAD detectable");
        check(Files.readString(conflict.repo.resolve("b.txt")).equals("base-b\n"),"unfinished edits safely shelved during conflict");
        conflict.run("merge","--abort").require();String oid=conflict.run("rev-parse","refs/stash").require().out().strip();conflict.run("stash","apply","--index",oid).require();
        check(Files.readString(conflict.repo.resolve("b.txt")).equals("unfinished-b\n"),"abort plus shelf restore recovers unfinished edits");
        Git lock=fixture("lock-refusal");Files.writeString(lock.repo.resolve("new.txt"),"pending\n");Path indexLock=lock.gitPath("index.lock");Files.writeString(indexLock,"other-process");
        try{lock.stage(List.of(new Git.Change("??","new.txt","")));throw new AssertionError("Lock ignored");}catch(java.io.IOException expected){check(Files.readString(indexLock).equals("other-process"),"existing index lock refused and never deleted");}
        Git unicode=fixture("unicode");Path special=unicode.repo.resolve("space [a] ü.txt");Files.writeString(special,"hello\n");unicode.stage(List.of(new Git.Change("??",special.getFileName().toString(),"")));unicode.commit("Unicode");
        check(unicode.changes().isEmpty(),"literal pathspec staging and commit for Unicode/bracket names");
        Files.move(special,unicode.repo.resolve("renamed ü.txt"));unicode.run("add","-A").require();List<Git.Change> renamed=unicode.changes();unicode.unstage(renamed);
        check(unicode.run("diff","--cached","--name-only").out().isBlank(),"unstage handles both sides of rename");
        ExecutorService pool=Executors.newFixedThreadPool(2);try{
            Future<Git.Result> a=pool.submit(() -> unicode.run("config","test.one","1"));Future<Git.Result> b=pool.submit(() -> unicode.run("config","test.two","2"));
            check(a.get().code()==0&&b.get().code()==0,"concurrent requests on one runner serialize without config locks");
        }finally{pool.shutdown();}
        try(Auth auth=new Auth(q -> q.equals("fixture prompt")?"fixture-answer":"")){
            ProcessBuilder pb=new ProcessBuilder(Path.of(System.getProperty("java.home"),"bin","java.exe").toString(),"-cp",System.getProperty("java.class.path"),"gitport.Main","fixture prompt");
            pb.environment().putAll(auth.environment(Path.of("unused.exe")));Process child=pb.start();String answer=new String(child.getInputStream().readAllBytes(),StandardCharsets.UTF_8).strip();
            String errors=new String(child.getErrorStream().readAllBytes(),StandardCharsets.UTF_8);
            int exit=child.waitFor();
            if(exit!=0&&errors.contains("Permission denied: getsockopt"))System.out.println("SKIP loopback askpass integration: this execution sandbox blocks local socket connections; retest in an ordinary Windows session.");
            else check(exit==0&&answer.equals("fixture-answer"),"authenticated loopback askpass round trip without secret arguments: "+errors);
        }
        Portable movable=new Portable(root.resolve("portability"));Path relative=root.resolve("portability/../repo").normalize();check(movable.resolve(movable.portablePath(relative)).equals(relative),"relative bookmarks resolve independently of drive letter");
        Path profileRoot=root.resolve("multiple-profiles");Portable personal=new Portable(profileRoot);personal.settings.setProperty("name","Personal Author");personal.settings.setProperty("username","personal-login");personal.settings.setProperty("repos","personal-repository");personal.save();
        Vault.save(personal.data.resolve("credentials.vault"),pass,"personal-token");
        String companyId=Portable.createProfile(profileRoot,"Company");Portable.activate(profileRoot,companyId);Portable company=new Portable(profileRoot);
        check(company.profileId.equals(companyId)&&company.profileName().equals("Company"),"named profile creation and active profile selection persist");
        check(company.settings.isEmpty()&&!Files.exists(company.data.resolve("credentials.vault")),"new profile inherits no identity, repository bookmarks or credentials");
        company.settings.setProperty("name","Company Author");company.settings.setProperty("username","company-login");company.save();
        Portable.activate(profileRoot,"default");Portable restoredPersonal=new Portable(profileRoot);
        check(restoredPersonal.settings.getProperty("name").equals("Personal Author")&&restoredPersonal.settings.getProperty("username").equals("personal-login"),"switching back restores the original profile identity and username");
        check(Vault.decrypt(pass,Files.readAllBytes(restoredPersonal.data.resolve("credentials.vault"))).equals("personal-token"),"profile vault survives switching independently");
        check(!personal.data.equals(company.data)&&!personal.temp.equals(company.temp),"profiles use separate data and temporary folders");
        try{new Portable(profileRoot,"../escape");throw new AssertionError("Path traversal accepted");}catch(java.io.IOException expected){check(true,"profile identifiers cannot escape the app folder");}
        Path legacyRoot=root.resolve("legacy");Files.createDirectories(legacyRoot.resolve("data"));Files.writeString(legacyRoot.resolve("data/settings.properties"),"name=Legacy Author\n");
        Portable legacy=new Portable(legacyRoot);check(legacy.settings.getProperty("name").equals("Legacy Author")&&legacy.data.equals(legacyRoot.resolve("data")),"older single-profile data stays in place without silent migration");
        Git wtRepo=fixture("worktree-repo");
        List<Git.Worktree> wtList=wtRepo.worktrees();
        check(wtList.size()==1&&wtList.get(0).isMain(),"main worktree discovered");
        Path linkedWt=root.resolve("linked-worktree-agent");
        wtRepo.worktreeAdd(linkedWt,"agent-branch",null,true).require();
        List<Git.Worktree> wtAfter=wtRepo.worktrees();
        check(wtAfter.size()==2&&wtAfter.stream().anyMatch(w -> w.displayBranch().equals("agent-branch")),"worktree add creates linked worktree record");
        wtRepo.worktreeLock(linkedWt,"testing agent lock").require();
        check(wtRepo.worktrees().stream().anyMatch(w -> w.isLocked()&&w.lockReason().equals("testing agent lock")),"worktree lock records reason");
        wtRepo.worktreeUnlock(linkedWt).require();
        check(wtRepo.worktrees().stream().anyMatch(w -> !w.isLocked()),"worktree unlock clears lock");
        wtRepo.worktreeRemove(linkedWt,false).require();
        check(wtRepo.worktrees().size()==1,"worktree remove deletes linked worktree");
        System.out.println("ALL "+passed+" EXECUTED CHECKS PASSED. Review any SKIP above. Disposable fixtures: "+root);
    }
}
