package gitport;

import javax.swing.*;
import java.awt.event.WindowEvent;
import java.awt.image.BufferedImage;
import java.nio.file.*;
import javax.imageio.ImageIO;

/** Renders only this app offscreen. Never captures the user's desktop. */
public final class UiSmokeTest {
    private static void layout(java.awt.Container c){c.doLayout();for(var child:c.getComponents())if(child instanceof java.awt.Container container)layout(container);}
    public static void main(String[] args) throws Exception {
        Path root=Path.of("build/ui-smoke").toAbsolutePath();
        SwingUtilities.invokeAndWait(() -> {
            try {
                App.theme();App app=new App(new Portable(root));app.addNotify();app.validate();
                app.getContentPane().setSize(app.getWidth(),app.getHeight());layout(app.getContentPane());
                BufferedImage image=new BufferedImage(app.getWidth(),app.getHeight(),BufferedImage.TYPE_INT_RGB);
                var graphics=image.createGraphics();app.getContentPane().printAll(graphics);graphics.dispose();
                if(image.getRGB(20,20)==java.awt.Color.BLACK.getRGB())throw new AssertionError("UI did not render");
                ImageIO.write(image,"png",Path.of("build/ui-smoke.png").toFile());
                app.dispatchEvent(new WindowEvent(app,WindowEvent.WINDOW_CLOSING));
                System.out.println("PASS Swing window constructs, lays out, renders and closes without opening a repository.");
            }catch(Exception e){throw new RuntimeException(e);}
        });
    }
}
