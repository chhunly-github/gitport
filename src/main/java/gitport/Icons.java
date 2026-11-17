package gitport;

import javax.swing.*;
import java.awt.*;
import java.awt.geom.*;
import java.util.*;

/** Small UI icons are vector strokes, never platform emoji or font glyphs. */
public final class Icons implements Icon {
    public static final Map<String,String> PATHS=Map.ofEntries(
        Map.entry("folder","M2 6 L9 6 L11 8 L22 8 L22 20 L2 20 Z M2 8 L2 4 L9 4 L11 6"),
        Map.entry("repo","M5 3 L19 3 L19 21 L5 21 Z M9 3 L9 21 M13 8 L16 8 M13 12 L16 12"),
        Map.entry("branch","M7 5 L7 18 M7 13 C17 13 17 9 17 6"),
        Map.entry("history","M4 8 C7 1 19 3 21 10 C24 20 10 25 5 18 M4 3 L4 9 L10 9 M13 7 L13 13 L17 15"),
        Map.entry("file","M5 2 L14 2 L20 8 L20 22 L5 22 Z M14 2 L14 8 L20 8 M9 13 L16 13 M9 17 L16 17"),
        Map.entry("refresh","M4 9 C7 1 19 2 21 9 M21 3 L21 9 L15 9 M20 15 C17 23 5 22 3 15 M3 21 L3 15 L9 15"),
        Map.entry("fetch","M12 3 L12 15 M7 10 L12 15 L17 10 M4 17 L4 21 L20 21 L20 17"),
        Map.entry("pull","M12 3 L12 17 M7 12 L12 17 L17 12 M5 21 L19 21"),
        Map.entry("push","M12 20 L12 5 M7 10 L12 5 L17 10 M5 2 L19 2"),
        Map.entry("plus","M12 5 L12 19 M5 12 L19 12"),
        Map.entry("check","M4 12 L9 17 L20 6"),
        Map.entry("minus","M5 12 L19 12"),
        Map.entry("search","M16 16 L22 22"),
        Map.entry("settings","M12 2 L14 5 L18 5 L19 9 L22 12 L19 15 L18 19 L14 19 L12 22 L10 19 L6 19 L5 15 L2 12 L5 9 L6 5 L10 5 Z"),
        Map.entry("profile","M3 22 C3 13 21 13 21 22"),
        Map.entry("terminal","M3 4 L21 4 L21 20 L3 20 Z M7 8 L11 12 L7 16 M13 16 L17 16"),
        Map.entry("merge","M6 5 L6 19 M18 5 L18 9 C18 13 6 11 6 16 M3 16 L6 19 L9 16"),
        Map.entry("shelf","M3 7 L21 7 L19 21 L5 21 Z M7 3 L17 3 M9 12 L15 12"),
        Map.entry("conflict","M12 2 L23 21 L1 21 Z M12 8 L12 14 M12 17 L12 18"),
        Map.entry("chevron-down","M6 9 L12 15 L18 9"),
        Map.entry("chevron-right","M9 6 L15 12 L9 18"),
        Map.entry("close","M6 6 L18 18 M18 6 L6 18"),
        Map.entry("more","M4 12 L4 12 M12 12 L12 12 M20 12 L20 12"),
        Map.entry("diff","M5 2 L19 2 L19 22 L5 22 Z M8 8 L16 8 M12 5 L12 11 M8 16 L16 16"),
        Map.entry("worktree","M6 5 L6 19 M6 12 C13 12 14 9 14 7 M14 4 L21 4 L21 10 L14 10 Z")
    );
    private final String name;private final Color color;private final int size;
    private Icons(String name,Color color,int size){this.name=name;this.color=color;this.size=size;}
    public static Icon get(String name,Color color,int size){return new Icons(name,color,size);}
    public int getIconWidth(){return size;}public int getIconHeight(){return size;}
    public void paintIcon(Component c,Graphics graphics,int x,int y){Graphics2D g=(Graphics2D)graphics.create();g.translate(x,y);g.scale(size/24.,size/24.);g.setRenderingHint(RenderingHints.KEY_ANTIALIASING,RenderingHints.VALUE_ANTIALIAS_ON);g.setRenderingHint(RenderingHints.KEY_STROKE_CONTROL,RenderingHints.VALUE_STROKE_PURE);g.setColor(c!=null&&!c.isEnabled()?AtomTheme.MUTED:color);g.setStroke(new BasicStroke(1.45f,BasicStroke.CAP_ROUND,BasicStroke.JOIN_ROUND));g.draw(path(PATHS.getOrDefault(name,PATHS.get("file"))));
        if(name.equals("branch")||name.equals("worktree")){g.draw(new Ellipse2D.Double(3,2,6,6));g.draw(new Ellipse2D.Double(3,16,6,6));if(name.equals("branch"))g.draw(new Ellipse2D.Double(14,2,6,6));}
        if(name.equals("profile"))g.draw(new Ellipse2D.Double(7,2,10,10));if(name.equals("search"))g.draw(new Ellipse2D.Double(3,3,14,14));if(name.equals("settings"))g.draw(new Ellipse2D.Double(8,8,8,8));
        if(name.equals("more")){g.fill(new Ellipse2D.Double(3,11,2,2));g.fill(new Ellipse2D.Double(11,11,2,2));g.fill(new Ellipse2D.Double(19,11,2,2));}g.dispose();}
    private static Path2D path(String source){String[] a=source.replaceAll("([MLCZ])"," $1 ").trim().split("\\s+");Path2D.Double p=new Path2D.Double();for(int i=0;i<a.length;){String token=a[i++];switch(token){case "M" -> p.moveTo(Double.parseDouble(a[i++]),Double.parseDouble(a[i++]));case "L" -> p.lineTo(Double.parseDouble(a[i++]),Double.parseDouble(a[i++]));case "C" -> p.curveTo(Double.parseDouble(a[i++]),Double.parseDouble(a[i++]),Double.parseDouble(a[i++]),Double.parseDouble(a[i++]),Double.parseDouble(a[i++]),Double.parseDouble(a[i++]));case "Z" -> p.closePath();default -> throw new IllegalArgumentException(token);}}return p;}
}
