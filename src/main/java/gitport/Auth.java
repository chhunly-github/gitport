package gitport;

import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.*;
import java.util.*;
import java.util.function.Function;

/** Loopback-only askpass bridge. Secrets are never passed in command-line arguments. */
public final class Auth implements AutoCloseable {
    private final ServerSocket server;
    private final String nonce=UUID.randomUUID().toString()+UUID.randomUUID();
    private volatile boolean closed;
    public Auth(Function<String,String> prompt) throws IOException {
        server=new ServerSocket(0,8,InetAddress.getByName("127.0.0.1"));
        Thread worker=new Thread(() -> {
            while (!closed) {
                try (Socket socket=server.accept()) {
                    socket.setSoTimeout(5000);
                    var in=new DataInputStream(socket.getInputStream()); var out=new DataOutputStream(socket.getOutputStream());
                    String supplied=in.readUTF();
                    if (!MessageDigest.isEqual(nonce.getBytes(StandardCharsets.UTF_8),supplied.getBytes(StandardCharsets.UTF_8))) continue;
                    String question=in.readUTF(); socket.setSoTimeout(0);
                    String answer=prompt.apply(question); out.writeUTF(answer==null ? "" : answer); out.flush();
                } catch(Exception ignored) { /* Closed/cancelled authentication; never log secrets. */ }
            }
        },"GitPort-askpass"); worker.setDaemon(true); worker.start();
    }
    public Map<String,String> environment(Path launcher) {
        return Map.of("GIT_ASKPASS",launcher.toString(),"GITPORT_ASKPASS_PORT",Integer.toString(server.getLocalPort()),"GITPORT_ASKPASS_TOKEN",nonce);
    }
    public static void answer(String question) throws Exception {
        try (Socket socket=new Socket()) {
            socket.connect(new InetSocketAddress("127.0.0.1",Integer.parseInt(System.getenv("GITPORT_ASKPASS_PORT"))),3000);
            socket.setSoTimeout(300_000);
            var out=new DataOutputStream(socket.getOutputStream()); var in=new DataInputStream(socket.getInputStream());
            out.writeUTF(System.getenv("GITPORT_ASKPASS_TOKEN")); out.writeUTF(question); out.flush();
            System.out.println(in.readUTF());
        }
    }
    @Override public void close() throws IOException { closed=true; server.close(); }
}
