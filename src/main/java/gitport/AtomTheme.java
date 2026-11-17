package gitport;

import javax.swing.*;
import javax.swing.border.*;
import javax.swing.plaf.*;
import javax.swing.plaf.basic.*;
import javax.swing.plaf.metal.*;
import java.awt.*;
import java.util.*;

/** Navy Blue surfaces matching concept branding; balanced contrast (not too dark, not light). */
public final class AtomTheme {
    public static final Color RAIL=c(0x132032), HEADER=c(0x152438), TREE=c(0x152438), EDITOR=c(0x152438), INPUT=c(0x1E324C), HOVER=c(0x243A59), SELECT=c(0x1E518A), BORDER=c(0x293E5C);
    public static final Color TEXT=c(0xE6EEF8), BRIGHT=c(0xFFFFFF), SECONDARY=c(0x8FA8C4), MUTED=c(0x829EC0), BLUE=c(0x00B8E5), GREEN=c(0x98C379), GOLD=c(0xFF943D), RED=c(0xE06C75), PURPLE=c(0xC678DD), INK=c(0x10263E);
    public static final Color ADDED_BG=c(0x24362A), ADDED_TEXT=c(0xB8E39D), DELETED_BG=c(0x3B252D), DELETED_TEXT=c(0xFFB0B7);
    public static final Font UI=choose(new String[]{"Segoe UI","Inter","Noto Sans","SansSerif"},13), CODE=choose(new String[]{"Consolas","Cascadia Code","JetBrains Mono","Monospaced"},13);
    public static Color c(int rgb){return new Color(rgb);}
    private static Font choose(String[] choices,int size){
        Set<String> names=new HashSet<>(Arrays.asList(GraphicsEnvironment.getLocalGraphicsEnvironment().getAvailableFontFamilyNames()));for(String choice:choices)if(names.contains(choice))return new Font(choice,Font.PLAIN,size);
        String windows=System.getenv("SystemRoot");if(windows!=null)try{String file=choices[0].equals("Segoe UI")?"segoeui.ttf":"consola.ttf";return Font.createFont(Font.TRUETYPE_FONT,java.nio.file.Path.of(windows,"Fonts",file).toFile()).deriveFont((float)size);}catch(Exception ignored){}
        return new Font(choices[choices.length-1],Font.PLAIN,size);
    }
    public static void install() throws Exception {
        MetalLookAndFeel.setCurrentTheme(new DefaultMetalTheme(){
            private final FontUIResource f=new FontUIResource(UI);
            public FontUIResource getControlTextFont(){return f;} public FontUIResource getSystemTextFont(){return f;} public FontUIResource getUserTextFont(){return f;} public FontUIResource getMenuTextFont(){return f;} public FontUIResource getWindowTitleFont(){return f;} public FontUIResource getSubTextFont(){return f;}
            protected ColorUIResource getPrimary1(){return new ColorUIResource(BORDER);} protected ColorUIResource getPrimary2(){return new ColorUIResource(SELECT);} protected ColorUIResource getPrimary3(){return new ColorUIResource(INPUT);}
            protected ColorUIResource getSecondary1(){return new ColorUIResource(BORDER);} protected ColorUIResource getSecondary2(){return new ColorUIResource(INPUT);} protected ColorUIResource getSecondary3(){return new ColorUIResource(TREE);}
            public ColorUIResource getControlTextColor(){return new ColorUIResource(TEXT);} public ColorUIResource getUserTextColor(){return new ColorUIResource(TEXT);} public ColorUIResource getSystemTextColor(){return new ColorUIResource(TEXT);} public ColorUIResource getInactiveControlTextColor(){return new ColorUIResource(MUTED);}
            public ColorUIResource getWindowBackground(){return new ColorUIResource(EDITOR);} public ColorUIResource getWindowTitleBackground(){return new ColorUIResource(HEADER);}
        });
        UIManager.setLookAndFeel(new MetalLookAndFeel());
        TextRendering.installDefaults();
        for(Object key:Collections.list(UIManager.getDefaults().keys()))if(key.toString().toLowerCase(Locale.ROOT).endsWith("font"))UIManager.put(key,new FontUIResource(UI));
        for(String prefix:new String[]{"Panel","Viewport","OptionPane","MenuBar","Menu","PopupMenu","CheckBox","RadioButton","TabbedPane"}){UIManager.put(prefix+".background",TREE);UIManager.put(prefix+".foreground",TEXT);}
        for(String prefix:new String[]{"Button","ToggleButton","TextField","FormattedTextField","PasswordField","ComboBox","Spinner","TextArea","TextPane","EditorPane","Table","List","Tree"}){
            UIManager.put(prefix+".background",prefix.equals("Tree")?TREE:EDITOR);UIManager.put(prefix+".foreground",TEXT);UIManager.put(prefix+".inactiveForeground",MUTED);UIManager.put(prefix+".disabledText",MUTED);UIManager.put(prefix+".disabledForeground",MUTED);
            UIManager.put(prefix+".selectionBackground",SELECT);UIManager.put(prefix+".selectionForeground",BRIGHT);UIManager.put(prefix+".caretForeground",BRIGHT);
        }
        UIManager.put("Label.foreground",TEXT);UIManager.put("Label.disabledForeground",MUTED);UIManager.put("OptionPane.messageForeground",TEXT);UIManager.put("ToolTip.background",INPUT);UIManager.put("ToolTip.foreground",BRIGHT);UIManager.put("ToolTip.border",new LineBorder(BORDER));
        for(String prefix:new String[]{"Menu","MenuItem","CheckBoxMenuItem","RadioButtonMenuItem"}){UIManager.put(prefix+".background",TREE);UIManager.put(prefix+".foreground",TEXT);UIManager.put(prefix+".selectionBackground",SELECT);UIManager.put(prefix+".selectionForeground",BRIGHT);UIManager.put(prefix+".disabledForeground",MUTED);}
        UIManager.put("TextField.border",new CompoundBorder(new LineBorder(BORDER),new EmptyBorder(6,8,6,8)));UIManager.put("PasswordField.border",UIManager.get("TextField.border"));
        UIManager.put("ComboBox.selectionBackground",SELECT);UIManager.put("ComboBox.selectionForeground",BRIGHT);UIManager.put("ComboBox.buttonBackground",INPUT);UIManager.put("ComboBox.buttonDarkShadow",BORDER);UIManager.put("ComboBox.buttonHighlight",BORDER);
        UIManager.put("TableHeader.background",HEADER);UIManager.put("TableHeader.foreground",SECONDARY);UIManager.put("Table.gridColor",BORDER);UIManager.put("Table.focusCellHighlightBorder",new EmptyBorder(0,0,0,0));UIManager.put("TableHeader.cellBorder",new EmptyBorder(7,9,7,9));
        UIManager.put("Tree.textBackground",TREE);UIManager.put("Tree.textForeground",TEXT);UIManager.put("Tree.selectionBackground",SELECT);UIManager.put("Tree.selectionForeground",BRIGHT);UIManager.put("Tree.selectionBorderColor",SELECT);UIManager.put("Tree.hash",TREE);UIManager.put("Tree.expandedIcon",Icons.get("chevron-down",MUTED,14));UIManager.put("Tree.collapsedIcon",Icons.get("chevron-right",MUTED,14));
        UIManager.put("ScrollPane.border",new EmptyBorder(0,0,0,0));UIManager.put("SplitPane.border",new EmptyBorder(0,0,0,0));UIManager.put("SplitPaneDivider.border",new EmptyBorder(0,0,0,0));UIManager.put("SplitPane.background",BORDER);UIManager.put("SplitPane.dividerSize",3);
        UIManager.put("ScrollBar.width",10);UIManager.put("ScrollBar.thumb",BORDER);UIManager.put("ScrollBar.thumbDarkShadow",BORDER);UIManager.put("ScrollBar.thumbHighlight",BORDER);UIManager.put("ScrollBar.track",EDITOR);
        UIManager.put("ScrollBarUI","gitport.AtomScrollBarUI");
        UIManager.put("TabbedPane.selected",EDITOR);UIManager.put("TabbedPane.contentAreaColor",EDITOR);UIManager.put("TabbedPane.focus",BLUE);UIManager.put("TabbedPane.tabInsets",new Insets(10,16,10,16));UIManager.put("TabbedPane.contentBorderInsets",new Insets(0,0,0,0));
        UIManager.put("OptionPaneUI","gitport.AtomOptionPaneUI");
    }
    public static void polish(Component component){
        if(component instanceof JComponent swing)TextRendering.configure(swing);
        Font previous=component.getFont();if(previous!=null){Font base=previous.getFamily().equalsIgnoreCase(CODE.getFamily())?CODE:UI;component.setFont(base.deriveFont(previous.getStyle(),previous.getSize2D()));}
        if(component instanceof AbstractButton b&&b.getUI().getClass().getName().contains("MetalButtonUI"))button(b,false);
        if(component instanceof Container container)for(Component child:container.getComponents())polish(child);
    }
    public static void button(AbstractButton button,boolean primary){
        button.setFont(UI);button.setForeground(primary?INK:TEXT);button.setBackground(primary?BLUE:INPUT);button.setBorder(new EmptyBorder(6,11,6,11));button.setFocusPainted(false);button.setContentAreaFilled(false);button.setOpaque(false);button.setCursor(Cursor.getPredefinedCursor(Cursor.HAND_CURSOR));
        button.setUI(new BasicButtonUI(){
            public void paint(Graphics g,JComponent c){AbstractButton b=(AbstractButton)c;Graphics2D p=(Graphics2D)g.create();p.setRenderingHint(RenderingHints.KEY_ANTIALIASING,RenderingHints.VALUE_ANTIALIAS_ON);p.setRenderingHint(RenderingHints.KEY_STROKE_CONTROL,RenderingHints.VALUE_STROKE_NORMALIZE);
                Color fill=!b.isEnabled()?INPUT:primary?(b.getModel().isRollover()?c(0x79BAF2):BLUE):(b.getModel().isRollover()?HOVER:b.getBackground());
                Color border=!b.isEnabled()?BORDER:primary?(b.getModel().isRollover()?c(0x90C8F8):BLUE):(b.getModel().isRollover()?c(0x525C6D):BORDER);
                p.setColor(fill);p.fillRoundRect(0,0,c.getWidth()-1,c.getHeight()-1,4,4);
                p.setColor(border);p.drawRoundRect(0,0,c.getWidth()-1,c.getHeight()-1,4,4);
                if(b.hasFocus()){p.setColor(BLUE);p.drawRoundRect(1,1,c.getWidth()-3,c.getHeight()-3,4,4);}p.dispose();
                super.paint(g,c);
            }
            protected void paintText(Graphics g,AbstractButton b,Rectangle r,String text){Graphics2D textGraphics=(Graphics2D)g.create();try{TextRendering.apply(textGraphics);textGraphics.setFont(b.getFont());textGraphics.setColor(!b.isEnabled()?MUTED:b.getForeground());FontMetrics fm=textGraphics.getFontMetrics();int y=r.y+(r.height-fm.getHeight())/2+fm.getAscent();textGraphics.drawString(text,r.x,y);}finally{textGraphics.dispose();}}
        });
        button.setRolloverEnabled(true);
    }
    public static void tabs(JTabbedPane tabs){tabs.setUI(new BasicTabbedPaneUI(){
        protected void paintContentBorder(Graphics g,int placement,int selected){}
        protected void paintTabBorder(Graphics g,int place,int index,int x,int y,int w,int h,boolean selected){if(selected){g.setColor(BLUE);g.fillRect(x,y+h-2,w,2);}}
        protected void paintFocusIndicator(Graphics g,int p,Rectangle[] r,int i,Rectangle a,Rectangle b,boolean s){}
        protected void paintTabBackground(Graphics g,int place,int index,int x,int y,int w,int h,boolean selected){g.setColor(selected?EDITOR:HEADER);g.fillRect(x,y,w,h);}
    });}
    public static double contrast(Color a,Color b){double x=luminance(a),y=luminance(b);return (Math.max(x,y)+.05)/(Math.min(x,y)+.05);}
    private static double luminance(Color color){double[] a={color.getRed()/255.,color.getGreen()/255.,color.getBlue()/255.};for(int i=0;i<3;i++)a[i]=a[i]<=.04045?a[i]/12.92:Math.pow((a[i]+.055)/1.055,2.4);return .2126*a[0]+.7152*a[1]+.0722*a[2];}
}
