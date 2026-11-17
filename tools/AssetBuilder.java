import java.awt.*;
import java.awt.geom.*;
import java.awt.image.BufferedImage;
import java.io.*;
import java.nio.*;
import java.nio.file.*;
import java.util.*;
import javax.imageio.ImageIO;

/** Approved toolbox artwork, optical icon exports, Windows ICO and splash screens. Java 17+. */
public class AssetBuilder {
    static final String NAVY="#10263E", BLUE="#00B8E5", ORANGE="#FF943D", WHITE="#FFFFFF";
    static final int[] SIZES={16,20,24,32,40,48,64,128,256,512,1024};
    static Path root;
    static boolean monoLight;
    static class Art {
        final Graphics2D g; final StringBuilder svg=new StringBuilder();
        Art(BufferedImage image){g=image.createGraphics();g.scale(image.getWidth()/64.0,image.getHeight()/64.0);g.setRenderingHint(RenderingHints.KEY_ANTIALIASING,RenderingHints.VALUE_ANTIALIAS_ON);}
        void shape(Shape s,String tag,String fill,String stroke,double width){
            if(fill!=null){g.setColor(Color.decode(fill));g.fill(s);}
            if(stroke!=null){g.setColor(Color.decode(stroke));g.setStroke(new BasicStroke((float)width,BasicStroke.CAP_ROUND,BasicStroke.JOIN_ROUND));g.draw(s);}
            svg.append(tag).append(" fill=\"").append(fill==null?"none":fill).append("\"");
            if(stroke!=null)svg.append(" stroke=\"").append(stroke).append("\" stroke-width=\"").append(width).append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\"");
            svg.append("/>\n");
        }
        void rect(double x,double y,double w,double h,double r,String fill){shape(new RoundRectangle2D.Double(x,y,w,h,r*2,r*2),"<rect x=\""+x+"\" y=\""+y+"\" width=\""+w+"\" height=\""+h+"\" rx=\""+r+"\"",fill,null,0);}
        void circle(double x,double y,double r,String fill){shape(new Ellipse2D.Double(x-r,y-r,r*2,r*2),"<circle cx=\""+x+"\" cy=\""+y+"\" r=\""+r+"\"",fill,null,0);}
        void path(String d,String fill,String stroke,double width){
            String[] t=d.split(" +"); int i=0; Path2D.Double p=new Path2D.Double();
            while(i<t.length){String c=t[i++];switch(c){
                case "M":p.moveTo(Double.parseDouble(t[i++]),Double.parseDouble(t[i++]));break;
                case "L":p.lineTo(Double.parseDouble(t[i++]),Double.parseDouble(t[i++]));break;
                case "Q":p.quadTo(Double.parseDouble(t[i++]),Double.parseDouble(t[i++]),Double.parseDouble(t[i++]),Double.parseDouble(t[i++]));break;
                case "Z":p.closePath();break;
                default:throw new IllegalArgumentException(c);
            }}shape(p,"<path d=\""+d+"\"",fill,stroke,width);
        }
        void finish(String name,String title)throws Exception{
            g.dispose();Files.writeString(root.resolve(name+".svg"),"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1024\" height=\"1024\" viewBox=\"0 0 64 64\" role=\"img\" aria-label=\""+title+"\">\n<title>"+title+"</title>\n"+svg+"</svg>\n");
        }
    }
    static BufferedImage draw(int size,boolean mono,boolean compact,boolean save)throws Exception{
        BufferedImage img=new BufferedImage(size,size,BufferedImage.TYPE_INT_ARGB);Art a=new Art(img);
        String ink=mono&&monoLight?WHITE:NAVY;
        // Broad handle and one-piece silhouette survive Windows title-bar sizes.
        a.path("M 20 19 L 20 13 Q 20 6 27 6 L 37 6 Q 44 6 44 13 L 44 19",null,ink,6);
        if(!mono)a.path("M 20 19 L 20 13 Q 20 6 27 6 L 37 6 Q 44 6 44 13 L 44 19",null,BLUE,compact?3.5:3);
        a.rect(2,17,60,44,10,ink);
        if(!mono){
            a.rect(5,20,54,38,7,BLUE);
            a.path("M 37 20 L 52 20 Q 59 20 59 27 L 59 51 Q 59 58 52 58 L 37 58 Z",ORANGE,null,0);
            if(!compact)a.path("M 6 32 L 58 32",null,NAVY,2.5);
        }
        a.circle(32,40,16,ink);
        glyph(a,32,40,mono&&monoLight?NAVY:WHITE,compact);
        if(save)a.finish("gitport"+(mono?(monoLight?"-mono-light":"-mono-dark"):compact?"-small":""),"GitPort gitport"+(mono?" monochrome":compact?" small-size":" logo"));else a.g.dispose();
        return img;
    }
    static void glyph(Art a,double x,double y,String color,boolean compact){
        double r=compact?3.5:3.1;
        a.path("M "+(x-4)+" "+(y-9)+" L "+(x-4)+" "+(y+9)+" M "+(x-4)+" "+(y+3)+" L "+(x+7)+" "+(y-4),null,color,compact?3.8:3.4);
        a.circle(x-4,y-9,r,color);a.circle(x-4,y+9,r,color);a.circle(x+7,y-4,r,color);
    }
    static void ico(ArrayList<byte[]> bytes,ArrayList<Integer> sizes)throws Exception{
        int offset=6+16*bytes.size(),len=offset;for(byte[] b:bytes)len+=b.length;
        ByteBuffer out=ByteBuffer.allocate(len).order(ByteOrder.LITTLE_ENDIAN);out.putShort((short)0).putShort((short)1).putShort((short)bytes.size());
        for(int i=0;i<bytes.size();i++){int n=sizes.get(i);out.put((byte)(n==256?0:n)).put((byte)(n==256?0:n)).put((byte)0).put((byte)0).putShort((short)1).putShort((short)32).putInt(bytes.get(i).length).putInt(offset);offset+=bytes.get(i).length;}
        for(byte[] b:bytes)out.put(b);Files.write(root.resolve("GitPort.ico"),out.array());
    }
    static void text(Graphics2D g,String s,int x,int y,int size,int color,boolean bold){g.setColor(new Color(color));g.setFont(new Font("Segoe UI",bold?Font.BOLD:Font.PLAIN,size));g.drawString(s,x,y);}
    static BufferedImage splash(int width,int height,boolean labels)throws Exception{
        BufferedImage image=new BufferedImage(width,height,BufferedImage.TYPE_INT_RGB);
        Graphics2D g=image.createGraphics();
        g.scale(width/1280.0,height/720.0);
        g.setRenderingHint(RenderingHints.KEY_ANTIALIASING,RenderingHints.VALUE_ANTIALIAS_ON);
        g.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING,RenderingHints.VALUE_TEXT_ANTIALIAS_ON);
        g.setRenderingHint(RenderingHints.KEY_INTERPOLATION,RenderingHints.VALUE_INTERPOLATION_BICUBIC);
        g.setPaint(new GradientPaint(0,0,new Color(0x11283F),1280,720,new Color(0x091827)));
        g.fillRect(0,0,1280,720);
        // Quiet repository paths frame the artwork without competing with the mark.
        g.setColor(new Color(0x18364C));g.setStroke(new BasicStroke(2));
        Path2D route=new Path2D.Double();route.moveTo(1030,0);route.lineTo(1030,104);route.quadTo(1030,144,1070,144);route.lineTo(1280,144);g.draw(route);
        route=new Path2D.Double();route.moveTo(0,588);route.lineTo(270,588);route.quadTo(310,588,310,628);route.lineTo(310,720);g.draw(route);
        g.fillOval(1023,64,14,14);g.fillOval(174,581,14,14);
        g.setColor(new Color(0x00B8E5));g.fillRoundRect(64,58,36,5,5,5);
        g.setColor(new Color(0xFF943D));g.fillRoundRect(108,58,14,5,5,5);
        if(labels){
            text(g,"YOUR PORTABLE GIT WORKSPACE",64,100,15,0xA2B9CC,true);
            g.drawImage(draw(1024,false,false,false),112,174,352,352,null);
            text(g,"GitPort",560,286,80,0xF5F9FC,true);
            text(g,"Your code. Your workspace.",565,338,27,0xBBCDDD,false);
            text(g,"Portable Git  /  Multiple repositories  /  Worktrees",565,393,18,0x94ADBF,false);
            g.setColor(new Color(0x00B8E5));g.fillOval(566,463,7,7);
            text(g,"Opening your workspace",586,474,17,0xBBCDDD,false);
            text(g,"BUILT TO GO WITH YOU",64,662,13,0x7894AB,true);
        }
        g.dispose();return image;
    }
    public static void main(String[] args)throws Exception{
        Path project=Path.of(args.length>0?args[0]:".").toAbsolutePath();
        root=project.resolve("assets");Path resources=project.resolve("src/main/resources/gitport/brand");
        Files.createDirectories(root.resolve("icons"));Files.createDirectories(resources);
        ImageIO.write(draw(1024,false,false,true),"png",root.resolve("gitport-master.png").toFile());
        draw(1024,false,true,true);draw(1024,true,false,true);
        monoLight=true;draw(1024,true,false,true);monoLight=false;
        ArrayList<byte[]> bytes=new ArrayList<>();ArrayList<Integer> sizes=new ArrayList<>();
        for(int n:SIZES){
            BufferedImage icon=draw(n,false,n<=32,false);
            Path png=root.resolve("icons/gitport-"+n+".png");ImageIO.write(icon,"png",png.toFile());
            Files.copy(png,resources.resolve("gitport-"+n+".png"),StandardCopyOption.REPLACE_EXISTING);
            if(n<=256){ByteArrayOutputStream b=new ByteArrayOutputStream();ImageIO.write(icon,"png",b);bytes.add(b.toByteArray());sizes.add(n);}
        }
        ico(bytes,sizes);
        ImageIO.write(splash(1280,720,false),"png",root.resolve("splash-background.png").toFile());
        ImageIO.write(splash(1280,720,true),"png",root.resolve("splashscreen.png").toFile());
        Path small=root.resolve("splashscreen-960x540.png");ImageIO.write(splash(960,540,true),"png",small.toFile());
        Files.copy(small,resources.resolve("splashscreen.png"),StandardCopyOption.REPLACE_EXISTING);
        System.out.println("Exported approved toolbox SVGs, 11 optical PNG sizes, Windows ICO and refreshed splash screens.");
    }
}
