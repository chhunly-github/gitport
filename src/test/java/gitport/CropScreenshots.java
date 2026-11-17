package gitport;

import java.awt.image.BufferedImage;
import java.io.File;
import javax.imageio.ImageIO;

public class CropScreenshots {
    public static void main(String[] args) throws Exception {
        File file = new File("C:/Users/Lenovo/.gemini/antigravity/brain/6e378a57-4302-4285-9b81-6a7632b1d17e/.user_uploaded/media_1794894328560_f7dd4f21.jpg");
        BufferedImage im = ImageIO.read(file);
        int w = im.getWidth(), h = im.getHeight();
        System.out.println("Size: " + w + "x" + h);

        ImageIO.write(im.getSubimage((int)(w*0.28), 0, (int)(w*0.52), (int)(h*0.4)), "PNG", new File("build/crop_top_commit.png"));
        ImageIO.write(im.getSubimage((int)(w*0.38), 0, (int)(w*0.37), (int)(h*0.75)), "PNG", new File("build/crop_menu.png"));
        ImageIO.write(im.getSubimage((int)(w*0.75), 0, (int)(w*0.25), (int)(h*0.95)), "PNG", new File("build/crop_right_tree.png"));
        ImageIO.write(im.getSubimage(0, 0, (int)(w*0.32), h), "PNG", new File("build/crop_left_tree.png"));
        System.out.println("Cropped ok");
    }
}
