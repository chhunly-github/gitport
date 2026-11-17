package gitport;

import java.awt.*;
import java.awt.image.BufferedImage;
import java.io.File;
import java.nio.file.Path;
import javax.imageio.ImageIO;
import javax.swing.*;

public class CaptureCurrentUi {
    public static void main(String[] args) throws Exception {
        AtomTheme.install();
        Path root = Path.of("dist/GitPort").toAbsolutePath();
        Portable p = new Portable(root);
        App app = new App(p);
        app.setSize(1300, 800);
        app.setVisible(true);
        Thread.sleep(1500);

        BufferedImage img = new BufferedImage(app.getWidth(), app.getHeight(), BufferedImage.TYPE_INT_RGB);
        Graphics2D g2 = img.createGraphics();
        app.printAll(g2);
        g2.dispose();
        ImageIO.write(img, "PNG", new File("build/current_ui.png"));
        System.out.println("Written current_ui.png");
        app.dispose();
        System.exit(0);
    }
}
