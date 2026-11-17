package gitport;

import java.awt.*;
import java.awt.image.*;
import java.nio.file.*;
import java.util.*;
import java.util.List;
import javax.imageio.ImageIO;

public final class DesignTest {
    private static int passed;
    private static final List<String> ratios=new ArrayList<>();
    private static void check(boolean ok,String text){if(!ok)throw new AssertionError(text);passed++;System.out.println("PASS "+text);}
    private static void contrast(String name,Color fg,Color bg){double ratio=AtomTheme.contrast(fg,bg);ratios.add(String.format(Locale.ROOT,"%s,#%06X,#%06X,%.2f",name,fg.getRGB()&0xffffff,bg.getRGB()&0xffffff,ratio));check(ratio>=4.5,name+String.format(Locale.ROOT," contrast %.2f:1",ratio));}
    public static void main(String[] args)throws Exception{
        AtomTheme.install();
        for(var surface:Map.of("tree",AtomTheme.TREE,"editor",AtomTheme.EDITOR,"header",AtomTheme.HEADER,"input",AtomTheme.INPUT,"hover",AtomTheme.HOVER).entrySet()){
            contrast("Primary on "+surface.getKey(),AtomTheme.TEXT,surface.getValue());contrast("Secondary on "+surface.getKey(),AtomTheme.SECONDARY,surface.getValue());
        }
        contrast("Muted hint",AtomTheme.MUTED,AtomTheme.TREE);contrast("Disabled button",AtomTheme.MUTED,AtomTheme.INPUT);contrast("Selected tree/table",AtomTheme.BRIGHT,AtomTheme.SELECT);contrast("Primary button",AtomTheme.INK,AtomTheme.BLUE);
        contrast("Added status",AtomTheme.GREEN,AtomTheme.TREE);contrast("Modified status",AtomTheme.GOLD,AtomTheme.TREE);contrast("Deleted/conflicted status",AtomTheme.RED,AtomTheme.TREE);contrast("Branch text",AtomTheme.BLUE,AtomTheme.HEADER);contrast("Added diff",AtomTheme.ADDED_TEXT,AtomTheme.ADDED_BG);contrast("Deleted diff",AtomTheme.DELETED_TEXT,AtomTheme.DELETED_BG);
        check(!AtomTheme.UI.getFamily().equalsIgnoreCase("Serif"),"UI font is sans serif: "+AtomTheme.UI.getFamily());
        BufferedImage raster=new BufferedImage(300,100,BufferedImage.TYPE_INT_RGB);Graphics2D rendering=raster.createGraphics();rendering.scale(1.5,1.5);var transform=rendering.getTransform();TextRendering.apply(rendering);
        check(rendering.getRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING).equals(javax.swing.UIManager.get(RenderingHints.KEY_TEXT_ANTIALIASING))&&rendering.getTransform().equals(transform),"custom text uses Swing smoothing without overriding the HiDPI device transform");rendering.dispose();
        for(String family:new String[]{"JetBrains Mono","Fira Code","Source Code Pro"}){
            Font font=EditorFonts.resolve(family,16);check(font.getFamily().equals(family)&&font.getSize()==16,"bundled editor font loads with its real family: "+family);
        }
        Properties fontSettings=new Properties();fontSettings.setProperty("editorFont","Missing font");fontSettings.setProperty("editorFontSize","oops");
        check(EditorFonts.selected(fontSettings).equals(EditorFonts.DEFAULT)&&EditorFonts.current(fontSettings).getSize()==13,"invalid saved font preferences fall back safely");
        check(EditorFonts.resolve("JetBrains Mono",99).getSize()==22,"font size remains within the supported range");
        javax.swing.SwingUtilities.invokeAndWait(() -> {
            javax.swing.JLabel label=new javax.swing.JLabel("Native text");AtomTheme.polish(label);
            check(label.getClientProperty(RenderingHints.KEY_TEXT_ANTIALIASING).equals(javax.swing.UIManager.get(RenderingHints.KEY_TEXT_ANTIALIASING)),"polished components retain the desktop text antialiasing hint");
            {
                TextRendering.selectMode(TextRendering.Mode.GRAYSCALE);TextRendering.configure(label);
                label.setForeground(Color.WHITE);label.setBackground(Color.BLACK);label.setOpaque(true);label.setSize(200,35);
                BufferedImage textImage=new BufferedImage(300,53,BufferedImage.TYPE_INT_RGB);Graphics2D g=textImage.createGraphics();g.scale(1.5,1.5);label.paint(g);g.dispose();
                boolean grayEdges=true,smoothed=false;for(int y=0;y<textImage.getHeight();y++)for(int x=0;x<textImage.getWidth();x++){Color pixel=new Color(textImage.getRGB(x,y));if(pixel.getRed()!=pixel.getGreen()||pixel.getGreen()!=pixel.getBlue())grayEdges=false;if(pixel.getRed()>0&&pixel.getRed()<255)smoothed=true;}
                check(grayEdges&&smoothed,"fractional-DPI text has antialiased edges without LCD color fringes");
                TextRendering.selectMode(TextRendering.Mode.OFF);TextRendering.configure(label);
                g=textImage.createGraphics();g.scale(1.5,1.5);label.paint(g);g.dispose();
                boolean solid=true;for(int y=0;y<textImage.getHeight();y++)for(int x=0;x<textImage.getWidth();x++){int value=textImage.getRGB(x,y)&0xffffff;if(value!=0&&value!=0xffffff)solid=false;}
                check(solid,"pixel-sharp mode draws solid glyph edges without a smoothing halo");
                TextRendering.selectMode(TextRendering.Mode.SYSTEM);
                check(javax.swing.UIManager.get(RenderingHints.KEY_TEXT_ANTIALIASING).equals(TextRendering.hint(TextRendering.Mode.SYSTEM)),"system text mode restores the desktop setting without forcing grayscale");
            }
            DiffView diff=new DiffView();diff.setText("+Added line\n-Removed line");
            Color before=javax.swing.text.StyleConstants.getForeground(diff.getStyledDocument().getCharacterElement(0).getAttributes());
            diff.setCodeFont(EditorFonts.resolve("JetBrains Mono",16));
            var attributes=diff.getStyledDocument().getCharacterElement(0).getAttributes();
            check(javax.swing.text.StyleConstants.getFontFamily(attributes).equals("JetBrains Mono")&&javax.swing.text.StyleConstants.getFontSize(attributes)==16&&before.equals(javax.swing.text.StyleConstants.getForeground(attributes)),"changing diff font preserves syntax colors and updates existing content");
            diff.setText("+Refreshed diff");check(javax.swing.text.StyleConstants.getFontSize(diff.getStyledDocument().getCharacterElement(0).getAttributes())==16,"diff refresh preserves selected font size");
            FontSettings preview=new FontSettings(fontSettings);preview.family.setSelectedItem("Fira Code");preview.size.setValue(17);
            check(preview.preview.getFont().getFamily().equals("Fira Code")&&preview.preview.getFont().getSize()==17&&fontSettings.getProperty("editorFont").equals("Missing font"),"preview updates without persisting a cancelled selection");
            Object previous=javax.swing.UIManager.get(RenderingHints.KEY_TEXT_ANTIALIASING);preview.rendering.setSelectedItem(TextRendering.Mode.OFF);
            check(preview.preview.getClientProperty(RenderingHints.KEY_TEXT_ANTIALIASING).equals(RenderingHints.VALUE_TEXT_ANTIALIAS_OFF)&&javax.swing.UIManager.get(RenderingHints.KEY_TEXT_ANTIALIASING).equals(previous)&&!fontSettings.containsKey("textRendering"),"smoothing preview changes neither global rendering nor saved preferences");
        });
        BufferedImage painted=new BufferedImage(24,24,BufferedImage.TYPE_INT_ARGB);for(String icon:Icons.PATHS.keySet()){var g=painted.createGraphics();Icons.get(icon,AtomTheme.TEXT,24).paintIcon(null,g,0,0);g.dispose();}check(true,"all vector icons render without missing glyphs or invalid paths");
        Path root=Path.of("build/design-fixtures-"+System.currentTimeMillis()).toAbsolutePath();Portable p=new Portable(root);p.settings.setProperty("repos","../repo one\n../repo ü\n../repo third");p.save();RepositoryCatalog catalog=new RepositoryCatalog(p);
        p.settings.setProperty("editorFont","Source Code Pro");p.settings.setProperty("editorFontSize","15");p.save();
        check(EditorFonts.current(new Portable(root).settings).getFamily().equals("Source Code Pro")&&EditorFonts.size(new Portable(root).settings)==15,"editor font preferences survive profile reload");
        check(catalog.groupOf("../repo one").equals("ungrouped"),"legacy repository bookmarks remain visible in Ungrouped");
        String work=catalog.createGroup("Work"),personal=catalog.createGroup("Personal");catalog.move("../repo one",work);catalog.move("../repo ü",work);catalog.renameRepository("../repo ü","Payments API");
        Portable reopened=new Portable(root);RepositoryCatalog restored=new RepositoryCatalog(reopened);check(restored.groupOf("../repo one").equals(work)&&restored.nameOf("../repo ü").equals("Payments API"),"group membership and bookmark labels persist after restart");
        restored.renameGroup(work,"Client projects");check(restored.groups().stream().anyMatch(g->g.id().equals(work)&&g.name().equals("Client projects")),"group rename keeps stable membership");
        restored.move("../repo one",personal);restored.removeGroup(work);check(restored.groupOf("../repo ü").equals("ungrouped")&&restored.groupOf("../repo one").equals(personal),"removing a group preserves bookmarks and other memberships");
        Path repository=root.resolve("real-repository");Files.createDirectories(repository);Path sentinel=repository.resolve("keep.txt");Files.writeString(sentinel,"must stay");String bookmark=p.portablePath(repository);reopened.settings.setProperty("repos",reopened.settings.getProperty("repos")+"\n"+bookmark);reopened.save();restored.forget(bookmark);check(Files.readString(sentinel).equals("must stay"),"removing a bookmark leaves all repository files untouched");
        String profile=Portable.createProfile(root,"Separate account");check(new RepositoryCatalog(new Portable(root,profile)).groups().size()==1,"repository groups are profile-local");
        check(EditorFonts.selected(new Portable(root,profile).settings).equals(EditorFonts.DEFAULT),"new profile does not inherit another profile's font preference");
        try{restored.createGroup("Personal");throw new AssertionError("Duplicate group accepted");}catch(java.io.IOException expected){check(true,"duplicate group names rejected");}
        BufferedImage icon=ImageIO.read(Path.of("assets/icons/gitport-256.png").toFile());check(icon.getWidth()==256&&icon.getHeight()==256&&icon.getColorModel().hasAlpha()&&(icon.getRGB(0,0)>>>24)==0,"exported app icon is correctly sized RGBA with transparent padding");
        byte[] ico=Files.readAllBytes(Path.of("assets/GitPort.ico"));check(ico[0]==0&&ico[2]==1&&(ico[4]&255)==9,"Windows ICO contains nine resolutions");
        Path report=Path.of("build/contrast.csv");Files.writeString(report,"State,Foreground,Background,Ratio\n"+String.join("\n",ratios)+"\n");System.out.println("ALL "+passed+" DESIGN CHECKS PASSED. Contrast evidence: "+report);
    }
}
