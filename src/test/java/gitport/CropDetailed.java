package gitport;

import java.awt.image.BufferedImage;
import java.io.File;
import javax.imageio.ImageIO;

public class CropDetailed {
    public static void main(String[] args) throws Exception {
        BufferedImage im = ImageIO.read(new File("C:/Users/Lenovo/.gemini/antigravity/brain/6e378a57-4302-4285-9b81-6a7632b1d17e/.user_uploaded/media_1794894328560_f7dd4f21.jpg"));
        int w = im.getWidth(), h = im.getHeight();
        
        // Let's crop the exact area around the top row of the commit table and right pane header
        ImageIO.write(im.getSubimage((int)(w * 0.25), (int)(h * 0.15), (int)(w * 0.55), (int)(h * 0.35)), "PNG", new File("build/crop_table_header.png"));
        // And bottom right commit details card
        ImageIO.write(im.getSubimage((int)(w * 0.70), (int)(h * 0.70), (int)(w * 0.30), (int)(h * 0.30)), "PNG", new File("build/crop_bottom_card.png"));
        System.out.println("Done detailed crop");
    }
}
