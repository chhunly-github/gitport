package gitport;

import javax.swing.*;
import java.nio.file.*;

public final class Main {
    public static void main(String[] args) throws Exception {
        if (args.length==1 && args[0].equals("--version")) { System.out.println("GitPort 0.2.0 · Java " + System.getProperty("java.version")); return; }
        if (args.length>0 && System.getenv("GITPORT_ASKPASS_TOKEN")!=null) { Auth.answer(args[0]); return; }
        Path root=System.getProperty("gitport.home")!=null ? Path.of(System.getProperty("gitport.home"))
            : Path.of(Main.class.getProtectionDomain().getCodeSource().getLocation().toURI()).getParent().getParent();
        Portable portable=new Portable(root);
        System.setProperty("java.io.tmpdir",portable.temp.toString());
        System.setProperty("user.home",portable.data.toString());
        SwingUtilities.invokeLater(() -> {
            JWindow splash=null;
            try { App.theme();splash=Brand.splash();new App(portable).setVisible(true); }
            catch(Exception e) { JOptionPane.showMessageDialog(null,e.getMessage(),"GitPort",JOptionPane.ERROR_MESSAGE); }
            finally {if(splash!=null)splash.dispose();}
        });
    }
}

