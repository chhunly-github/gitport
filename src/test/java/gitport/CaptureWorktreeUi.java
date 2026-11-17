package gitport;

import java.awt.*;
import java.awt.image.BufferedImage;
import java.io.File;
import java.nio.file.Path;
import java.util.List;
import javax.imageio.ImageIO;
import javax.swing.*;

public class CaptureWorktreeUi {
    public static void main(String[] args) throws Exception {
        AtomTheme.install();
        Path root = Path.of("dist/GitPort").toAbsolutePath();
        Portable p = new Portable(root);
        JFrame parent = new JFrame();
        parent.setSize(800, 600);
        parent.setLocationRelativeTo(null);
        parent.setVisible(true);

        Git git = new Git(p, p.git(), Path.of(".").toAbsolutePath());
        
        SwingUtilities.invokeLater(() -> {
            NewWorktreeDialog.show(
                parent,
                git,
                "origin/master",
                List.of("ly", "origin/master", "origin/Chhorvon"),
                (title, init) -> null,
                repo -> {},
                msg -> {},
                err -> {}
            );
        });

        Thread.sleep(1000);
        
        Window[] windows = Window.getWindows();
        for (Window w : windows) {
            if (w instanceof JDialog d && d.isVisible()) {
                BufferedImage img = new BufferedImage(d.getWidth(), d.getHeight(), BufferedImage.TYPE_INT_RGB);
                Graphics2D g2 = img.createGraphics();
                d.printAll(g2);
                g2.dispose();
                ImageIO.write(img, "PNG", new File("build/worktree_dialog.png"));
                System.out.println("Written worktree_dialog.png");
                break;
            }
        }
        System.exit(0);
    }
}
