package gitport;

import java.nio.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.*;
import java.util.Arrays;
import javax.crypto.*;
import javax.crypto.spec.*;

/** Password-based, machine-independent token storage. No Windows DPAPI dependency. */
public final class Vault {
    private static final int MAGIC=0x47505631, ITERATIONS=310_000;
    private static SecretKey key(char[] password, byte[] salt) throws Exception {
        PBEKeySpec spec=new PBEKeySpec(password,salt,ITERATIONS,256);
        try { return new SecretKeySpec(SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256").generateSecret(spec).getEncoded(),"AES"); }
        finally { spec.clearPassword(); }
    }
    public static byte[] encrypt(char[] password, String token) throws Exception {
        byte[] salt=new byte[16], iv=new byte[12]; SecureRandom rng=new SecureRandom(); rng.nextBytes(salt); rng.nextBytes(iv);
        Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding"); cipher.init(Cipher.ENCRYPT_MODE,key(password,salt),new GCMParameterSpec(128,iv));
        cipher.updateAAD(ByteBuffer.allocate(4).putInt(MAGIC).array());
        byte[] plain=token.getBytes(StandardCharsets.UTF_8);
        try { byte[] sealed=cipher.doFinal(plain); return ByteBuffer.allocate(4+16+12+sealed.length).putInt(MAGIC).put(salt).put(iv).put(sealed).array(); }
        finally { Arrays.fill(plain,(byte)0); }
    }
    public static String decrypt(char[] password, byte[] sealed) throws Exception {
        if (sealed.length<48) throw new GeneralSecurityException("Invalid vault.");
        ByteBuffer b=ByteBuffer.wrap(sealed); if (b.getInt()!=MAGIC) throw new GeneralSecurityException("Unknown vault format.");
        byte[] salt=new byte[16], iv=new byte[12], body=new byte[b.remaining()-28]; b.get(salt); b.get(iv); b.get(body);
        Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding"); cipher.init(Cipher.DECRYPT_MODE,key(password,salt),new GCMParameterSpec(128,iv));
        cipher.updateAAD(ByteBuffer.allocate(4).putInt(MAGIC).array()); byte[] plain=cipher.doFinal(body);
        try { return new String(plain,StandardCharsets.UTF_8); } finally { Arrays.fill(plain,(byte)0); }
    }
    public static void save(Path file, char[] password, String token) throws Exception {
        Path tmp=Files.createTempFile(file.getParent(),"vault-",".tmp");
        try { Files.write(tmp,encrypt(password,token)); Files.move(tmp,file,StandardCopyOption.REPLACE_EXISTING); }
        finally { Files.deleteIfExists(tmp); }
    }
}
