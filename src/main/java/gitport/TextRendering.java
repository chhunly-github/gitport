package gitport;

import java.awt.*;
import java.util.Map;
import java.util.Properties;
import javax.swing.*;

/** Consistent, selectable text rasterization without changing Windows display settings. */
public final class TextRendering {
    public enum Mode {
        SYSTEM("System / Windows ClearType"), GRAYSCALE("Grayscale (smooth)"), OFF("No smoothing (pixel sharp)");
        private final String label;Mode(String label){this.label=label;}@Override public String toString(){return label;}
    }
    public static Mode selected(Properties settings){try{return Mode.valueOf(settings.getProperty("textRendering","SYSTEM"));}catch(IllegalArgumentException e){return Mode.SYSTEM;}}
    public static void installDefaults(){
        Object desktop=Toolkit.getDefaultToolkit().getDesktopProperty("awt.font.desktophints");
        if(desktop instanceof Map<?,?> hints){
            Object aa=hints.get(RenderingHints.KEY_TEXT_ANTIALIASING);
            if(aa!=null)UIManager.put(RenderingHints.KEY_TEXT_ANTIALIASING,aa);
            Object contrast=hints.get(RenderingHints.KEY_TEXT_LCD_CONTRAST);
            if(contrast instanceof Integer cVal){
                UIManager.put(RenderingHints.KEY_TEXT_LCD_CONTRAST,Math.max(140,cVal));
            }else if(contrast!=null){
                UIManager.put(RenderingHints.KEY_TEXT_LCD_CONTRAST,contrast);
            }
        }
        if(UIManager.get(RenderingHints.KEY_TEXT_ANTIALIASING)==null)UIManager.put(RenderingHints.KEY_TEXT_ANTIALIASING,RenderingHints.VALUE_TEXT_ANTIALIAS_ON);
        if(UIManager.get(RenderingHints.KEY_TEXT_LCD_CONTRAST)==null)UIManager.put(RenderingHints.KEY_TEXT_LCD_CONTRAST,140);
        UIManager.put(RenderingHints.KEY_FRACTIONALMETRICS,RenderingHints.VALUE_FRACTIONALMETRICS_OFF);
        UIManager.put(RenderingHints.KEY_RENDERING,RenderingHints.VALUE_RENDER_QUALITY);
        UIManager.put(RenderingHints.KEY_STROKE_CONTROL,RenderingHints.VALUE_STROKE_NORMALIZE);
    }
    public static Object hint(Mode mode){
        if(mode==Mode.GRAYSCALE)return RenderingHints.VALUE_TEXT_ANTIALIAS_ON;
        if(mode==Mode.OFF)return RenderingHints.VALUE_TEXT_ANTIALIAS_OFF;
        Object desktop=Toolkit.getDefaultToolkit().getDesktopProperty("awt.font.desktophints");
        if(desktop instanceof Map<?,?> hints&&hints.get(RenderingHints.KEY_TEXT_ANTIALIASING)!=null)return hints.get(RenderingHints.KEY_TEXT_ANTIALIASING);
        return RenderingHints.VALUE_TEXT_ANTIALIAS_ON;
    }
    public static void selectMode(Mode mode){installDefaults();UIManager.put(RenderingHints.KEY_TEXT_ANTIALIASING,hint(mode));}
    public static void configureTree(Component component){if(component instanceof JComponent swing){configure(swing);swing.revalidate();swing.repaint();}if(component instanceof Container container)for(Component child:container.getComponents())configureTree(child);}
    public static void configure(JComponent component){
        component.putClientProperty(RenderingHints.KEY_TEXT_ANTIALIASING,UIManager.get(RenderingHints.KEY_TEXT_ANTIALIASING));
        component.putClientProperty(RenderingHints.KEY_TEXT_LCD_CONTRAST,UIManager.get(RenderingHints.KEY_TEXT_LCD_CONTRAST));
        component.putClientProperty(RenderingHints.KEY_FRACTIONALMETRICS,RenderingHints.VALUE_FRACTIONALMETRICS_OFF);
    }
    public static void apply(Graphics2D graphics){
        Object aa=UIManager.get(RenderingHints.KEY_TEXT_ANTIALIASING);
        if(aa!=null)graphics.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING,aa);
        Object contrast=UIManager.get(RenderingHints.KEY_TEXT_LCD_CONTRAST);
        if(contrast!=null)graphics.setRenderingHint(RenderingHints.KEY_TEXT_LCD_CONTRAST,contrast);
        graphics.setRenderingHint(RenderingHints.KEY_FRACTIONALMETRICS,RenderingHints.VALUE_FRACTIONALMETRICS_OFF);
        graphics.setRenderingHint(RenderingHints.KEY_RENDERING,RenderingHints.VALUE_RENDER_QUALITY);
        graphics.setRenderingHint(RenderingHints.KEY_STROKE_CONTROL,RenderingHints.VALUE_STROKE_NORMALIZE);
    }
    private TextRendering(){}
}
