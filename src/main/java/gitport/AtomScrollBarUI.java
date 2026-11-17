package gitport;
import javax.swing.*;
import javax.swing.plaf.*;
import javax.swing.plaf.basic.*;
import java.awt.*;

public final class AtomScrollBarUI extends BasicScrollBarUI {
    public static ComponentUI createUI(JComponent c){return new AtomScrollBarUI();}
    private JButton empty(){JButton b=new JButton();Dimension zero=new Dimension(0,0);b.setPreferredSize(zero);b.setMinimumSize(zero);b.setMaximumSize(zero);return b;}
    protected JButton createDecreaseButton(int orientation){return empty();}protected JButton createIncreaseButton(int orientation){return empty();}
    protected void paintTrack(Graphics g,JComponent c,Rectangle track){g.setColor(AtomTheme.EDITOR);g.fillRect(track.x,track.y,track.width,track.height);}
    protected void paintThumb(Graphics g,JComponent c,Rectangle thumb){if(thumb.isEmpty()||!scrollbar.isEnabled())return;Graphics2D p=(Graphics2D)g.create();p.setRenderingHint(RenderingHints.KEY_ANTIALIASING,RenderingHints.VALUE_ANTIALIAS_ON);p.setRenderingHint(RenderingHints.KEY_STROKE_CONTROL,RenderingHints.VALUE_STROKE_NORMALIZE);p.setColor(isThumbRollover()?AtomTheme.SECONDARY:AtomTheme.c(0x566072));p.fillRoundRect(thumb.x+2,thumb.y+2,Math.max(4,thumb.width-4),Math.max(4,thumb.height-4),4,4);p.dispose();}
}
