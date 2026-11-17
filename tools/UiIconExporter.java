import gitport.Icons;
import java.nio.file.*;

public final class UiIconExporter {
    public static void main(String[] args)throws Exception{
        Path output=Path.of(args.length==0?"assets/ui":args[0]);Files.createDirectories(output);
        for(var icon:Icons.PATHS.entrySet()){
            String extras=switch(icon.getKey()){
                case "branch"->"<circle cx=\"7\" cy=\"5\" r=\"3\"/><circle cx=\"17\" cy=\"5\" r=\"3\"/><circle cx=\"7\" cy=\"19\" r=\"3\"/>";
                case "profile"->"<circle cx=\"12\" cy=\"7\" r=\"5\"/>";
                case "search"->"<circle cx=\"10\" cy=\"10\" r=\"7\"/>";
                case "settings"->"<circle cx=\"12\" cy=\"12\" r=\"4\"/>";
                case "more"->"<circle cx=\"4\" cy=\"12\" r=\"1\"/><circle cx=\"12\" cy=\"12\" r=\"1\"/><circle cx=\"20\" cy=\"12\" r=\"1\"/>";
                default->"";
            };
            Files.writeString(output.resolve(icon.getKey()+".svg"),"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" style=\"color:#A4ADBC\" stroke-width=\"1.65\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><title>"+icon.getKey()+"</title><path d=\""+icon.getValue()+"\"/>"+extras+"</svg>\n");
        }
        System.out.println("Exported "+Icons.PATHS.size()+" editable SVG interface icons.");
    }
}
