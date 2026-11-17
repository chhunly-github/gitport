package gitport;

import javax.swing.*;
import java.awt.*;
import java.awt.event.WindowEvent;
import java.awt.image.BufferedImage;
import java.lang.reflect.*;
import java.nio.file.*;
import javax.imageio.ImageIO;

/** Captures only GitPort components, using real Git operations on disposable fixture repositories. */
public final class UiAcceptanceTest {
    private static App app;private static Path evidence;
    private static void layout(Container c){c.doLayout();for(var child:c.getComponents())if(child instanceof Container container)layout(container);}
    private static Object field(Object owner,String name)throws Exception{Field f=owner.getClass().getDeclaredField(name);f.setAccessible(true);return f.get(owner);}
    private static void invoke(String name)throws Exception{Method m=App.class.getDeclaredMethod(name);m.setAccessible(true);m.invoke(app);}
    private static void capture(Container content,String name)throws Exception{layout(content);BufferedImage image=new BufferedImage(content.getWidth(),content.getHeight(),BufferedImage.TYPE_INT_RGB);var g=image.createGraphics();content.printAll(g);g.dispose();ImageIO.write(image,"png",evidence.resolve(name+".png").toFile());if(image.getRGB(20,20)==Color.BLACK.getRGB())throw new AssertionError("UI did not render: "+name);}
    private static void frame(String name,int width,int height)throws Exception{SwingUtilities.invokeAndWait(() -> {try{app.setSize(width,height);app.getContentPane().setSize(width,height);layout(app.getContentPane());capture(app.getContentPane(),name);}catch(Exception e){throw new RuntimeException(e);}});}
    private static void scaledCaptures()throws Exception{SwingUtilities.invokeAndWait(() -> {try{
        Container content=app.getContentPane();layout(content);
        for(double scale:new double[]{1.25,1.5,2.0}){
            BufferedImage raster=new BufferedImage((int)Math.ceil(content.getWidth()*scale),(int)Math.ceil(content.getHeight()*scale),BufferedImage.TYPE_INT_RGB);
            Graphics2D graphics=raster.createGraphics();graphics.scale(scale,scale);content.printAll(graphics);graphics.dispose();
            ImageIO.write(raster,"png",evidence.resolve("10-workspace-"+(int)(scale*100)+"pct.png").toFile());
        }
    }catch(Exception e){throw new RuntimeException(e);}});}
    private static JTabbedPane settingsTabs(Container container){for(Component c:container.getComponents()){if(c instanceof JTabbedPane p&&p.getTabCount()==4&&p.getTitleAt(0).equals("Identity"))return p;if(c instanceof Container child){JTabbedPane p=settingsTabs(child);if(p!=null)return p;}}return null;}
    private static FontSettings fontSettings(Container container){for(Component c:container.getComponents()){if(c instanceof FontSettings f)return f;if(c instanceof Container child){FontSettings f=fontSettings(child);if(f!=null)return f;}}return null;}
    private static void captureFontChoices(JDialog dialog,JTabbedPane tabs)throws Exception{
        tabs.setSelectedIndex(3);FontSettings fonts=fontSettings(dialog.getContentPane());if(fonts==null)throw new AssertionError("Font settings missing");
        String[] families={"JetBrains Mono","Fira Code","Source Code Pro"};
        for(int i=0;i<families.length;i++){fonts.family.setSelectedItem(families[i]);fonts.size.setValue(14);capture(dialog.getContentPane(),"05"+(char)('d'+i)+"-font-"+families[i].toLowerCase(java.util.Locale.ROOT).replace(' ','-'));}
        for(TextRendering.Mode mode:TextRendering.Mode.values()){fonts.rendering.setSelectedItem(mode);capture(dialog.getContentPane(),"05g-smoothing-"+mode.name().toLowerCase(java.util.Locale.ROOT));}
    }
    private static void acceptFontSettings()throws Exception{
        java.util.concurrent.atomic.AtomicReference<Throwable> error=new java.util.concurrent.atomic.AtomicReference<>();
        SwingUtilities.invokeAndWait(() -> {
            javax.swing.Timer timer=new javax.swing.Timer(250,null);int[] attempts={0};timer.addActionListener(event -> {try{
                for(Window w:app.getOwnedWindows())if(w instanceof JDialog d&&d.isVisible()){
                    FontSettings fonts=fontSettings(d.getContentPane());if(fonts==null)continue;
                    fonts.family.setSelectedItem("JetBrains Mono");fonts.size.setValue(15);fonts.rendering.setSelectedItem(TextRendering.Mode.OFF);
                    Container parent=fonts;while(parent!=null&&!(parent instanceof JOptionPane))parent=parent.getParent();
                    if(!(parent instanceof JOptionPane pane))throw new AssertionError("Settings option pane missing");
                    pane.setValue(JOptionPane.OK_OPTION);timer.stop();return;
                }
                if(++attempts[0]>80){timer.stop();error.set(new AssertionError("Settings did not open"));for(Window w:app.getOwnedWindows())if(w instanceof JDialog d)d.dispose();}
            }catch(Throwable e){error.set(e);timer.stop();for(Window w:app.getOwnedWindows())if(w instanceof JDialog d)d.dispose();}});
            timer.start();try{invoke("settings");}catch(Throwable e){timer.stop();throw new RuntimeException(e);}
        });
        if(error.get()!=null)throw new AssertionError("Cannot apply font preferences",error.get());idle();
        Portable p=(Portable)field(app,"portable");
        if(!p.settings.getProperty("editorFont","").equals("JetBrains Mono")||EditorFonts.size(new Portable(p.root).settings)!=15)throw new AssertionError("Font preferences were not saved");
        if(TextRendering.selected(new Portable(p.root).settings)!=TextRendering.Mode.OFF)throw new AssertionError("Smoothing mode not saved");
        if(!((DiffView)field(app,"diff")).getClientProperty(RenderingHints.KEY_TEXT_ANTIALIASING).equals(RenderingHints.VALUE_TEXT_ANTIALIAS_OFF))throw new AssertionError("Whole-interface smoothing not applied");
        SwingUtilities.invokeAndWait(() -> {try{if(!((DiffView)field(app,"diff")).getFont().getFamily().equals("JetBrains Mono")||((JTextArea)field(app,"output")).getFont().getSize()!=15)throw new AssertionError("Font not applied immediately");}catch(Exception e){throw new RuntimeException(e);}});
    }
    private static void idle()throws Exception{for(int i=0;i<400;i++){boolean[] done={false};SwingUtilities.invokeAndWait(() -> {try{done[0]=field(app,"git")!=null&&!(boolean)field(app,"busy");}catch(Exception e){throw new RuntimeException(e);}});if(done[0])return;Thread.sleep(50);}throw new AssertionError("GitPort UI operation did not finish in 20 seconds.");}
    private static void dialog(String method,String name)throws Exception{
        var error=new java.util.concurrent.atomic.AtomicReference<Throwable>();
        SwingUtilities.invokeAndWait(() -> {javax.swing.Timer timer=new javax.swing.Timer(250,null);final int[] attempts={0};timer.addActionListener(event -> {try{
            for(Window window:app.getOwnedWindows())if(window instanceof JDialog d&&d.isVisible()){capture(d.getContentPane(),name);if(method.equals("settings")){JTabbedPane sections=settingsTabs(d.getContentPane());if(sections==null)throw new AssertionError("Settings tabs missing");sections.setSelectedIndex(1);capture(d.getContentPane(),"05b-authentication");sections.setSelectedIndex(2);capture(d.getContentPane(),"05c-runtime");captureFontChoices(d,sections);}d.dispose();timer.stop();return;}
            if(++attempts[0]>80){timer.stop();error.set(new AssertionError("Dialog did not open: "+name));}
        }catch(Throwable e){error.set(e);timer.stop();for(Window w:app.getOwnedWindows())if(w instanceof JDialog d)d.dispose();}});timer.start();try{invoke(method);}catch(Throwable e){timer.stop();throw new RuntimeException(e);}});
        for(int i=0;i<240&&!Files.exists(evidence.resolve(name+".png"));i++){if(error.get()!=null)throw new AssertionError(error.get());Thread.sleep(50);}
        if(error.get()!=null||!Files.exists(evidence.resolve(name+".png")))throw new AssertionError("No dialog capture: "+name,error.get());
    }
    public static void main(String[] args){try{run(args);}catch(Throwable e){e.printStackTrace();System.exit(1);}}
    private static void run(String[] args)throws Exception{
        evidence=Path.of(args.length>0?args[0]:"build/screenshots").toAbsolutePath();Files.createDirectories(evidence);
        Path root=Path.of("build/ui-fixtures-"+System.currentTimeMillis()).toAbsolutePath();Portable p=new Portable(root);Path gitPath=Path.of(System.getenv().getOrDefault("GITPORT_TEST_GIT","dist/GitPort/git/cmd/git.exe")).toAbsolutePath();p.settings.setProperty("git",gitPath.toString());
        Path repo=root.resolve("projects/GitPort");Files.createDirectories(repo.resolve("src"));Git git=new Git(p,gitPath,repo);git.run("init","-b","main").require();git.run("config","user.name","UI Fixture").require();git.run("config","user.email","fixture@example.invalid").require();git.run("config","core.autocrlf","false").require();git.run("config","commit.gpgsign","false").require();
        Files.writeString(repo.resolve("README.md"),"# GitPort\nPortable Git workspace\n");Files.writeString(repo.resolve("src/ProjectExplorer.java"),"class ProjectExplorer {\n    String section = \"Recent repositories\";\n}\n");Files.writeString(repo.resolve("src/Theme.java"),"class Theme { String palette = \"Dark\"; }\n");git.run("add",".").require();git.commit("Create portable workspace");git.run("branch","develop").require();git.run("branch","feature/grouped-explorer").require();
        Files.writeString(repo.resolve("src/ProjectExplorer.java"),"class ProjectExplorer {\n    String section = \"Project explorer\";\n    boolean repositoryGroups = true;\n    boolean filterByName = true;\n}\n");Files.writeString(repo.resolve("README.md"),"# GitPort\nPortable Git workspace\n\nOrganize repositories into groups.\n");Files.writeString(repo.resolve("src/Theme.java"),"class Theme { String palette = \"Atom One Dark\"; }\n");Files.writeString(repo.resolve("src/BrandAssets.java"),"class BrandAssets { /* PNG, ICO and startup splash */ }\n");git.run("add","README.md").require();
        String bookmark=p.portablePath(repo);p.settings.setProperty("repos",String.join("\n",bookmark,"projects/Shared tools","projects/Payment API","projects/Customer portal","projects/Notes","projects/Playground"));p.settings.setProperty("lastRepo",bookmark);p.save();
        RepositoryCatalog catalog=new RepositoryCatalog(p);String work=catalog.createGroup("Development tools"),client=catalog.createGroup("Client projects"),personal=catalog.createGroup("Personal");catalog.move(bookmark,work);catalog.move("projects/Shared tools",work);catalog.move("projects/Payment API",client);catalog.move("projects/Customer portal",client);catalog.move("projects/Notes",personal);catalog.move("projects/Playground",personal);Portable.createProfile(root,"Personal");Portable.createProfile(root,"Client workspace");
        SwingUtilities.invokeAndWait(() -> {try{App.theme();app=new App(p);app.addNotify();app.validate();}catch(Exception e){throw new RuntimeException(e);}});idle();
        SwingUtilities.invokeAndWait(() -> {try{JTable table=(JTable)field(app,"files");for(int r=0;r<table.getRowCount();r++)if(table.getValueAt(r,1).toString().contains("ProjectExplorer")){table.setRowSelectionInterval(r,r);break;}}catch(Exception e){throw new RuntimeException(e);}});idle();
        frame("01-workspace",1460,920);frame("02-compact-layout",1100,760);frame("02b-small-layout",1024,720);
        SwingUtilities.invokeAndWait(() -> {try{((JTextField)field(field(app,"explorer"),"filter")).setText("payment");}catch(Exception e){throw new RuntimeException(e);}});frame("03-filtered-explorer",1460,920);
        SwingUtilities.invokeAndWait(() -> {try{((JTextField)field(field(app,"explorer"),"filter")).setText("");}catch(Exception e){throw new RuntimeException(e);}});dialog("profiles","04-profiles");dialog("settings","05-settings");
        if(p.settings.containsKey("editorFont"))throw new AssertionError("Cancelled settings persisted preview fonts");
        acceptFontSettings();frame("08-jetbrains-font-workspace",1460,920);frame("09-jetbrains-font-small",1024,720);scaledCaptures();
        git.run("restore","--staged",".").require();git.run("add",".").require();git.commit("Explorer design");git.run("switch","-c","fixture/incoming").require();Files.writeString(repo.resolve("src/Theme.java"),"class Theme { String palette = \"Deep blue\"; }\n");git.run("add","src/Theme.java").require();git.commit("Incoming palette");git.run("switch","main").require();Files.writeString(repo.resolve("src/Theme.java"),"class Theme { String palette = \"Atom contrast\"; }\n");git.run("add","src/Theme.java").require();git.commit("Current palette");
        if(git.merge("fixture/incoming").code()==0||git.changes().stream().noneMatch(Git.Change::conflict))throw new AssertionError("Fixture did not produce the expected real conflict.");
        SwingUtilities.invokeAndWait(() -> {try{invoke("refresh");}catch(Exception e){throw new RuntimeException(e);}});idle();SwingUtilities.invokeAndWait(() -> {try{((JTable)field(app,"files")).setRowSelectionInterval(0,0);}catch(Exception e){throw new RuntimeException(e);}});idle();dialog("resolveConflict","06-conflict-resolver");idle();
        SwingUtilities.invokeAndWait(() -> {try{JWindow splash=Brand.splash();if(splash!=null){capture(splash.getContentPane(),"07-splashscreen");splash.dispose();}app.dispatchEvent(new WindowEvent(app,WindowEvent.WINDOW_CLOSING));}catch(Exception e){throw new RuntimeException(e);}});
        Files.writeString(evidence.resolve("README.txt"),"Fresh compiled GitPort UI using a disposable fixture repository. Status, diff and merge-conflict operations are real Git fixture operations. Project names/bookmarks/sample code are test data, not company repositories. Capture time: "+java.time.Instant.now()+". No user desktop, private profiles or credentials captured.\n");
        System.out.println("PASS fixture Git operations, wide/compact layouts, explorer filtering, profiles/settings dialogs, font preview/cancel/save/immediate application, real-conflict resolver and splash. Evidence: "+evidence);System.out.println("Fonts: "+AtomTheme.UI.getFamily()+" / "+EditorFonts.current(p.settings).getFamily());
    }
}
