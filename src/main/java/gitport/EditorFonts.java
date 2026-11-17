package gitport;

import java.awt.*;
import java.io.*;
import java.util.*;

/** Portable editor fonts; bundled faces never need a Windows font installation. */
public final class EditorFonts {
    public static final String DEFAULT="System default";
    public static final String[] CHOICES={DEFAULT,"JetBrains Mono","Fira Code","Source Code Pro","Cascadia Code","Consolas","Monospaced"};
    private static final Map<String,Font> BUNDLED=load();
    private static Map<String,Font> load(){
        Map<String,Font> fonts=new LinkedHashMap<>();
        String[] names={"JetBrains Mono","Fira Code","Source Code Pro"};
        String[] files={"JetBrainsMono-Regular.ttf","FiraCode-Regular.ttf","SourceCodePro-Regular.ttf"};
        for(int i=0;i<names.length;i++)try(InputStream in=EditorFonts.class.getResourceAsStream("/fonts/"+files[i])){
            if(in==null)throw new IOException("Missing bundled font: "+files[i]);
            Font font=Font.createFont(Font.TRUETYPE_FONT,in);GraphicsEnvironment.getLocalGraphicsEnvironment().registerFont(font);fonts.put(names[i],font);
        }catch(IOException|FontFormatException e){throw new IllegalStateException("Cannot load editor fonts",e);}
        return Collections.unmodifiableMap(fonts);
    }
    public static boolean available(String name){
        if(DEFAULT.equals(name)||BUNDLED.containsKey(name)||"Monospaced".equals(name))return true;
        if("Consolas".equals(name)&&AtomTheme.CODE.getFamily().equalsIgnoreCase(name))return true;
        return Arrays.stream(GraphicsEnvironment.getLocalGraphicsEnvironment().getAvailableFontFamilyNames()).anyMatch(name::equalsIgnoreCase);
    }
    public static String selected(Properties settings){String name=settings.getProperty("editorFont",DEFAULT);return Arrays.asList(CHOICES).contains(name)?name:DEFAULT;}
    public static int size(Properties settings){try{return Math.max(11,Math.min(22,Integer.parseInt(settings.getProperty("editorFontSize","13"))));}catch(NumberFormatException e){return 13;}}
    public static Font resolve(String name,int size){
        int bounded=Math.max(11,Math.min(22,size));
        if(BUNDLED.containsKey(name))return BUNDLED.get(name).deriveFont((float)bounded);
        if(DEFAULT.equals(name)||!available(name)||"Consolas".equals(name)&&AtomTheme.CODE.getFamily().equalsIgnoreCase(name))return AtomTheme.CODE.deriveFont((float)bounded);
        return new Font(name,Font.PLAIN,bounded);
    }
    public static Font current(Properties settings){return resolve(selected(settings),size(settings));}
    private EditorFonts(){}
}
