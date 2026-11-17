package gitport;

import java.awt.*;
import java.awt.image.BufferedImage;
import java.io.File;
import java.nio.file.Path;
import javax.imageio.ImageIO;
import javax.swing.*;

public class CaptureHistoryUi {
    public static void main(String[] args) throws Exception {
        AtomTheme.install();
        Path root = Path.of("dist/GitPort").toAbsolutePath();
        Portable p = new Portable(root);
        App app = new App(p);
        app.setSize(1400, 850);
        app.setVisible(true);
        
        for (int i = 0; i < 40; i++) {
            Thread.sleep(200);
            var f = App.class.getDeclaredField("busy");
            f.setAccessible(true);
            boolean busy = (boolean) f.get(app);
            var g = App.class.getDeclaredField("git");
            g.setAccessible(true);
            Git git = (Git) g.get(app);
            if (!busy && git != null) break;
        }
        
        // Switch to History tab
        var t = App.class.getDeclaredField("tabs");
        t.setAccessible(true);
        JTabbedPane tabs = (JTabbedPane) t.get(app);
        tabs.setSelectedIndex(1);
        Thread.sleep(1000);

        BufferedImage img = new BufferedImage(app.getWidth(), app.getHeight(), BufferedImage.TYPE_INT_RGB);
        Graphics2D g2 = img.createGraphics();
        app.printAll(g2);
        g2.dispose();
        ImageIO.write(img, "PNG", new File("build/history_ui.png"));
        System.out.println("Written history_ui.png");
        app.dispose();
        System.exit(0);
    }
}
