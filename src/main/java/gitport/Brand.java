package gitport;

import javax.swing.*;
import java.awt.*;
import java.awt.image.*;
import java.util.*;
import java.util.List;
import javax.imageio.ImageIO;

public final class Brand {
    private static BufferedImage read(String file){try(var in=Brand.class.getResourceAsStream("/gitport/brand/"+file)){return in==null?null:ImageIO.read(in);}catch(Exception ignored){return null;}}
    public static Icon icon(int size){BufferedImage image=read("gitport-"+size+".png");if(image!=null)return new ImageIcon(image);image=read("gitport-64.png");return image==null?Icons.get("repo",AtomTheme.BLUE,size):new ImageIcon(image.getScaledInstance(size,size,Image.SCALE_SMOOTH));}
    public static List<Image> windowIcons(){List<Image> images=new ArrayList<>();for(int size:new int[]{16,20,24,32,40,48,64,128,256}){BufferedImage image=read("gitport-"+size+".png");if(image!=null)images.add(image);}return images;}
    public static JWindow splash(){BufferedImage image=read("splashscreen.png");if(image==null)return null;JWindow window=new JWindow();window.setIconImages(windowIcons());window.add(new JLabel(new ImageIcon(image)));window.pack();window.setLocationRelativeTo(null);window.setVisible(true);window.getRootPane().paintImmediately(0,0,window.getWidth(),window.getHeight());return window;}
}
