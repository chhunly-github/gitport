package gitport;

import java.io.*;
import java.nio.file.*;
import java.util.*;

/** All application-owned persistent state is relative to the application folder. */
public final class Portable {
    public final Path root, data, temp;
    public final String profileId;
    public final Properties settings = new Properties();
    public Portable(Path root) throws IOException { this(root, catalog(root).getProperty("active", "default")); }
    public Portable(Path root, String profileId) throws IOException {
        this.root = root.toAbsolutePath().normalize();
        if (!profileId.matches("[a-zA-Z0-9-]{1,64}")) throw new IOException("Invalid profile identifier.");
        this.profileId=profileId;
        Path shared=this.root.resolve("data");
        // Preserve an older single-profile layout in place; never silently move its data.
        data = profileId.equals("default") && Files.exists(shared.resolve("settings.properties")) ? shared : shared.resolve("profiles").resolve(profileId);
        temp = this.root.resolve("temp").resolve(profileId);
        Files.createDirectories(data); Files.createDirectories(temp);
        Path settingsFile = data.resolve("settings.properties");
        if (Files.exists(settingsFile)) try (var in = Files.newInputStream(settingsFile)) { settings.load(in); }
        if (!Files.exists(data.resolve("gitconfig"))) Files.writeString(data.resolve("gitconfig"), "# GitPort private configuration\n");
    }
    public static Properties catalog(Path root) throws IOException {
        Properties p=new Properties();Path file=root.resolve("data/profiles.properties");
        if(Files.exists(file))try(var in=Files.newInputStream(file)){p.load(in);}
        p.putIfAbsent("profile.default","Default");p.putIfAbsent("active","default");return p;
    }
    private static void saveCatalog(Path root,Properties p) throws IOException {
        Path folder=root.resolve("data");Files.createDirectories(folder);Path tmp=Files.createTempFile(folder,"profiles-",".tmp");
        try{try(var out=Files.newOutputStream(tmp)){p.store(out,"GitPort profile catalog - no secrets");}Files.move(tmp,folder.resolve("profiles.properties"),StandardCopyOption.REPLACE_EXISTING);}
        finally{Files.deleteIfExists(tmp);}
    }
    public static String createProfile(Path root,String name) throws IOException {
        if(name==null||name.isBlank()||name.length()>80||name.chars().anyMatch(Character::isISOControl))throw new IOException("Enter a profile name between 1 and 80 characters.");
        Properties p=catalog(root);if(p.values().stream().anyMatch(v -> v.toString().equalsIgnoreCase(name.strip())))throw new IOException("A profile with that name already exists.");
        String id=UUID.randomUUID().toString();p.setProperty("profile."+id,name.strip());saveCatalog(root,p);return id;
    }
    public static void activate(Path root,String id) throws IOException {
        Properties p=catalog(root);if(!p.containsKey("profile."+id))throw new IOException("Unknown profile.");p.setProperty("active",id);saveCatalog(root,p);
    }
    public String profileName() throws IOException {return catalog(root).getProperty("profile."+profileId,profileId);}
    public synchronized void save() throws IOException {
        Path draft = Files.createTempFile(data, "settings-", ".tmp");
        try {
            try (var out = Files.newOutputStream(draft)) { settings.store(out, "GitPort - no secrets in this file"); }
            Files.move(draft, data.resolve("settings.properties"), StandardCopyOption.REPLACE_EXISTING);
        } finally { Files.deleteIfExists(draft); }
    }
    public Path git() throws IOException {
        String configured = settings.getProperty("git", "");
        List<Path> candidates = new ArrayList<>();
        if (!configured.isBlank()) {
            Path path = Path.of(configured);
            candidates.add(path.isAbsolute() ? path : root.resolve(path));
        }
        candidates.add(root.resolve("git/cmd/git.exe"));
        for (Path path : candidates) if (Files.isRegularFile(path)) return path.toAbsolutePath().normalize();
        throw new IOException("The bundled Git runtime is missing. Restore the complete GitPort folder, or choose another portable Git executable in Settings.");
    }
    public String portablePath(Path path) {
        try { return root.relativize(path.toAbsolutePath().normalize()).toString(); }
        catch (IllegalArgumentException differentDrive) { return path.toAbsolutePath().toString(); }
    }
    public Path resolve(String value) { Path p = Path.of(value); return (p.isAbsolute() ? p : root.resolve(p)).normalize(); }
}
